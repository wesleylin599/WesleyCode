using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using CliWrap;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Services;

internal sealed class CommandProvider : AIContextProvider
{
    private static readonly string FileName = OperatingSystem.IsWindows() ? "powershell" : "bin/bash";

    static CommandProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

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
                命令行工具的工作目录在`{_options.Value.BasePath}`
                使用`run_command`来调用命令行工具执行命令
                确保命令输出的字符编码为UTF8
                禁止用于文件写入操作
                """,
                Tools = [AIFunctionFactory.Create(Command, new AIFunctionFactoryOptions { Name = "command_run", Description = "执行命令行" })],
            }
        );
    }

    private async Task<CommandResult> Command([Description("命令调用模型")] CommandItem item, CancellationToken cancellationToken = default)
    {
        CommandResult output = new CommandResult();
        try
        {
            if (string.IsNullOrEmpty(item.Command))
                throw new ArgumentNullException(nameof(item.Command));

            var timeoutSeconds = item.TimeoutSeconds <= 0 ? 300 : Math.Min(item.TimeoutSeconds, 3600);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();

            var cli = Cli.Wrap(FileName)
                .WithArguments(item.Command)
                .WithWorkingDirectory(_options.Value.BasePath)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(standardOutput, Encoding.UTF8))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(standardError, Encoding.UTF8))
                .WithValidation(CommandResultValidation.None);

            var execute = await cli.ExecuteAsync(timeoutSource.Token);
            output.ExitCode = execute.ExitCode;
            output.Output = standardOutput.ToString();
            output.Error = standardError.ToString();
        }
        catch (Exception ex)
        {
            output.Error = $"调用失败 {ex.Message} 修复后重试";
        }

        return output;
    }

    sealed class CommandItem
    {
        [Description("命令行")]
        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [Description("执行超时时间")]
        [JsonPropertyName("timeout_seconds")]
        public int TimeoutSeconds { get; set; }
    }

    sealed class CommandResult
    {
        [JsonPropertyName("exit_code")]
        public int ExitCode { get; set; }

        [JsonPropertyName("output")]
        public string Output { get; set; } = string.Empty;

        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }
}
