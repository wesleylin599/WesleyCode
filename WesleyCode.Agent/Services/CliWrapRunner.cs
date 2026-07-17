using System.Text;
using System.Text.Json;
using CliWrap;
using Microsoft.Agents.AI;
using UtfUnknown;

namespace WesleyCode.Agent.Services;

internal static class CliWrapRunner
{
    public static readonly string FileName = OperatingSystem.IsWindows() ? "powershell" : "bin/bash";

    public static async Task<object?> RunAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken
    )
    {
        using var standardOutput = new MemoryStream();
        using var standardError = new MemoryStream();
        var (commandPath, commandArguments) = BuildCommand(script.FullPath, ParseArguments(arguments));

        try
        {
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
                output = DecodeOutput(standardOutput),
                error = DecodeOutput(standardError),
            };
        }
        catch (Exception ex)
        {
            return new
            {
                code = -1,
                output = DecodeOutput(standardOutput),
                error = $"脚本执行失败：{ex.Message}",
            };
        }
    }

    public static string DecodeOutput(MemoryStream stream)
    {
        var bytes = stream.ToArray();

        if (bytes.Length == 0)
            return string.Empty;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var result = CharsetDetector.DetectFromBytes(bytes);

        var encoding = Encoding.GetEncoding(result.Detected?.EncodingName ?? "UTF-8");

        return encoding.GetString(bytes);
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
