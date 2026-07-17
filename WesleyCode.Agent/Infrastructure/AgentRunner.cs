using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Infrastructure;

internal class AgentRunner : IAgentRunner
{
    private readonly AIAgent _agent;
    private readonly IOutputCapture _capture;

    public AgentRunner(IChatClient client, IOutputCapture capture, IServiceProvider provider, IOptions<AgentOptions> options)
    {
        this._agent = client.AsAIAgent(
            options: new ChatClientAgentOptions
            {
                Name = options.Value.Name,
                Description = options.Value.Description,
                ChatOptions = new ChatOptions { Instructions = options.Value.Instructions },
                AIContextProviders = provider.GetServices<AIContextProvider>(),
            }
        );
        this._capture = capture;
    }

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default) => _agent.CreateSessionAsync(cancellationToken);

    public ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken = default) =>
        _agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);

    public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement serializedState, CancellationToken cancellationToken = default) =>
        _agent.DeserializeSessionAsync(serializedState, cancellationToken: cancellationToken);

    public async Task<AgentResponse> ExecuteAsync(List<ChatMessage> input, AgentSession session, CancellationToken cancellationToken = default)
    {
        StringBuilder builder = new StringBuilder();
        List<AgentResponseUpdate> agentResponses = new List<AgentResponseUpdate>();
        try
        {
            await foreach (var responseUpdate in _agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken))
            {
                foreach (var content in responseUpdate.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        builder.Append(textContent.Text);
                    }
                    else if (builder.Length > 0)
                    {
                        _capture.WriteAgentMessage(builder.ToString());
                        builder.Clear();
                    }

                    CommonWriteMessage(responseUpdate.AuthorName, content);
                }
                agentResponses.Add(responseUpdate);
            }
        }
        finally
        {
            if (builder.Length > 0)
            {
                _capture.WriteAgentMessage(builder.ToString());
                builder.Clear();
            }
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

                        CommonWriteMessage(message.AuthorName, content);
                    }
                }
            }
        }
        return Task.CompletedTask;
    }

    private void CommonWriteMessage(string? author, AIContent content)
    {
        if (content is ErrorContent errorContent)
        {
            _capture.WriteSystemMessage(errorContent.Message);
        }
        else if (content is FunctionCallContent callContent)
        {
            _capture.WriteToolCall(callContent.CallId, author, callContent.Name, callContent.Arguments);
        }
        else if (content is FunctionResultContent resultContent)
        {
            var result = resultContent.Exception?.Message ?? resultContent.Result;
            _capture.WriteToolResult(resultContent.CallId, author, result);
        }
    }
}
