using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WesleyCode.Agent.Infrastructure;

namespace WesleyCode.Tests;

/// <summary>
/// <see cref="ContinueErrorChatClient"/> 的单元测试。
/// </summary>
public class ContinueErrorChatClientTests
{
    private sealed class ThrowingChatClient : IChatClient
    {
        private readonly Exception _exception;

        public ThrowingChatClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw _exception;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask;
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            throw _exception;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class SuccessChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public async Task GetResponseAsync_OnException_ReturnsErrorContent()
    {
        var client = new ContinueErrorChatClient(new ThrowingChatClient(new InvalidOperationException("boom")));

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var content = Assert.IsType<ErrorContent>(Assert.Single(response.Messages[0].Contents));
        Assert.Contains("boom", content.Message);
    }

    [Fact]
    public async Task GetResponseAsync_OnSuccess_ReturnsOriginalResponse()
    {
        var client = new ContinueErrorChatClient(new SuccessChatClient());

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("ok", response.Messages[0].Text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_OnException_YieldsErrorContent()
    {
        var client = new ContinueErrorChatClient(new ThrowingChatClient(new InvalidOperationException("stream boom")));

        var updates = new List<ChatResponseUpdate>();
        await foreach (var item in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            updates.Add(item);
        }

        var single = Assert.Single(updates);
        var content = Assert.IsType<ErrorContent>(Assert.Single(single.Contents));
        Assert.Contains("stream boom", content.Message);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_OnSuccess_PassesThroughUpdates()
    {
        var client = new ContinueErrorChatClient(new SuccessChatClient());

        var updates = new List<ChatResponseUpdate>();
        await foreach (var item in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            updates.Add(item);
        }

        var single = Assert.Single(updates);
        Assert.Equal("partial", single.Text);
    }
}
