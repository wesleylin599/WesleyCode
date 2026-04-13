using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using Microsoft.Extensions.AI;

namespace TestConsole5.Services;

public class ToolManager
{
    private static List<TaskItem> _task = new List<TaskItem>();

    public static AITool CommandFunction = AIFunctionFactory.Create(Command);
    public static AITool WriteFileFunction = AIFunctionFactory.Create(WriteFile);
    public static AITool EditFileFunction = AIFunctionFactory.Create(EditFile);
    public static AITool ReadFileFunction = AIFunctionFactory.Create(ReadFile);
    public static AITool ReadTasksFunction = AIFunctionFactory.Create(ReadTasks);
    public static AITool UpdateTasksFunction = AIFunctionFactory.Create(UpdateTasks);

    public static readonly AITool[] ReadFunctions = [CommandFunction, ReadFileFunction, ReadTasksFunction];
    public static readonly AITool[] AllFunctions = [.. ReadFunctions, WriteFileFunction, EditFileFunction, UpdateTasksFunction];

    [Description("命令行工具(Windows系统使用`powershell`;其他系统为`/bin/bash`),禁止用于文件读写,超时一分钟")]
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
            var cli = OperatingSystem.IsWindows() ? Cli.Wrap("powershell").WithArguments(command) : Cli.Wrap("/bin/bash").WithArguments(command);
            cli = cli.WithWorkingDirectory(Directory.GetCurrentDirectory())
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr));

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

    [Description("读取文件内容并返回实际字符编码,超时一分钟")]
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

            using (var reader = new StreamReader(path, true))
            {
                var encoding = reader.CurrentEncoding.WebName;
                var content = await reader.ReadToEndAsync(cts.Token);
                callback = $"""
                    Readed {path}.
                    encoding: {encoding}
                    content: {content}
                    """;
            }
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

    [Description("写入文件内容,必要时创建目录,超时一分钟")]
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
                Wrote {content.Length} bytes to {path} {encoding}.
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

    [Description("使用正则替换文件内容,默认只替换第一次,超时一分钟")]
    private static async Task<string> EditFile(
        [Description("文件路径")] string path,
        [Description("正则表达式")] string pattern,
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
            Console.WriteLine($"{path}");
            Console.ResetColor();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));

            var enc = Encoding.GetEncoding(encoding);
            var content = await File.ReadAllTextAsync(path, enc, cts.Token);

            var regex = new Regex(pattern, RegexOptions.Multiline);

            if (!regex.IsMatch(content))
            {
                callback = $"Error: 在 {path} 中未匹配到正则";
            }
            else
            {
                await File.WriteAllTextAsync(path, regex.Replace(content, newText), enc, cts.Token);

                callback = $"""
                    Edited {newText.Length} bytes to {path} {encoding}.
                    pattern: {pattern}
                    newText: {newText}
                    """;
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
        [Description("任务标题")] string Title,
        [Description("任务状态")] string Status,
        [Description("任务详情")] string Content,
        [Description("执行结果")] string Result
    );
}
