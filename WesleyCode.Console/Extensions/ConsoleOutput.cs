namespace WesleyCode.ConsoleHost.Extensions;

internal static class ConsoleOutput
{
    public static void WritePrompt()
    {
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("> User >>>");
        Console.ResetColor();
        Console.Write("  ");
    }

    public static void WriteUserMessage(string message) => WriteBlock("User", message, ConsoleColor.Cyan, ConsoleColor.Gray);

    public static void WriteAgentMessage(string message) => WriteBlock("Agent", message, ConsoleColor.Green, ConsoleColor.Gray);

    public static void WriteSystemMessage(string message) => WriteBlock("System", message, ConsoleColor.Magenta, ConsoleColor.Gray);

    public static void WriteToolResult(string callId, string message) =>
        WriteBlock($"[{callId}] Tool", message, ConsoleColor.DarkBlue, ConsoleColor.DarkGray);

    public static void WriteToolCall(string callId, string target, string toolName, string arguments) =>
        WriteBlock($"[{callId}] {target}:{toolName}", arguments, ConsoleColor.DarkYellow, ConsoleColor.DarkGray);

    private static void WriteBlock(string title, string message, ConsoleColor titleColor, ConsoleColor contentColor)
    {
        Console.ForegroundColor = titleColor;
        Console.WriteLine($"> {title} >>>");

        Console.ForegroundColor = contentColor;
        foreach (var line in Normalize(message))
        {
            Console.WriteLine($"  {line}");
        }
        Console.ResetColor();
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
