using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Infrastructure;

public class ContinueErrorChatClient : DelegatingChatClient
{
    public ContinueErrorChatClient(IChatClient innerClient)
        : base(innerClient) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, [new ErrorContent($"发生一个错误: {ex.Message}")]));
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await using IAsyncEnumerator<ChatResponseUpdate> enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        IList<AIContent> contents = [];
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                contents = enumerator.Current.Contents;
            }
            catch (Exception ex)
            {
                contents = [new ErrorContent($"发生一个错误: {ex.GetBaseException().Message}")];
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, contents);
        }
    }
}
