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

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => base.GetResponseAsync(messages, UseDefaultModel(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => base.GetStreamingResponseAsync(messages, UseDefaultModel(options), cancellationToken);

    private ChatOptions UseDefaultModel(ChatOptions? options)
    {
        var requestOptions = options?.Clone() ?? new ChatOptions();

        if (string.IsNullOrWhiteSpace(requestOptions.ModelId))
        {
            requestOptions.ModelId = _modelId;
        }

        return requestOptions;
    }
}
