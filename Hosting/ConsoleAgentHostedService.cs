using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Options;
using WesleyCode.Services;

namespace WesleyCode.Hosting;

internal sealed class ConsoleAgentHostedService : BackgroundService
{
    private readonly IAgentRunner _agentRunner;
    private readonly ISessionStore _sessionStore;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ConsoleAgentHostedService> _logger;
    private readonly SessionOptions _sessionOptions;
    private DateTimeOffset _lastSavedAt;
    private AgentSession? _session;
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
            PrintHelp();
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
        {
            await SafeSaveAsync(_session, cancellationToken, force: true);
        }
        await base.StopAsync(cancellationToken);
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, stoppingToken);
                Console.Write("> User : ");
                var input = await ReadInputAsync(stoppingToken);
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

                if (await TryHandleCommandAsync(input, stoppingToken))
                {
                    continue;
                }

                using var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var cancelTask = CancelAgentAsync(source);
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    var response = await _agentRunner.RunAsync(input, _session!, source.Token);
                    stopwatch.Stop();
                    Console.WriteLine($"> Agent: {response.Text}");
                    _logger.LogInformation("Agent response completed in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
                    MarkSessionDirty();
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested && source.IsCancellationRequested)
                {
                    Console.WriteLine("> System: 已取消当前代理执行。");
                }
                finally
                {
                    source.Cancel();
                    await cancelTask;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
                {
                    await SafeSaveAsync(_session, stoppingToken, force: false);
                }
                Console.ResetColor();
            }
        }
    }

    private static Task<string?> ReadInputAsync(CancellationToken cancellationToken)
    {
        return Task.Run(static () => Console.ReadLine()).WaitAsync(cancellationToken);
    }

    private async Task PrintHistoryAsync(AgentSession activeSession, CancellationToken cancellationToken)
    {
        if (activeSession.TryGetInMemoryChatHistory(out var history) && history != null)
        {
            foreach (var message in history)
            {
                if (string.IsNullOrWhiteSpace(message.Text) || message.Role == ChatRole.Tool)
                {
                    continue;
                }

                string? target = null;
                if (message.Role == ChatRole.User)
                {
                    target = $"> User : {message.Text}";
                }
                else if (message.Role == ChatRole.Assistant)
                {
                    target = $"> Agent: {message.Text}";
                }

                if (string.IsNullOrEmpty(target))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                Console.WriteLine(target);
            }
        }

        await Task.CompletedTask;
    }

    private async Task<bool> TryHandleCommandAsync(string input, CancellationToken cancellationToken)
    {
        if (string.Equals(input, "/help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return true;
        }

        if (string.Equals(input, "/cls", StringComparison.OrdinalIgnoreCase))
        {
            Console.Clear();
            return true;
        }

        if (string.Equals(input, "/history", StringComparison.OrdinalIgnoreCase))
        {
            if (_session != null)
            {
                await PrintHistoryAsync(_session, cancellationToken);
            }

            return true;
        }

        if (string.Equals(input, "/save", StringComparison.OrdinalIgnoreCase))
        {
            if (_session != null)
            {
                await SafeSaveAsync(_session, cancellationToken, force: true);
                Console.WriteLine("> System: 会话已保存。");
            }

            return true;
        }

        if (string.Equals(input, "/reset", StringComparison.OrdinalIgnoreCase) || string.Equals(input, "/clear", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(input, "/clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
            }

            await _sessionStore.ClearAsync(cancellationToken);
            _session = await _agentRunner.CreateSessionAsync(cancellationToken);
            MarkSessionDirty();
            Console.WriteLine("> System: 会话已重置。");
            return true;
        }

        return false;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("> System: 可用命令 /help /history /save /reset /clear /cls /exit");
        Console.WriteLine("> System: 执行中按 Esc 可中断当前代理。");
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
