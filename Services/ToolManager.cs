using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using CliWrap;
using Microsoft.Extensions.AI;

namespace TestConsole5.Services;

public class ToolManager
{
    private static readonly ConcurrentQueue<TaskItem> _tasks = new();
    public static AITool CommandFunction = AIFunctionFactory.Create(Command, null, "命令行工具,禁止执行文件读写,超时一分钟");
    public static AITool WriteFileFunction = AIFunctionFactory.Create(WriteFile, null, "写入文件内容,必要时创建目录,超时一分钟");
    public static AITool EditFileFunction = AIFunctionFactory.Create(EditFile, null, "替换文件中的精确文本,仅替换第一次匹配,超时一分钟");
    public static AITool ReadFileFunction = AIFunctionFactory.Create(ReadFile, null, "读取文本文件内容,超时一分钟");
    public static AITool EnqueueTaskFunction = AIFunctionFactory.Create(EnqueueTask, null, "添加任务到队列中");
    public static AITool DequeueTaskFunction = AIFunctionFactory.Create(DequeueTask, null, "从队列中取出任务");
    public static AITool SelectTasksFunction = AIFunctionFactory.Create(SelectTasks, null, "获取任务队列列表");
    public static AITool ClearTasksFunction = AIFunctionFactory.Create(ClearTasks, null, "清理任务队列列表");

    public static readonly AITool[] AllFunctions =
    [
        CommandFunction,
        ReadFileFunction,
        WriteFileFunction,
        EditFileFunction,
        EnqueueTaskFunction,
        DequeueTaskFunction,
        SelectTasksFunction,
        ClearTasksFunction,
    ];

    private static async Task<string> Command([Description("命令")] string command, CancellationToken cancellationToken = default)
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
            var cli = OperatingSystem.IsWindows()
                ? Cli.Wrap("powershell").WithArguments(args => args.Add("-NoLogo").Add("-NoProfile").Add("-Command").Add(command))
                : Cli.Wrap("/bin/bash").WithArguments(args => args.Add("-lc").Add(command));
            cli = cli.WithWorkingDirectory(Directory.GetCurrentDirectory())
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout, Encoding.Default))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr, Encoding.Default));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));
            var result = await cli.ExecuteAsync(cts.Token);

            callback = $"[{result.ExitCode}]{stdout.ToString()}{stderr.ToString()}";
        }
        catch (OperationCanceledException)
        {
            callback = "Error: 命令执行已取消";
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }
        EmitToolEvent(callback);
        return callback;
    }

    private static async Task<string> ReadFile([Description("文件路径")] string path, CancellationToken cancellationToken = default)
    {
        string callback;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ read ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(path);
            Console.ResetColor();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));
            callback = await File.ReadAllTextAsync(path, Encoding.Default, cts.Token);
        }
        catch (OperationCanceledException)
        {
            callback = "Error: 读取文件已取消";
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }
        EmitToolEvent(callback);
        return callback;
    }

    private static async Task<string> WriteFile(
        [Description("文件路径")] string path,
        [Description("文本内容")] string content,
        [Description("写入的字符编码")] string encoding,
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ write ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(path);
            Console.ResetColor();

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));
            await File.WriteAllTextAsync(path, content, Encoding.GetEncoding(encoding), cts.Token);
            callback = $"""
                Wrote {content.Length} bytes to {path}.
                {content}
                """;
        }
        catch (OperationCanceledException)
        {
            callback = "Error: 写入文件已取消";
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }
        EmitToolEvent(callback);
        return callback;
    }

    private static async Task<string> EditFile(
        [Description("文件路径")] string path,
        [Description("旧文本内容")] string oldText,
        [Description("新文本内容")] string newText,
        [Description("写入的字符编码")] string encoding,
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ edit ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(path);
            Console.ResetColor();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));
            var content = await File.ReadAllTextAsync(path, Encoding.Default, cts.Token);
            if (content.Contains(oldText, StringComparison.Ordinal))
            {
                var index = content.IndexOf(oldText, StringComparison.Ordinal);
                var newContent = content[..index] + newText + content[(index + oldText.Length)..];
                await File.WriteAllTextAsync(path, newContent, Encoding.GetEncoding(encoding), cts.Token);
                callback = $"""
                    Edited {path} {oldText.Length} => {newText.Length}.
                    oldText: {oldText}  
                    newText: {newText}
                    """;
            }
            else
            {
                callback = $"Error: 在 {path} 中未找到匹配文本";
            }
        }
        catch (OperationCanceledException)
        {
            callback = "Error: 修改文件已取消";
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }
        EmitToolEvent(callback);
        return callback;
    }

    private static string EnqueueTask([Description("任务内容")] TaskItem item)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("$ enqueue task ");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(item.Content);
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"status: {item.Status} result: {item.Result}");
        Console.ResetColor();

        _tasks.Enqueue(item);
        return $"{item.Content} 任务添加完成";
    }

    private static TaskItem DequeueTask()
    {
        if (!_tasks.TryDequeue(out var item))
            return new TaskItem("获取失败", "Error", "Error");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("$ take task ");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(item.Content);
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"status: {item.Status} result: {item.Result}");
        Console.ResetColor();

        return item;
    }

    private static List<TaskItem> SelectTasks()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"$ select task count {_tasks.Count}");
        Console.ResetColor();

        return _tasks.ToList();
    }

    private static string ClearTasks()
    {
        var count = _tasks.Count;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"$ clear task count {count}");
        Console.ResetColor();

        _tasks.Clear();
        return $"清理 {count} 条任务添加完成";
    }

    private static void EmitToolEvent(string args)
    {
        bool is_truncated = false;
        var output = new StringBuilder();
        var lines = args.Split(["\r\n", "\n"], StringSplitOptions.None).Where(item => !string.IsNullOrEmpty(item)).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (lines.Count > 10 && i > 5 && i < lines.Count - 5)
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
        Console.WriteLine(output);
        Console.ResetColor();
    }

    private record TaskItem(
        [Description("任务详情")] string Content,
        [Description("任务清单")] string Status,
        [Description("执行结果")] string Result
    );
}
