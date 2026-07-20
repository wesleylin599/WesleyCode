using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;

namespace WesleyCode.Agent.Infrastructure;

public sealed class SessionStore : ISessionStore
{
    private static readonly UTF8Encoding SessionEncoding = new(true);
    private readonly IAgentRunner _agentRunner;
    private readonly SessionOptions _options;
    private readonly ILogger<SessionStore> _logger;
    private readonly string _sessionHistoryPath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SessionStore(IAgentRunner agentRunner, IOptions<WorkingOptions> working, IOptions<SessionOptions> options, ILogger<SessionStore> logger)
    {
        _agentRunner = agentRunner;
        _options = options.Value;
        _logger = logger;
        var baseDir = AppContext.BaseDirectory;
        var sessionDir = Path.Combine(baseDir, _options.DirectoryName);
        _sessionHistoryPath = Path.Combine(sessionDir, $"{working.Value.BasePath.ComputeMd5()}.json");
    }

    public async Task<AgentSession> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_sessionHistoryPath))
            return await _agentRunner.CreateSessionAsync(cancellationToken);

        var lockAcquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            lockAcquired = true;

            var content = await File.ReadAllTextAsync(_sessionHistoryPath, Encoding.UTF8, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                BackupInvalidSession("会话文件为空");
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
            BackupInvalidSession("会话文件损坏或无法读取");
            _logger.LogWarning(ex, "Failed to load session history, starting new session: {SessionPath}", _sessionHistoryPath);
            return await _agentRunner.CreateSessionAsync(cancellationToken);
        }
        finally
        {
            if (lockAcquired)
            {
                _fileLock.Release();
            }
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

        var lockAcquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            lockAcquired = true;

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
            if (lockAcquired)
            {
                _fileLock.Release();
            }

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
            try
            {
                File.Delete(_sessionHistoryPath);
                _logger.LogInformation("Session history cleared: {SessionPath}", _sessionHistoryPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear session history: {SessionPath}", _sessionHistoryPath);
            }
        }
        return Task.CompletedTask;
    }

    private void BackupInvalidSession(string reason)
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Reason}，但备份会话文件失败: {SessionPath}", reason, _sessionHistoryPath);
        }
    }
}
