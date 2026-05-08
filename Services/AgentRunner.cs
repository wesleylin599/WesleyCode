using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Services;

internal sealed class AgentRunner : IAgentRunner
{
    private const int MaxEmptyResponseRetries = 8;
    private readonly AIAgent _agent;

    public AgentRunner(AIAgent agent)
    {
        _agent = agent;
    }

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken) => _agent.CreateSessionAsync(cancellationToken);

    public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement element, CancellationToken cancellationToken) =>
        _agent.DeserializeSessionAsync(element, cancellationToken: cancellationToken);

    public ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken) =>
        _agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);

    public async Task<AgentResponse> RunAsync(string input, AgentSession session, CancellationToken cancellationToken)
    {
        var response = new AgentResponse(new ChatMessage(ChatRole.User, input));
        for (var attempt = 0; attempt < MaxEmptyResponseRetries; attempt++)
        {
            response = await _agent.RunAsync(response.Messages, session, cancellationToken: cancellationToken);
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                return response;
            }
        }

        throw new InvalidOperationException("代理连续多次未返回可显示内容,请调整输入后重试。");
    }
}
