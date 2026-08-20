using System.Runtime.CompilerServices;
using DeepSeek.Core;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Infrastructure;

internal sealed class DefaultModelIdChatClient : DelegatingChatClient
{
    private readonly string _modelId;

    public DefaultModelIdChatClient(IChatClient innerClient, string modelId)
        : base(innerClient)
    {
        _modelId = modelId;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var response = await base.GetResponseAsync(messages, UseDefaultModel(options), cancellationToken);
        Throw();
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var item in base.GetStreamingResponseAsync(messages, UseDefaultModel(options), cancellationToken))
        {
            Throw();
            yield return item;
        }
        Throw();
    }

    private ChatOptions UseDefaultModel(ChatOptions? options)
    {
        var requestOptions = options?.Clone() ?? new ChatOptions();

        if (string.IsNullOrWhiteSpace(requestOptions.ModelId))
        {
            requestOptions.ModelId = _modelId;
        }

        return requestOptions;
    }

    private void Throw()
    {
        if (GetService(typeof(DeepSeekClient)) is DeepSeekClient deepSeek && !string.IsNullOrEmpty(deepSeek.ErrorMsg))
        {
            throw new Exception(deepSeek.ErrorMsg);
        }
    }
}
