using System.Text;
using Microsoft.Agents.AI;

namespace TestConsole5.Services;

internal class SystemPromptProvider : AIContextProvider
{
    private const string _promptName = "SYSTEM.md";

    private readonly string _workDirectory;

    public SystemPromptProvider(string workDirectory)
    {
        _workDirectory = workDirectory;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var local = Path.Combine(_workDirectory, _promptName);
        var main = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _promptName);
        var builder = new StringBuilder();
        builder.AppendLine($"你是位于 {_workDirectory} 的代理工具,使用中文输出;");
        if (File.Exists(local))
        {
            var prompt = await File.ReadAllTextAsync(local);
            builder.AppendLine(prompt);
        }
        if (File.Exists(main))
        {
            var prompt = await File.ReadAllTextAsync(main);
            builder.AppendLine(prompt);
        }
        return new AIContext { Instructions = builder.ToString() };
    }
}
