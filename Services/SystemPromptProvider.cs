using Microsoft.Agents.AI;

namespace TestConsole5.Services;

internal class SystemPromptProvider : AIContextProvider
{
    private readonly string _workDirectory;

    public SystemPromptProvider(string workDirectory)
    {
        _workDirectory = workDirectory;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new AIContext { Instructions = $"你是位于 {_workDirectory} 的代理工具,使用中文输出;" });
    }
}
