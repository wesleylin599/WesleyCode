using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Extensions;

internal static class AgentRunnerExtensions
{
    private const int MaxEmptyResponseRetries = 8;

    public static async Task<AgentResponse> ExecuteAsync(this AIAgent agent, string input, AgentSession session, CancellationToken cancellationToken)
    {
        var response = new AgentResponse(new ChatMessage(ChatRole.User, input));
        for (var attempt = 0; attempt < MaxEmptyResponseRetries; attempt++)
        {
            response = await agent.RunAsync(response.Messages, session, cancellationToken: cancellationToken);
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                return response;
            }
        }

        throw new InvalidOperationException("代理连续多次未返回可显示内容,请调整输入后重试。");
    }
}
