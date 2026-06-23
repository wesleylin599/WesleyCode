using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using WesleyCode.Agent.Services;

namespace WesleyCode.Agent.Extensions;

internal static class AgentRunnerExtensions
{
    private const int MaxEmptyResponseRetries = 8;

    private static async IAsyncEnumerable<AgentResponseUpdate> WriteToolContentAsync(
        this IAsyncEnumerable<AgentResponseUpdate> updates,
        IOutputCapture capture,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var agentResponse in updates)
        {
            if (agentResponse.Contents.HasToolContent())
            {
                capture.WriteTool(agentResponse.Contents, agentResponse.AuthorName);
            }
            yield return agentResponse;
        }
    }

    public static async Task<AgentResponse> ExecuteAsync(
        this AIAgent agent,
        string input,
        AgentSession session,
        IOutputCapture capture,
        CancellationToken cancellationToken = default
    )
    {
        var options = new AgentRunOptions();
        for (var attempt = 0; attempt < MaxEmptyResponseRetries; attempt++)
        {
            var response = await agent
                .RunStreamingAsync(input, session, options, cancellationToken)
                .WriteToolContentAsync(capture, cancellationToken)
                .ToAgentResponseAsync(cancellationToken);
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
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
