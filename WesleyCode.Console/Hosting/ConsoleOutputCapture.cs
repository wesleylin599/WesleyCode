using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spectre.Console;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;

namespace WesleyCode.Console.Hosting;

internal class ConsoleOutputCapture : IOutputCapture
{
    private const int MaxLogLength = 512;
    private const string TruncatedSuffix = "[输出被截断，内容过长]";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IOptions<ChatClientOptions> _options;

    public ConsoleOutputCapture(IOptions<ChatClientOptions> options)
    {
        this._options = options;
    }

    public void WriteUserTitle()
    {
        AnsiConsole.Cursor.Show();
        WriteText($"> User >>>{Environment.NewLine}", Color.Aqua);
        WriteText("  ", Color.Silver);
    }

    public void WriteUserMessage(string message) => WriteBlock("User", message, Color.Aqua, Color.Silver);

    public void WriteAgentMessage(string message) => WriteBlock("Agent", message.TrimMarker(_options.Value.StopMark), Color.Lime, Color.Silver);

    public void WriteSystemMessage(string message) => WriteBlock("System", message, Color.Fuchsia, Color.Silver);

    public void WriteToolCall(string callId, string? target, string toolName, IDictionary<string, object?>? arguments) =>
        WriteBlock($"[{callId}] {target ?? "unknown"}:{toolName}", TruncateLine(arguments), Color.Olive, Color.Grey);

    public void WriteToolResult(string callId, string? target, object? result) =>
        WriteBlock($"[{callId}] {target ?? "unknown"}:result", TruncateLine(result), Color.Navy, Color.Grey);

    private static void WriteBlock(string title, string message, Color titleColor, Color contentColor)
    {
        WriteText($"> {title} >>>{Environment.NewLine}", titleColor);

        foreach (var line in Normalize(message))
        {
            WriteText($"  {line}{Environment.NewLine}", contentColor);
        }
    }

    private static void WriteText(string text, Color color) => AnsiConsole.Write(new Text(text, new Style(color)));

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

        var message = JsonSerializer.Serialize(result, JsonOptions).Replace("\\r\\n", "").Replace("\\n", "").Replace("  ", "");
        if (message.Length > MaxLogLength)
        {
            var contentLength = Math.Max(0, MaxLogLength - TruncatedSuffix.Length);
            return message[..contentLength] + TruncatedSuffix;
        }
        return message;
    }
}
