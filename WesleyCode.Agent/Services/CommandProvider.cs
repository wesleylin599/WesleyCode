using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;

namespace WesleyCode.Agent.Services;

internal sealed class CommandProvider : AIContextProvider
{
    private IReadOnlyList<string> ProbeTools { get; init; } = ["git", "dotnet", "node", "python", "docker", "curl"];

    private readonly ShellExecutor _executor;

    public CommandProvider(ShellExecutor executor)
    {
        this._executor = executor;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = await this.ProbeAsync(cancellationToken).ConfigureAwait(false);
        return new AIContext { Instructions = DefaultInstructionsFormatter(snapshot), Tools = [_executor.AsAIFunction(requireApproval: false)] };
    }

    private async Task<(string? Version, string Cwd)> ProbeShellAndCwdAsync(ShellFamily family, CancellationToken cancellationToken)
    {
        var probe =
            family == ShellFamily.PowerShell
                ? "Write-Output (\"VERSION=\" + $PSVersionTable.PSVersion.ToString()); Write-Output (\"CWD=\" + (Get-Location).Path)"
                : "echo \"VERSION=${BASH_VERSION:-${ZSH_VERSION:-unknown}}\"; echo \"CWD=$PWD\"";

        var result = await this.RunProbeAsync(probe, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return (null, string.Empty);
        }

        string? version = null;
        string cwd = string.Empty;
        foreach (var line in result.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("VERSION=", StringComparison.Ordinal))
            {
                var v = line.Substring("VERSION=".Length).Trim();
                version = string.IsNullOrEmpty(v) || v == "unknown" ? null : v;
            }
            else if (line.StartsWith("CWD=", StringComparison.Ordinal))
            {
                cwd = line.Substring("CWD=".Length).Trim();
            }
        }
        return (version, cwd);
    }

    private async Task<string?> ProbeToolVersionAsync(string tool, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tool) || !s_toolNamePattern.IsMatch(tool))
        {
            return null;
        }

        var probe = $"{tool} --version";
        var result = await this.RunProbeAsync(probe, cancellationToken).ConfigureAwait(false);
        if (result is null || result.ExitCode != 0)
        {
            return null;
        }

        var firstLine = FirstNonEmptyLine(result.Stdout) ?? FirstNonEmptyLine(result.Stderr);
        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine!.Trim();

        static string? FirstNonEmptyLine(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private async Task<ShellResult?> RunProbeAsync(string command, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            return await this._executor.RunAsync(command, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is ShellCommandRejectedException || ex is IOException || ex is TimeoutException)
        {
            return null;
        }
    }

    private async Task<ShellEnvironmentSnapshot> ProbeAsync(CancellationToken cancellationToken)
    {
        var family = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ShellFamily.PowerShell : ShellFamily.Posix;

        await this._executor.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var (shellVersion, workingDir) = await this.ProbeShellAndCwdAsync(family, cancellationToken).ConfigureAwait(false);

        var toolVersions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in this.ProbeTools)
        {
            if (toolVersions.ContainsKey(tool))
            {
                continue;
            }
            toolVersions[tool] = await this.ProbeToolVersionAsync(tool, cancellationToken).ConfigureAwait(false);
        }

        return new ShellEnvironmentSnapshot(
            Family: family,
            OSDescription: RuntimeInformation.OSDescription,
            ShellVersion: shellVersion,
            WorkingDirectory: workingDir,
            ToolVersions: toolVersions
        );
    }

    private static string DefaultInstructionsFormatter(ShellEnvironmentSnapshot snapshot)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("## Shell environment");

        if (snapshot.Family == ShellFamily.PowerShell)
        {
            var version = snapshot.ShellVersion is null ? string.Empty : $" {snapshot.ShellVersion}";
            _ = sb.Append("You are operating a PowerShell").Append(version).Append(" session on ").Append(snapshot.OSDescription).AppendLine(".");
            _ = sb.AppendLine("Use PowerShell idioms, NOT bash:");
            _ = sb.AppendLine("- Set environment variables with `$env:NAME = 'value'` (NOT `NAME=value`).");
            _ = sb.AppendLine("- Change directory with `Set-Location` or `cd`. Paths use `\\` separators.");
            _ = sb.AppendLine("- Reference environment variables as `$env:NAME` (NOT `$NAME`).");
            _ = sb.AppendLine("- The system temp directory is `[System.IO.Path]::GetTempPath()` (NOT `/tmp`).");
            _ = sb.AppendLine("- Pipe to `Out-Null` to suppress output (NOT `> /dev/null`).");
        }
        else
        {
            var version = snapshot.ShellVersion is null ? string.Empty : $" {snapshot.ShellVersion}";
            _ = sb.Append("You are operating a POSIX shell").Append(version).Append(" session on ").Append(snapshot.OSDescription).AppendLine(".");
            _ = sb.AppendLine("Use POSIX shell idioms (bash/sh).");
            _ = sb.AppendLine("- Set environment variables for the next command with `export NAME=value`.");
            _ = sb.AppendLine("- Reference environment variables as `$NAME` or `${NAME}`.");
            _ = sb.AppendLine("- Paths use `/` separators.");
        }

        if (!string.IsNullOrEmpty(snapshot.WorkingDirectory))
        {
            _ = sb.Append("Working directory: ").AppendLine(snapshot.WorkingDirectory);
        }

        var installed = snapshot.ToolVersions.Where(kv => kv.Value is not null).Select(kv => $"{kv.Key} ({kv.Value})").ToList();
        var missing = snapshot.ToolVersions.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList();

        if (installed.Count > 0)
        {
            _ = sb.Append("Available CLIs: ").AppendLine(string.Join(", ", installed));
        }
        if (missing.Count > 0)
        {
            _ = sb.Append("Not installed: ").AppendLine(string.Join(", ", missing));
        }

        return sb.ToString().TrimEnd();
    }

    private static readonly System.Text.RegularExpressions.Regex s_toolNamePattern = new(
        "^[A-Za-z0-9._-]+$",
        System.Text.RegularExpressions.RegexOptions.Compiled
    );
}
