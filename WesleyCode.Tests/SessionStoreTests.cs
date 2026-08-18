using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Infrastructure;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;

namespace WesleyCode.Tests;

public class SessionStoreTests
{
    private sealed class TestAgentSession : AgentSession { }

    private sealed class FakeAgentRunner : IAgentRunner
    {
        public AgentSession CreatedSession { get; } = new TestAgentSession();
        public int CreateSessionCallCount { get; private set; }

        public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
        {
            CreateSessionCallCount++;
            return ValueTask.FromResult(CreatedSession);
        }

        public ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement("{}"));

        public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement serializedState, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreatedSession);

        public Task<AgentResponse> ExecuteAsync(List<ChatMessage> input, AgentSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentResponse([]));

        public Task RestartSessionAsync(AgentSession activeSession, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static SessionStore CreateStore(FakeAgentRunner runner, string basePath = "C:\\work")
    {
        var working = Options.Create(new WorkingOptions { BasePath = basePath });
        var sessionOptions = Options.Create(new SessionOptions { DirectoryName = "session-test-" + Guid.NewGuid().ToString("N") });
        return new SessionStore(runner, working, sessionOptions, NullLogger<SessionStore>.Instance);
    }

    [Fact]
    public async Task LoadAsync_WhenNoFileExists_CreatesNewSession()
    {
        var runner = new FakeAgentRunner();
        var store = CreateStore(runner);

        var session = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal(1, runner.CreateSessionCallCount);
    }

    [Fact]
    public async Task ClearAsync_WhenNoFileExists_DoesNotThrow()
    {
        var runner = new FakeAgentRunner();
        var store = CreateStore(runner);

        await store.ClearAsync(CancellationToken.None);
    }
}
