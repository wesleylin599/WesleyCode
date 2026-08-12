using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Infrastructure;

public sealed class NonEmptyLoopEvaluator : LoopEvaluator
{
    public override ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
    {
        if (context.LastResponse.Messages.LastOrDefault() is not { } message)
        {
            return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("继续处理请求,消息不能为空。"));
        }

        if (message.Contents.OfType<TextContent>().LastOrDefault() is TextContent textContent && string.IsNullOrEmpty(textContent.Text))
        {
            return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("继续处理请求,回复的文本消息不能为空。"));
        }

        return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
    }
}
