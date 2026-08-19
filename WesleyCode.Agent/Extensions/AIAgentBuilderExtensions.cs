using Microsoft.Agents.AI;
using WesleyCode.Agent.Infrastructure;
using WesleyCode.Agent.Interfaces;

namespace WesleyCode.Agent.Extensions;

/// <summary>
/// 提供 <see cref="AIAgentBuilder"/> 的组装扩展方法。
/// </summary>
public static class AIAgentBuilderExtensions
{
    /// <summary>
    /// 包装 Agent，使其在运行过程中将输出转发到指定的 <paramref name="capture"/>。
    /// </summary>
    public static AIAgentBuilder UseAgentOutput(this AIAgentBuilder builder, IOutputCapture capture) =>
        builder.Use(innerAgent => new OutputAgent(innerAgent, capture));

    /// <summary>
    /// 为 Agent 注册循环执行器，直到满足 <see cref="NonEmptyLoopEvaluator"/> 的完成条件。
    /// </summary>
    public static AIAgentBuilder UseAgentLoop(this AIAgentBuilder builder, string completionMarker) =>
        builder.Use(innerAgent => new LoopAgent(
            innerAgent,
            new NonEmptyLoopEvaluator(completionMarker),
            new LoopAgentOptions { OnBehalfOfAuthorName = "loop", ExcludeOnBehalfOfMessages = true }
        ));
}
