using Microsoft.Agents.AI;

namespace WesleyCode.Agent.Interfaces;

public interface IAgentRunner
{
    ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default);

    Task ExecuteAsync(string input, AgentSession session, CancellationToken cancellationToken = default);

    void RestartSession(AgentSession activeSession);
}
