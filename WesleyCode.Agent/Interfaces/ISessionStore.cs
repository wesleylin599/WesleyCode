using Microsoft.Agents.AI;

namespace WesleyCode.Agent.Interfaces;

public interface ISessionStore
{
    Task<AgentSession> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AgentSession session, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
