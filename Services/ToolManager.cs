using System.ComponentModel;
using System.Text;
using CliWrap;
using Microsoft.Extensions.AI;

namespace TestConsole5.Services;

public class ToolManager
{
    private const int MAX_LOG_LINE = 10;
    private const int MAX_LOG_LENGTH = 30000;

    private static readonly List<TaskItem> _task = new();

    public static AITool CommandFunction = AIFunctionFactory.Create(Command);
    public static AITool WriteFileFunction = AIFunctionFactory.Create(WriteFile);
    public static AITool ReadFileFunction = AIFunctionFactory.Create(ReadFile);
    public static AITool ReadTasksFunction = AIFunctionFactory.Create(ReadTasks);
    public static AITool UpdateTasksFunction = AIFunctionFactory.Create(UpdateTasks);

    public static readonly AITool[] ReadFunctions = [CommandFunction, ReadFileFunction, ReadTasksFunction];
    public static readonly AITool[] AllFunctions = [.. ReadFunctions, WriteFileFunction, UpdateTasksFunction];

    [Description("命令行工具,用于执行命令操作,超时一分钟")]
    private static async Task<string> Command([Description("命令,禁止文件读写命令")] string command, CancellationToken cancellationToken = default)
    {
        string callback;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ ran ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(command);
            Console.ResetColor();

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var cli = OperatingSystem.IsWindows() ? Cli.Wrap("powershell").WithArguments(command) : Cli.Wrap("/bin/bash").WithArguments(command);
            cli = cli.WithWorkingDirectory(Directory.GetCurrentDirectory())
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));
            var result = await cli.ExecuteAsync(cts.Token);

            callback = $"[{result.ExitCode}]{stdout}{stderr}";
        }
        catch (OperationCanceledException)
        {
            callback = "Error: 命令执行已取消";
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }
        ToolConsoleLog(callback);
        return ToolResult(callback);
    }

    [Description("读取文件内容并返回实际字符编码,超时一分钟")]
    private static async Task<string> ReadFile([Description("文件路径")] string path, CancellationToken cancellationToken = default)
    {
        string callback;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ read file ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(path);
            Console.ResetColor();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));

            using var reader = new StreamReader(path, true);
            var encoding = reader.CurrentEncoding.WebName;
            var content = await reader.ReadToEndAsync(cts.Token);
            StringBuilder stringBuilder = new StringBuilder();
            var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                stringBuilder.AppendLine($"[{i}] {lines[i]}");
            }
            callback = $"""
                encoding: {encoding}
                {stringBuilder}
                """;
        }
        catch (OperationCanceledException)
        {
            callback = "Error: 读取文件已取消";
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        ToolConsoleLog(callback);
        return ToolResult(callback);
    }

    [Description("按照行号操作文件内容,必要时创建目录,超时一分钟")]
    private static async Task<string> WriteFile(
        [Description("文件路径")] string path,
        [Description("行操作列表")] List<LineItem> contents,
        [Description("写入的字符编码")] string encoding,
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ write file ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{path}");
            Console.ResetColor();

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));

            var content = File.Exists(path) ? await File.ReadAllTextAsync(path, Encoding.GetEncoding(encoding), cts.Token) : string.Empty;
            var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();

            StringBuilder stringBuilder = new StringBuilder();
            foreach (var line in contents)
            {
                var index = line.LineNumber;
                var target = line.Content.Split(["\r\n", "\n"], StringSplitOptions.None);
                foreach (var item in target)
                {
                    if (string.IsNullOrWhiteSpace(item))
                        continue;
                    switch (line.Operation)
                    {
                        case LineOperation.Add:
                            stringBuilder.AppendLine($"[{index}] ADDED {item}");
                            lines.Insert(index, item);
                            break;
                        case LineOperation.Update:
                            stringBuilder.AppendLine($"[{index}] UPDATED {lines[index]} => {item}");
                            lines[index] = item;
                            break;
                        case LineOperation.Delete:
                            stringBuilder.AppendLine($"[{index}] DELETED");
                            lines.RemoveAt(index);
                            break;
                    }
                }
            }

            await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, lines), Encoding.GetEncoding(encoding), cts.Token);

            callback = $"""
                encoding: {encoding}
                {stringBuilder}
                """;
        }
        catch (OperationCanceledException)
        {
            callback = "Error: 修改文件已取消";
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        ToolConsoleLog(callback);
        return ToolResult(callback);
    }

    [Description("更新任务清单,调用需要传入完整的工作清单,任务状态只有: 未开始, 进行中, 已完成")]
    private static string UpdateTasks([Description("任务清单列表")] List<TaskItem> tasks)
    {
        _task.Clear();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("$ chage tasks ");
        Console.ResetColor();
        foreach (var item in tasks)
        {
            _task.Add(item);
            Console.WriteLine($"[{item.Status}]{item.Title}");
        }
        return "完成更新";
    }

    [Description("获取任务清单")]
    private static List<TaskItem> ReadTasks()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"$ read {_task.Count} tasks ");
        Console.ResetColor();
        return _task;
    }

    private static void ToolConsoleLog(string args)
    {
        bool is_truncated = false;
        var output = new StringBuilder();
        var lines = args.Split(["\r\n", "\n"], StringSplitOptions.None).Where(item => !string.IsNullOrEmpty(item)).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (lines.Count > MAX_LOG_LINE && i > MAX_LOG_LINE / 2 && i < lines.Count - MAX_LOG_LINE / 2)
            {
                if (!is_truncated)
                {
                    output.AppendLine("{ truncated }");
                    is_truncated = true;
                }
                continue;
            }
            output.AppendLine(line);
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(ToolResult(output.ToString()));
        Console.ResetColor();
    }

    private static string ToolResult(string args)
    {
        return args.Length > MAX_LOG_LENGTH ? $"{args[..MAX_LOG_LENGTH]} ..." : args;
    }

    private readonly record struct LineItem(
        [Description("行号")] int LineNumber,
        [Description("行内容")] string Content,
        [Description("行操作")] LineOperation Operation
    );

    private readonly record struct TaskItem(
        [Description("任务标题")] string Title,
        [Description("任务状态")] string Status,
        [Description("任务详情")] string Content,
        [Description("执行结果")] string Result
    );

    private enum LineOperation
    {
        Add,
        Update,
        Delete,
    }
}
