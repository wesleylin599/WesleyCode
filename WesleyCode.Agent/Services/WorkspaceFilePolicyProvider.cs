using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Services;

internal sealed class WorkspaceFilePolicyProvider : AIContextProvider
{
    private readonly AgentFileStore _store;
    private readonly string _workspaceRoot;

    public WorkspaceFilePolicyProvider(IOptions<WorkingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _workspaceRoot = options.Value.BasePath;
        _store = new FileSystemAgentFileStore(_workspaceRoot);
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(
            new AIContext
            {
                Instructions = $"""
                ## Workspace File Access
                你可以使用 `workspace_*` 工具直接操作当前工作区文件，工作区根目录是 `{_workspaceRoot}`。
                所有文件路径都必须相对于工作区根目录，不要使用绝对路径。
                读取、列目录、搜索、写入文件时优先使用这些工具，不要通过命令行执行文件写入。
                除非用户明确要求，否则不要删除已有文件，也不要覆盖已有文件。
                """,
                Tools =
                [
                    AIFunctionFactory.Create(
                        SaveFileAsync,
                        new AIFunctionFactoryOptions { Name = "workspace_save_file", Description = "保存文件到工作区；默认不覆盖已有文件。" }
                    ),
                    AIFunctionFactory.Create(
                        ReadFileAsync,
                        new AIFunctionFactoryOptions { Name = "workspace_read_file", Description = "读取工作区中的文件内容。" }
                    ),
                    AIFunctionFactory.Create(
                        DeleteFileAsync,
                        new AIFunctionFactoryOptions { Name = "workspace_delete_file", Description = "删除工作区中的文件。" }
                    ),
                    AIFunctionFactory.Create(
                        ListChildrenAsync,
                        new AIFunctionFactoryOptions { Name = "workspace_list_children", Description = "列出工作区目录下的直接子文件和子目录。" }
                    ),
                    AIFunctionFactory.Create(
                        SearchFilesAsync,
                        new AIFunctionFactoryOptions { Name = "workspace_search_files", Description = "按正则表达式搜索工作区文件内容。" }
                    ),
                ],
            }
        );
    }

    private async Task<string> SaveFileAsync(
        [Description("要保存的相对文件路径")] string fileName,
        [Description("要写入文件的内容")] string content,
        [Description("是否覆盖已存在文件，默认 false")] bool overwrite = false,
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(_workspaceRoot);
        if (!overwrite && await _store.FileExistsAsync(fileName, cancellationToken))
        {
            return $"文件已存在：{fileName}。如需覆盖请将 overwrite 设为 true。";
        }

        await _store.WriteAsync(fileName, content, cancellationToken);
        return overwrite ? $"已写入文件：{fileName}（已覆盖）。" : $"已写入文件：{fileName}。";
    }

    private async Task<string> ReadFileAsync([Description("要读取的相对文件路径")] string fileName, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_workspaceRoot))
        {
            return $"工作区目录不存在：{_workspaceRoot}";
        }

        var content = await _store.ReadAsync(fileName, cancellationToken);
        return content ?? $"文件不存在：{fileName}";
    }

    private async Task<string> DeleteFileAsync([Description("要删除的相对文件路径")] string fileName, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_workspaceRoot))
        {
            return $"工作区目录不存在：{_workspaceRoot}";
        }

        var deleted = await _store.DeleteAsync(fileName, cancellationToken);
        return deleted ? $"已删除文件：{fileName}" : $"文件不存在：{fileName}";
    }

    private Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(
        [Description("要列出的相对目录路径；留空表示工作区根目录")] string? directory = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(_workspaceRoot))
        {
            return Task.FromResult<IReadOnlyList<FileStoreEntry>>([]);
        }

        return _store.ListChildrenAsync(directory ?? string.Empty, cancellationToken);
    }

    private Task<IReadOnlyList<FileSearchResult>> SearchFilesAsync(
        [Description("用于匹配文件内容的正则表达式，大小写不敏感")] string regexPattern,
        [Description("可选的文件 glob 过滤模式，例如 **/*.cs；留空表示搜索全部文件")] string? filePattern = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(_workspaceRoot))
        {
            return Task.FromResult<IReadOnlyList<FileSearchResult>>([]);
        }

        return _store.SearchAsync(string.Empty, regexPattern, filePattern, false, cancellationToken);
    }
}
