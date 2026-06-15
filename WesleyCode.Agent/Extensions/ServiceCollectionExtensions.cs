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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    public static IHostApplicationBuilder AddAgentHost(this IHostApplicationBuilder builder, string workDirectory)
    {
        builder.Services.AddAgentHost(builder.Configuration, workDirectory);
        return builder;
    }

    public static IServiceCollection AddAgentHost(this IServiceCollection services, IConfiguration configuration, string workDirectory)
    {
        services
            .AddOptions<ChatClientOptions>()
            .Configure(config =>
            {
                config.Provider = configuration.GetValue<string>("WINTEAM_PROVIDER");
                config.ModelId = configuration.GetValue<string>("WINTEAM_MODELID");
                config.BaseUrl = configuration.GetValue<string>("WINTEAM_BASEURL");
                config.ApiKey = configuration.GetValue<string>("WINTEAM_APIKEY");
            });
        services
            .AddOptions<AgentOptions>()
            .Configure(config =>
            {
                config.Name = "main";
                config.Instructions = """
                使用工具执行操作完成用户需求;
                使用工具跟踪任务清单;
                完成后输出总结;
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

        services.AddSingleton<IChatClient>(provider =>
        {
            var capture = provider.GetRequiredService<IOutputCapture>();
            var cache = provider.GetRequiredService<IDistributedCache>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptions<ChatClientOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.ModelId))
            {
                throw new InvalidOperationException("未配置 Model Id，请设置 WINTEAM_MODELID。");
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException("未配置 API Key，请设置 WINTEAM_APIKEY。");
            }

            var providerName = NormalizeProvider(options.Provider);
            var client = CreateChatClient(options, loggerFactory);
            capture.WriteSystemMessage($"Provider:{providerName}");
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                capture.WriteSystemMessage($"BaseUrl:{options.BaseUrl}");
            }
            capture.WriteSystemMessage($"ModelId:{options.ModelId}");

            return client.AsBuilder().UseDistributedCache(cache).UseLogging(loggerFactory).Build();
        });

        services.AddSingleton<IDistributedCache>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
            var cacheOptions = new MemoryDistributedCacheOptions();
            if (options.SizeLimit > 0)
            {
                cacheOptions.SizeLimit = options.SizeLimit;
            }
            return new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(cacheOptions));
        });

        services.AddSingleton<CompactionProvider>(provider =>
        {
            var compactionOptions = provider.GetRequiredService<IOptions<CompactionOptions>>().Value;
            var crsClient = provider.GetRequiredService<IChatClient>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var pipeline = new PipelineCompactionStrategy(
                new ToolResultCompactionStrategy(CompactionTriggers.TokensExceed(compactionOptions.ToolResultTokenLimit)),
                new SummarizationCompactionStrategy(crsClient, CompactionTriggers.TokensExceed(compactionOptions.SummaryTokenLimit)),
                new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(compactionOptions.SlidingWindowTurnLimit)),
                new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(compactionOptions.TruncationTokenLimit))
            );
            return new CompactionProvider(pipeline, loggerFactory: loggerFactory);
        });

        services.AddSingleton<AgentSkillsProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var systemSkills = Path.Combine(AppContext.BaseDirectory, "skills", "system");
            var localSkills = Path.Combine(AppContext.BaseDirectory, "skills", "user");

            var defaultInstructions = new StringBuilder(_baseSkillsInstructions);
            defaultInstructions.AppendLine($"skills 操作目标目录 \"{localSkills}\"");

            return new AgentSkillsProvider(
                skillPaths: [systemSkills, localSkills],
                options: new AgentSkillsProviderOptions { SkillsInstructionPrompt = defaultInstructions.ToString() },
                loggerFactory: loggerFactory
            );
        });

        services.AddSingleton<SystemPromptProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new SystemPromptProvider(workDirectory, loggerFactory);
        });

        services.AddSingleton<SubAgentProvider>(provider =>
        {
            var crsClient = provider.GetRequiredService<IChatClient>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var compactionProvider = provider.GetRequiredService<CompactionProvider>();
            var promptProvider = provider.GetRequiredService<SystemPromptProvider>();
            var skillsProvider = provider.GetRequiredService<AgentSkillsProvider>();
            return new SubAgentProvider(crsClient, [compactionProvider, promptProvider, skillsProvider], loggerFactory);
        });

        services.AddSingleton<AIAgent>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AgentOptions>>().Value;
            var compactionProvider = provider.GetRequiredService<CompactionProvider>();
            var promptProvider = provider.GetRequiredService<SystemPromptProvider>();
            var skillsProvider = provider.GetRequiredService<AgentSkillsProvider>();
            var agentProvider = provider.GetRequiredService<SubAgentProvider>();
            var chatClient = provider.GetRequiredService<IChatClient>();
            return chatClient.AsAIAgent(
                options: new ChatClientAgentOptions
                {
                    Name = options.Name,
                    ChatOptions = new ChatOptions
                    {
                        Reasoning = new ReasoningOptions { Output = ReasoningOutput.Summary },
                        Instructions = options.Instructions,
                        Tools = ToolManager.AllFunctions,
                        ToolMode = ChatToolMode.Auto,
                    },
                    AIContextProviders = [compactionProvider, promptProvider, skillsProvider, agentProvider],
                }
            );
        });

        services.TryAddSingleton<IOutputCapture, NullOutputCapture>();
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<IAgentRunner, AgentRunner>();

        return services;
    }

    private static IChatClient CreateChatClient(ChatClientOptions options, ILoggerFactory loggerFactory)
    {
        var providerName = NormalizeProvider(options.Provider);
        return providerName switch
        {
            "anthropic" => CreateAnthropicChatClient(options),
            "openai" => CreateOpenAiCompatibleChatClient(options, loggerFactory),
            "crs" => CreateCrsCompatibleChatClient(options, loggerFactory),
            _ => throw new InvalidOperationException($"不支持的 IChatClient Provider: {options.Provider}。"),
        };
    }

    private static IChatClient CreateOpenAiCompatibleChatClient(ChatClientOptions options, ILoggerFactory loggerFactory, Uri? defaultEndpoint = null)
    {
        var clientOptions = new OpenAIClientOptions { MessageLoggingPolicy = new LoggingAuthPolicy(false, true, loggerFactory) };
        var endpoint = GetEndpoint(options.BaseUrl, defaultEndpoint);
        if (endpoint is not null)
        {
            clientOptions.Endpoint = endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey!), clientOptions).GetChatClient(options.ModelId).AsIChatClient();
    }

    private static IChatClient CreateCrsCompatibleChatClient(ChatClientOptions options, ILoggerFactory loggerFactory, Uri? defaultEndpoint = null)
    {
        var baseClient = CreateOpenAiCompatibleChatClient(options, loggerFactory, defaultEndpoint);

        return CrsChatClient.Create(baseClient);
    }

    private static IChatClient CreateAnthropicChatClient(ChatClientOptions options)
    {
        var endpoint = GetEndpoint(options.BaseUrl, null);
        var client = endpoint is null
            ? new AnthropicClient { ApiKey = options.ApiKey! }
            : new AnthropicClient { ApiKey = options.ApiKey!, BaseUrl = endpoint.ToString().TrimEnd('/') };

        return client.AsIChatClient(options.ModelId);
    }

    private static Uri? GetEndpoint(string? baseUrl, Uri? defaultEndpoint)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return defaultEndpoint;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"BaseUrl 配置无效: {baseUrl}");
        }

        return endpoint;
    }

    private static string NormalizeProvider(string? provider)
    {
        var value = string.IsNullOrWhiteSpace(provider) ? "crs" : provider.Trim().ToLowerInvariant();
        return value switch
        {
            "authropic" => "anthropic",
            _ => value,
        };
    }
}
