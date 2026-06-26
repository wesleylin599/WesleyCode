using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Services;

public interface IOutputCapture
{
    void WritePrompt();

    void WriteUserMessage(string message);

    void WriteAgentMessage(string message);

    void WriteSystemMessage(string message);

    void WriteThinkingMessage(string message);

    void WriteTool(IList<AIContent> contents, string? target = null);
}
