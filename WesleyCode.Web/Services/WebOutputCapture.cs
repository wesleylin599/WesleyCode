using Microsoft.Extensions.AI;
using System.Text.Encodings.Web;
using System.Text.Json;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Web.Interfaces;

namespace WesleyCode.Web.Services;

public sealed class WebOutputCapture : IOutputCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly IWebOutputCaptureState _state;

    public WebOutputCapture(IWebOutputCaptureState state)
    {
        _state = state;
    }

    public void WritePrompt() { }

    public void WriteUserMessage(string message) => _state.AddCurrentMessage(ChatRole.User, "你", message);

    public void WriteAgentMessage(string message) => _state.AddCurrentMessage(ChatRole.Assistant, "WesleyCode", message);

    public void WriteSystemMessage(string message) => _state.AddCurrentMessage(ChatRole.System, "系统", message);

    public void WriteToolCall(string callId, string? target, string toolName, IDictionary<string, object?>? arguments)
    {
        var title = $"{target ?? "unknow"} - {callId} - {toolName}";
        var content = arguments is { Count: > 0 } ? JsonSerializer.Serialize(arguments, JsonOptions) : "无参数";
        _state.AddCurrentMessage(ChatRole.Tool, title, content);
    }

    public void WriteToolResult(string callId, string? target, string? message)
    {
        var title = $"{target ?? "unknow"} - {callId} - result";
        _state.AddCurrentMessage(ChatRole.Tool, title, message ?? "空结果");
    }
}