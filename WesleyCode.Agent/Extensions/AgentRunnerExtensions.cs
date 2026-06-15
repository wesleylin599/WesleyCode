using Microsoft.Agents.AI;
using WesleyCode.Agent.Services;

namespace WesleyCode.Agent.Extensions;

internal static class AgentRunnerExtensions
{
    private const int MaxEmptyResponseRetries = 8;

    public static async Task<AgentResponse> ExecuteAsync(
        this AIAgent agent,
        string input,
        AgentSession session,
        IOutputCapture? capture = null,
        CancellationToken cancellationToken = default
    )
    {
        var updates = new List<AgentResponseUpdate>();
        for (var attempt = 0; attempt < MaxEmptyResponseRetries; attempt++)
        {
            updates.Clear();
            await foreach (var agentResponse in agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken))
            {
                updates.Add(agentResponse);
                capture?.WriteTool(agentResponse.Contents, agentResponse.AuthorName);
            }

            var response = updates.ToAgentResponse();
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                return response;
            }
        }

        throw new InvalidOperationException("代理连续多次未返回可显示内容,请调整输入后重试。");
    }
}
