using System.ComponentModel;
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
    private static readonly string FileName = OperatingSystem.IsWindows() ? "powershell" : "bin/bash";

    private readonly IOptions<WorkingOptions> _options;

    public CommandProvider(IOptions<WorkingOptions> options)
    {
        this._options = options;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(
            new AIContext
            {
                Instructions = $"""
                ## Command
                当前使用的命令行工具是`{FileName}`
                命令行工具的工作目录路径是`{_options.Value.BasePath}`
                使用`run_command`来调用命令行工具执行命令
                """,
                Tools =
                [
                    AIFunctionFactory.Create(CommandRunAsync, new AIFunctionFactoryOptions { Name = "command_run", Description = "执行命令行" }),
                ],
            }
        );
    }

    private async Task<CommandRunResult> CommandRunAsync(
        [Description("命令行")] string command,
        [Description("执行超时时间/秒")] int timeout,
        CancellationToken cancellationToken = default
    )
    {
        CommandRunResult output = new CommandRunResult();
        try
        {
            if (string.IsNullOrEmpty(command))
                throw new ArgumentNullException(nameof(command));

            var timeoutSeconds = timeout <= 0 ? 300 : Math.Min(timeout, 3600);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var standardOutput = new MemoryStream();
            using var standardError = new MemoryStream();

            var cli = Cli.Wrap(FileName)
                .WithArguments(
                    FileName switch
                    {
                        "bin/bash" => ["--noprofile", "--norc", "-c", command],
                        "powershell" => ["-NoProfile", "-NoLogo", "-NonInteractive", "-Command", command],
                        _ => throw new InvalidOperationException($"Unsupported shell: {FileName}"),
                    }
                )
                .WithWorkingDirectory(_options.Value.BasePath)
                .WithStandardOutputPipe(PipeTarget.ToStream(standardOutput))
                .WithStandardErrorPipe(PipeTarget.ToStream(standardError))
                .WithValidation(CommandResultValidation.None);

            var execute = await cli.ExecuteAsync(timeoutSource.Token);
            output.ExitCode = execute.ExitCode;
            output.Output = standardOutput.DecodeOutput();
            output.Error = standardError.DecodeOutput();
        }
        catch (Exception ex)
        {
            output.Error = $"调用失败 {ex.Message} 修复后重试";
        }

        return output;
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
}
