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
    private const int MaxOutputLine = 10;

    private static readonly UTF8Encoding Utf8StrictEncoding = new(false, true);
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
                当前使用的命令行工具是`{FileName}`
                命令行工具的工作目录在`{_options.Value.BasePath}`
                使用`run_command`来调用命令行工具执行命令
                """,
                Tools = [AIFunctionFactory.Create(Command, new AIFunctionFactoryOptions { Name = "run_command", Description = "执行命令行" })],
            }
        );
    }

    private async Task<string> Command([Description("命令调用模型")] CommandItem item, CancellationToken cancellationToken = default)
    {
        string output = string.Empty;
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

            var result = await cli.ExecuteAsync(timeoutSource.Token);
            var standardOutput = DecodeCommandOutput(standardOutputStream.ToArray());
            var standardError = DecodeCommandOutput(standardErrorStream.ToArray());

            output = FormatCommandResult(result.ExitCode, standardOutput, standardError);
        }
        catch (Exception ex)
        {
            output = $"执行失败: {ex.Message}";
        }

        return output;
    }

    private static string DecodeCommandOutput(byte[] buffer)
    {
        if (buffer.Length == 0)
        {
            return string.Empty;
        }

        foreach (var encoding in GetCommandOutputEncodings())
        {
            if (TryDecode(buffer, encoding, out var text))
            {
                return text;
            }
        }

        return Encoding.Default.GetString(buffer);
    }

    private static IEnumerable<Encoding> GetCommandOutputEncodings()
    {
        var codePages = new HashSet<int>();

        yield return Utf8StrictEncoding;
        codePages.Add(Encoding.UTF8.CodePage);

        foreach (var encoding in new[] { Console.OutputEncoding, Encoding.Default, Encoding.GetEncoding("GB18030") })
        {
            if (codePages.Add(encoding.CodePage))
            {
                yield return Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
        }
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

    private static string FormatCommandResult(int exitCode, string standardOutput, string standardError)
    {
        var output = new StringBuilder();
        output.Append("exit_code: ").AppendLine(exitCode.ToString());

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            output.AppendLine("stdout:");
            var truncatedOutput = standardOutput.TrimEnd().TruncateLine(MaxOutputLine);
            output.AppendLine(truncatedOutput);
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            output.AppendLine("stderr:");
            var truncatedError = standardError.TrimEnd().TruncateLine(MaxOutputLine);
            output.AppendLine(truncatedError);
        }

        if (string.IsNullOrWhiteSpace(standardOutput) && string.IsNullOrWhiteSpace(standardError))
        {
            output.AppendLine("(no output)");
        }

        return output.ToString().TrimEnd();
    }
}

public sealed class CommandItem
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("timeout")]
    public int TimeoutSeconds { get; set; }
}
