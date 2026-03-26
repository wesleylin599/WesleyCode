using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using Microsoft.Extensions.AI;

namespace TestConsole5;

public class ToolManager
{
    private const int MaxOutputChars = 50000;
    private static readonly ConcurrentQueue<TaskItem> _tasks = new();
    private static readonly Regex BlockedCommandRegex = new(
        @"(?im)(^|[|;&]\\s*)(get-content|gc|cat|more|less|head|tail|set-content|add-content|out-file|tee-object|tee|new-item|copy-item|move-item|remove-item|del|erase|touch|cp|mv|rm|ni)(\\s|$)",
        RegexOptions.Compiled
    );
    private static readonly Regex RedirectionRegex = new(
        @"(?im)(^|\\s)(\\d*>>|\\d*>|\\d*<|\\*?>>|\\*?>|&>>|&>|<<?|\\|\\s*(?:out-file|set-content|add-content|tee-object|tee)\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static AITool CommandFunction = AIFunctionFactory.Create(Command, null, "命令行工具,不要执行文件读写,超时一分钟");
    public static AITool WriteFileFunction = AIFunctionFactory.Create(WriteFile, null, "写入文件内容，必要时创建目录");
    public static AITool ReadFileFunction = AIFunctionFactory.Create(ReadFile, null, "读取文本文件内容，返回 UTF-8 文本");
    public static AITool EditFileFunction = AIFunctionFactory.Create(EditFile, null, "替换文件中的精确文本，仅替换第一次匹配");
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

            if (ContainsBlockedFileIo(command, out var reason))
            {
                callback = $"Error: 命令包含文件读写操作，已被屏蔽 ({reason})";
            }
            else
            {
                var target = OperatingSystem.IsWindows() ? "powershell" : "/bin/bash";
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var cli = OperatingSystem.IsWindows()
                    ? Cli.Wrap("powershell").WithArguments(args => args.Add("-NoLogo").Add("-NoProfile").Add("-Command").Add(command))
                    : Cli.Wrap("/bin/bash").WithArguments(args => args.Add("-lc").Add(command));
                cli = cli.WithWorkingDirectory(Directory.GetCurrentDirectory())
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout, Encoding.UTF8))
                    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr, Encoding.UTF8));

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMinutes(1));
                var result = await cli.ExecuteAsync(cts.Token);

                var _output = stdout.ToString();
                var _errpr = stderr.ToString();

                var output = _output.Length > MaxOutputChars ? _output[..MaxOutputChars] + "\n...(truncated)" : _output;
                var error = _errpr.Length > MaxOutputChars ? _errpr[..MaxOutputChars] + "\n...(truncated)" : _errpr;

                callback = $"[{result.ExitCode}]{output}{error}";
            }
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

            callback = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
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

            await File.WriteAllTextAsync(path, content, cancellationToken);
            callback = $"""
                Wrote {content.Length} bytes to {path}.
                {content}
                """;
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

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            if (content.Contains(oldText, StringComparison.Ordinal))
            {
                var index = content.IndexOf(oldText, StringComparison.Ordinal);
                var newContent = content[..index] + newText + content[(index + oldText.Length)..];
                await File.WriteAllTextAsync(path, newContent, cancellationToken);
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
        var output = new StringBuilder();
        foreach (var item in args.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (string.IsNullOrEmpty(item))
                continue;
            output.AppendLine(item);
            if (output.Length > 200)
            {
                output.AppendLine("{ truncated } ...... ");
                break;
            }
        }
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(output);
        Console.ResetColor();
    }

    private static bool ContainsBlockedFileIo(string command, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
            return false;

        if (RedirectionRegex.IsMatch(command))
        {
            reason = "redirection";
            return true;
        }

        if (BlockedCommandRegex.IsMatch(command))
        {
            reason = "file command";
            return true;
        }

        return false;
    }

    private record TaskItem(
        [Description("任务详情")] string Content,
        [Description("任务清单")] string Status,
        [Description("执行结果")] string Result
    );
}
