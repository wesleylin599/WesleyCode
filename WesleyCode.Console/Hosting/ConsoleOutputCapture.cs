using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using WesleyCode.Agent.Services;

namespace WesleyCode.Console.Hosting;

internal class ConsoleOutputCapture : IOutputCapture
{
    private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void WritePrompt()
    {
        System.Console.ResetColor();
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine("> User >>>");
        System.Console.ResetColor();
        System.Console.Write("  ");
    }

    public void WriteUserMessage(string message) => WriteBlock("User", message, ConsoleColor.Cyan, ConsoleColor.Gray);

    public void WriteAgentMessage(string message) => WriteBlock("Agent", message, ConsoleColor.Green, ConsoleColor.Gray);

    public void WriteSystemMessage(string message) => WriteBlock("System", message, ConsoleColor.Magenta, ConsoleColor.Gray);

    public void WriteTool(IList<AIContent> contents, string? target = null)
    {
        target ??= "unknown";
        foreach (var content in contents)
        {
            if (content is FunctionCallContent callContent)
            {
                WriteToolCall(callContent.CallId, target, callContent.Name, FormatToolArguments(callContent.Arguments));
            }
            else if (content is FunctionResultContent resultContent)
            {
                WriteToolResult(resultContent.CallId, resultContent.Exception?.Message ?? resultContent.Result?.ToString() ?? string.Empty);
            }
        }
    }

    private void WriteToolCall(string callId, string target, string toolName, string arguments) =>
        WriteBlock($"[{callId}] {target}:{toolName}", arguments, ConsoleColor.DarkYellow, ConsoleColor.DarkGray);

    private void WriteToolResult(string callId, string message) =>
        WriteBlock($"[{callId}] Tool Result", message, ConsoleColor.DarkBlue, ConsoleColor.DarkGray);

    private static void WriteBlock(string title, string message, ConsoleColor titleColor, ConsoleColor contentColor)
    {
        System.Console.ForegroundColor = titleColor;
        System.Console.WriteLine($"> {title} >>>");

        System.Console.ForegroundColor = contentColor;
        foreach (var line in Normalize(message))
        {
            System.Console.WriteLine($"  {line}");
        }
        System.Console.ResetColor();
    }

    private static string FormatToolArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is not { Count: > 0 })
        {
            return "(no args)";
        }

        return JsonSerializer.Serialize(arguments, _options);
    }

    private static IEnumerable<string> Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            yield return "(empty)";
            yield break;
        }

        using var reader = new StringReader(message.Trim());
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
