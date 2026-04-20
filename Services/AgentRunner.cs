using System.Text.Json;
using Microsoft.Agents.AI;

namespace WesleyCode.Services;

internal sealed class AgentRunner : IAgentRunner
{
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

    public Task<AgentResponse> RunAsync(string input, AgentSession session, CancellationToken cancellationToken) =>
        _agent.RunAsync(input, session, cancellationToken: cancellationToken);
}
