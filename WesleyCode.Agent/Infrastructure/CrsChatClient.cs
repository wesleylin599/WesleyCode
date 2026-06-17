using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Infrastructure;

[DebuggerStepThrough]
public sealed class CrsChatClient : DelegatingChatClient
{
    public static CrsChatClient Create(IChatClient innerClient) => new(innerClient);

    public CrsChatClient(IChatClient innerClient)
        : base(innerClient) { }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => this.GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
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

        return base.GetStreamingResponseAsync(request, options, cancellationToken);
    }
}
