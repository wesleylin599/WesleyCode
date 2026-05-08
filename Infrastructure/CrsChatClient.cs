using Microsoft.Extensions.AI;

namespace WesleyCode.Infrastructure;

public sealed class CrsChatClient : DelegatingChatClient
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="innerClient"></param>
    /// <returns></returns>
    public static CrsChatClient Create(IChatClient innerClient) => new CrsChatClient(innerClient);

    public CrsChatClient(IChatClient innerClient)
        : base(innerClient) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var chatMessages = new List<ChatMessage>();
        try
        {
            var response = await this.GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
            chatMessages.AddRange(response.Messages);
        }
        catch (Exception ex)
        {
            chatMessages.Add(new ChatMessage(ChatRole.Assistant, ex.Message));
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
        List<ChatMessage> request = [new ChatMessage(ChatRole.User, options.Instructions)];
        foreach (var message in messages)
            if (message.Role == ChatRole.System)
                request.Add(new ChatMessage(ChatRole.User, message.Text));
            else
                request.Add(message);
        return base.GetStreamingResponseAsync(request, options, cancellationToken);
    }
}
