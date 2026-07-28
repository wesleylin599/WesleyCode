using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
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
        services.AddAIProviders();
        services.AddAIAgent();

        return services;
    }

    private static IServiceCollection AddAIProviders(this IServiceCollection services)
    {
        var skills = Path.Combine(AppContext.BaseDirectory, "skills");

        services.AddTransient<AIContextProvider, CommandProvider>();

        services.AddTransient<AIContextProvider, SystemPromptProvider>();

        services.AddTransient<AIContextProvider, NetworkRequestProvider>();

        services.AddTransient<AIContextProvider, ImageGenerationProvider>();

        services.AddTransient<AIContextProvider, WorkspaceFilePolicyProvider>();

        services.AddTransient<AIContextProvider>(provider => new UserSkillsProvider(skills));

        services.AddTransient<AIContextProvider>(provider => new TodoProvider(new TodoProviderOptions { SuppressTodoListMessage = true }));

        services.AddTransient<AIContextProvider>(provider => new AgentModeProvider(
            new AgentModeProviderOptions
            {
                DefaultMode = "execute",
                Modes =
                [
                    new AgentModeProviderOptions.AgentMode(
                        "plan",
                        """
                        在分析需求、拆解任务和制定计划时，请使用此模式。这是一种交互式模式——在继续下一步之前，你需要提出澄清问题、讨论各种方案并获得用户的批准。

                        处于“计划模式”时应遵循的流程：
                        1. 分析请求，目标是制定研究计划。
                        2. 创建待办事项列表。
                        3. 如有需要，利用现有工具进行初步的探索性检查，以辅助制定计划并确定需要向用户提出的澄清问题。
                        4. 在必要时向用户寻求澄清。  
                         1.逐一提出澄清问题。  
                         2.寻求澄清时，如果你已有具体的方案构想，请将其展示给用户，以便用户直接选择，而无需重新输入完​​整回复。  
                         3.在获得所有必要的澄清信息之前，不要继续进行后续步骤。  
                         4.如果进行简短的探索性研究有助于向用户提出合理的澄清问题，则可以进行此类研究。 
                        5. 将计划写入内存文件，以确保即使发生压缩（compaction）操作，计划也能得以保留。如果用户要求更改，请务必更新该计划文件。
                        6.使用 `mode_set` 工具切换到“execute”  
                        7.遵循“execute”下的步骤实施该计划。 

                        """
                    ),
                    new AgentModeProviderOptions.AgentMode(
                        "execute",
                        """
                        确定请求的类型：
                        1. 无需额外工作即可回答的简单问题。 
                        2. 涉及其他工作的请求，包括需要多步骤流程才能满足的复杂用户需求。 

                        若属于第 1 类：直接回答问题。

                        若属于第 2 类：根据自身判断自主开展工作——无需向用户提问或等待反馈，并遵循以下流程：
                        1. 若尚未制定计划或任务，请分析用户请求，并创建相应的任务与计划。（**若已处于“计划模式”，则跳过此步骤**）
                        2. 自主工作——凭借自身判断做出决策并持续推进，无需向用户提问。目标是在用户返回时，已准备好完整且有用的结果。 
                        3. 若在执行过程中遇到歧义或意外情况，请选择最合理的方案，记录下您的选择，然后继续进行。 
                        4. 任务完成后，将其标记为“已完成”。 
                        5. 持续工作、思考并调用工具，直至得出可交付给用户的研究结果。
                        6. 使用 `mode_set` 工具切换到“review”  
                        7. 遵循“review”下的步骤审查执行结果。 

                        """
                    ),
                    new AgentModeProviderOptions.AgentMode(
                        "review",
                        """
                        审查模式：用于检查任务执行结果、代码、方案或文档质量。
                        - 验证执行结果是否满足用户目标。
                        - 发现错误、遗漏、风险和改进空间。
                        - 不直接修改结果，只提供审查意见。

                        工作流程：

                        1. 理解原始需求和执行结果。
                        2. 检查：
                            - 正确性
                            - 完整性
                            - 可维护性
                            - 性能与安全风险
                        3. 输出审查报告。

                        输出格式：

                            ## 审查结果
                            总体评价。

                            ## 发现的问题
                            - 严重问题：
                            - 一般问题：
                            - 优化建议：

                        如果没有发现明显问题，说明：
                        “审查通过，可以结束任务。”

                        如果发现关键问题，说明：
                        “需要返回 execute 模式修正。”

                        """
                    ),
                ],
            }
        ));

        services.AddTransient<AIContextProvider>(provider => new CompactionProvider(
            new TruncationCompactionStrategy(
                trigger: CompactionTriggers.Any(CompactionTriggers.GroupsExceed(50), CompactionTriggers.TokensExceed(50000)),
                minimumPreservedGroups: 32,
                target: CompactionTriggers.TokensBelow(20000)
            ),
            loggerFactory: provider.GetRequiredService<ILoggerFactory>()
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
