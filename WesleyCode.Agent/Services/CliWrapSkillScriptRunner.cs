using System.Text;
using System.Text.Json;
using CliWrap;
using Microsoft.Agents.AI;

namespace WesleyCode.Agent.Services;

internal static class CliWrapSkillScriptRunner
{
    private static readonly UTF8Encoding Utf8StrictEncoding = new(false, true);

    public static async Task<object?> RunAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken
    )
    {
        var scriptPath = script.FullPath;
        var scriptArguments = ParseArguments(arguments);
        var (commandPath, commandArguments) = BuildCommand(scriptPath, scriptArguments);
        var scriptDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory;

        using var standardOutputStream = new MemoryStream();
        using var standardErrorStream = new MemoryStream();

        var command = Cli.Wrap(commandPath)
            .WithArguments(commandArguments)
            .WithWorkingDirectory(scriptDirectory)
            .WithStandardOutputPipe(PipeTarget.ToStream(standardOutputStream))
            .WithStandardErrorPipe(PipeTarget.ToStream(standardErrorStream))
            .WithValidation(CommandResultValidation.None);

        var execute = await command.ExecuteAsync(cancellationToken);
        var standardOutput = DecodeCommandOutput(standardOutputStream.ToArray());
        var standardError = DecodeCommandOutput(standardErrorStream.ToArray());

        return new
        {
            code = execute.ExitCode,
            output = standardOutput,
            error = standardError,
        };
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

    private static (string CommandPath, IReadOnlyList<string> Arguments) BuildCommand(string scriptPath, IReadOnlyList<string> arguments)
    {
        var extension = Path.GetExtension(scriptPath);
        if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return ("powershell", ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, .. arguments]);
        }

        if (
            string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ("cmd", ["/c", scriptPath, .. arguments]);
        }

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

        if (string.Equals(extension, ".sh", StringComparison.OrdinalIgnoreCase))
        {
            return ("bash", [scriptPath, .. arguments]);
        }

        return (scriptPath, arguments);
    }
}
