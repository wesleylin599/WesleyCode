using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Services;

internal sealed class UserSkillsProvider : AIContextProvider
{
    private readonly string _skillsRoot;
    private readonly AgentFileStore _store;

    public UserSkillsProvider(string skillsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillsRoot);

        _skillsRoot = skillsRoot;
        _store = new FileSystemAgentFileStore(_skillsRoot);
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(
            new AIContext
            {
                Instructions = $"""
                ## User Skills Access
                你可以使用 `user_skills_*` 工具直接操作用户 skills 目录，根目录是 `{_skillsRoot}`。
                所有文件路径都必须相对于该 skills 根目录，不要使用绝对路径。
                当用户要求创建或修改 skill 时，优先使用这些工具，不要把用户 skills 写到当前工作区。
                除非用户明确要求，否则不要删除已有 skill 文件，也不要覆盖已有文件。
                """,
                Tools =
                [
                    AIFunctionFactory.Create(
                        SaveFileAsync,
                        new AIFunctionFactoryOptions
                        {
                            Name = "user_skills_save_file",
                            Description = "保存文件到用户 skills 目录；默认不覆盖已有文件。",
                        }
                    ),
                    AIFunctionFactory.Create(
                        ReadFileAsync,
                        new AIFunctionFactoryOptions { Name = "user_skills_read_file", Description = "读取用户 skills 目录中的文件内容。" }
                    ),
                    AIFunctionFactory.Create(
                        DeleteFileAsync,
                        new AIFunctionFactoryOptions { Name = "user_skills_delete_file", Description = "删除用户 skills 目录中的文件。" }
                    ),
                    AIFunctionFactory.Create(
                        ListFilesAsync,
                        new AIFunctionFactoryOptions { Name = "user_skills_list_files", Description = "列出用户 skills 目录下的直接子文件。" }
                    ),
                    AIFunctionFactory.Create(
                        ListSubdirectoriesAsync,
                        new AIFunctionFactoryOptions
                        {
                            Name = "user_skills_list_subdirectories",
                            Description = "列出用户 skills 目录下的直接子目录。",
                        }
                    ),
                    AIFunctionFactory.Create(
                        SearchFilesAsync,
                        new AIFunctionFactoryOptions { Name = "user_skills_search_files", Description = "按正则表达式搜索用户 skills 文件内容。" }
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
        Directory.CreateDirectory(_skillsRoot);
        if (!overwrite && await _store.FileExistsAsync(fileName, cancellationToken))
        {
            return $"文件已存在：{fileName}。如需覆盖请将 overwrite 设为 true。";
        }

        await _store.WriteFileAsync(fileName, content, cancellationToken);
        return overwrite ? $"已写入 skills 文件：{fileName}（已覆盖）。" : $"已写入 skills 文件：{fileName}。";
    }

    private async Task<string> ReadFileAsync([Description("要读取的相对文件路径")] string fileName, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_skillsRoot))
        {
            return $"skills 目录不存在：{_skillsRoot}";
        }

        var content = await _store.ReadFileAsync(fileName, cancellationToken);
        return content ?? $"文件不存在：{fileName}";
    }

    private async Task<string> DeleteFileAsync([Description("要删除的相对文件路径")] string fileName, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_skillsRoot))
        {
            return $"skills 目录不存在：{_skillsRoot}";
        }

        var deleted = await _store.DeleteFileAsync(fileName, cancellationToken);
        return deleted ? $"已删除 skills 文件：{fileName}" : $"文件不存在：{fileName}";
    }

    private Task<IReadOnlyList<string>> ListFilesAsync(
        [Description("要列出的相对目录路径；留空表示 skills 根目录")] string? directory = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(_skillsRoot))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        return _store.ListFilesAsync(directory ?? string.Empty, cancellationToken);
    }

    private Task<IReadOnlyList<string>> ListSubdirectoriesAsync(
        [Description("要列出的相对目录路径；留空表示 skills 根目录")] string? directory = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(_skillsRoot))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        return _store.ListDirectoriesAsync(directory ?? string.Empty, cancellationToken);
    }

    private Task<IReadOnlyList<FileSearchResult>> SearchFilesAsync(
        [Description("用于匹配文件内容的正则表达式，大小写不敏感")] string regexPattern,
        [Description("可选的文件 glob 过滤模式，例如 **/*.md；留空表示搜索全部文件")] string? filePattern = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(_skillsRoot))
        {
            return Task.FromResult<IReadOnlyList<FileSearchResult>>([]);
        }

        return _store.SearchFilesAsync(string.Empty, regexPattern, filePattern ?? string.Empty, false, cancellationToken);
    }
}
