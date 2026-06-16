using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WesleyCode.Agent.Services;

internal sealed class SystemPromptProvider : AIContextProvider
{
    private const string SystemPromptName = "SYSTEM.md";

    private readonly string _workDirectory;
    private readonly ILogger<SystemPromptProvider> _logger;
    private string? _agentPrompt;

    public SystemPromptProvider(string workDirectory, ILoggerFactory? loggerFactory = null)
    {
        _workDirectory = workDirectory;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SystemPromptProvider>();
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_agentPrompt))
        {
            return new AIContext { Instructions = _agentPrompt };
        }

        _agentPrompt ??= await BuildPromptAsync(cancellationToken);

        return new AIContext { Instructions = _agentPrompt };
    }

    private async Task<string> BuildPromptAsync(CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"你是位于\"{_workDirectory}\"的代理工具;");
        foreach (var path in EnumeratePromptFiles())
        {
            _logger.LogDebug("加载提示词文件 `{PromptPath}`", path);
            var prompt = await File.ReadAllTextAsync(path, cancellationToken);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

            builder.AppendLine($"以下附加指令来自 {path}:");
            builder.AppendLine(prompt);
        }

        return builder.ToString().Trim();
    }

    private IEnumerable<string> EnumeratePromptFiles()
    {
        var localSystemPath = Path.Combine(_workDirectory, SystemPromptName);
        if (File.Exists(localSystemPath))
        {
            yield return localSystemPath;
        }

        var baseSystemPath = Path.Combine(AppContext.BaseDirectory, SystemPromptName);
        if (File.Exists(baseSystemPath) && !string.Equals(baseSystemPath, localSystemPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return baseSystemPath;
        }
    }
}
