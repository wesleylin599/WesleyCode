using Microsoft.Agents.AI;

namespace WesleyCode.Services;

internal interface ISessionStore
{
    Task<AgentSession> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AgentSession session, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
