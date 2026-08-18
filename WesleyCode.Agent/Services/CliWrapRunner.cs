using System.Text.Json;
using CliWrap;
using Microsoft.Agents.AI;
using WesleyCode.Agent.Extensions;

namespace WesleyCode.Agent.Services;

internal static class CliWrapRunner
{
    public static async Task<object?> RunAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken
    )
    {
        var (commandPath, commandArguments) = BuildCommand(script.FullPath, ParseArguments(arguments));

        try
        {
            using var standardOutput = new MemoryStream();
            using var standardError = new MemoryStream();
            var command = Cli.Wrap(commandPath)
                .WithArguments(commandArguments)
                .WithWorkingDirectory(skill.Path)
                .WithStandardOutputPipe(PipeTarget.ToStream(standardOutput))
                .WithStandardErrorPipe(PipeTarget.ToStream(standardError))
                .WithValidation(CommandResultValidation.None);

            var execute = await command.ExecuteAsync(cancellationToken);
            return new
            {
                code = execute.ExitCode,
                output = standardOutput.DecodeOutput(),
                error = standardError.DecodeOutput(),
            };
        }
        catch (Exception ex)
        {
            return new { code = -1, error = $"脚本执行失败：{ex.Message}" };
        }
    }

    private static IReadOnlyList<string> ParseArguments(JsonElement? arguments)
    {
        List<string> values = [];
        if (arguments is { ValueKind: JsonValueKind.String } args)
        {
            values.Add(args.GetString() ?? string.Empty);
            return values;
        }

        if (arguments is { ValueKind: JsonValueKind.Array } json)
        {
            foreach (var element in json.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException($"数组中的元素需要为字符串, 当前的是 {element.ValueKind}");
                }
                values.Add(element.GetString() ?? string.Empty);
            }
        }
        else if (arguments is not null && arguments.Value.ValueKind != JsonValueKind.Null && arguments.Value.ValueKind != JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"需要的参数是数组的 Json, 当前传入的是 {arguments.Value.ValueKind}");
        }
        return values;
    }

    private static (string CommandPath, IReadOnlyList<string> Arguments) BuildCommand(string scriptPath, IReadOnlyList<string> arguments)
    {
        var extension = Path.GetExtension(scriptPath);
        if (string.Equals(extension, ".py", StringComparison.OrdinalIgnoreCase))
        {
            return ("python", [scriptPath, .. arguments]);
        }

        if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return ("powershell", ["-NoProfile", "-NoLogo", "-NonInteractive", "-Command", scriptPath, .. arguments]);
        }

        if (
            string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".mjs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".cjs", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ("node", [scriptPath, .. arguments]);
        }

        if (
            string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".csx", StringComparison.OrdinalIgnoreCase)
        )
        {
            return BuildCsCommand(scriptPath, arguments);
        }

        throw new InvalidOperationException($"不支持的脚本类型: {extension}，请使用 .py, .js, .mjs, .cjs, .cs, .csx");
    }

    private static (string CommandPath, IReadOnlyList<string> Arguments) BuildCsCommand(string scriptPath, IReadOnlyList<string> arguments)
    {
        var projectFile = FindNearestProjectFile(Path.GetDirectoryName(scriptPath));

        if (projectFile is null)
        {
            throw new InvalidOperationException(
                $"C# 脚本 {Path.GetFileName(scriptPath)} 需要在所属项目（.csproj）上下文中运行，但未找到项目文件。请将脚本放入含 .csproj 的目录中。"
            );
        }

        // dotnet run --project <csproj> -- <scriptPath> <args...>
        return ("dotnet", ["run", "--project", projectFile, "--", scriptPath, .. arguments]);
    }

    private static string? FindNearestProjectFile(string? startDirectory)
    {
        var current = startDirectory;
        while (!string.IsNullOrEmpty(current) && Directory.Exists(current))
        {
            var projectFile = Directory.EnumerateFiles(current, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (projectFile is not null)
            {
                return projectFile;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }
            current = parent.FullName;
        }

        return null;
    }
}
