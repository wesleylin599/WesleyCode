using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Extensions;

internal static class AgentRunnerExtensions
{
    private const int MaxLogLine = 10;
    private const int MaxEmptyResponseRetries = 8;

    private static string ToolConsoleLog(string args)
    {
        bool isTruncated = false;
        var output = new StringBuilder();
        var lines = args.Split(["\r\n", "\n"], StringSplitOptions.None).Where(item => !string.IsNullOrEmpty(item)).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (lines.Count > MaxLogLine && i > MaxLogLine / 2 && i < lines.Count - MaxLogLine / 2)
            {
                if (!isTruncated)
                {
                    output.AppendLine("{ truncated }");
                    isTruncated = true;
                }
                continue;
            }
            output.AppendLine(line);
        }
        return output.ToString();
    }

    public static void ConsoleLog(this IList<AIContent> contents, string? target = null)
    {
        target ??= "unknow";
        foreach (var content in contents)
        {
            if (content is FunctionCallContent callContent)
            {
                var arguments = callContent.Arguments is { Count: > 0 } ? callContent.Arguments.Values : ["null"];
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"[{target}:{callContent.Name}] {string.Join(" ", arguments)}");
            }
            else if (content is FunctionResultContent resultContent)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(ToolConsoleLog(resultContent.Result?.ToString() ?? "null"));
            }
        }
        Console.ResetColor();
    }

    public static async Task<AgentResponse> ExecuteAsync(this AIAgent agent, string input, AgentSession session, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxEmptyResponseRetries; attempt++)
        {
            var updates = new List<AgentResponseUpdate>();
            await foreach (var agentResponse in agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken))
            {
                updates.Add(agentResponse);
                agentResponse.Contents.ConsoleLog(agent.Name);
            }
            AgentResponse response = updates.ToAgentResponse();
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                return response;
            }
        }

        throw new InvalidOperationException("代理连续多次未返回可显示内容,请调整输入后重试。");
    }
}
