using System.ComponentModel;
using System.Text;
using CliWrap;
using Microsoft.Extensions.AI;

namespace WesleyCode.Services;

internal static class ToolManager
{
    private const int MaxLogLength = 30000;
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

    [Description("命令行工具,用于执行命令操作")]
    private static async Task<string> Command([Description("命令")] CommandItem command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.FileName))
        {
            return "Error: 工具名称为空";
        }

        var workPath = string.IsNullOrWhiteSpace(command.WorkPath) ? Directory.GetCurrentDirectory() : command.WorkPath;
        if (!Directory.Exists(workPath))
        {
            return $"Error: 工作目录不存在: {workPath}";
        }

        string output = string.Empty;

        try
        {
            var arguments = command.Arguments ?? [];
            var timeoutSeconds = command.TimeoutSeconds <= 0 ? 300 : Math.Min(command.TimeoutSeconds, 3600);
            using var standardOutputStream = new MemoryStream();
            using var standardErrorStream = new MemoryStream();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var cli = Cli.Wrap(command.FileName)
                .WithArguments(arguments)
                .WithWorkingDirectory(workPath)
                .WithStandardOutputPipe(PipeTarget.ToStream(standardOutputStream))
                .WithStandardErrorPipe(PipeTarget.ToStream(standardErrorStream));

            var result = await cli.ExecuteAsync(timeoutSource.Token);
            var standardOutput = DecodeCommandOutput(standardOutputStream.ToArray());
            var standardError = DecodeCommandOutput(standardErrorStream.ToArray());

            output = $"[{result.ExitCode}]{standardOutput}{standardError}";
        }
        catch (Exception ex)
        {
            output = ex.Message;
        }

        return ToolResult(output);
    }

    [Description("更新任务清单,调用需要传入完整的工作清单")]
    private static string UpdateTasks([Description("任务清单列表")] List<TaskItem> tasks)
    {
        Tasks.Clear();
        foreach (var task in tasks ?? [])
        {
            if (string.IsNullOrWhiteSpace(task.Num) || string.IsNullOrWhiteSpace(task.Title))
            {
                continue;
            }
            Tasks.Add(task with { Status = string.IsNullOrWhiteSpace(task.Status) ? "未开始" : task.Status });
        }
        return "完成更新";
    }

    [Description("获取任务清单")]
    private static List<TaskItem> ReadTasks() => Tasks;

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

    private static string ToolResult(string args) =>
        args.Length > MaxLogLength ? $"{args[..(MaxLogLength / 2)]} {{truncated}} {args[(MaxLogLength / 2)..]}" : args;

    private sealed record CommandItem(
        [Description("工具名称")] string FileName,
        [Description("工具工作目录")] string? WorkPath = null,
        [Description("工具参数集合")] List<string>? Arguments = null,
        [Description("执行超时秒数,默认 60 秒,最大 600 秒")] int TimeoutSeconds = 60
    );

    private sealed record TaskItem(
        [Description("任务序号")] string Num,
        [Description("任务标题")] string Title,
        [Description("任务详情")] string Content,
        [Description("执行结果")] string Result,
        [Description("任务状态,只有: 未开始, 进行中, 已完成")] string Status = "未开始"
    );
}
