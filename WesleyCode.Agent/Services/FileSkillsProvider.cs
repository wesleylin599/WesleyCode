using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Services;

internal sealed class FileSkillsProvider : AIContextProvider
{
    private readonly string _skillsRoot;
    private readonly AgentFileStore _store;
    private readonly AIFunction[] _tools;

    public FileSkillsProvider(IOptions<SkillOptions> options)
    {
        _skillsRoot = options.Value.SkillPath;
        _store = new FileSystemAgentFileStore(_skillsRoot);
        _tools = CreateTools();
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            new AIContext
            {
                Instructions = $"""
                ## Skills File Access
                你可以使用以下工具直接操作 skills 目录，根目录是 `{_skillsRoot}`：
                - `list_skills_children`：列出指定目录下的直接子项。
                - `read_skills_file`：读取文件内容。
                - `save_skills_file`：保存文件，默认不覆盖已存在文件。
                - `delete_skills_file`：删除文件。
                - `search_skills_files`：按正则表达式递归搜索文件内容。
                所有文件路径都必须相对于该 skills 根目录，不要使用绝对路径。
                当需要创建或修改 skill 时，使用这些工具。
                """,
                Tools = _tools,
            }
        );

    private AIFunction[] CreateTools() =>
        [
            AIFunctionFactory.Create(
                SaveFileAsync,
                new AIFunctionFactoryOptions { Name = "save_skills_file", Description = "保存文件到 skills 目录，默认不覆盖已存在文件。" }
            ),
            AIFunctionFactory.Create(
                ReadFileAsync,
                new AIFunctionFactoryOptions { Name = "read_skills_file", Description = "读取 skills 目录中的文件内容。" }
            ),
            AIFunctionFactory.Create(
                DeleteFileAsync,
                new AIFunctionFactoryOptions { Name = "delete_skills_file", Description = "删除 skills 目录中的文件。" }
            ),
            AIFunctionFactory.Create(
                ListChildrenAsync,
                new AIFunctionFactoryOptions { Name = "list_skills_children", Description = "列出 skills 目录中指定目录下的直接子项。" }
            ),
            AIFunctionFactory.Create(
                SearchFilesAsync,
                new AIFunctionFactoryOptions { Name = "search_skills_files", Description = "按正则表达式递归搜索 skills 文件内容。" }
            ),
        ];

    private async Task<string> ReadFileAsync([Description("要读取的相对文件路径")] string fileName, CancellationToken cancellationToken = default)
    {
        var content = await _store.ReadAsync(fileName, cancellationToken);
        return content ?? $"文件不存在：{fileName}";
    }

    private async Task<string> DeleteFileAsync([Description("要删除的相对文件路径")] string fileName, CancellationToken cancellationToken = default)
    {
        var deleted = await _store.DeleteAsync(fileName, cancellationToken);
        return deleted ? $"已删除 skills 文件：{fileName}" : $"文件不存在：{fileName}";
    }

    private Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(
        [Description("要列出的相对目录路径；留空表示 skills 根目录")] string? directory = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.ListChildrenAsync(directory ?? string.Empty, cancellationToken);
    }

    private Task<IReadOnlyList<FileSearchResult>> SearchFilesAsync(
        [Description("用于匹配文件内容的正则表达式，大小写不敏感")] string regexPattern,
        [Description("可选的文件 glob 过滤模式，例如 **/*.md；留空表示搜索全部文件")] string? filePattern = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.SearchAsync(string.Empty, regexPattern, filePattern, recursive: true, cancellationToken);
    }

    private async Task<string> SaveFileAsync(
        [Description("要保存的相对文件路径")] string fileName,
        [Description("要写入的文件内容")] string content,
        [Description("是否覆盖已存在文件，默认 false")] bool overwrite = false,
        CancellationToken cancellationToken = default
    )
    {
        if (!overwrite && await _store.FileExistsAsync(fileName, cancellationToken))
        {
            return $"文件已存在：{fileName}。如需覆盖请将 overwrite 设为 true。";
        }

        await _store.WriteAsync(fileName, content, cancellationToken);
        return overwrite ? $"已写入 skills 文件：{fileName}（已覆盖）。" : $"已写入 skills 文件：{fileName}。";
    }
}
