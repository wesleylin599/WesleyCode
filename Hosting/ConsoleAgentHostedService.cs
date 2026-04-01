using System.Diagnostics;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using TestConsole5.Options;
using TestConsole5.Services;

namespace TestConsole5.Hosting;

internal sealed class ConsoleAgentHostedService : BackgroundService
{
    private readonly IAgentRunner _agentRunner;
    private readonly ISessionStore _sessionStore;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ConsoleAgentHostedService> _logger;
    private readonly SessionOptions _sessionOptions;
    private AgentSession? _session;
    private DateTimeOffset _lastSavedAt;
    private bool _sessionDirty;

    public ConsoleAgentHostedService(
        IAgentRunner agentRunner,
        ISessionStore sessionStore,
        IHostApplicationLifetime lifetime,
        IOptions<SessionOptions> sessionOptions,
        ILogger<ConsoleAgentHostedService> logger
    )
    {
        _agentRunner = agentRunner;
        _sessionStore = sessionStore;
        _lifetime = lifetime;
        _sessionOptions = sessionOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!_lifetime.ApplicationStarted.IsCancellationRequested)
            {
                var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var reg = _lifetime.ApplicationStarted.Register(() => startedTcs.TrySetResult());
                await startedTcs.Task.WaitAsync(stoppingToken);
            }

            _session = await _sessionStore.LoadAsync(stoppingToken);
            _lastSavedAt = DateTimeOffset.UtcNow;
            _sessionDirty = false;
            await PrintHistoryAsync(_session, stoppingToken);
            await RunLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Console loop failed.");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_session != null)
            await SafeSaveAsync(_session, cancellationToken, force: true);
        await base.StopAsync(cancellationToken);
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Console.Write("> User : ");
                var input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                    continue;
                if (string.Equals(input, "/clear", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Clear();
                    await _sessionStore.ClearAsync(stoppingToken);
                    _session = await _agentRunner.CreateSessionAsync(stoppingToken);
                    MarkSessionDirty();
                    continue;
                }
                if (string.Equals(input, "/exit", StringComparison.OrdinalIgnoreCase))
                    break;

                using (var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    var task = CancelAgentAsync(source);
                    var stopwatch = Stopwatch.StartNew();
                    var response = await _agentRunner.RunAsync(input, _session!, source.Token);
                    stopwatch.Stop();
                    if (string.IsNullOrEmpty(response.Text))
                    {
                        var stringBuilder = new StringBuilder();
                        foreach (var message in response.Messages)
                        foreach (var content in message.Contents)
                            if (content is ErrorContent error)
                                stringBuilder.AppendLine(error.Message);
                        Console.WriteLine($"> Agent: {stringBuilder}");
                    }
                    else
                        Console.WriteLine($"> Agent: {response}");
                    _logger.LogInformation("Agent response completed in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
                    MarkSessionDirty();
                    source.Cancel();
                    await task;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"> Agent: {ex.Message}");
            }
            finally
            {
                if (_session != null)
                    await SafeSaveAsync(_session, stoppingToken, force: false);
                Console.ResetColor();
            }
        }
    }

    private async Task PrintHistoryAsync(AgentSession activeSession, CancellationToken cancellationToken)
    {
        if (activeSession.TryGetInMemoryChatHistory(out var history) && history != null)
        {
            foreach (var message in history)
            {
                if (string.IsNullOrEmpty(message.Text))
                    continue;
                if (message.Role == ChatRole.Tool)
                    continue;
                string target = string.Empty;
                if (message.Role == ChatRole.User)
                    target = $"> User : {message}";
                else if (message.Role == ChatRole.Assistant)
                    target = $"> Agent: {message}";
                else
                    target = $"> {message.Role}: {message}";
                cancellationToken.ThrowIfCancellationRequested();
                Console.WriteLine(target);
            }
        }
        await Task.CompletedTask;
    }

    private async Task CancelAgentAsync(CancellationTokenSource source)
    {
        while (!source.IsCancellationRequested)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        _logger.LogInformation("Cancel key received; stopping agent execution.");
                        source.Cancel();
                    }
                }
                await Task.Delay(50, source.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SafeSaveAsync(AgentSession session, CancellationToken cancellationToken, bool force)
    {
        if (!force && !ShouldSaveSession())
            return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _sessionStore.SaveAsync(session, cancellationToken);
            stopwatch.Stop();
            _lastSavedAt = DateTimeOffset.UtcNow;
            _sessionDirty = false;
            _logger.LogInformation("Session persisted in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist session.");
        }
    }

    private bool ShouldSaveSession()
    {
        if (!_sessionDirty)
            return false;

        var debounceSeconds = _sessionOptions.SaveDebounceSeconds;
        if (debounceSeconds <= 0)
            return true;

        var elapsed = DateTimeOffset.UtcNow - _lastSavedAt;
        return elapsed.TotalSeconds >= debounceSeconds;
    }

    private void MarkSessionDirty()
    {
        _sessionDirty = true;
    }
}
