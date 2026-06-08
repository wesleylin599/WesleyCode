using Microsoft.Agents.AI;

namespace WesleyCode.Agent.Services;

public interface ISessionStore
{
    Task<AgentSession> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AgentSession session, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
