using System.Text;
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

    public async Task ExecuteAsync(string input, AgentSession session, CancellationToken cancellationToken = default)
    {
        StringBuilder builder = new StringBuilder();
        await foreach (var responseUpdate in _agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken))
        {
            foreach (var content in responseUpdate.Contents)
            {
                if (content is FunctionCallContent callContent)
                {
                    _capture.WriteToolCall(callContent.CallId, responseUpdate.AuthorName, callContent.Name, callContent.Arguments);
                }
                else if (content is FunctionResultContent resultContent)
                {
                    _capture.WriteToolResult(resultContent.CallId, resultContent.Exception?.Message ?? resultContent.Result?.ToString());
                }
                else if (content is TextContent textContent)
                {
                    builder.Append(textContent.Text);
                }
                if (builder.Length > 0 && responseUpdate.FinishReason == ChatFinishReason.Stop)
                {
                    _capture.WriteAgentMessage(builder.ToString());
                    builder.Clear();
                }
            }
        }
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
                else
                {
                    foreach (var content in message.Contents)
                    {
                        if (content is FunctionCallContent callContent)
                        {
                            _capture.WriteToolCall(callContent.CallId, message.AuthorName, callContent.Name, callContent.Arguments);
                        }
                        else if (content is FunctionResultContent resultContent)
                        {
                            _capture.WriteToolResult(resultContent.CallId, resultContent.Exception?.Message ?? resultContent.Result?.ToString());
                        }
                        else if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                        {
                            _capture.WriteAgentMessage(textContent.Text);
                        }
                    }
                }
            }
        }
    }
}
