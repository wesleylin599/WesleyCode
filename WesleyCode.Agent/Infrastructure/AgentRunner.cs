using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Interfaces;

namespace WesleyCode.Agent.Infrastructure;

internal class AgentRunner : IAgentRunner
{
    private readonly AIAgent _agent;
    private readonly IOutputCapture _capture;

    public AgentRunner(IChatClient client, IOutputCapture capture, IOptions<ChatOptions> options, IEnumerable<AIContextProvider> providers)
    {
        this._agent = client
            .AsAIAgent(BuildChatClientAgentOptions(options, providers))
            .AsBuilder()
            .UseToolApproval(BuildToolApprovalAgentOptions())
            .UseOutput(capture)
            .UseLoop()
            .Build();
        this._capture = capture;
    }

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default) => _agent.CreateSessionAsync(cancellationToken);

    public ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken = default) =>
        _agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);

    public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement serializedState, CancellationToken cancellationToken = default) =>
        _agent.DeserializeSessionAsync(serializedState, cancellationToken: cancellationToken);

    public Task<AgentResponse> ExecuteAsync(List<ChatMessage> input, AgentSession session, CancellationToken cancellationToken = default) =>
        _agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken).ToAgentResponseAsync(cancellationToken);

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

    private static ChatClientAgentOptions BuildChatClientAgentOptions(IOptions<ChatOptions> options, IEnumerable<AIContextProvider> providers) =>
        new ChatClientAgentOptions
        {
            Name = "main",
            ChatOptions = options.Value,
            AIContextProviders = providers,
            ChatHistoryProvider = new InMemoryChatHistoryProvider(
                new InMemoryChatHistoryProviderOptions
                {
                    StorageInputRequestMessageFilter = message => message,
                    StorageInputResponseMessageFilter = message => message,
                }
            ),
        };

    private static ToolApprovalAgentOptions BuildToolApprovalAgentOptions() =>
        new ToolApprovalAgentOptions() { AutoApprovalRules = [context => ValueTask.FromResult(true)] };
}
