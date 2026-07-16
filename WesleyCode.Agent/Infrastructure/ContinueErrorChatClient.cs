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
            return new ChatResponse(
                new ChatMessage(
                    ChatRole.System,
                    [
                        new ErrorContent($"发生一个错误: {ex.Message}")
                        {
                            RawRepresentation = ex,
                            Details = ex.StackTrace,
                            ErrorCode = ex.HResult.ToString(),
                        },
                    ]
                )
            );
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        IAsyncEnumerator<ChatResponseUpdate> enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            ChatResponseUpdate update;
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (Exception ex)
                {
                    update = new ChatResponseUpdate(
                        ChatRole.System,
                        [
                            new ErrorContent($"发生一个错误: {ex.Message}")
                            {
                                RawRepresentation = ex,
                                Details = ex.StackTrace,
                                ErrorCode = ex.HResult.ToString(),
                            },
                        ]
                    );
                }

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }
}
