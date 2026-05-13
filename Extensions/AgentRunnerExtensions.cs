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

    public static async void ConsoleLog(this IList<AIContent> contents)
    {
        foreach (var content in contents)
        {
            if (content is FunctionCallContent callContent)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                var arguments = callContent.Arguments?.Values ?? ["null"];
                Console.WriteLine($"[{callContent.Name}] {string.Join(" ", arguments)}");
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
        var updates = new List<AgentResponseUpdate>();
        var response = new AgentResponse(new ChatMessage(ChatRole.User, input));
        for (var attempt = 0; attempt < MaxEmptyResponseRetries; attempt++)
        {
            await foreach (var agentResponse in agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken))
            {
                updates.Add(agentResponse);
                agentResponse.Contents.ConsoleLog();
            }
            response = updates.ToAgentResponse();
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                return response;
            }
        }

        throw new InvalidOperationException("代理连续多次未返回可显示内容,请调整输入后重试。");
    }
}
