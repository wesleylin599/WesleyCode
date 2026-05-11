using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Options;
using WesleyCode.Services;

namespace WesleyCode.Infrastructure;

internal sealed class SessionStore : ISessionStore
{
    private static readonly UTF8Encoding SessionEncoding = new(true);
    private readonly AIAgent _agentRunner;
    private readonly SessionOptions _options;
    private readonly ILogger<SessionStore> _logger;
    private readonly string _sessionHistoryPath;

    public SessionStore(AIAgent agentRunner, IOptions<SessionOptions> options, ILogger<SessionStore> logger)
    {
        _agentRunner = agentRunner;
        _options = options.Value;
        _logger = logger;
        var baseDir = AppContext.BaseDirectory;
        var sessionDir = Path.Combine(baseDir, _options.DirectoryName);
        var workDirectory = Directory.GetCurrentDirectory();
        _sessionHistoryPath = Path.Combine(sessionDir, $"{ComputeMd5(workDirectory)}.json");
    }

    public async Task<AgentSession> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_sessionHistoryPath))
            return await _agentRunner.CreateSessionAsync(cancellationToken);

        try
        {
            var content = await File.ReadAllTextAsync(_sessionHistoryPath, Encoding.UTF8, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                await BackupInvalidSessionAsync("会话文件为空", cancellationToken);
                return await _agentRunner.CreateSessionAsync(cancellationToken);
            }

            var element = JsonSerializer.Deserialize<JsonElement>(content);
            return await _agentRunner.DeserializeSessionAsync(element, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await BackupInvalidSessionAsync("会话文件损坏或无法读取", cancellationToken);
            _logger.LogWarning(ex, "Failed to load session history, starting new session: {SessionPath}", _sessionHistoryPath);
            return await _agentRunner.CreateSessionAsync(cancellationToken);
        }
    }

    public async Task SaveAsync(AgentSession session, CancellationToken cancellationToken)
    {
        var element = await _agentRunner.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        var directory = Path.GetDirectoryName(_sessionHistoryPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory ?? string.Empty,
            $"{Path.GetFileName(_sessionHistoryPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp"
        );

        try
        {
            await File.WriteAllTextAsync(tempPath, element.GetRawText(), SessionEncoding, cancellationToken);
            if (File.Exists(_sessionHistoryPath))
            {
                File.Replace(tempPath, _sessionHistoryPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _sessionHistoryPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_sessionHistoryPath))
        {
            File.Delete(_sessionHistoryPath);
            _logger.LogInformation("Session history cleared: {SessionPath}", _sessionHistoryPath);
        }
        return Task.CompletedTask;
    }

    private static string ComputeMd5(string input)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = md5.ComputeHash(bytes);
        var segment = BitConverter.ToString(hash, 4, 8);
        return segment.Replace("-", string.Empty);
    }

    private async Task BackupInvalidSessionAsync(string reason, CancellationToken cancellationToken)
    {
        if (!File.Exists(_sessionHistoryPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_sessionHistoryPath) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            var backupPath = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(_sessionHistoryPath)}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.corrupt{Path.GetExtension(_sessionHistoryPath)}"
            );
            File.Move(_sessionHistoryPath, backupPath);
            _logger.LogWarning("{Reason}，已备份原会话文件: {BackupPath}", reason, backupPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Reason}，但备份会话文件失败: {SessionPath}", reason, _sessionHistoryPath);
        }
    }
}
