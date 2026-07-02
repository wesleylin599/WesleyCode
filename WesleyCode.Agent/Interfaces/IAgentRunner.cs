using System.Text.Json;
using Microsoft.Agents.AI;

namespace WesleyCode.Agent.Interfaces;

public interface IAgentRunner
{
    ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default);

    ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken = default);

    ValueTask<AgentSession> DeserializeSessionAsync(JsonElement serializedState, CancellationToken cancellationToken = default);

    Task ExecuteAsync(string input, AgentSession session, CancellationToken cancellationToken = default);

    void RestartSession(AgentSession activeSession);
}
