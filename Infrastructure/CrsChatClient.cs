using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace WesleyCode.Infrastructure;

[DebuggerStepThrough]
public sealed class CrsChatClient : DelegatingChatClient
{
    public static CrsChatClient Create(IChatClient innerClient) => new CrsChatClient(innerClient);

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
        options ??= new ChatOptions();
        List<ChatMessage> request = [];
        if (!string.IsNullOrWhiteSpace(options.Instructions))
        {
            request.Add(new ChatMessage(ChatRole.User, options.Instructions));
        }

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    request.Add(new ChatMessage(ChatRole.User, message.Text));
                }

                continue;
            }

            request.Add(message);
        }

        return base.GetStreamingResponseAsync(request, options, cancellationToken);
    }
}
