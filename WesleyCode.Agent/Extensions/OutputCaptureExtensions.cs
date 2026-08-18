using Microsoft.Extensions.AI;
using WesleyCode.Agent.Interfaces;

namespace WesleyCode.Agent.Extensions;

/// <summary>
/// 提供 <see cref="IOutputCapture"/> 的扩展方法。
/// </summary>
public static class OutputCaptureExtensions
{
    /// <summary>
    /// 根据 <see cref="AIContent"/> 的具体类型，将内容分派到对应的捕获回调。
    /// </summary>
    public static void CommonWriteMessage(this IOutputCapture capture, string? author, AIContent content)
    {
        if (content is ErrorContent errorContent)
        {
            capture.WriteSystemMessage(errorContent.Message);
        }
        else if (content is FunctionCallContent callContent)
        {
            capture.WriteToolCall(callContent.CallId, author, callContent.Name, callContent.Arguments);
        }
        else if (content is FunctionResultContent resultContent)
        {
            var result = resultContent.Exception?.Message ?? resultContent.Result;
            capture.WriteToolResult(resultContent.CallId, author, result);
        }
    }
}
