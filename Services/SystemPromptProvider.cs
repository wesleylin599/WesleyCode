using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace WesleyCode.Services;

internal class SystemPromptProvider : AIContextProvider
{
    private const string _promptName = "SYSTEM.md";

    private readonly string _workDirectory;
    private readonly ILogger<SystemPromptProvider> _logger;

    public SystemPromptProvider(string workDirectory, ILoggerFactory? loggerFactory = null)
    {
        _workDirectory = workDirectory;
        this._logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SystemPromptProvider>();
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var local = Path.Combine(_workDirectory, _promptName);
        var main = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _promptName);
        var builder = new StringBuilder();
        builder.AppendLine($"你是位于 {_workDirectory} 的代理工具,使用中文输出;");
        builder.AppendLine("操作时要求简洁高效,最小改动实现需求");
        if (File.Exists(local))
        {
            _logger.LogInformation($"加载`{local}`");
            var prompt = await File.ReadAllTextAsync(local, cancellationToken);
            builder.AppendLine(prompt);
        }
        if (File.Exists(main))
        {
            _logger.LogInformation($"加载`{main}`");
            var prompt = await File.ReadAllTextAsync(main, cancellationToken);
            builder.AppendLine(prompt);
        }
        return new AIContext { Instructions = builder.ToString() };
    }
}
