using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace WesleyCode.Agent.Extensions;

public static class AgentRunnerExtensions
{
    private const int MaxEmptyResponseRetries = 8;

    public static async Task<AgentResponse> ExecuteAsync(
        this AIAgent agent,
        string input,
        AgentSession session,
        CancellationToken cancellationToken,
        Action<AgentResponseUpdate>? onUpdate = null
    )
    {
        var updates = new List<AgentResponseUpdate>();
        for (var attempt = 0; attempt < MaxEmptyResponseRetries; attempt++)
        {
            updates.Clear();
            await foreach (var agentResponse in agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken))
            {
                updates.Add(agentResponse);
                onUpdate?.Invoke(agentResponse);
            }

            var response = updates.ToAgentResponse();
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                return response;
            }
        }

        throw new InvalidOperationException("代理连续多次未返回可显示内容,请调整输入后重试。");
    }

    public static void LogEventId(this ILogger logger, string message, [CallerLineNumber] int lineNumber = 0) =>
        logger.LogError(new EventId(lineNumber), message);
}
