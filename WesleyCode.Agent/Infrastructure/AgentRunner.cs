using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Infrastructure;

internal class AgentRunner : IAgentRunner
{
    private readonly AIAgent _agent;
    private readonly IOutputCapture _capture;

    public AgentRunner(IChatClient client, IOutputCapture capture, IEnumerable<AIContextProvider> providers, IOptions<AgentOptions> options)
    {
        this._agent = client
            .AsAIAgent(BuildChatClientAgentOptions(options, providers))
            .AsBuilder()
            .UseToolApproval(BuildToolApprovalAgentOptions())
            .Use(BuildLoopAgent)
            .Build();
        this._capture = capture;
    }

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default) => _agent.CreateSessionAsync(cancellationToken);

    public ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken = default) =>
        _agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);

    public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement serializedState, CancellationToken cancellationToken = default) =>
        _agent.DeserializeSessionAsync(serializedState, cancellationToken: cancellationToken);

    public async Task<AgentResponse> ExecuteAsync(List<ChatMessage> input, AgentSession session, CancellationToken cancellationToken = default)
    {
        bool currentMove = true;
        StringBuilder currentBuilder = new();
        List<AgentResponseUpdate> agentResponses = new();
        await using IAsyncEnumerator<AgentResponseUpdate> enumerator = _agent
            .RunStreamingAsync(input, session, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (currentMove)
        {
            currentMove = await enumerator.MoveNextAsync();
            AgentResponseUpdate responseUpdate = currentMove switch
            {
                false => new(ChatRole.Assistant, [new StopContent()]),
                true => enumerator.Current,
            };
            foreach (var content in responseUpdate.Contents)
            {
                if (content is TextContent textContent)
                {
                    currentBuilder.Append(textContent.Text);
                }
                else if (currentBuilder.Length > 0)
                {
                    _capture.WriteAgentMessage(currentBuilder.ToString());
                    currentBuilder.Clear();
                }
                _capture.CommonWriteMessage(responseUpdate.AuthorName, content);
            }
            agentResponses.Add(responseUpdate);
        }
        return agentResponses.ToAgentResponse();
    }

    public Task RestartSessionAsync(AgentSession activeSession, CancellationToken cancellationToken = default)
    {
        if (activeSession.TryGetInMemoryChatHistory(out var history) && history != null)
        {
            foreach (var message in history)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (message.Role == ChatRole.User && !string.IsNullOrEmpty(message.Text))
                {
                    _capture.WriteUserMessage(message.Text);
                }
                else if (message.Role == ChatRole.System && !string.IsNullOrEmpty(message.Text))
                {
                    _capture.WriteSystemMessage(message.Text);
                }
                else
                {
                    foreach (var content in message.Contents)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                        {
                            _capture.WriteAgentMessage(textContent.Text);
                        }

                        _capture.CommonWriteMessage(message.AuthorName, content);
                    }
                }
            }
        }
        return Task.CompletedTask;
    }

    private static ChatClientAgentOptions BuildChatClientAgentOptions(IOptions<AgentOptions> options, IEnumerable<AIContextProvider> providers) =>
        new ChatClientAgentOptions
        {
            Name = options.Value.Name,
            Description = options.Value.Description,
            ChatOptions = new ChatOptions { Instructions = options.Value.Instructions },
            AIContextProviders = providers,
        };

    private static ToolApprovalAgentOptions BuildToolApprovalAgentOptions() =>
        new ToolApprovalAgentOptions() { AutoApprovalRules = [context => ValueTask.FromResult(true)] };

    private static LoopAgent BuildLoopAgent(AIAgent innerAgent) =>
        new LoopAgent(
            innerAgent,
            [new NonEmptyLoopEvaluator()],
            new LoopAgentOptions { OnBehalfOfAuthorName = "loop", ExcludeOnBehalfOfMessages = true }
        );

    private sealed class StopContent() : AIContent;

    private sealed class NonEmptyLoopEvaluator : LoopEvaluator
    {
        public override ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
        {
            if (context.LastResponse.Messages.LastOrDefault()?.Contents.LastOrDefault() is not TextContent textConten)
            {
                return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("继续处理请求,完成任务后回复文本消息。"));
            }

            if (string.IsNullOrEmpty(textConten.Text))
            {
                return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("继续处理请求,完成任务后回复文本消息不能为空。"));
            }

            return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
        }
    }
}
