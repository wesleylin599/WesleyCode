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
    private static readonly string fileName = OperatingSystem.IsWindows() ? "powershell" : "bash";

    private readonly IOptions<WorkingOptions> _options;

    public CommandProvider(IOptions<WorkingOptions> options)
    {
        this._options = options;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            new AIContext
            {
                Instructions = $"""
                ## Command Environment
                当前使用的命令行工具是`{fileName}`
                命令行工具的工作目录路径是`{_options.Value.BasePath}`
                使用`command_run`来调用命令行工具执行命令
                """,
                Tools =
                [
                    AIFunctionFactory.Create(CommandRunAsync, new AIFunctionFactoryOptions { Name = "command_run", Description = "执行命令行" }),
                ],
            }
        );

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

            List<string> arguments = fileName switch
            {
                "bash" => ["--noprofile", "--norc", "-c", command],
                "powershell" => ["-NoProfile", "-NoLogo", "-NonInteractive", "-Command", command],
                _ => throw new InvalidOperationException($"Unsupported shell: {fileName}"),
            };

            var timeoutSeconds = timeout <= 0 ? 300 : Math.Min(timeout, 3600);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var standardOutput = new MemoryStream();
            using var standardError = new MemoryStream();

            var cli = Cli.Wrap(fileName)
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
        catch (Exception ex)
        {
            output.ExitCode = -1;
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
