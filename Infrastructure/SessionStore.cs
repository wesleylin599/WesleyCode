using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TestConsole5.Options;
using TestConsole5.Services;

namespace TestConsole5.Infrastructure;

internal sealed class SessionStore : ISessionStore
{
    private readonly IAgentRunner _agentRunner;
    private readonly SessionOptions _options;
    private readonly ILogger<SessionStore> _logger;
    private readonly string _sessionHistoryPath;

    public SessionStore(IAgentRunner agentRunner, IOptions<SessionOptions> options, ILogger<SessionStore> logger)
    {
        _agentRunner = agentRunner;
        _options = options.Value;
        _logger = logger;
        var baseDir = AppContext.BaseDirectory;
        var sessionDir = string.IsNullOrWhiteSpace(_options.SessionDir) ? Path.Combine(baseDir, _options.DirectoryName) : _options.SessionDir;
        var workDirectory = Directory.GetCurrentDirectory();
        _sessionHistoryPath = Path.Combine(sessionDir, $"{ComputeMd5(workDirectory)}.json");
    }

    public async Task<AgentSession> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_sessionHistoryPath))
            return await _agentRunner.CreateSessionAsync(cancellationToken);

        var content = await File.ReadAllTextAsync(_sessionHistoryPath, cancellationToken);
        var element = JsonSerializer.Deserialize<JsonElement>(content);
        return await _agentRunner.DeserializeSessionAsync(element, cancellationToken);
    }

    public async Task SaveAsync(AgentSession session, CancellationToken cancellationToken)
    {
        var element = await _agentRunner.SerializeSessionAsync(session, cancellationToken);
        var directory = Path.GetDirectoryName(_sessionHistoryPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(_sessionHistoryPath, element.GetRawText(), cancellationToken);
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
        var bytes = Encoding.Default.GetBytes(input);
        var hash = md5.ComputeHash(bytes);
        var segment = BitConverter.ToString(hash, 4, 8);
        return segment.Replace("-", string.Empty);
    }
}
