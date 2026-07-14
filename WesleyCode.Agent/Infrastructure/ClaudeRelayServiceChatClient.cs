using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace WesleyCode.Agent.Infrastructure;

public sealed class ClaudeRelayServiceChatClient : IChatClient
{
    private readonly IChatClient _responseClient;

    public ClaudeRelayServiceChatClient(string modelId, ApiKeyCredential credential, OpenAIClientOptions options)
    {
        _responseClient = new OpenAIClient(credential, options).GetResponsesClient().AsIChatClient(modelId);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => this.GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        List<ChatMessage> request = [];
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            request.Add(new ChatMessage(ChatRole.User, options.Instructions));
        }
        if (options is { ResponseFormat: ChatResponseFormatJson formatJson } && formatJson.Schema is not null)
        {
            request.Add(
                new ChatMessage(
                    ChatRole.User,
                    $$"""
                    You must respond with valid JSON only.

                    The JSON must conform to this JSON Schema:
                    {{formatJson.Schema}}

                    Rules:
                    - Output JSON only.
                    - Do not include markdown code fences.
                    - Do not include explanations.
                    - Do not include comments.
                    - Do not omit required fields.
                    - Use null only when the schema allows null.
                    """
                )
            );
        }
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System && !string.IsNullOrWhiteSpace(message.Text))
            {
                request.Add(new ChatMessage(ChatRole.User, message.Text));
                continue;
            }
            request.Add(message);
        }

        return _responseClient.GetStreamingResponseAsync(request, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => _responseClient.GetService(serviceType, serviceKey);

    public void Dispose() => _responseClient.Dispose();
}
