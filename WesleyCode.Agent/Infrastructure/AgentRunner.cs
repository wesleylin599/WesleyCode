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

    public AgentRunner(IChatClient client, IOutputCapture capture, IOptions<ChatClientOptions> options, IEnumerable<AIContextProvider> providers)
    {
        this._agent = client
            .AsAIAgent(BuildChatClientAgentOptions(options.Value, providers))
            .AsBuilder()
            .UseToolApproval(BuildToolApprovalAgentOptions())
            .UseAgentLoop(options.Value.StopMark)
            .UseAgentOutput(capture)
            .Build();
        this._capture = capture;
    }

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default) => _agent.CreateSessionAsync(cancellationToken);

    public ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken = default) =>
        _agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);

    public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement serializedState, CancellationToken cancellationToken = default) =>
        _agent.DeserializeSessionAsync(serializedState, cancellationToken: cancellationToken);

    public Task<AgentResponse> ExecuteAsync(List<ChatMessage> input, AgentSession session, CancellationToken cancellationToken = default) =>
        _agent
            .RunStreamingAsync(input.Select(x => x.WithMessageId(Guid.NewGuid().ToString())), session, cancellationToken: cancellationToken)
            .ToAgentResponseAsync(cancellationToken);

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

    private static ChatClientAgentOptions BuildChatClientAgentOptions(ChatClientOptions options, IEnumerable<AIContextProvider> providers) =>
        new ChatClientAgentOptions
        {
            Name = "main",
            ChatOptions = new ChatOptions
            {
                Instructions = $"""
                    每次准备结束当前回复时，必须在回复中输出标记 `{options.StopMark}`。

                    以下情况都必须输出该标记：
                    - 已完成用户请求；
                    - 需要用户提供额外信息；
                    - 需要用户确认后才能继续；
                    - 当前无法继续，需要用户采取行动。

                    只有在仍然可以通过工具或内部推理继续处理任务时，才不要输出该标记。

                    """,
                AllowMultipleToolCalls = options.AllowMultipleToolCalls,
                AllowBackgroundResponses = options.AllowBackgroundResponses,
                Reasoning = new ReasoningOptions { Effort = options.Effort, Output = options.Output },
                MaxOutputTokens = options.MaxOutputTokens,
                StopSequences = [options.StopMark],
            },
            AIContextProviders = providers,
            ChatHistoryProvider = new InMemoryChatHistoryProvider(
                new InMemoryChatHistoryProviderOptions
                {
                    StorageInputRequestMessageFilter = static messages =>
                        messages.Where(m =>
                            m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory
                            && m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider
                        ),
                    StorageInputResponseMessageFilter = static messages => messages,
                }
            ),
        };

    private static ToolApprovalAgentOptions BuildToolApprovalAgentOptions() =>
        new ToolApprovalAgentOptions() { AutoApprovalRules = [static context => ValueTask.FromResult(true)] };
}
