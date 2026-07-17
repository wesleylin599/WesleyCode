using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Interfaces;

public interface IAgentRunner
{
    ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default);

    ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken = default);

    ValueTask<AgentSession> DeserializeSessionAsync(JsonElement serializedState, CancellationToken cancellationToken = default);

    Task<AgentResponse> ExecuteAsync(List<ChatMessage> input, AgentSession session, CancellationToken cancellationToken = default);

    Task RestartSessionAsync(AgentSession activeSession, CancellationToken cancellationToken = default);
}
