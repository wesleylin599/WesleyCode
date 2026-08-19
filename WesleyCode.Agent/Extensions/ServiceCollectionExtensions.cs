using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using WesleyCode.Agent.Infrastructure;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;
using WesleyCode.Agent.Services;

namespace WesleyCode.Agent.Extensions;

/// <summary>
/// 负责 Agent 宿主相关的服务注册。
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string AgentHttpClientName = "Wesley";

    public static IHttpClientBuilder ConfigureHttpClientAgents(this IServiceCollection services, Action<HttpClient> configureClient) =>
        services.AddHttpClient(AgentHttpClientName).ConfigureHttpClient(configureClient);

    public static IServiceCollection AddAgentHost(this IServiceCollection services, string workDirectory)
    {
        services
            .AddOptions<WorkingOptions>()
            .Configure(config =>
            {
                config.BasePath = workDirectory;
            });
        services
            .AddOptions<SkillOptions>()
            .Configure(config =>
            {
                config.SkillPath = Path.Combine(AppContext.BaseDirectory, "skills");
            });
        services
            .AddOptions<ChatClientOptions>()
            .Configure<IConfiguration>(
                (options, configuration) =>
                {
                    options.Provider = configuration.GetValue<string?>("WESLEY_PROVIDER");
                    options.ModelId = configuration.GetValue<string?>("WESLEY_MODELID");
                    options.BaseUrl = configuration.GetValue<string?>("WESLEY_BASEURL");
                    options.ApiKey = configuration.GetValue<string?>("WESLEY_APIKEY");
                    options.AllowBackgroundResponses = configuration.GetValue<bool?>("AllowBackgroundResponses");
                    options.AllowMultipleToolCalls = configuration.GetValue<bool?>("AllowMultipleToolCalls");
                    options.Effort = configuration.GetValue<ReasoningEffort?>("Effort");
                    options.Output = configuration.GetValue<ReasoningOutput?>("Output");
                    options.MaxOutputTokens = configuration.GetValue<int?>("MaxOutputTokens");
                    options.StopMark = configuration.GetValue<string>("StopMark") ?? "[EOF]";
                }
            );
        services
            .AddOptions<SessionOptions>()
            .Configure(config =>
            {
                config.DirectoryName = "session";
            });

        services.AddOptions<ChatOptions>().BindConfiguration("ChatClient");

        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<IAgentRunner, AgentRunner>();

        services.RegisterAIProviders();
        services.RegisterAIAgent();

        return services;
    }

    private static void RegisterAIProviders(this IServiceCollection services)
    {
        // 基础 Provider
        services.AddTransient<AIContextProvider, CommandProvider>();
        services.AddTransient<AIContextProvider, FileSkillsProvider>();
        services.AddTransient<AIContextProvider, SystemPromptProvider>();

        // 上下文压缩 Provider
        services.AddTransient<AIContextProvider>(provider => new CompactionProvider(
            new TruncationCompactionStrategy(
                trigger: CompactionTriggers.Any(CompactionTriggers.GroupsExceed(100), CompactionTriggers.TokensExceed(50000)),
                minimumPreservedGroups: 32,
                target: CompactionTriggers.TokensBelow(20000)
            )
        ));

        // Todo Provider（禁用列表消息）
        services.AddTransient<AIContextProvider>(provider => new TodoProvider());

        // Agent 模式 Provider
        services.AddTransient<AIContextProvider>(provider => new AgentModeProvider(
            new AgentModeProviderOptions { DefaultMode = AgentModes.DefaultMode, Modes = AgentModes.Modes }
        ));

        // Memory Provider
        services.AddTransient<AIContextProvider>(provider => new FileMemoryProvider(
            new FileSystemAgentFileStore(provider.GetRequiredService<IOptions<WorkingOptions>>().Value.BasePath)
        ));

        // File Access Provider
        services.AddTransient<AIContextProvider>(provider => new FileAccessProvider(
            new FileSystemAgentFileStore(provider.GetRequiredService<IOptions<WorkingOptions>>().Value.BasePath),
            new FileAccessProviderOptions { DisableReadOnlyToolApproval = true, DisableWriteToolApproval = true }
        ));

        // 技能执行 Provider（禁用审批）
        services.AddTransient<AIContextProvider>(provider =>
            new AgentSkillsProviderBuilder()
                .UseFileSkill(provider.GetRequiredService<IOptions<SkillOptions>>().Value.SkillPath)
                .UseOptions(options =>
                {
                    options.DisableReadSkillResourceApproval = true;
                    options.DisableRunSkillScriptApproval = true;
                    options.DisableLoadSkillApproval = true;
                })
                .UseFileScriptRunner(CliWrapRunner.RunAsync)
                .DisableCaching()
                .Build()
        );
    }

    private static void RegisterAIAgent(this IServiceCollection services)
    {
        services.AddChatClient(provider =>
            ChatClientFactory
                .Create(
                    provider.GetRequiredService<IOptions<ChatClientOptions>>().Value,
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient(AgentHttpClientName)
                )
                .AsBuilder()
                .UseFunctionInvocation()
                .UseContinueError()
                .Build()
        );
    }

    private static ChatClientBuilder UseContinueError(this ChatClientBuilder builder)
    {
        return builder.Use(
            (innerClient, services) =>
            {
                return new ContinueErrorChatClient(innerClient);
            }
        );
    }
}
