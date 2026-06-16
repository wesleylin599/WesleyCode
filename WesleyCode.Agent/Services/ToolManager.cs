using System.ComponentModel;
using System.Text;
using CliWrap;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Services;

internal sealed record CommandItem(
    [Description("命令行")] string? Command = null,
    [Description("执行超时秒数,默认 60 秒,最大 600 秒")] int TimeoutSeconds = 60
);

internal sealed record TaskItem(
    [Description("任务序号")] int Num,
    [Description("任务描述")] string Description,
    [Description("任务状态")] string? Status = null
);

internal static class ToolManager
{
    private static readonly List<TaskItem> Tasks = [];
    private static readonly UTF8Encoding Utf8StrictEncoding = new(false, true);

    static ToolManager()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static readonly AITool CommandFunction = AIFunctionFactory.Create(Command);
    public static readonly AITool ReadTasksFunction = AIFunctionFactory.Create(ReadTasks);
    public static readonly AITool UpdateTasksFunction = AIFunctionFactory.Create(UpdateTasks);

    public static readonly AITool[] ReadFunctions = [CommandFunction, ReadTasksFunction];
    public static readonly AITool[] AllFunctions = [.. ReadFunctions, UpdateTasksFunction];
    public static readonly string FileName = OperatingSystem.IsWindows() ? "powershell" : "bin/bash";

    [Description("使用命令行工具执行命令")]
    private static async Task<string> Command([Description("命令调用模型")] CommandItem item, CancellationToken cancellationToken = default)
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
                .WithWorkingDirectory(Directory.GetCurrentDirectory())
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
            output = ex.Message;
        }

        return output;
    }

    [Description("更新任务清单")]
    private static string UpdateTasks([Description("任务清单列表")] List<TaskItem> tasks)
    {
        Tasks.Clear();
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine($"任务清单已更新,共 {tasks.Count} 条任务");
        foreach (var task in tasks ?? [])
        {
            if (string.IsNullOrWhiteSpace(task.Num + task.Description))
                continue;
            Tasks.Add(task);
            stringBuilder.AppendLine($"[{task.Status ?? "未开始"}]{task.Num} {task.Description}");
        }
        return stringBuilder.ToString();
    }

    [Description("获取一条未开始的任务")]
    private static List<TaskItem> ReadTasks() => Tasks.OrderBy(x => x.Num).ToList();

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
            output.AppendLine(standardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            output.AppendLine("stderr:");
            output.AppendLine(standardError.TrimEnd());
        }

        if (string.IsNullOrWhiteSpace(standardOutput) && string.IsNullOrWhiteSpace(standardError))
        {
            output.AppendLine("(no output)");
        }

        return output.ToString().TrimEnd();
    }
}
