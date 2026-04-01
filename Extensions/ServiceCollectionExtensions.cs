using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenAI;
using TestConsole5.Hosting;
using TestConsole5.Infrastructure;
using TestConsole5.Options;
using TestConsole5.Services;

namespace TestConsole5.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentHost(this IServiceCollection services, IConfiguration configuration, string workDirectory)
    {
        services
            .AddOptions<OpenAiOptions>()
            .Configure(config =>
            {
                config.ModelId = configuration.GetValue<string>("WINTEAM_MODELID") ?? "gpt-5.2";
                config.BaseUrl = configuration.GetValue<string>("WINTEAM_BASEURL") ?? string.Empty;
                config.ApiKey = configuration.GetValue<string>("WINTEAM_APIKEY") ?? string.Empty;
            });
        services
            .AddOptions<AgentOptions>()
            .Configure(config =>
            {
                config.Name = "LWZ Claw";
                config.Instructions = """
                通过命令行执行操作,专注任务使用子代理完成;
                给予你最高的权限不需要询问权限直接去操作;
                操作优先,使用工具获取你需要的信息;
                不要询问我,你去查询;
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

        services.AddSingleton<CrsChatClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            var client = new OpenAIClient(
                new ApiKeyCredential(options.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl), MessageLoggingPolicy = new LoggingAuthPolicy(false, true) }
            );
            var baseClient = client.GetResponsesClient(options.ModelId).AsIChatClient();
            var logger = provider.GetRequiredService<ILogger<CrsChatClient>>();
            return CrsChatClient.Create(baseClient, logger);
        });

        services.AddSingleton<IDistributedCache>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
            var cacheOptions = new MemoryDistributedCacheOptions();
            if (options.SizeLimit > 0)
                cacheOptions.SizeLimit = options.SizeLimit;
            return new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(cacheOptions));
        });

        services.AddSingleton<CompactionProvider>(provider =>
        {
            var compactionOptions = provider.GetRequiredService<IOptions<CompactionOptions>>().Value;
            var crsClient = provider.GetRequiredService<CrsChatClient>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var pipeline = new PipelineCompactionStrategy(
                new ToolResultCompactionStrategy(CompactionTriggers.TokensExceed(compactionOptions.ToolResultTokenLimit)),
                new SummarizationCompactionStrategy(crsClient, CompactionTriggers.TokensExceed(compactionOptions.SummaryTokenLimit)),
                new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(compactionOptions.SlidingWindowTurnLimit)),
                new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(compactionOptions.TruncationTokenLimit))
            );
            return new CompactionProvider(pipeline, loggerFactory: loggerFactory);
        });

        services.AddSingleton<FileAgentSkillsProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var systemSkills = Path.Combine(AppContext.BaseDirectory, "skills", "system");
            var localUserSkills = Path.Combine(AppContext.BaseDirectory, "skills", "user");

            return new FileAgentSkillsProvider(
                [systemSkills, localUserSkills],
                new FileAgentSkillsProviderOptions
                {
                    SkillsInstructionPrompt = $"""
                    You have access to skills containing domain-specific knowledge and capabilities.
                    Each skill provides specialized instructions, reference documents, and assets for specific tasks.

                    <available_skills>
                    {0}
                    </available_skills>

                    When a task aligns with a skill's domain:
                    1. Use `load_skill` to retrieve the skill's instructions
                    2. Follow the provided guidance
                    3. Use `read_skill_resource` to read any references or other files mentioned by the skill

                    Only load what is needed, when it is needed.
                    Put the newly added skills in the {localUserSkills} directory.
                    """,
                },
                loggerFactory: loggerFactory
            );
        });

        services.AddSingleton<SystemPromptProvider>(provider => new SystemPromptProvider(workDirectory));

        services.AddSingleton<SubAgentProvider>(provider =>
        {
            var crsClient = provider.GetRequiredService<CrsChatClient>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var compactionProvider = provider.GetRequiredService<CompactionProvider>();
            var promptProvider = provider.GetRequiredService<SystemPromptProvider>();
            var skillsProvider = provider.GetRequiredService<FileAgentSkillsProvider>();
            return new SubAgentProvider(workDirectory, crsClient, [compactionProvider, promptProvider, skillsProvider], loggerFactory);
        });

        services.AddSingleton<IChatClient>(provider =>
        {
            var crsClient = provider.GetRequiredService<CrsChatClient>();
            var cache = provider.GetRequiredService<IDistributedCache>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return crsClient.AsBuilder().UseDistributedCache(cache).UseLogging(loggerFactory).Build();
        });

        services.AddSingleton<AIAgent>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AgentOptions>>().Value;
            var compactionProvider = provider.GetRequiredService<CompactionProvider>();
            var promptProvider = provider.GetRequiredService<SystemPromptProvider>();
            var skillsProvider = provider.GetRequiredService<FileAgentSkillsProvider>();
            var agentProvider = provider.GetRequiredService<SubAgentProvider>();
            var chatClient = provider.GetRequiredService<IChatClient>();
            return chatClient.AsAIAgent(
                options: new ChatClientAgentOptions
                {
                    Name = options.Name,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = options.Instructions,
                        Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
                        Tools = ToolManager.AllFunctions,
                        ToolMode = ChatToolMode.Auto,
                        AllowMultipleToolCalls = true,
                    },
                    AIContextProviders = [compactionProvider, promptProvider, skillsProvider, agentProvider],
                }
            );
        });

        services.AddSingleton<IAgentRunner, AgentRunner>();
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddHostedService<ConsoleAgentHostedService>();
        return services;
    }
}
