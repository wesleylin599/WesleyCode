namespace WesleyCode.Agent.Services;

public interface IOutputCapture
{
    void WritePrompt();

    void WriteUserMessage(string message);

    void WriteAgentMessage(string message);

    void WriteSystemMessage(string message);

    void WriteToolCall(string callId, string? target, string toolName, IDictionary<string, object?>? arguments);

    void WriteToolResult(string callId, string? message);
}
