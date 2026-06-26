using System.ClientModel;
using System.ClientModel.Primitives;
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
    private const string AgentHttpClientName = "Wesley";

    public static bool HasToolContent(this IList<AIContent> contents) =>
        contents.Any(static content => content is FunctionCallContent or FunctionResultContent);

    public static IHttpClientBuilder ConfigureHttpClientAgents(this IServiceCollection services, Action<HttpClient> configureClient)
    {
        return services.AddHttpClient(AgentHttpClientName).ConfigureHttpClient(configureClient);
    }

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
                config.Description = "全自动智能体";
                config.Instructions = """
                调用工具高效完成用户需求
                行动优先不要过多询问
                完成后进行校验
                """;
            });
        services
            .AddOptions<CacheOptions>()
            .Configure(config =>
            {
                config.SizeLimit = 1024 * 1024;
            });
        services
            .AddOptions<CompactionOptions>()
            .Configure(config =>
            {
                config.ToolResultMessageLimit = 64;
                config.SlidingWindowTurnLimit = 10;
                config.TruncationGroupsLimit = 12;
            });
        services
            .AddOptions<SessionOptions>()
            .Configure(config =>
            {
                config.DirectoryName = "session";
            });

        services.AddSingleton<IDistributedCache>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
            var cacheOptions = new MemoryDistributedCacheOptions { SizeLimit = options.SizeLimit };
            return new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(cacheOptions));
        });

        services.TryAddSingleton<IOutputCapture, NullOutputCapture>();
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<IAgentRunner, AgentRunner>();
        services.AddAIProviders();
        services.AddAIAgent();

        return services;
    }

    private static IServiceCollection AddAIProviders(this IServiceCollection services)
    {
        services.AddSingleton<AIContextProvider, CommandProvider>();

        services.AddSingleton<AIContextProvider, AgentModeProvider>();

        services.AddSingleton<AIContextProvider, WorkspaceFilePolicyProvider>();

        services.AddSingleton<AIContextProvider>(provider => new TodoProvider(new TodoProviderOptions { SuppressTodoListMessage = true }));

        services.AddSingleton<AIContextProvider>(provider => new FileAccessProvider(
            new FileSystemAgentFileStore(
                Path.Combine(AppContext.BaseDirectory, "shared", provider.GetRequiredService<IOptions<WorkingOptions>>().Value.BasePath.ComputeMd5())
            )
        ));

        services.AddSingleton<AIContextProvider>(provider => new FileMemoryProvider(
            new FileSystemAgentFileStore(
                Path.Combine(AppContext.BaseDirectory, "cache", provider.GetRequiredService<IOptions<WorkingOptions>>().Value.BasePath.ComputeMd5())
            )
        ));

        services.AddSingleton<AIContextProvider>(provider => new AgentSkillsProvider(
            skillPaths:
            [
                Path.Combine(provider.GetRequiredService<IOptions<WorkingOptions>>().Value.BasePath, "skills"),
                Path.Combine(AppContext.BaseDirectory, "skills"),
            ],
            loggerFactory: provider.GetRequiredService<ILoggerFactory>()
        ));

        services.AddSingleton<AIContextProvider>(provider => new SystemPromptProvider(
            provider.GetRequiredService<IOptions<WorkingOptions>>().Value.BasePath,
            provider.GetRequiredService<ILoggerFactory>()
        ));

        services.AddSingleton<AIContextProvider>(provider =>
        {
            var compactionOptions = provider.GetRequiredService<IOptions<CompactionOptions>>().Value;
            var pipeline = new PipelineCompactionStrategy(
                new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(compactionOptions.SlidingWindowTurnLimit)),
                new ToolResultCompactionStrategy(CompactionTriggers.MessagesExceed(compactionOptions.ToolResultMessageLimit)),
                new TruncationCompactionStrategy(CompactionTriggers.GroupsExceed(compactionOptions.TruncationGroupsLimit))
            );
            return new CompactionProvider(pipeline, loggerFactory: provider.GetRequiredService<ILoggerFactory>());
        });

        return services;
    }

    private static IServiceCollection AddAIAgent(this IServiceCollection services)
    {
        services.AddChatClient(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptions<ChatClientOptions>>();
            var working = provider.GetRequiredService<IOptions<WorkingOptions>>();

            var client = CreateChatClient(options.Value, loggerFactory, provider.GetRequiredService<IHttpClientFactory>());
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Provider:{options.Value.Provider}");
            if (!string.IsNullOrWhiteSpace(options.Value.BaseUrl))
            {
                builder.AppendLine($"BaseUrl:{options.Value.BaseUrl}");
            }
            builder.AppendLine($"ModelId:{options.Value.ModelId}");
            builder.AppendLine($"Working:{working.Value.BasePath}");
            provider.GetRequiredService<IOutputCapture>().WriteSystemMessage(builder.ToString());

            return client
                .AsBuilder()
                .UseFunctionInvocation()
                .UseDistributedCache(provider.GetRequiredService<IDistributedCache>())
                .UseLogging(loggerFactory)
                .Build();
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
                        Description = options.Value.Description,
                        ChatOptions = new ChatOptions { Instructions = options.Value.Instructions },
                        AIContextProviders = provider.GetServices<AIContextProvider>(),
                    }
                );
        });
        return services;
    }

    private static IChatClient CreateChatClient(ChatClientOptions options, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory)
    {
        return options.Provider switch
        {
            "anthropic" => CreateAnthropicChatClient(options, httpClientFactory),
            "openai" => CreateOpenAiChatClient(options, loggerFactory, httpClientFactory),
            "crs" => CreateCrsChatClient(options, loggerFactory, httpClientFactory),
            "ollama" => CreateOllamaChatClient(options, httpClientFactory),
            _ => throw new InvalidOperationException($"不支持的 IChatClient Provider: {options.Provider}。可选值: openai、anthropic、crs、ollama。"),
        };
    }

    private static IChatClient CreateOpenAiChatClient(ChatClientOptions options, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WINTEAM_APIKEY。");
        }
        var httpClient = httpClientFactory.CreateClient(AgentHttpClientName);
        var clientOptions = new OpenAIClientOptions
        {
            MessageLoggingPolicy = new LoggingAuthPolicy(false, true, loggerFactory),
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

    private static IChatClient CreateCrsChatClient(ChatClientOptions options, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WINTEAM_APIKEY。");
        }
        var baseClient = CreateOpenAiChatClient(options, loggerFactory, httpClientFactory);
        return CrsChatClient.Create(baseClient);
    }

    private static IChatClient CreateAnthropicChatClient(ChatClientOptions options, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("未配置 API Key，请设置 WINTEAM_APIKEY。");
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

    private static IChatClient CreateOllamaChatClient(ChatClientOptions options, IHttpClientFactory httpClientFactory)
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
        var httpClient = httpClientFactory.CreateClient(AgentHttpClientName);
        httpClient.BaseAddress = endpoint;
        return new OllamaApiClient(httpClient, options.ModelId);
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
