using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using CliWrap;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Services;

internal sealed class CommandProvider : AIContextProvider
{
    private static readonly CommandFamily family = OperatingSystem.IsWindows() ? CommandFamily.PowerShell : CommandFamily.Bash;

    private const int DefaultTimeoutSeconds = 300;
    private const int MaxTimeoutSeconds = 3600;

    private readonly IOptions<WorkingOptions> _options;

    public CommandProvider(IOptions<WorkingOptions> options)
    {
        this._options = options;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            new AIContext
            {
                Instructions = DefaultInstructionsFormatter(family, _options.Value.BasePath),
                Tools =
                [
                    AIFunctionFactory.Create(CommandRunAsync, new AIFunctionFactoryOptions { Name = "command_run", Description = "执行命令行" }),
                ],
            }
        );

    private async Task<CommandRunResult> CommandRunAsync(
        [Description("命令行")] string command,
        [Description("执行超时时间（秒）")] int timeout,
        CancellationToken cancellationToken = default
    )
    {
        CommandRunResult output = new CommandRunResult();
        using var standardOutput = new MemoryStream();
        using var standardError = new MemoryStream();
        var timeoutSeconds = timeout <= 0 ? DefaultTimeoutSeconds : Math.Min(timeout, MaxTimeoutSeconds);

        try
        {
            if (string.IsNullOrEmpty(command))
                throw new ArgumentNullException(nameof(command));

            List<string> arguments = family switch
            {
                CommandFamily.Bash => ["--noprofile", "--norc", "-c", command],
                CommandFamily.PowerShell => ["-NoProfile", "-NoLogo", "-NonInteractive", "-Command", command],
                _ => throw new InvalidOperationException($"Unsupported shell: {family}"),
            };

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var cli = Cli.Wrap(family.ToString())
                .WithArguments(arguments)
                .WithWorkingDirectory(_options.Value.BasePath)
                .WithStandardOutputPipe(PipeTarget.ToStream(standardOutput))
                .WithStandardErrorPipe(PipeTarget.ToStream(standardError))
                .WithValidation(CommandResultValidation.None);

            var execute = await cli.ExecuteAsync(timeoutSource.Token);
            output.ExitCode = execute.ExitCode;
            output.Output = standardOutput.DecodeOutput();
            output.Error = standardError.DecodeOutput();
        }
        catch (OperationCanceledException)
        {
            output.ExitCode = 124;
            output.Output = standardOutput.DecodeOutput();
            output.Error = $"命令执行超时（>{timeoutSeconds} 秒），已返回部分或没有输出。";
        }
        catch (Exception ex)
        {
            output.ExitCode = -1;
            output.Output = standardOutput.DecodeOutput();
            output.Error = $"调用失败 {ex.Message} 修复后重试";
        }

        return output;
    }

    private static string DefaultInstructionsFormatter(CommandFamily family, string working)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("## Command environment");

        if (family == CommandFamily.PowerShell)
        {
            _ = sb.Append("你正在使用 PowerShell。");
            _ = sb.AppendLine("请使用 PowerShell 语法，而不是 bash：");
            _ = sb.AppendLine("- 使用 `$env:NAME = 'value'` 设置环境变量（不要使用 `NAME=value`）。");
            _ = sb.AppendLine("- 使用 `Set-Location` 或 `cd` 切换目录。路径使用 `\\` 作为分隔符。");
            _ = sb.AppendLine("- 使用 `$env:NAME` 引用环境变量（不要使用 `$NAME`）。");
            _ = sb.AppendLine("- 系统临时目录为 `[System.IO.Path]::GetTempPath()`（不要使用 `/tmp`）。");
            _ = sb.AppendLine("- 使用 `Out-Null` 管道来抑制输出（不要使用 `> /dev/null`）。");
        }
        else
        {
            _ = sb.Append("你正在使用 Bash。");
            _ = sb.AppendLine("请使用 Bash 语法。");
            _ = sb.AppendLine("- 使用 `export NAME=value` 为后续命令设置环境变量。");
            _ = sb.AppendLine("- 使用 `$NAME` 或 `${NAME}` 引用环境变量。");
            _ = sb.AppendLine("- 路径使用 `/` 作为分隔符。");
        }

        _ = sb.Append("工作目录：").AppendLine(working);
        _ = sb.Append("使用 `command_run` 来调用命令行工具执行命令");

        return sb.ToString().TrimEnd();
    }

    private sealed class CommandRunResult
    {
        [JsonPropertyName("exit_code")]
        public int ExitCode { get; set; }

        [JsonPropertyName("output")]
        public string Output { get; set; } = string.Empty;

        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }

    private enum CommandFamily
    {
        PowerShell,
        Bash,
    }
}
