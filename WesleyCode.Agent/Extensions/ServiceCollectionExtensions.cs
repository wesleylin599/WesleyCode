using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
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
        services.AddAIProviders();
        services.AddAIAgent();

        return services;
    }

    private static IServiceCollection AddAIProviders(this IServiceCollection services)
    {
        var skills = Path.Combine(AppContext.BaseDirectory, "skills");

        services.AddTransient<AIContextProvider, CommandProvider>();

        services.AddTransient<AIContextProvider, AgentModeProvider>();

        services.AddTransient<AIContextProvider, SystemPromptProvider>();

        services.AddTransient<AIContextProvider, NetworkRequestProvider>();

        services.AddTransient<AIContextProvider, WorkspaceFilePolicyProvider>();

        services.AddTransient<AIContextProvider>(provider => new UserSkillsProvider(skills));

        services.AddTransient<AIContextProvider>(provider => new TodoProvider(new TodoProviderOptions { SuppressTodoListMessage = true }));

        services.AddTransient<AIContextProvider>(provider => new FileAccessProvider(
            new FileSystemAgentFileStore(Path.Combine(AppContext.BaseDirectory, "shared")),
            new FileAccessProviderOptions { DisableReadOnlyToolApproval = true, DisableWriteToolApproval = true }
        ));

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
                .UseFileSkill(skills)
                .DisableCaching()
                .Build()
        );

        return services;
    }

    private static IServiceCollection AddAIAgent(this IServiceCollection services)
    {
        services.AddChatClient(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ChatClientOptions>>();
            var httpFactory = provider.GetRequiredService<IHttpClientFactory>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return CreateChatClient(options.Value, httpFactory)
                .AsBuilder()
                .UseLogging(loggerFactory)
                .UseFunctionInvocation()
                .UseContinueError()
                .Build();
        });

        return services;
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

    private static IChatClient CreateChatClient(ChatClientOptions options, IHttpClientFactory httpClientFactory)
    {
        return options.Provider switch
        {
            "anthropic" => CreateAnthropicChatClient(options, httpClientFactory),
            "ollama" => CreateOllamaChatClient(options, httpClientFactory),
            "openai" => CreateOpenAiChatClient(options, httpClientFactory),
            "crs" => CreateCrsChatClient(options, httpClientFactory),
            _ => throw new InvalidOperationException($"不支持的 IChatClient Provider: {options.Provider}。可选值: openai、anthropic、crs、ollama。"),
        };
    }

    private static IChatClient CreateOpenAiChatClient(ChatClientOptions options, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new InvalidOperationException("未配置 Model Id，请设置 WESLEY_MODELID。");
        }
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WESLEY_APIKEY。");
        }
        var httpClient = httpClientFactory.CreateClient(AgentHttpClientName);
        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = Timeout.InfiniteTimeSpan,
            Transport = new HttpClientPipelineTransport(httpClient),
        };
        var endpoint = GetEndpoint(options.BaseUrl);
        if (endpoint is not null)
        {
            clientOptions.Endpoint = endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions).GetResponsesClient().AsIChatClient(options.ModelId);
    }

    private static IChatClient CreateAnthropicChatClient(ChatClientOptions options, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WESLEY_APIKEY。");
        }
        var endpoint = GetEndpoint(options.BaseUrl);
        var httpClient = httpClientFactory.CreateClient(AgentHttpClientName);
        var client = endpoint is null
            ? new AnthropicClient { ApiKey = options.ApiKey, HttpClient = httpClient }
            : new AnthropicClient
            {
                ApiKey = options.ApiKey,
                BaseUrl = endpoint.ToString().TrimEnd('/'),
                HttpClient = httpClient,
            };

        return client.AsIChatClient(options.ModelId);
    }

    private static OllamaApiClient CreateOllamaChatClient(ChatClientOptions options, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new InvalidOperationException("未配置 Model Id，请设置 WESLEY_MODELID。");
        }
        var endpoint = GetEndpoint(options.BaseUrl) ?? throw new InvalidOperationException("未配置 BaseUrl，请设置 WESLEY_BASEURL。");
        var httpClient = httpClientFactory.CreateClient(AgentHttpClientName);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {options.ApiKey}");
        httpClient.BaseAddress = endpoint;
        return new OllamaApiClient(httpClient, options.ModelId);
    }

    private static ClaudeRelayServiceChatClient CreateCrsChatClient(ChatClientOptions options, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new InvalidOperationException("未配置 Model Id，请设置 WESLEY_MODELID。");
        }
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WESLEY_APIKEY。");
        }
        var httpClient = httpClientFactory.CreateClient(AgentHttpClientName);
        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = Timeout.InfiniteTimeSpan,
            Transport = new HttpClientPipelineTransport(httpClient),
        };
        var endpoint = GetEndpoint(options.BaseUrl);
        if (endpoint is not null)
        {
            clientOptions.Endpoint = endpoint;
        }

        return new ClaudeRelayServiceChatClient(options.ModelId, new ApiKeyCredential(options.ApiKey), clientOptions);
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
