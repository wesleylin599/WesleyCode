using System.ComponentModel;
using System.Globalization;
using System.Text;
using CliWrap;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.AI;
using WesleyCode.Infrastructure;

namespace WesleyCode.Services;

internal static class ToolManager
{
    private const int MaxLogLength = 30000;
    private const int DefaultMaxResults = 200;
    private const int DefaultReadLineCount = 200;
    private static readonly object TaskSync = new();
    private static readonly List<TaskItem> Tasks = [];
    private static readonly UTF8Encoding Utf8StrictEncoding = new(false, true);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    static ToolManager()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static readonly AITool CommandFunction = AIFunctionFactory.Create(Command);
    public static readonly AITool ReadFileFunction = AIFunctionFactory.Create(ReadFile);
    public static readonly AITool ReadFileLinesFunction = AIFunctionFactory.Create(ReadFileLines);
    public static readonly AITool GetFileInfoFunction = AIFunctionFactory.Create(GetFileInfo);
    public static readonly AITool ListDirectoryFunction = AIFunctionFactory.Create(ListDirectory);
    public static readonly AITool FindFilesFunction = AIFunctionFactory.Create(FindFiles);
    public static readonly AITool SearchTextInFilesFunction = AIFunctionFactory.Create(SearchTextInFiles);
    public static readonly AITool GetWorkingDirectoryFunction = AIFunctionFactory.Create(GetWorkingDirectory);
    public static readonly AITool WebSearchFunction = AIFunctionFactory.Create(WebSearch);
    public static readonly AITool ReadTasksFunction = AIFunctionFactory.Create(ReadTasks);
    public static readonly AITool EditFileFunction = AIFunctionFactory.Create(EditFile);
    public static readonly AITool WriteFileFunction = AIFunctionFactory.Create(WriteFile);
    public static readonly AITool CreateDirectoryFunction = AIFunctionFactory.Create(CreateDirectory);
    public static readonly AITool CopyPathFunction = AIFunctionFactory.Create(CopyPath);
    public static readonly AITool MovePathFunction = AIFunctionFactory.Create(MovePath);
    public static readonly AITool UpdateTasksFunction = AIFunctionFactory.Create(UpdateTasks);

    public static readonly AITool[] ReadFunctions =
    [
        CommandFunction,
        ReadFileFunction,
        ReadFileLinesFunction,
        GetFileInfoFunction,
        ListDirectoryFunction,
        FindFilesFunction,
        SearchTextInFilesFunction,
        GetWorkingDirectoryFunction,
        WebSearchFunction,
        ReadTasksFunction,
    ];

    public static readonly AITool[] AllFunctions =
    [
        .. ReadFunctions,
        EditFileFunction,
        WriteFileFunction,
        CreateDirectoryFunction,
        CopyPathFunction,
        MovePathFunction,
        UpdateTasksFunction,
    ];

    [Description("命令行工具,用于执行命令操作")]
    private static async Task<string> Command([Description("命令")] CommandItem command, CancellationToken cancellationToken = default)
    {
        string callback;
        try
        {
            if (string.IsNullOrWhiteSpace(command.FileName))
            {
                callback = "Error: 工具名称为空";
                return ToolResult(callback);
            }

            var workingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(command.WorkingDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                callback = $"Error: 工作目录不存在: {workingDirectory}";
                return ToolResult(callback);
            }

            var arguments = command.Arguments ?? [];
            var timeoutSeconds = command.TimeoutSeconds <= 0 ? 300 : Math.Min(command.TimeoutSeconds, 3600);
            using var standardOutputStream = new MemoryStream();
            using var standardErrorStream = new MemoryStream();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var cli = Cli.Wrap(command.FileName)
                .WithArguments(arguments)
                .WithWorkingDirectory(workingDirectory)
                .WithStandardOutputPipe(PipeTarget.ToStream(standardOutputStream))
                .WithStandardErrorPipe(PipeTarget.ToStream(standardErrorStream))
                .WithValidation(CommandResultValidation.None);

            var result = await cli.ExecuteAsync(timeoutSource.Token);
            var standardOutput = DecodeCommandOutput(standardOutputStream.ToArray());
            var standardError = DecodeCommandOutput(standardErrorStream.ToArray());

            callback = $"[{result.ExitCode}]{standardOutput}{standardError}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            callback = "Error: 命令执行超时";
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }
        return ToolResult(callback);

        static string DecodeCommandOutput(byte[] buffer)
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

        static IEnumerable<Encoding> GetCommandOutputEncodings()
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

        static bool TryDecode(byte[] buffer, Encoding encoding, out string text)
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

    [Description("读取文件内容并返回实际字符编码")]
    private static async Task<string> ReadFile([Description("文件路径")] string path, CancellationToken cancellationToken = default)
    {
        string callback;
        try
        {
            var fullPath = Path.GetFullPath(path);

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

        return ToolResult(callback);
    }

    [Description("按行读取文件片段,用于快速查看大文件的指定行范围")]
    private static async Task<string> ReadFileLines(
        [Description("文件路径")] string path,
        [Description("起始行号,从 1 开始")] int startLine = 1,
        [Description("读取行数,默认 200,最大 2000")] int lineCount = DefaultReadLineCount,
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (startLine <= 0)
            {
                callback = "Error: 起始行号必须大于 0";
                return ToolResult(callback);
            }

            var normalizedLineCount = lineCount <= 0 ? DefaultReadLineCount : Math.Min(lineCount, 2000);
            var file = await TextFileUtility.ReadAsync(fullPath, cancellationToken);
            var lines = file.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            if (startLine > lines.Length)
            {
                callback = $"Error: 起始行号超出范围,总行数 {lines.Length}";
            }
            else
            {
                var endLine = Math.Min(lines.Length, startLine + normalizedLineCount - 1);
                var builder = new StringBuilder();
                builder.AppendLine($"path: {fullPath}");
                builder.AppendLine($"encoding: {file.EncodingName}");
                builder.AppendLine($"start_line: {startLine}");
                builder.AppendLine($"end_line: {endLine}");
                builder.AppendLine($"total_lines: {lines.Length}");
                for (var index = startLine; index <= endLine; index++)
                {
                    builder.AppendLine($"{index}:{lines[index - 1]}");
                }
                callback = builder.ToString().TrimEnd();
            }
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("获取文件或目录信息,包括类型、大小、时间和属性")]
    private static string GetFileInfo([Description("文件或目录路径")] string path)
    {
        string callback;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                callback = "Error: 路径为空";
                return ToolResult(callback);
            }

            var fullPath = Path.GetFullPath(path);

            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                callback = BuildPathInfoResult(
                    fullPath,
                    isDirectory: false,
                    size: fileInfo.Length,
                    attributes: fileInfo.Attributes,
                    creationTime: fileInfo.CreationTime,
                    lastWriteTime: fileInfo.LastWriteTime,
                    lastAccessTime: fileInfo.LastAccessTime
                );
            }
            else if (Directory.Exists(fullPath))
            {
                var directoryInfo = new DirectoryInfo(fullPath);
                callback = BuildPathInfoResult(
                    fullPath,
                    isDirectory: true,
                    size: null,
                    attributes: directoryInfo.Attributes,
                    creationTime: directoryInfo.CreationTime,
                    lastWriteTime: directoryInfo.LastWriteTime,
                    lastAccessTime: directoryInfo.LastAccessTime
                );
            }
            else
            {
                callback = $"Error: 路径不存在: {fullPath}";
            }
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("列出目录内容,用于快速查看文件和文件夹")]
    private static string ListDirectory(
        [Description("目录路径,为空时默认当前目录")] string? path = null,
        [Description("是否递归遍历子目录")] bool recursive = false,
        [Description("是否包含隐藏文件和隐藏目录")] bool includeHidden = false,
        [Description("最多返回多少条,默认 200,最大 1000")] int maxResults = DefaultMaxResults
    )
    {
        string callback;
        try
        {
            var fullPath = ResolveDirectoryPath(path);

            var limit = NormalizeMaxResults(maxResults);
            var entries = Directory
                .EnumerateFileSystemEntries(fullPath, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Select(CreateFileSystemEntry)
                .Where(entry => includeHidden || !entry.IsHidden)
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Take(limit + 1)
                .ToList();

            callback = BuildFileSystemEntriesResult(fullPath, recursive, limit, entries);
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("按文件名模式查找文件,例如 *.cs 或 *.md")]
    private static string FindFiles(
        [Description("查找起始目录,为空时默认当前目录")] string? path = null,
        [Description("文件名模式,默认 *")] string pattern = "*",
        [Description("是否递归遍历子目录")] bool recursive = true,
        [Description("最多返回多少条,默认 200,最大 1000")] int maxResults = DefaultMaxResults
    )
    {
        string callback;
        try
        {
            var fullPath = ResolveDirectoryPath(path);
            var limit = NormalizeMaxResults(maxResults);

            var files = Directory
                .EnumerateFiles(
                    fullPath,
                    string.IsNullOrWhiteSpace(pattern) ? "*" : pattern,
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
                )
                .Select(Path.GetFullPath)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Take(limit + 1)
                .ToList();

            callback = BuildPathListResult(fullPath, recursive, pattern, limit, files);
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("在文本文件中搜索指定文本,返回匹配文件、行号和内容")]
    private static async Task<string> SearchTextInFiles(
        [Description("查找起始目录,为空时默认当前目录")] string? path,
        [Description("要搜索的文本")] string searchText,
        [Description("文件名模式,默认 *")] string pattern = "*",
        [Description("是否递归遍历子目录")] bool recursive = true,
        [Description("是否区分大小写")] bool caseSensitive = false,
        [Description("最多返回多少条匹配结果,默认 200,最大 1000")] int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                callback = "Error: 搜索文本为空";
                return ToolResult(callback);
            }

            var fullPath = ResolveDirectoryPath(path);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var limit = NormalizeMaxResults(maxResults);
            var results = new List<string>(Math.Min(limit, 128));
            var hasMore = false;
            foreach (
                var filePath in Directory.EnumerateFiles(
                    fullPath,
                    string.IsNullOrWhiteSpace(pattern) ? "*" : pattern,
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
                )
            )
            {
                cancellationToken.ThrowIfCancellationRequested();

                TextFileContent file;
                try
                {
                    file = await TextFileUtility.ReadAsync(filePath, cancellationToken);
                }
                catch
                {
                    continue;
                }

                var lines = file.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                for (var index = 0; index < lines.Length; index++)
                {
                    if (lines[index].Contains(searchText, comparison))
                    {
                        results.Add($"{Path.GetFullPath(filePath)}:{index + 1}:{lines[index].Trim()}");
                        if (results.Count >= limit)
                        {
                            hasMore = true;
                            break;
                        }
                    }
                }

                if (hasMore)
                {
                    break;
                }
            }

            callback = BuildSearchTextResult(fullPath, pattern, searchText, recursive, caseSensitive, limit, results, hasMore);
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("获取当前工作目录")]
    private static string GetWorkingDirectory()
    {
        var callback = $"path: {Directory.GetCurrentDirectory()}";
        return ToolResult(callback);
    }

    [Description("请求网络地址获取结果")]
    private static async Task<string> WebSearch([Description("请求地址")] string url, CancellationToken cancellationToken = default)
    {
        string callback;
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                callback = "Error: 仅支持 http/https 绝对地址";
                return ToolResult(callback);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("WesleyCode/1.0");
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            callback = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("更新任务清单,调用需要传入完整的工作清单,任务状态只有: 未开始, 进行中, 已完成")]
    private static string UpdateTasks([Description("任务清单列表")] List<TaskItem>? tasks)
    {
        tasks ??= [];
        lock (TaskSync)
        {
            Tasks.Clear();
            foreach (var item in tasks)
            {
                Tasks.Add(item);
            }
        }
        return "完成更新";
    }

    [Description("获取任务清单")]
    private static List<TaskItem> ReadTasks()
    {
        List<TaskItem> snapshot;
        lock (TaskSync)
        {
            snapshot = Tasks.Select(item => item with { }).ToList();
        }
        return snapshot;
    }

    [Description("写入文件内容,必要时创建目录")]
    private static async Task<string> WriteFile(
        [Description("文件路径")] string path,
        [Description("文本内容")] string content,
        [Description("写入的字符编码,支持 auto / utf-8 / utf-8-bom")] string encoding = "auto",
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            var fullPath = Path.GetFullPath(path);

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

        return ToolResult(callback);
    }

    [Description("创建目录,目录已存在时直接返回")]
    private static string CreateDirectory([Description("目录路径")] string path)
    {
        string callback;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                callback = "Error: 目录路径为空";
            }
            else
            {
                var fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(fullPath);
                callback = $"path: {fullPath}";
            }
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("复制文件或目录,目录会递归复制")]
    private static string CopyPath(
        [Description("源路径")] string sourcePath,
        [Description("目标路径")] string destinationPath,
        [Description("目标已存在时是否覆盖,默认 false")] bool overwrite = false
    )
    {
        string callback;
        try
        {
            var sourceFullPath = Path.GetFullPath(sourcePath);
            var destinationFullPath = Path.GetFullPath(destinationPath);

            if (File.Exists(sourceFullPath))
            {
                EnsureParentDirectory(destinationFullPath);
                File.Copy(sourceFullPath, destinationFullPath, overwrite);
                callback = $"copied_file: {sourceFullPath} -> {destinationFullPath}";
            }
            else if (Directory.Exists(sourceFullPath))
            {
                CopyDirectory(sourceFullPath, destinationFullPath, overwrite);
                callback = $"copied_directory: {sourceFullPath} -> {destinationFullPath}";
            }
            else
            {
                callback = $"Error: 源路径不存在: {sourceFullPath}";
            }
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("移动文件或目录,目标已存在时可选择覆盖")]
    private static string MovePath(
        [Description("源路径")] string sourcePath,
        [Description("目标路径")] string destinationPath,
        [Description("目标已存在时是否覆盖,默认 false")] bool overwrite = false
    )
    {
        string callback;
        try
        {
            var sourceFullPath = Path.GetFullPath(sourcePath);
            var destinationFullPath = Path.GetFullPath(destinationPath);

            if (File.Exists(sourceFullPath))
            {
                EnsureParentDirectory(destinationFullPath);
                File.Move(sourceFullPath, destinationFullPath, overwrite);
                callback = $"moved_file: {sourceFullPath} -> {destinationFullPath}";
            }
            else if (Directory.Exists(sourceFullPath))
            {
                MoveDirectory(sourceFullPath, destinationFullPath, overwrite);
                callback = $"moved_directory: {sourceFullPath} -> {destinationFullPath}";
            }
            else
            {
                callback = $"Error: 源路径不存在: {sourceFullPath}";
            }
        }
        catch (Exception ex)
        {
            callback = $"Error: {ex.Message}";
        }

        return ToolResult(callback);
    }

    [Description("替换文件中的文本内容")]
    private static async Task<string> EditFile(
        [Description("文件路径")] string path,
        [Description("旧文本内容")] string oldText,
        [Description("新文本内容")] string newText,
        [Description("写入的字符编码,支持 auto / utf-8 / utf-8-bom")] string encoding = "auto",
        CancellationToken cancellationToken = default
    )
    {
        string callback;
        try
        {
            var fullPath = Path.GetFullPath(path);

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

        return ToolResult(callback);

        static string BuildDiff(string oldText, string newText)
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
    }

    private static string ResolveDirectoryPath(string? path)
    {
        var fullPath = string.IsNullOrWhiteSpace(path) ? Directory.GetCurrentDirectory() : Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"目录不存在: {fullPath}");
        }

        return fullPath;
    }

    private static int NormalizeMaxResults(int maxResults)
    {
        return maxResults <= 0 ? DefaultMaxResults : Math.Min(maxResults, 1000);
    }

    private static FileSystemEntryInfo CreateFileSystemEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        return new FileSystemEntryInfo(
            Path: Path.GetFullPath(path),
            Name: Path.GetFileName(path),
            IsDirectory: attributes.HasFlag(FileAttributes.Directory),
            IsHidden: attributes.HasFlag(FileAttributes.Hidden),
            Size: attributes.HasFlag(FileAttributes.Directory) ? null : new FileInfo(path).Length
        );
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        var sourceFullPath = Path.GetFullPath(sourceDirectory);
        var destinationFullPath = Path.GetFullPath(destinationDirectory);
        if (destinationFullPath.StartsWith(sourceFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标目录不能位于源目录内部");
        }

        Directory.CreateDirectory(destinationFullPath);
        foreach (var directoryPath in Directory.EnumerateDirectories(sourceFullPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFullPath, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationFullPath, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceFullPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFullPath, filePath);
            var destinationFilePath = Path.Combine(destinationFullPath, relativePath);
            EnsureParentDirectory(destinationFilePath);
            File.Copy(filePath, destinationFilePath, overwrite);
        }
    }

    private static void MoveDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        var sourceFullPath = Path.GetFullPath(sourceDirectory);
        var destinationFullPath = Path.GetFullPath(destinationDirectory);
        if (destinationFullPath.StartsWith(sourceFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标目录不能位于源目录内部");
        }

        if (Directory.Exists(destinationFullPath))
        {
            if (!overwrite)
            {
                throw new IOException($"目标目录已存在: {destinationFullPath}");
            }

            Directory.Delete(destinationFullPath, recursive: true);
        }
        else
        {
            EnsureParentDirectory(destinationFullPath);
        }

        Directory.Move(sourceFullPath, destinationFullPath);
    }

    private static string BuildPathInfoResult(
        string fullPath,
        bool isDirectory,
        long? size,
        FileAttributes attributes,
        DateTime creationTime,
        DateTime lastWriteTime,
        DateTime lastAccessTime
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine($"path: {fullPath}");
        builder.AppendLine($"type: {(isDirectory ? "directory" : "file")}");
        if (size.HasValue)
        {
            builder.AppendLine($"size: {size.Value}");
        }
        builder.AppendLine($"attributes: {attributes}");
        builder.AppendLine($"creation_time: {creationTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"last_write_time: {lastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"last_access_time: {lastAccessTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        return builder.ToString().TrimEnd();
    }

    private static string BuildFileSystemEntriesResult(string rootPath, bool recursive, int limit, List<FileSystemEntryInfo> entries)
    {
        var hasMore = entries.Count > limit;
        var actualEntries = hasMore ? entries.Take(limit) : entries;
        var builder = new StringBuilder();
        builder.AppendLine($"path: {rootPath}");
        builder.AppendLine($"recursive: {recursive}");
        builder.AppendLine($"count: {actualEntries.Count()}");
        if (hasMore)
        {
            builder.AppendLine($"truncated: true ({entries.Count - limit}+ more)");
        }

        foreach (var entry in actualEntries)
        {
            builder.Append(entry.IsDirectory ? "[D] " : "[F] ");
            builder.Append(entry.Path);
            if (entry.Size.HasValue)
            {
                builder.Append($" ({entry.Size.Value} bytes)");
            }
            if (entry.IsHidden)
            {
                builder.Append(" [hidden]");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildPathListResult(string rootPath, bool recursive, string pattern, int limit, List<string> entries)
    {
        var hasMore = entries.Count > limit;
        var actualEntries = hasMore ? entries.Take(limit) : entries;
        var builder = new StringBuilder();
        builder.AppendLine($"path: {rootPath}");
        builder.AppendLine($"pattern: {pattern}");
        builder.AppendLine($"recursive: {recursive}");
        builder.AppendLine($"count: {actualEntries.Count()}");
        if (hasMore)
        {
            builder.AppendLine($"truncated: true ({entries.Count - limit}+ more)");
        }

        foreach (var entry in actualEntries)
        {
            builder.AppendLine(entry);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildSearchTextResult(
        string rootPath,
        string pattern,
        string searchText,
        bool recursive,
        bool caseSensitive,
        int limit,
        List<string> results,
        bool hasMore
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine($"path: {rootPath}");
        builder.AppendLine($"pattern: {pattern}");
        builder.AppendLine($"search: {searchText}");
        builder.AppendLine($"recursive: {recursive}");
        builder.AppendLine($"case_sensitive: {caseSensitive}");
        builder.AppendLine($"count: {results.Count}");
        if (hasMore)
        {
            builder.AppendLine($"truncated: true ({limit}+ matches)");
        }

        foreach (var result in results)
        {
            builder.AppendLine(result);
        }

        return builder.ToString().TrimEnd();
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

    private static string ToolResult(string args)
    {
        return args.Length > MaxLogLength ? $"{args[..MaxLogLength]} ..." : args;
    }

    private record CommandItem(
        [Description("工具名称")] string FileName,
        [Description("工具工作目录")] string WorkingDirectory,
        [Description("工具参数集合")] List<string>? Arguments,
        [Description("执行超时秒数,默认 60 秒,最大 600 秒")] int TimeoutSeconds = 60
    );

    private record TaskItem(
        [Description("任务序号")] string Num,
        [Description("任务标题")] string Title,
        [Description("任务详情")] string Content,
        [Description("执行结果")] string Result,
        [Description("任务状态")] string Status = "未开始"
    );

    private sealed record FileSystemEntryInfo(string Path, string Name, bool IsDirectory, bool IsHidden, long? Size);
}
