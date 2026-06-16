using System.Security.Cryptography;
using System.Text;
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
        IOutputCapture capture,
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
                if (agentResponse.Contents.HasToolContent())
                {
                    capture.WriteTool(agentResponse.Contents, agentResponse.AuthorName);
                }
            }

            var response = updates.ToAgentResponse();
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                return response;
            }
        }

        throw new InvalidOperationException("代理连续多次未返回可显示内容,请调整输入后重试。");
    }

    public static string ComputeMd5(this string target)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(target));
        return BitConverter.ToString(hash, 4, 8).Replace("-", string.Empty).ToLowerInvariant();
    }
}
