namespace WesleyCode.Extensions;

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

    public static void WriteUserMessage(string message) => WriteBlock("User", message, ConsoleColor.Cyan);

    public static void WriteAgentMessage(string message) => WriteBlock("Agent", message, ConsoleColor.Green);

    public static void WriteSystemMessage(string message) => WriteBlock("System", message, ConsoleColor.Blue);

    public static void WriteToolCall(string target, string toolName, string? arguments)
    {
        var header = $"{target}:{toolName}";
        WriteBlock(header, string.IsNullOrWhiteSpace(arguments) ? "(no args)" : arguments, ConsoleColor.DarkYellow);
    }

    public static void WriteToolResult(string message) => WriteBlock("Tool", message, ConsoleColor.DarkGray);

    private static void WriteBlock(string title, string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"> {title} >>>");

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
