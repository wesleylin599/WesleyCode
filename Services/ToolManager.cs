using System.ComponentModel;
using System.Text;
using CliWrap;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.AI;
using WesleyCode.Infrastructure;

namespace WesleyCode.Services;

public class ToolManager
{
    private const int MAX_LOG_LINE = 10;
    private const int MAX_LOG_LENGTH = 30000;
    private static readonly UTF8Encoding Utf8StrictEncoding = new(false, true);

    private static readonly List<TaskItem> _task = new();
    private static readonly object TaskSync = new();
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    static ToolManager()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

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
            using var standardOutputStream = new MemoryStream();
            using var standardErrorStream = new MemoryStream();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ run ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{command.FileName} {string.Join(" ", command.Arguments)}");
            Console.ResetColor();

            var cli = Cli.Wrap(command.FileName)
                .WithArguments(command.Arguments)
                .WithWorkingDirectory(command.WorkingDirectory)
                .WithStandardOutputPipe(PipeTarget.ToStream(standardOutputStream))
                .WithStandardErrorPipe(PipeTarget.ToStream(standardErrorStream))
                .WithValidation(CommandResultValidation.None);

            var result = await cli.ExecuteAsync(cancellationToken);
            var standardOutput = DecodeCommandOutput(standardOutputStream.ToArray());
            var standardError = DecodeCommandOutput(standardErrorStream.ToArray());

            callback = $"[{result.ExitCode}]{standardOutput}{standardError}";
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
            var fullPath = Path.GetFullPath(path);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ read file ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(fullPath);
            Console.ResetColor();

            var file = await TextFileUtility.ReadAsync(fullPath, cancellationToken);
            callback = $"""
                path: {fullPath}
                encoding: {file.EncodingName}
                {file.Content}
                """;
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
        lock (TaskSync)
        {
            _task.Clear();
            foreach (var item in tasks)
            {
                _task.Add(item);
            }
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("$ change tasks ");
        Console.ResetColor();
        foreach (var item in tasks)
        {
            Console.WriteLine($"[{item.Status}]{item.Num} {item.Title}");
        }
        return "完成更新";
    }

    [Description("获取任务清单")]
    private static List<TaskItem> ReadTasks()
    {
        List<TaskItem> snapshot;
        lock (TaskSync)
        {
            snapshot = _task.Select(item => item with { }).ToList();
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"$ read {snapshot.Count} tasks ");
        Console.ResetColor();
        return snapshot;
    }

    [Description("写入文件内容,必要时创建目录")]
    private static async Task<string> WriteFile(
        [Description("文件路径")] string path,
        [Description("文本内容")] string content,
        [Description("写入的字符编码,支持 auto / utf-8 / utf-8-bom")] string encoding,
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            var fullPath = Path.GetFullPath(path);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ write file ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(fullPath);
            Console.ResetColor();

            TextFileContent? existingFile = null;
            if (File.Exists(fullPath))
            {
                existingFile = await TextFileUtility.ReadAsync(fullPath, cancellationToken);
            }

            var resolvedEncoding = TextFileUtility.ResolveEncoding(encoding, existingFile?.Encoding);
            var normalizedContent = existingFile is null ? content : TextFileUtility.NormalizeLineEndings(content, existingFile.LineEnding);
            await TextFileUtility.WriteAllTextAsync(fullPath, normalizedContent, resolvedEncoding, cancellationToken);
            callback = $"""
                path: {fullPath}
                encoding: {TextFileUtility.GetEncodingName(resolvedEncoding)}
                {normalizedContent}
                """;
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
        [Description("写入的字符编码,支持 auto / utf-8 / utf-8-bom")] string encoding,
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            var fullPath = Path.GetFullPath(path);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("$ edit file ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(fullPath);
            Console.ResetColor();

            var file = File.Exists(fullPath) ? await TextFileUtility.ReadAsync(fullPath, cancellationToken) : null;

            if (string.IsNullOrEmpty(oldText))
            {
                callback = "Error: 旧文本内容为空";
            }
            else if (file is null)
            {
                callback = "Error: 文件不存在";
            }
            else if (!file.Content.Contains(oldText, StringComparison.Ordinal))
            {
                callback = "Error: 文件内容不包含旧文本内容";
            }
            else
            {
                var replacedCount = CountOccurrences(file.Content, oldText);
                var replacedContent = file.Content.Replace(oldText, newText, StringComparison.Ordinal);
                var normalizedContent = TextFileUtility.NormalizeLineEndings(replacedContent, file.LineEnding);
                var resolvedEncoding = TextFileUtility.ResolveEncoding(encoding, file.Encoding);
                await TextFileUtility.WriteAllTextAsync(fullPath, normalizedContent, resolvedEncoding, cancellationToken);
                callback = $"""
                    path: {fullPath}
                    encoding: {TextFileUtility.GetEncodingName(resolvedEncoding)}
                    replace_count: {replacedCount}
                    {BuildDiff(oldText, newText)}
                    """;
            }
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

    private static int CountOccurrences(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var count = 0;
        var startIndex = 0;
        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private static void ToolConsoleLog(string args)
    {
        bool isTruncated = false;
        var output = new StringBuilder();
        var lines = args.Split(["\r\n", "\n"], StringSplitOptions.None).Where(item => !string.IsNullOrEmpty(item)).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (lines.Count > MAX_LOG_LINE && i > MAX_LOG_LINE / 2 && i < lines.Count - MAX_LOG_LINE / 2)
            {
                if (!isTruncated)
                {
                    output.AppendLine("{ truncated }");
                    isTruncated = true;
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

    private record CommandItem(
        [Description("工具名称")] string FileName,
        [Description("工具工作目录")] string WorkingDirectory,
        [Description("工具参数集合")] List<string> Arguments
    );

    private record TaskItem(
        [Description("任务序号")] string Num,
        [Description("任务标题")] string Title,
        [Description("任务状态")] string Status,
        [Description("任务详情")] string Content,
        [Description("执行结果")] string Result
    );
}
