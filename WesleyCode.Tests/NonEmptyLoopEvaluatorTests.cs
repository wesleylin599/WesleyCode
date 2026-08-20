using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WesleyCode.Agent.Infrastructure;

namespace WesleyCode.Tests;

/// <summary>
/// <see cref="NonEmptyLoopEvaluator"/> 的单元测试。
/// </summary>
public class NonEmptyLoopEvaluatorTests
{
    private sealed class TestAgent : AIAgent
    {
        private readonly AgentSession session = new TestAgentSession();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(session);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(session);

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new AgentResponse());

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(300, cancellationToken);
            yield return new AgentResponseUpdate();
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(JsonSerializer.SerializeToElement(session));
    }

    private sealed class TestAgentSession : AgentSession { }

    private static AIAgent agent = new TestAgent();

    private static LoopContext CreateContext(AgentResponse response, AgentSession session) =>
        new(agent: agent, session: session, initialMessages: [], lastResponse: response, runOptions: null);

    private static AgentResponse CreateResponse(params ChatMessage[] messages) => new(messages);

    private const string CompletionMarker = "[EOF]";

    [Fact]
    public async Task EvaluateAsync_EmptyMessages_ContinuesWithFeedback()
    {
        var evaluator = new NonEmptyLoopEvaluator(CompletionMarker);
        var session = await agent.CreateSessionAsync();
        var context = CreateContext(CreateResponse(), session);

        var result = await evaluator.EvaluateAsync(context);

        Assert.True(result.ShouldReinvoke);
    }

    [Fact]
    public async Task EvaluateAsync_NoAssistantMessage_ContinuesWithFeedback()
    {
        var evaluator = new NonEmptyLoopEvaluator(CompletionMarker);
        var userMessage = new ChatMessage(ChatRole.User, "hello");
        var session = await agent.CreateSessionAsync();
        var context = CreateContext(CreateResponse(userMessage), session);

        var result = await evaluator.EvaluateAsync(context);

        Assert.True(result.ShouldReinvoke);
    }

    [Fact]
    public async Task EvaluateAsync_ErrorContent_Stops()
    {
        var evaluator = new NonEmptyLoopEvaluator(CompletionMarker);
        var assistantMessage = new ChatMessage(ChatRole.Assistant, [new ErrorContent("some error")]);
        var session = await agent.CreateSessionAsync();
        var context = CreateContext(CreateResponse(assistantMessage), session);

        var result = await evaluator.EvaluateAsync(context);

        Assert.False(result.ShouldReinvoke);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyText_ContinuesWithFeedback()
    {
        var evaluator = new NonEmptyLoopEvaluator(CompletionMarker);
        var assistantMessage = new ChatMessage(ChatRole.Assistant, "");
        var session = await agent.CreateSessionAsync();
        var context = CreateContext(CreateResponse(assistantMessage), session);

        var result = await evaluator.EvaluateAsync(context);

        Assert.True(result.ShouldReinvoke);
    }

    [Fact]
    public async Task EvaluateAsync_NoCompletionMarker_ContinuesWithFeedback()
    {
        var evaluator = new NonEmptyLoopEvaluator(CompletionMarker);
        var assistantMessage = new ChatMessage(ChatRole.Assistant, "这是回复但没完成标记");
        var session = await agent.CreateSessionAsync();
        var context = CreateContext(CreateResponse(assistantMessage), session);

        var result = await evaluator.EvaluateAsync(context);

        Assert.True(result.ShouldReinvoke);
    }

    [Fact]
    public async Task EvaluateAsync_WithCompletionMarker_Stops()
    {
        var evaluator = new NonEmptyLoopEvaluator(CompletionMarker);
        var assistantMessage = new ChatMessage(ChatRole.Assistant, $"任务已完成 {CompletionMarker}");
        var session = await agent.CreateSessionAsync();
        var context = CreateContext(CreateResponse(assistantMessage), session);

        var result = await evaluator.EvaluateAsync(context);

        Assert.False(result.ShouldReinvoke);
    }
}
