using System.Text;
using Microsoft.Extensions.AI;

namespace WesleyCode.ConsoleHost.Extensions;

internal static class ConsoleContentExtensions
{
    private const int MaxLogLine = 10;

    public static void ConsoleLog(this IList<AIContent> contents, string? target = null)
    {
        target ??= "unknown";
        foreach (var content in contents)
        {
            if (content is FunctionCallContent callContent)
            {
                var arguments = callContent.Arguments is { Count: > 0 }
                    ? string.Join(Environment.NewLine, callContent.Arguments.Select(static item => $"{item.Key}: {item.Value}"))
                    : "(no args)";

                ConsoleOutput.WriteToolCall(callContent.CallId, target, callContent.Name, arguments);
            }
            else if (content is FunctionResultContent resultContent)
            {
                ConsoleOutput.WriteToolResult(resultContent.CallId, FormatToolResult(resultContent.Result?.ToString()));
            }
        }
    }

    private static string FormatToolResult(string? result)
    {
        if (string.IsNullOrEmpty(result))
        {
            return "null";
        }

        var isTruncated = false;
        var output = new StringBuilder();
        var lines = result.Split(["\r\n", "\n"], StringSplitOptions.None).Where(static item => !string.IsNullOrEmpty(item)).ToList();
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines.Count > MaxLogLine && index > MaxLogLine / 2 && index < lines.Count - MaxLogLine / 2)
            {
                if (!isTruncated)
                {
                    output.AppendLine("{ truncated }");
                    isTruncated = true;
                }

                continue;
            }

            output.AppendLine(lines[index]);
        }

        return output.ToString();
    }
}
