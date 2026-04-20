using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Services;

internal interface IAgentRunner
{
    ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken);
    ValueTask<AgentSession> DeserializeSessionAsync(JsonElement element, CancellationToken cancellationToken);
    ValueTask<JsonElement> SerializeSessionAsync(AgentSession session, CancellationToken cancellationToken);
    Task<AgentResponse> RunAsync(string input, AgentSession session, CancellationToken cancellationToken);
}
