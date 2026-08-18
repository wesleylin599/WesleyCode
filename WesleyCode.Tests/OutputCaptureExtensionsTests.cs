using Microsoft.Extensions.AI;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Interfaces;

namespace WesleyCode.Tests;

/// <summary>
/// <see cref="OutputCaptureExtensions.CommonWriteMessage"/> 的单元测试。
/// </summary>
public class OutputCaptureExtensionsTests
{
    private sealed class FakeCapture : IOutputCapture
    {
        public List<string> SystemMessages { get; } = [];
        public List<(string CallId, string? Target, string ToolName, IDictionary<string, object?>? Args)> ToolCalls { get; } = [];
        public List<(string CallId, string? Target, object? Result)> ToolResults { get; } = [];

        public void WriteUserTitle() { }

        public void WriteUserMessage(string message) => throw new NotSupportedException();

        public void WriteAgentMessage(string message) => throw new NotSupportedException();

        public void WriteSystemMessage(string message) => SystemMessages.Add(message);

        public void WriteToolCall(string callId, string? target, string toolName, IDictionary<string, object?>? arguments) =>
            ToolCalls.Add((callId, target, toolName, arguments));

        public void WriteToolResult(string callId, string? target, object? result) => ToolResults.Add((callId, target, result));
    }

    [Fact]
    public void CommonWriteMessage_ErrorContent_WritesSystemMessage()
    {
        var capture = new FakeCapture();
        var content = new ErrorContent("boom");

        capture.CommonWriteMessage("author", content);

        Assert.Contains("boom", capture.SystemMessages);
        Assert.Empty(capture.ToolCalls);
        Assert.Empty(capture.ToolResults);
    }

    [Fact]
    public void CommonWriteMessage_FunctionCallContent_WritesToolCall()
    {
        var capture = new FakeCapture();
        var args = new Dictionary<string, object?> { ["key"] = "value" };
        var content = new FunctionCallContent("call-1", "my_tool", arguments: args);

        capture.CommonWriteMessage("author", content);

        var call = Assert.Single(capture.ToolCalls);
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("author", call.Target);
        Assert.Equal("my_tool", call.ToolName);
        Assert.Equal("value", call.Args!["key"]);
        Assert.Empty(capture.SystemMessages);
    }

    [Fact]
    public void CommonWriteMessage_FunctionResultContent_WritesToolResult()
    {
        var capture = new FakeCapture();
        var content = new FunctionResultContent("call-2", "some-result");

        capture.CommonWriteMessage("author", content);

        var result = Assert.Single(capture.ToolResults);
        Assert.Equal("call-2", result.CallId);
        Assert.Equal("author", result.Target);
        Assert.Equal("some-result", result.Result);
        Assert.Empty(capture.SystemMessages);
    }

    [Fact]
    public void CommonWriteMessage_FunctionResultContentWithException_UsesExceptionMessage()
    {
        var capture = new FakeCapture();
        var content = new FunctionResultContent("call-3", (object?)null) { Exception = new InvalidOperationException("exec failed") };

        capture.CommonWriteMessage("author", content);

        var result = Assert.Single(capture.ToolResults);
        Assert.Equal("exec failed", result.Result);
    }
}
