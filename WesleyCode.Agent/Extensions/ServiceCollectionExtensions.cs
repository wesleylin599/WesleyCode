using System.ClientModel;
using System.Text;
using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;
using WesleyCode.Agent.Infrastructure;
using WesleyCode.Agent.Options;
using WesleyCode.Agent.Services;

namespace WesleyCode.Agent.Extensions;

public static class ServiceCollectionExtensions
{
    private const string _baseSkillsInstructions = """
        You have access to skills containing domain-specific knowledge and capabilities.
        Each skill provides specialized instructions, reference documents, and assets for specific tasks.

        <available_skills>
        {{skills}}
        </available_skills>

        When a task aligns with a skill's domain, follow these steps in exact order:
        - Use `load_skill` to retrieve the skill's instructions.
        - Follow the provided guidance.
        {{resource_instructions}}
        {{script_instructions}}
        Only load what is needed, when it is needed.
        """;

    public static bool HasToolContent(this IList<AIContent> contents) =>
        contents.Any(static content => content is FunctionCallContent or FunctionResultContent);

    public static IServiceCollection AddAgentHost(this IServiceCollection services, string workDirectory)
    {
        services.AddSingleton<IDistributedCache>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
            var cacheOptions = new MemoryDistributedCacheOptions { SizeLimit = options.SizeLimit };
            return new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(cacheOptions));
        });

        services.TryAddSingleton<IOutputCapture, NullOutputCapture>();
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<IAgentRunner, AgentRunner>();
        services.AddAIProviders(workDirectory);
        services.ConfigOptions();
        services.AddAIAgent();

        return services;
    }

    private static IServiceCollection AddAIProviders(this IServiceCollection services, string workDirectory)
    {
        services.AddSingleton<SystemPromptProvider>(provider => new(workDirectory, provider.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton<CompactionProvider>(provider =>
        {
            var compactionOptions = provider.GetRequiredService<IOptions<CompactionOptions>>().Value;
            var crsClient = provider.GetRequiredService<IChatClient>();
            var pipeline = new PipelineCompactionStrategy(
                new ToolResultCompactionStrategy(CompactionTriggers.TokensExceed(compactionOptions.ToolResultTokenLimit)),
                new SummarizationCompactionStrategy(crsClient, CompactionTriggers.TokensExceed(compactionOptions.SummaryTokenLimit)),
                new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(compactionOptions.SlidingWindowTurnLimit)),
                new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(compactionOptions.TruncationTokenLimit))
            );
            return new(pipeline, loggerFactory: provider.GetRequiredService<ILoggerFactory>());
        });

        services.AddSingleton<AgentSkillsProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var systemSkills = Path.Combine(AppContext.BaseDirectory, "skills", "system");
            var localSkills = Path.Combine(AppContext.BaseDirectory, "skills", "user");

            var defaultInstructions = new StringBuilder(_baseSkillsInstructions);
            defaultInstructions.AppendLine($"skills 操作目标目录 \"{localSkills}\"");

            return new(
                skillPaths: [systemSkills, localSkills],
                options: new AgentSkillsProviderOptions { SkillsInstructionPrompt = defaultInstructions.ToString() },
                loggerFactory: loggerFactory
            );
        });

        services.AddSingleton<SubAgentProvider>(provider =>
            new(
                provider.GetRequiredService<IChatClient>(),
                provider.GetRequiredService<IOutputCapture>(),
                [
                    provider.GetRequiredService<CompactionProvider>(),
                    provider.GetRequiredService<SystemPromptProvider>(),
                    provider.GetRequiredService<AgentSkillsProvider>(),
                ],
                provider.GetRequiredService<ILoggerFactory>()
            )
        );
        return services;
    }

    private static IServiceCollection ConfigOptions(this IServiceCollection services)
    {
        services
            .AddOptions<ChatClientOptions>()
            .Configure<IConfiguration>(
                (options, configuration) =>
                {
                    options.Provider = configuration.GetValue<string>("WINTEAM_PROVIDER");
                    options.ModelId = configuration.GetValue<string>("WINTEAM_MODELID");
                    options.BaseUrl = configuration.GetValue<string>("WINTEAM_BASEURL");
                    options.ApiKey = configuration.GetValue<string>("WINTEAM_APIKEY");
                }
            );
        services
            .AddOptions<AgentOptions>()
            .Configure(config =>
            {
                config.Name = "main";
                config.Instructions = """
                使用工具完成用户需求,并跟踪任务完成进度
                执行发生错误时,分析错误原因,修正后重试
                """;
            });
        services
            .AddOptions<CacheOptions>()
            .Configure(config =>
            {
                config.SizeLimit = 1024;
            });
        services
            .AddOptions<CompactionOptions>()
            .Configure(config =>
            {
                config.ToolResultTokenLimit = 1500;
                config.SummaryTokenLimit = 10000;
                config.SlidingWindowTurnLimit = 10;
                config.TruncationTokenLimit = 30000;
            });
        services
            .AddOptions<SessionOptions>()
            .Configure(config =>
            {
                config.DirectoryName = "session";
            });
        return services;
    }

    private static IServiceCollection AddAIAgent(this IServiceCollection services)
    {
        services.AddChatClient(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptions<ChatClientOptions>>();

            var client = CreateChatClient(options.Value, loggerFactory);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Provider:{options.Value.Provider}");
            if (!string.IsNullOrWhiteSpace(options.Value.BaseUrl))
            {
                builder.AppendLine($"BaseUrl:{options.Value.BaseUrl}");
            }
            builder.AppendLine($"ModelId:{options.Value.ModelId}");
            provider.GetRequiredService<IOutputCapture>().WriteSystemMessage(builder.ToString());

            return client.AsBuilder().UseDistributedCache(provider.GetRequiredService<IDistributedCache>()).UseLogging(loggerFactory).Build();
        });

        services.AddSingleton<AIAgent>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AgentOptions>>();
            return provider
                .GetRequiredService<IChatClient>()
                .AsAIAgent(
                    options: new ChatClientAgentOptions
                    {
                        Name = options.Value.Name,
                        ChatOptions = new ChatOptions
                        {
                            Reasoning = new ReasoningOptions { Output = ReasoningOutput.Summary },
                            Instructions = options.Value.Instructions,
                            Tools = ToolManager.AllFunctions,
                            ToolMode = ChatToolMode.Auto,
                        },
                        AIContextProviders =
                        [
                            provider.GetRequiredService<CompactionProvider>(),
                            provider.GetRequiredService<SystemPromptProvider>(),
                            provider.GetRequiredService<AgentSkillsProvider>(),
                            provider.GetRequiredService<SubAgentProvider>(),
                        ],
                    }
                );
        });
        return services;
    }

    private static IChatClient CreateChatClient(ChatClientOptions options, ILoggerFactory loggerFactory)
    {
        return options.Provider switch
        {
            "anthropic" => CreateAnthropicChatClient(options),
            "openai" => CreateOpenAiChatClient(options, loggerFactory),
            "crs" => CreateCrsChatClient(options, loggerFactory),
            "ollama" => CreateOllamaChatClient(options),
            _ => throw new InvalidOperationException($"不支持的 IChatClient Provider: {options.Provider}。可选值: openai、anthropic、crs、ollama。"),
        };
    }

    private static IChatClient CreateOpenAiChatClient(ChatClientOptions options, ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WINTEAM_APIKEY。");
        }
        var clientOptions = new OpenAIClientOptions { MessageLoggingPolicy = new LoggingAuthPolicy(false, true, loggerFactory) };
        var endpoint = GetEndpoint(options.BaseUrl);
        if (endpoint is not null)
        {
            clientOptions.Endpoint = endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions).GetResponsesClient().AsIChatClient(options.ModelId);
    }

    private static IChatClient CreateCrsChatClient(ChatClientOptions options, ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WINTEAM_APIKEY。");
        }
        var baseClient = CreateOpenAiChatClient(options, loggerFactory);
        return CrsChatClient.Create(baseClient);
    }

    private static IChatClient CreateAnthropicChatClient(ChatClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WINTEAM_APIKEY。");
        }
        var endpoint = GetEndpoint(options.BaseUrl);
        var client = endpoint is null
            ? new AnthropicClient { ApiKey = options.ApiKey }
            : new AnthropicClient { ApiKey = options.ApiKey, BaseUrl = endpoint.ToString().TrimEnd('/') };

        return client.AsIChatClient(options.ModelId);
    }

    private static IChatClient CreateOllamaChatClient(ChatClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WINTEAM_APIKEY。");
        }
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new InvalidOperationException("未配置 Model Id，请设置 WINTEAM_MODELID。");
        }
        var endpoint = GetEndpoint(options.BaseUrl);
        if (endpoint is null)
        {
            throw new InvalidOperationException("未配置 BaseUrl，请设置 WINTEAM_BASEURL。");
        }
        return new OllamaApiClient(endpoint, options.ModelId);
    }

    private static Uri? GetEndpoint(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"BaseUrl 配置无效: {baseUrl}");
        }

        return endpoint;
    }
}
