using System.Text;
using System.Text.Json;
using CliWrap;
using Microsoft.Agents.AI;

namespace WesleyCode.Agent.Services;

internal static class CliWrapSkillScriptRunner
{
    private static readonly string FileName = OperatingSystem.IsWindows() ? "powershell" : "bin/bash";

    public static async Task<object?> RunAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken
    )
    {
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var (commandPath, commandArguments) = BuildCommand(script.FullPath, ParseArguments(arguments));

        try
        {
            var command = Cli.Wrap(commandPath)
                .WithArguments(commandArguments)
                .WithWorkingDirectory(skill.Path)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(standardOutput))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(standardError))
                .WithValidation(CommandResultValidation.None);

            var execute = await command.ExecuteAsync(cancellationToken);
            return new
            {
                code = execute.ExitCode,
                output = standardOutput.ToString(),
                error = standardError.ToString(),
            };
        }
        catch (Exception ex)
        {
            return new
            {
                code = -1,
                output = standardOutput.ToString(),
                error = $"脚本执行失败：{ex.Message}",
            };
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
            return ("dotnet", ["run", scriptPath, .. arguments]);
        }

        return (FileName, [scriptPath, .. arguments]);
    }
}
