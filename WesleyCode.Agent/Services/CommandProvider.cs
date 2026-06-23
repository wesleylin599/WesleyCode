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
    private static readonly Encoding[] CommonEncodings;
    private static readonly string FileName = OperatingSystem.IsWindows() ? "powershell" : "bin/bash";

    static CommandProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CommonEncodings = [new UTF8Encoding(false, true), Console.OutputEncoding, Encoding.UTF8, Encoding.Default, Encoding.GetEncoding("GB18030")];
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
                当前使用的命令行工具是`{FileName}`
                命令行工具的工作目录在`{_options.Value.BasePath}`
                使用`run_command`来调用命令行工具执行命令
                """,
                Tools = [AIFunctionFactory.Create(Command, new AIFunctionFactoryOptions { Name = "run_command", Description = "执行命令行" })],
            }
        );
    }

    private async Task<CommandResult> Command([Description("命令调用模型")] CommandItem item, CancellationToken cancellationToken = default)
    {
        CommandResult output = new CommandResult();
        try
        {
            var timeoutSeconds = item.TimeoutSeconds <= 0 ? 300 : Math.Min(item.TimeoutSeconds, 3600);
            using var standardOutputStream = new MemoryStream();
            using var standardErrorStream = new MemoryStream();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var cli = Cli.Wrap(FileName)
                .WithArguments(item.Command ?? string.Empty)
                .WithWorkingDirectory(_options.Value.BasePath)
                .WithStandardOutputPipe(PipeTarget.ToStream(standardOutputStream))
                .WithStandardErrorPipe(PipeTarget.ToStream(standardErrorStream))
                .WithValidation(CommandResultValidation.None);

            var execute = await cli.ExecuteAsync(timeoutSource.Token);
            var standardOutput = DecodeCommandOutput(standardOutputStream.ToArray());
            var standardError = DecodeCommandOutput(standardErrorStream.ToArray());

            output.ExitCode = execute.ExitCode;
            output.Output = standardOutput;
            output.Error = standardError;
        }
        catch (Exception ex)
        {
            output.Error = ex.Message;
        }

        return output;
    }

    private static string DecodeCommandOutput(byte[] buffer)
    {
        if (buffer.Length == 0)
        {
            return string.Empty;
        }

        foreach (var encoding in CommonEncodings)
        {
            if (TryDecode(buffer, encoding, out var text))
            {
                return text;
            }
        }

        return Encoding.Default.GetString(buffer);
    }

    private static bool TryDecode(byte[] buffer, Encoding encoding, out string text)
    {
        try
        {
            text = encoding.GetString(buffer);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }
}

public sealed class CommandItem
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; }
}

public sealed class CommandResult
{
    [JsonPropertyName("exit_code")]
    public int ExitCode { get; set; }

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}
