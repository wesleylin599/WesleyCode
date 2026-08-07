using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Services;

internal sealed class CommandProvider : AIContextProvider
{
    private readonly IOptions<WorkingOptions> _options;

    public CommandProvider(IOptions<WorkingOptions> options)
    {
        this._options = options;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        await using var shellExecutor = new LocalShellExecutor(
            new LocalShellExecutorOptions
            {
                WorkingDirectory = _options.Value.BasePath,
                Timeout = TimeSpan.FromSeconds(15),
                ConfineWorkingDirectory = true,
                AcknowledgeUnsafe = true,
            }
        );
        return new AIContext
        {
            Instructions = $"""
                ## Command
                命令行工具的工作目录路径是`{_options.Value.BasePath}`
                使用`run_command`来调用命令行工具执行命令
                """,
            Tools = [shellExecutor.AsAIFunction(requireApproval: false)],
        };
    }
}
