namespace WesleyCode.Agent.Interfaces;

public interface IOutputCapture
{
    void WriteUserTitle();

    void WriteUserMessage(string message);

    void WriteAgentMessage(string message);

    void WriteSystemMessage(string message);

    void WriteToolCall(string callId, string? target, string toolName, IDictionary<string, object?>? arguments);

    void WriteToolResult(string callId, string? target, object? result);
}
