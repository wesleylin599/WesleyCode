using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Infrastructure;

public sealed class NonEmptyLoopEvaluator : LoopEvaluator
{
    public const string CompletionMarker = "[处理完成]";

    private static ValueTask<LoopEvaluation> Continue(Type type, string feedback) =>
        ValueTask.FromResult(
            LoopEvaluation.ContinueWithMessages([
                new ChatMessage(ChatRole.User, feedback).WithAgentRequestMessageSource(AgentRequestMessageSourceType.ChatHistory, type.Name),
            ])
        );

    public override ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.LastResponse.Messages is not { Count: > 0 } messages)
        {
            return Continue(this.GetType(), "继续处理请求，回复的消息不能为空。");
        }

        if (messages.LastOrDefault(m => m.Role == ChatRole.Assistant) is not { } message)
        {
            return Continue(this.GetType(), "继续处理请求，回复的消息必须包含助手消息。");
        }

        if (message.Contents.OfType<ErrorContent>().LastOrDefault() is not null)
        {
            return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
        }

        if (message.Contents.OfType<TextContent>().LastOrDefault() is not TextContent textContent)
        {
            return Continue(this.GetType(), "继续处理请求，回复的消息必须包含文本内容。");
        }

        if (string.IsNullOrWhiteSpace(textContent.Text))
        {
            return Continue(this.GetType(), "继续处理请求，回复必须包含非空文本内容。");
        }

        if (!textContent.Text.Contains(CompletionMarker, StringComparison.Ordinal))
        {
            return Continue(this.GetType(), $"完成任务后必须在最终回复中包含 `{CompletionMarker}`。如果尚未完成，请继续完成剩余工作。");
        }

        return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
    }
}
