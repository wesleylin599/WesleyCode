using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using WesleyCode.Agent.Infrastructure;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Extensions;

internal static class ChatClientFactory
{
    public static IChatClient Create(ChatClientOptions options, HttpClient httpClient)
    {
        return options.Provider switch
        {
            "anthropic" => CreateAnthropicChatClient(options, httpClient),
            "ollama" => CreateOllamaChatClient(options, httpClient),
            "openai" => CreateOpenAiChatClient(options, httpClient),
            "crs" => CreateCrsChatClient(options, httpClient),
            _ => throw new InvalidOperationException($"不支持的 IChatClient Provider: {options.Provider}。可选值: openai、anthropic、crs、ollama。"),
        };
    }

    private static IChatClient CreateOpenAiChatClient(ChatClientOptions options, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
            throw new InvalidOperationException("未配置 Model Id，请设置 WESLEY_MODELID。");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("未配置 API Key，请设置 WESLEY_APIKEY。");

        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = Timeout.InfiniteTimeSpan,
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        if (GetEndpoint(options.BaseUrl) is Uri endpoint)
            clientOptions.Endpoint = endpoint;

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions).GetResponsesClient().AsIChatClient(options.ModelId);
    }

    private static IChatClient CreateAnthropicChatClient(ChatClientOptions options, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("未配置 API Key，请设置 WESLEY_APIKEY。");

        var endpoint = GetEndpoint(options.BaseUrl);

        IAnthropicClient client;
        if (endpoint is null)
        {
            client = new AnthropicClient { ApiKey = options.ApiKey, HttpClient = httpClient };
        }
        else
        {
            client = new AnthropicClient
            {
                ApiKey = options.ApiKey,
                BaseUrl = endpoint.ToString().TrimEnd('/'),
                HttpClient = httpClient,
            };
        }

        return client.AsIChatClient(options.ModelId);
    }

    private static OllamaApiClient CreateOllamaChatClient(ChatClientOptions options, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
            throw new InvalidOperationException("未配置 Model Id，请设置 WESLEY_MODELID。");

        var endpoint = GetEndpoint(options.BaseUrl) ?? throw new InvalidOperationException("未配置 BaseUrl，请设置 WESLEY_BASEURL。");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {options.ApiKey}");
        httpClient.BaseAddress = endpoint;

        return new OllamaApiClient(httpClient, options.ModelId);
    }

    private static ClaudeRelayServiceChatClient CreateCrsChatClient(ChatClientOptions options, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
            throw new InvalidOperationException("未配置 Model Id，请设置 WESLEY_MODELID。");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("未配置 API Key，请设置 WESLEY_APIKEY。");

        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = Timeout.InfiniteTimeSpan,
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        if (GetEndpoint(options.BaseUrl) is Uri endpoint)
            clientOptions.Endpoint = endpoint;

        return new ClaudeRelayServiceChatClient(options.ModelId, new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    private static Uri? GetEndpoint(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException($"BaseUrl 配置无效: {baseUrl}");

        return endpoint;
    }
}
