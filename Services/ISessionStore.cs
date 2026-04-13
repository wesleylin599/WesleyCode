using Microsoft.Agents.AI;

namespace TestConsole5.Services;

internal interface ISessionStore
{
    Task<AgentSession> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AgentSession session, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
