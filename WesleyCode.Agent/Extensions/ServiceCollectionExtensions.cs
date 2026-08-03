using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using WesleyCode.Agent.Infrastructure;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;
using WesleyCode.Agent.Services;

namespace WesleyCode.Agent.Extensions;

public static class ServiceCollectionExtensions
{
    private const string AgentHttpClientName = "Wesley";

    public static string ComputeMd5(this string target) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(target))).ToLowerInvariant();

    public static void CommonWriteMessage(this IOutputCapture capture, string? author, AIContent content)
    {
        if (content is ErrorContent errorContent)
        {
            capture.WriteSystemMessage(errorContent.Message);
        }
        else if (content is FunctionCallContent callContent)
        {
            capture.WriteToolCall(callContent.CallId, author, callContent.Name, callContent.Arguments);
        }
        else if (content is FunctionResultContent resultContent)
        {
            var result = resultContent.Exception?.Message ?? resultContent.Result;
            capture.WriteToolResult(resultContent.CallId, author, result);
        }
    }

    public static AIAgentBuilder UseOutput(this AIAgentBuilder builder, IOutputCapture capture) =>
        builder.Use(innerAgent => new OutputAgent(innerAgent, capture));

    public static AIAgentBuilder UseLoop(this AIAgentBuilder builder) =>
        builder.Use(innerAgent => new LoopAgent(innerAgent, new NonEmptyLoopEvaluator()));

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
            .AddOptions<ChatClientOptions>()
            .Configure<IConfiguration>(
                (options, configuration) =>
                {
                    options.Provider = configuration.GetValue<string>("WESLEY_PROVIDER");
                    options.ModelId = configuration.GetValue<string>("WESLEY_MODELID");
                    options.BaseUrl = configuration.GetValue<string>("WESLEY_BASEURL");
                    options.ApiKey = configuration.GetValue<string>("WESLEY_APIKEY");
                }
            );
        services
            .AddOptions<ImageClientOptions>()
            .Configure<IConfiguration>(
                (options, configuration) =>
                {
                    options.ModelId = configuration.GetValue<string>("WESLEY_IMAGE_MODELID");
                    options.BaseUrl = configuration.GetValue<string>("WESLEY_IMAGE_BASEURL");
                    options.ApiKey = configuration.GetValue<string>("WESLEY_IMAGE_APIKEY");
                }
            );
        services
            .AddOptions<AgentOptions>()
            .Configure(config =>
            {
                config.Name = "main";
            });

        services
            .AddOptions<SessionOptions>()
            .Configure(config =>
            {
                config.DirectoryName = "session";
            });

        services.AddTransient<ISessionStore, SessionStore>();
        services.AddSingleton<IAgentRunner, AgentRunner>();

        var skills = Path.Combine(AppContext.BaseDirectory, "skills");
        services.RegisterAIProviders(skills);
        services.RegisterAIAgent();

        return services;
    }

    private static void RegisterAIProviders(this IServiceCollection services, string skillsPath)
    {
        // 基础 Provider
        services.AddTransient<AIContextProvider, CommandProvider>();
        services.AddTransient<AIContextProvider, SystemPromptProvider>();
        services.AddTransient<AIContextProvider, NetworkRequestProvider>();
        services.AddTransient<AIContextProvider, ImageGenerationProvider>();
        services.AddTransient<AIContextProvider, WorkspaceFilePolicyProvider>();

        // 技能 Provider
        services.AddTransient<AIContextProvider>(provider => new UserSkillsProvider(skillsPath));

        // Todo Provider（禁用列表消息）
        services.AddTransient<AIContextProvider>(provider => new TodoProvider(new TodoProviderOptions { SuppressTodoListMessage = true }));

        // Agent 模式 Provider
        services.AddTransient<AIContextProvider>(provider => new AgentModeProvider(
            new AgentModeProviderOptions { DefaultMode = AgentModes.DefaultMode, Modes = AgentModes.Modes }
        ));

        // 上下文压缩 Provider
        services.AddTransient<AIContextProvider>(provider => new CompactionProvider(
            new TruncationCompactionStrategy(
                trigger: CompactionTriggers.Any(CompactionTriggers.GroupsExceed(50), CompactionTriggers.TokensExceed(50000)),
                minimumPreservedGroups: 32,
                target: CompactionTriggers.TokensBelow(20000)
            ),
            loggerFactory: provider.GetRequiredService<ILoggerFactory>()
        ));

        // 技能执行 Provider（禁用审批）
        services.AddTransient<AIContextProvider>(provider =>
            new AgentSkillsProviderBuilder()
                .UseOptions(options =>
                {
                    options.DisableLoadSkillApproval = true;
                    options.DisableRunSkillScriptApproval = true;
                    options.DisableReadSkillResourceApproval = true;
                })
                .UseLoggerFactory(provider.GetRequiredService<ILoggerFactory>())
                .UseFileScriptRunner(CliWrapRunner.RunAsync)
                .UseFileSkill(skillsPath)
                .DisableCaching()
                .Build()
        );
    }

    private static void RegisterAIAgent(this IServiceCollection services)
    {
        services.AddChatClient(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ChatClientOptions>>();
            var httpFactory = provider.GetRequiredService<IHttpClientFactory>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

            return ChatClientFactory
                .Create(options.Value, httpFactory.CreateClient(AgentHttpClientName))
                .AsBuilder()
                .UseLogging(loggerFactory)
                .UseFunctionInvocation()
                .UseContinueError()
                .Build();
        });
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
