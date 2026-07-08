using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Services;

internal sealed class SystemPromptProvider : AIContextProvider
{
    private const string SystemPromptName = "SYSTEM.md";

    private readonly IOptions<WorkingOptions> _options;

    public SystemPromptProvider(IOptions<WorkingOptions> options)
    {
        _options = options;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
        new AIContext { Instructions = await BuildPromptAsync(cancellationToken) };

    private async Task<string> BuildPromptAsync(CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## System Prompt");
        builder.AppendLine($"当前操作系统是\"{Environment.OSVersion.VersionString}\"");
        builder.AppendLine($"当前工作目录是\"{_options.Value.BasePath}\"");

        var promptFiles = EnumeratePromptFiles().ToList();
        foreach (var path in promptFiles)
        {
            var prompt = await File.ReadAllTextAsync(path, cancellationToken);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

            builder.AppendLine(prompt);
        }

        return builder.ToString().Trim();
    }

    private IEnumerable<string> EnumeratePromptFiles()
    {
        var localSystemPath = Path.Combine(_options.Value.BasePath, SystemPromptName);
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
