using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Infrastructure;

public sealed class NonEmptyLoopEvaluator : LoopEvaluator
{
    private static LoopEvaluation ContinueWithAssistant(string feedback) =>
        LoopEvaluation.ContinueWithMessages([new(ChatRole.Assistant, [new TextContent(feedback), new ContinueContent()])]);

    public override ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
    {
        if (context.LastResponse.Messages.LastOrDefault() is not { } message)
        {
            return new ValueTask<LoopEvaluation>(ContinueWithAssistant("继续处理请求,消息不能为空。"));
        }

        if (message.Contents.OfType<TextContent>().LastOrDefault() is TextContent textConten && string.IsNullOrEmpty(textConten.Text))
        {
            return new ValueTask<LoopEvaluation>(ContinueWithAssistant("继续处理请求,完成任务后回复文本消息不能为空。"));
        }

        return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
    }
}
