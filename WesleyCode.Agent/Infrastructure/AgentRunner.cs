using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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

    public async Task<AgentResponse> ExecuteAsync(string input, AgentSession session, CancellationToken cancellationToken = default)
    {
        var options = new AgentRunOptions();
        var responseUpdates = new List<AgentResponseUpdate>();
        await foreach (var agentResponse in _agent.RunStreamingAsync(input, session, options, cancellationToken))
        {
            responseUpdates.Add(agentResponse);
            _capture.WriteContent(agentResponse.Contents, agentResponse.AuthorName);
        }
        var response = responseUpdates.ToAgentResponse();
        _capture.WriteAgentMessage(response.Messages.Last().Text);
        return response;
    }

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
                else if (message.Role == ChatRole.Assistant)
                {
                    if (message.Contents.Any(x => x is FunctionCallContent or FunctionResultContent or TextReasoningContent))
                    {
                        _capture.WriteContent(message.Contents, message.AuthorName);
                    }
                    else
                    {
                        _capture.WriteAgentMessage(message.Text);
                    }
                }
            }
        }
    }
}
