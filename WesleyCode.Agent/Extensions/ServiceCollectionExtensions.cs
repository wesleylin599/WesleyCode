using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using UtfUnknown;
using WesleyCode.Agent.Infrastructure;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;
using WesleyCode.Agent.Services;

namespace WesleyCode.Agent.Extensions;

public static class ServiceCollectionExtensions
{
    private const string AgentHttpClientName = "Wesley";

    public static string DecodeOutput(this MemoryStream stream)
    {
        var bytes = stream.ToArray();

        if (bytes.Length == 0)
            return string.Empty;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var result = CharsetDetector.DetectFromBytes(bytes);

        var encoding = Encoding.GetEncoding(result.Detected?.EncodingName ?? "UTF-8");

        return encoding.GetString(bytes).TrimEnd();
    }

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
                    options.Provider = configuration.GetValue<string>("WESLEY_PROVIDER");
                    options.ModelId = configuration.GetValue<string>("WESLEY_MODELID");
                    options.BaseUrl = configuration.GetValue<string>("WESLEY_BASEURL");
                    options.ApiKey = configuration.GetValue<string>("WESLEY_APIKEY");
                }
            );
        services
            .AddOptions<SessionOptions>()
            .Configure(config =>
            {
                config.DirectoryName = "session";
            });

        services.AddOptions<ChatOptions>().BindConfiguration("ChatClient");

        services.AddTransient<ISessionStore, SessionStore>();
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

        // Todo Provider（禁用列表消息）
        services.AddTransient<AIContextProvider>(provider => new TodoProvider(new TodoProviderOptions { SuppressTodoListMessage = true }));

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

        // 上下文压缩 Provider
        services.AddTransient<AIContextProvider>(provider => new CompactionProvider(
            new TruncationCompactionStrategy(
                trigger: CompactionTriggers.Any(CompactionTriggers.GroupsExceed(50), CompactionTriggers.TokensExceed(50000)),
                minimumPreservedGroups: 32,
                target: CompactionTriggers.TokensBelow(20000)
            )
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
