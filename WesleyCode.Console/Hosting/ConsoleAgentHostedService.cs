using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Options;
using WesleyCode.Agent.Services;

namespace WesleyCode.Console.Hosting;

internal sealed class ConsoleAgentHostedService : BackgroundService
{
    private readonly IAgentRunner _agentRunner;
    private readonly ISessionStore _sessionStore;
    private readonly IOutputCapture _outputCapture;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ConsoleAgentHostedService> _logger;
    private readonly SessionOptions _sessionOptions;
    private DateTimeOffset _lastSavedAt;
    private bool _sessionDirty;

    public ConsoleAgentHostedService(
        IAgentRunner agentRunner,
        ISessionStore sessionStore,
        IOutputCapture outputCapture,
        IHostApplicationLifetime lifetime,
        IOptions<SessionOptions> sessionOptions,
        ILogger<ConsoleAgentHostedService> logger
    )
    {
        _agentRunner = agentRunner;
        _sessionStore = sessionStore;
        _outputCapture = outputCapture;
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

            _lastSavedAt = DateTimeOffset.UtcNow;
            _sessionDirty = false;
            await RunLoopAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _outputCapture.WriteSystemMessage(ex.Message);
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    private async Task SafeSaveAsync(AgentSession session, CancellationToken cancellationToken, bool force)
    {
        if (!force && !ShouldSaveSession())
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _sessionStore.SaveAsync(session, cancellationToken);
            stopwatch.Stop();
            _lastSavedAt = DateTimeOffset.UtcNow;
            _sessionDirty = false;
            _logger.LogDebug("Session persisted in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex.Message, "Failed to persist session.");
        }
    }

    private async Task CancelAgentAsync(CancellationTokenSource source)
    {
        while (!source.IsCancellationRequested)
        {
            try
            {
                if (System.Console.KeyAvailable)
                {
                    var key = System.Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        _logger.LogInformation("收到取消指令，正在停止当前执行。");
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

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        var session = await _sessionStore.LoadAsync(stoppingToken);
        _agentRunner.RestartSession(session);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, stoppingToken);
                _outputCapture.WritePrompt();
                var input = System.Console.ReadLine();
                if (input is null)
                {
                    _logger.LogInformation("Standard input closed; exiting console loop.");
                    break;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                if (string.Equals(input, "/exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.Equals(input, "/clear", StringComparison.OrdinalIgnoreCase))
                {
                    System.Console.Clear();
                    await _sessionStore.ClearAsync(stoppingToken);
                    MarkSessionDirty();
                    _outputCapture.WriteSystemMessage("会话已重置。");
                    session = await _agentRunner.CreateSessionAsync(stoppingToken);
                    continue;
                }

                using var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var cancelTask = CancelAgentAsync(source);
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    var response = await _agentRunner.ExecuteAsync(input, session, source.Token);
                    stopwatch.Stop();
                    _outputCapture.WriteAgentMessage(response.Text);
                    _logger.LogInformation("Agent response completed in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
                    MarkSessionDirty();
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested && source.IsCancellationRequested)
                {
                    _outputCapture.WriteSystemMessage("已取消当前代理执行。");
                }
                finally
                {
                    source.Cancel();
                    await cancelTask;
                }
            }
            catch (Exception ex)
            {
                _outputCapture.WriteSystemMessage(ex.Message);
            }
            finally
            {
                await SafeSaveAsync(session, stoppingToken, force: false);
                System.Console.ResetColor();
            }
        }
    }

    private bool ShouldSaveSession()
    {
        if (!_sessionDirty)
        {
            return false;
        }

        var debounceSeconds = _sessionOptions.SaveDebounceSeconds;
        if (debounceSeconds <= 0)
        {
            return true;
        }

        var elapsed = DateTimeOffset.UtcNow - _lastSavedAt;
        return elapsed.TotalSeconds >= debounceSeconds;
    }

    private void MarkSessionDirty()
    {
        _sessionDirty = true;
    }
}
