using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Infrastructure;

public sealed class NonEmptyLoopEvaluator : LoopEvaluator
{
    private readonly string _completionMarker;

    public NonEmptyLoopEvaluator(string completionMarker)
    {
        this._completionMarker = completionMarker;
    }

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

        if (!textContent.Text.Contains(_completionMarker, StringComparison.Ordinal))
        {
            return Continue(this.GetType(), $"最终回复中必须包含`{_completionMarker}`。");
        }

        return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
    }
}
