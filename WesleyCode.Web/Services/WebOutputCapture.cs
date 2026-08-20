using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;
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
    private readonly IOptions<ChatClientOptions> _options;

    public WebOutputCapture(IWebOutputCaptureState state, IOptions<ChatClientOptions> options)
    {
        _state = state;
        this._options = options;
    }

    public void WriteUserTitle() { }

    public void WriteUserMessage(string message) => _state.AddCurrentMessage(ChatRole.User, ChatAuthorNames.User, message);

    public void WriteAgentMessage(string message) =>
        _state.AddCurrentMessage(ChatRole.Assistant, ChatAuthorNames.Assistant, message.TrimMarker(_options.Value.StopMark));

    public void WriteSystemMessage(string message) => _state.AddCurrentMessage(ChatRole.System, ChatAuthorNames.System, message);

    public void WriteToolCall(string callId, string? target, string toolName, IDictionary<string, object?>? arguments)
    {
        var title = $"{target ?? "unknown"} - {callId} - {toolName}";
        _state.AddCurrentMessage(ChatRole.Tool, title, JsonSerializer.Serialize(arguments, JsonOptions));
    }

    public void WriteToolResult(string callId, string? target, object? result)
    {
        var title = $"{target ?? "unknown"} - {callId} - result";
        _state.AddCurrentMessage(ChatRole.Tool, title, JsonSerializer.Serialize(result, JsonOptions));
    }
}
