using System.ComponentModel;
using System.Text;
using CliWrap;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.AI;

namespace WesleyCode.Services;

public class ToolManager
{
    private const int MAX_LOG_LINE = 10;
    private const int MAX_LOG_LENGTH = 30000;

    private static readonly List<TaskItem> _task = new();
    private static readonly HttpClient _httpClient = new HttpClient();

    public static AITool CommandFunction = AIFunctionFactory.Create(Command);
    public static AITool ReadFileFunction = AIFunctionFactory.Create(ReadFile);
    public static AITool WebSearchFunction = AIFunctionFactory.Create(WebSearch);
    public static AITool ReadTasksFunction = AIFunctionFactory.Create(ReadTasks);
    public static AITool EditFileFunction = AIFunctionFactory.Create(EditFile);
    public static AITool WriteFileFunction = AIFunctionFactory.Create(WriteFile);
    public static AITool UpdateTasksFunction = AIFunctionFactory.Create(UpdateTasks);

    public static readonly AITool[] ReadFunctions = [CommandFunction, ReadFileFunction, WebSearchFunction, ReadTasksFunction];
    public static readonly AITool[] AllFunctions = [.. ReadFunctions, EditFileFunction, WriteFileFunction, UpdateTasksFunction];

    [Description("命令行工具,用于执行命令操作,禁止文件读写")]
    private static async Task<string> Command([Description("命令")] CommandItem command, CancellationToken cancellationToken = default)
    {
        string callback;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ ran ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{command.FileName} {string.Join(" ", command.Arguments)}");
            Console.ResetColor();

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var cli = Cli.Wrap(command.FileName)
                .WithArguments(command.Arguments)
                .WithWorkingDirectory(Directory.GetCurrentDirectory())
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr));

            var result = await cli.ExecuteAsync(cancellationToken);

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

    [Description("读取文件内容并返回实际字符编码")]
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

            using var reader = new StreamReader(path, true);
            var encoding = reader.CurrentEncoding.WebName;
            var content = await reader.ReadToEndAsync(cancellationToken);
            callback = $"""
                encoding: {encoding}
                {content}
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

    [Description("请求网络地址获取结果")]
    private static async Task<string> WebSearch([Description("请求地址")] string url, CancellationToken cancellationToken = default)
    {
        string callback;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ web search ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(url);
            Console.ResetColor();

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            callback = await response.Content.ReadAsStringAsync(cancellationToken);
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
            Console.WriteLine($"[{item.Status}]{item.Num} {item.Title}");
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

    [Description("写入文件内容,必要时创建目录")]
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
            Console.Write("$ write file ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(path);
            Console.ResetColor();

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(path, content, Encoding.GetEncoding(encoding), cancellationToken);
            callback = $"""
                encoding: {encoding}
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
        ToolConsoleLog(callback);
        return ToolResult(callback);
    }

    [Description("替换文件中的文本内容")]
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
            Console.Write("$ edit file ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{path}");
            Console.ResetColor();

            var content = File.Exists(path) ? await File.ReadAllTextAsync(path, Encoding.GetEncoding(encoding), cancellationToken) : string.Empty;

            if (string.IsNullOrEmpty(oldText))
                callback = "Error: 旧文本内容为空";
            else if (!content.Contains(oldText))
                callback = "Error: 文件内容不包含旧文本内容";
            else
            {
                await File.WriteAllTextAsync(path, content.Replace(oldText, newText), Encoding.GetEncoding(encoding), cancellationToken);
                callback = $"""
                    encoding: {encoding}
                    {BuildDiff(oldText, newText)}
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

        ToolConsoleLog(callback);
        return ToolResult(callback);
    }

    public static string BuildDiff(string oldText, string newText)
    {
        var sb = new StringBuilder();
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);

        foreach (var line in diff.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    sb.AppendLine($"A {line.Text}");
                    break;

                case ChangeType.Deleted:
                    sb.AppendLine($"D {line.Text}");
                    break;

                case ChangeType.Modified:
                    sb.AppendLine($"U {line.Text}");
                    break;
            }
        }

        return sb.ToString();
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

    private record CommandItem([Description("工具名称")] string FileName, [Description("工具参数集合")] List<string> Arguments);

    private record TaskItem(
        [Description("任务序号")] string Num,
        [Description("任务标题")] string Title,
        [Description("任务状态")] string Status,
        [Description("任务详情")] string Content,
        [Description("执行结果")] string Result
    );
}
