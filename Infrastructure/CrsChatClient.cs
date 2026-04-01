using Microsoft.Extensions.AI;

namespace Microsoft.Extensions.AI;

public sealed class CrsChatClient : DelegatingChatClient
{
    public static CrsChatClient Create(IChatClient innerClient) => new CrsChatClient(innerClient);

    private CrsChatClient(IChatClient innerClient)
        : base(innerClient) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        List<ChatMessage> chatMessages = new List<ChatMessage>();
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var response = await this.GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
                chatMessages.AddRange(response.Messages);
                break;
            }
            catch (OperationCanceledException ex)
            {
                chatMessages.Add(new ChatMessage(ChatRole.Assistant, ex.Message));
                break;
            }
            catch (Exception ex)
            {
                chatMessages.Add(new ChatMessage(ChatRole.Assistant, ex.Message));
            }
        }
        return new ChatResponse(chatMessages);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new ChatOptions();
        List<ChatMessage> request =
        [
            new ChatMessage(
                ChatRole.User,
                $"""
                <instructions>
                {options.Instructions}
                </instructions>
                """
            ),
        ];
        foreach (var message in messages)
            if (message.Role == ChatRole.System)
                request.Add(new ChatMessage(ChatRole.User, $"<system>{message.Text}</system>"));
            else
                request.Add(message);
        return base.GetStreamingResponseAsync(request, options, cancellationToken);
    }
}
