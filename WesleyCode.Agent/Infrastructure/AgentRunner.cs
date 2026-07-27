using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
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
            .AsAIAgent(
                options: new ChatClientAgentOptions
                {
                    Name = options.Value.Name,
                    Description = options.Value.Description,
                    ChatOptions = new ChatOptions { Instructions = options.Value.Instructions },
                    AIContextProviders = providers,
                }
            )
            .AsBuilder()
            .UseToolApproval(new ToolApprovalAgentOptions() { AutoApprovalRules = [context => ValueTask.FromResult(true)] })
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
        string currentMessageId = string.Empty;
        ChatRole currentChatRole = ChatRole.Assistant;
        StringBuilder currentBuilder = new StringBuilder();
        List<AgentResponseUpdate> agentResponses = new List<AgentResponseUpdate>();
        await foreach (var responseUpdate in _agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken))
        {
            foreach (var content in responseUpdate.Contents)
            {
                if (content is TextContent textContent)
                {
                    currentBuilder.Append(textContent.Text);
                }
                CommonWriteMessage(responseUpdate.AuthorName, content);
            }
            if (NotEmptyOrEqual(responseUpdate.MessageId, currentMessageId) || NotNullOrEqual(responseUpdate.Role, currentChatRole))
            {
                if (responseUpdate.Role is ChatRole role)
                {
                    currentChatRole = role;
                }
                if (responseUpdate.MessageId is { Length: > 0 })
                {
                    currentMessageId = responseUpdate.MessageId;
                }
                WriteAgentMessage(_capture, currentBuilder);
            }
            agentResponses.Add(responseUpdate);
        }
        WriteAgentMessage(_capture, currentBuilder);

        return agentResponses.ToAgentResponse();

        static bool NotEmptyOrEqual(string? s1, string? s2) => s1 is { Length: > 0 } str1 && s2 is { Length: > 0 } str2 && str1 != str2;

        static bool NotNullOrEqual(ChatRole? r1, ChatRole? r2) => r1.HasValue && r2.HasValue && r1.Value != r2.Value;

        static void WriteAgentMessage(IOutputCapture capture, StringBuilder currentBuilder)
        {
            if (currentBuilder.Length > 0)
            {
                capture.WriteAgentMessage(currentBuilder.ToString());
                currentBuilder.Clear();
            }
        }
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
