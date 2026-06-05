using System.ClientModel;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenAI;
using WesleyCode.Hosting;
using WesleyCode.Infrastructure;
using WesleyCode.Options;
using WesleyCode.Services;

namespace WesleyCode.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddAgentHost(this IHostApplicationBuilder builder, string workDirectory)
    {
        builder.Services.AddAgentHost(builder.Configuration, workDirectory);
        return builder;
    }

    public static IServiceCollection AddAgentHost(this IServiceCollection services, IConfiguration configuration, string workDirectory)
    {
        services
            .AddOptions<OpenAiOptions>()
            .Configure(config =>
            {
                config.ModelId = configuration.GetValue<string>("WINTEAM_MODELID") ?? "gpt-5.4";
                config.BaseUrl = configuration.GetValue<string>("WINTEAM_BASEURL") ?? string.Empty;
                config.ApiKey = configuration.GetValue<string>("WINTEAM_APIKEY") ?? string.Empty;
            });
        services
            .AddOptions<AgentOptions>()
            .Configure(config =>
            {
                config.Name = "main";
                config.Instructions = """
                使用工具执行操作完成用户需求,输出操作总结;
                专注任务使用子代理完成;
                使用工具跟踪任务清单;
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
            var cache = provider.GetRequiredService<IDistributedCache>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = provider.GetRequiredService<ILogger<OpenAiOptions>>();
            var options = provider.GetRequiredService<IOptions<OpenAiOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException("未配置 OpenAI API Key，请设置 WINTEAM_APIKEY。");
            }

            logger.LogInformation("BaseUrl:{BaseUrl}", string.IsNullOrWhiteSpace(options.BaseUrl) ? "(default)" : options.BaseUrl);
            logger.LogInformation("ModelId:{ModelId}", options.ModelId);

            var clientOptions = new OpenAIClientOptions { MessageLoggingPolicy = new LoggingAuthPolicy(false, true, loggerFactory) };
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var endpoint))
                {
                    throw new InvalidOperationException($"BaseUrl 配置无效: {options.BaseUrl}");
                }

                clientOptions.Endpoint = endpoint;
            }

            var chatClient = new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions)
                .GetResponsesClient()
                .AsIChatClient(options.ModelId)
                .AsBuilder()
                .UseDistributedCache(cache)
                .UseLogging(loggerFactory)
                .Build();
            return CrsChatClient.Create(chatClient);
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
            var localUserSkills = Path.Combine(AppContext.BaseDirectory, "skills", "user");

            var defaultInstructions = new StringBuilder(
                """
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
                """
            );

            defaultInstructions.AppendLine($"Put the newly added skills in the {localUserSkills} directory.");

            return new AgentSkillsProvider(
                skillPaths: [systemSkills, localUserSkills],
                options: new AgentSkillsProviderOptions
                {
                    SkillsInstructionPrompt = defaultInstructions.ToString(),
                    ScriptApproval = false,
                    DisableCaching = false,
                },
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
                        Reasoning = new ReasoningOptions { Output = ReasoningOutput.Full },
                        Instructions = options.Instructions,
                        Tools = ToolManager.AllFunctions,
                        ToolMode = ChatToolMode.Auto,
                    },
                    AIContextProviders = [compactionProvider, promptProvider, skillsProvider, agentProvider],
                }
            );
        });

        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddHostedService<ConsoleAgentHostedService>();

        return services;
    }
}
