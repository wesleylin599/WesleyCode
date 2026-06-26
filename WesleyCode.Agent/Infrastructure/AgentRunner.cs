using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Services;

namespace WesleyCode.Agent.Infrastructure;

internal class AgentRunner : IAgentRunner
{
    private readonly AIAgent _agent;
    private readonly IOutputCapture _capture;

    public AgentRunner(AIAgent agent, IOutputCapture capture)
    {
        this._agent = agent;
        this._capture = capture;
    }

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default) => _agent.CreateSessionAsync(cancellationToken);

    public Task<AgentResponse> ExecuteAsync(string input, AgentSession session, CancellationToken cancellationToken = default) =>
        _agent.ExecuteAsync(input, session, _capture, cancellationToken);

    public void RestartSession(AgentSession activeSession)
    {
        if (activeSession.TryGetInMemoryChatHistory(out var history) && history != null)
        {
            foreach (var message in history)
            {
                if (message.Role == ChatRole.User)
                {
                    _capture.WriteUserMessage(message.Text);
                }
                else if (message.Role == ChatRole.System)
                {
                    _capture.WriteSystemMessage(message.Text);
                }
                else if (message.Contents.HasToolContent())
                {
                    _capture.WriteTool(message.Contents, message.AuthorName);
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        _capture.WriteThinkingMessage(message.Text);
                    }
                }
                else if (message.Role == ChatRole.Assistant)
                {
                    _capture.WriteAgentMessage(message.Text);
                }
            }
        }
    }
}
