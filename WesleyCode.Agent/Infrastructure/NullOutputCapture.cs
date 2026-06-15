using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WesleyCode.Agent.Services;

namespace WesleyCode.Agent.Infrastructure;

internal class NullOutputCapture : IOutputCapture
{
    private readonly ILogger<IOutputCapture> _logger;

    public NullOutputCapture(ILogger<IOutputCapture> logger)
    {
        this._logger = logger;
    }

    public void WritePrompt() => WriteBlock("Agent", string.Empty, ConsoleColor.Green, ConsoleColor.Gray);

    public void WriteUserMessage(string message) => WriteBlock("User", message, ConsoleColor.Cyan, ConsoleColor.Gray);

    public void WriteAgentMessage(string message) => WriteBlock("Agent", message, ConsoleColor.Green, ConsoleColor.Gray);

    public void WriteSystemMessage(string message) => WriteBlock("System", message, ConsoleColor.Magenta, ConsoleColor.Gray);

    public void WriteTool(IList<AIContent> contents, string? target = null) =>
        WriteBlock(
            "Tool",
            $"target={target}, contents={string.Join(", ", contents.Select(c => c.GetType().Name))}",
            ConsoleColor.DarkYellow,
            ConsoleColor.DarkGray
        );

    private void WriteBlock(string title, string message, ConsoleColor titleColor, ConsoleColor contentColor) =>
        _logger.LogWarning($"NullOutputFactory[{title}]{message}");
}
