using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WesleyCode.Agent.Interfaces;

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
        List<ChatMessage> inputMessages = [new ChatMessage(ChatRole.User, input)];
        do
        {
            bool isText = false;
            StringBuilder builder = new StringBuilder();
            List<ToolApprovalRequestContent> toolApprovals = new List<ToolApprovalRequestContent>();
            await foreach (var responseUpdate in _agent.RunStreamingAsync(inputMessages, session, cancellationToken: cancellationToken))
            {
                foreach (var content in responseUpdate.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        builder.Append(textContent.Text);
                        isText = true;
                    }
                    if (builder.Length > 0 && !isText)
                    {
                        _capture.WriteAgentMessage(builder.ToString());
                        builder.Clear();
                    }
                    if (content is FunctionCallContent callContent)
                    {
                        _capture.WriteToolCall(callContent.CallId, responseUpdate.AuthorName, callContent.Name, callContent.Arguments);
                    }
                    if (content is FunctionResultContent resultContent)
                    {
                        var result = resultContent.Exception?.Message ?? resultContent.Result;
                        _capture.WriteToolResult(resultContent.CallId, responseUpdate.AuthorName, result);
                    }
                    if (content is ToolApprovalRequestContent approvalRequestContent)
                    {
                        toolApprovals.Add(approvalRequestContent);
                    }
                    isText = false;
                }
            }
            if (builder.Length > 0 && !isText)
            {
                _capture.WriteAgentMessage(builder.ToString());
                builder.Clear();
            }
            inputMessages = toolApprovals.Select(x => new ChatMessage(ChatRole.User, [x.CreateResponse(true)])).ToList();
        } while (inputMessages.Count > 0);
    }

    public void RestartSession(AgentSession activeSession)
    {
        if (activeSession.TryGetInMemoryChatHistory(out var history) && history != null)
        {
            foreach (var message in history)
            {
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
                        if (content is FunctionCallContent callContent)
                        {
                            _capture.WriteToolCall(callContent.CallId, message.AuthorName, callContent.Name, callContent.Arguments);
                        }
                        if (content is FunctionResultContent resultContent)
                        {
                            var result = resultContent.Exception?.Message ?? resultContent.Result;
                            _capture.WriteToolResult(resultContent.CallId, message.AuthorName, result);
                        }
                        if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                        {
                            _capture.WriteAgentMessage(textContent.Text);
                        }
                    }
                }
            }
        }
    }
}
