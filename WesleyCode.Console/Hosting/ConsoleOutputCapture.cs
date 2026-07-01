using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using WesleyCode.Agent.Interfaces;

namespace WesleyCode.Console.Hosting;

internal class ConsoleOutputCapture : IOutputCapture
{
    private const int MaxLogLength = 512;
    private const string TruncatedSuffix = "[输出被截断，内容过长]";

    private static readonly Regex _whitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex _punctuationWhitespaceRegex = new(@"\s*([{}\[\](),:])\s*", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
    {
        WriteIndented = false,
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

    public void WriteToolCall(string callId, string? target, string toolName, IDictionary<string, object?>? arguments) =>
        WriteBlock($"[{callId}] {target ?? "unknow"}:{toolName}", FormatToolArguments(arguments), ConsoleColor.DarkYellow, ConsoleColor.DarkGray);

    public void WriteToolResult(string callId, string? target, object? result) =>
        WriteBlock($"[{callId}] {target ?? "unknow"}:result", TruncateLine(result), ConsoleColor.DarkBlue, ConsoleColor.DarkGray);

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

        return TruncateLine(arguments);
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

    private static string TruncateLine(object? result)
    {
        if (result == null)
            return "null";

        var message = JsonSerializer.Serialize(result, _options);
        var output = CompactOutput(message);
        if (output.Length <= MaxLogLength)
        {
            return output;
        }

        var contentLength = Math.Max(0, MaxLogLength - TruncatedSuffix.Length);
        return output[..contentLength] + TruncatedSuffix;
    }

    private static string CompactOutput(string message)
    {
        var output = UnescapeMessage(message);
        output = _whitespaceRegex.Replace(output.Trim(), " ");
        return _punctuationWhitespaceRegex.Replace(output, "$1");
    }

    private static string UnescapeMessage(string message)
    {
        try
        {
            return Regex.Unescape(message);
        }
        catch (ArgumentException)
        {
            return message;
        }
    }
}
