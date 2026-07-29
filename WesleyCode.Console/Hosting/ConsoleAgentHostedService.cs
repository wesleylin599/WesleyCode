using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Spectre.Console;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;

namespace WesleyCode.Console.Hosting;

internal sealed class ConsoleAgentHostedService : BackgroundService
{
    private readonly IAgentRunner _agentRunner;
    private readonly ISessionStore _sessionStore;
    private readonly IOutputCapture _outputCapture;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IOptions<WorkingOptions> _workingOptions;
    private readonly IOptions<ChatClientOptions> _chatClientOptions;
    private readonly ILogger<ConsoleAgentHostedService> _logger;

    public ConsoleAgentHostedService(
        IAgentRunner agentRunner,
        ISessionStore sessionStore,
        IOutputCapture outputCapture,
        IHostApplicationLifetime lifetime,
        IOptions<WorkingOptions> workingOptions,
        IOptions<ChatClientOptions> chatClientOptions,
        ILogger<ConsoleAgentHostedService> logger
    )
    {
        this._agentRunner = agentRunner;
        this._sessionStore = sessionStore;
        this._outputCapture = outputCapture;
        this._lifetime = lifetime;
        this._workingOptions = workingOptions;
        this._chatClientOptions = chatClientOptions;
        this._logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        LogConfig();
        return base.StartAsync(cancellationToken);
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

    private async Task SafeSaveAsync(AgentSession session, CancellationToken cancellationToken)
    {
        try
        {
            await _sessionStore.SaveAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex.Message, "Failed to persist session.");
        }
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        var session = await _sessionStore.LoadAsync(stoppingToken);
        await _agentRunner.RestartSessionAsync(session, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, stoppingToken);
                _outputCapture.WriteUserTitle();
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
                    session = await _agentRunner.CreateSessionAsync(stoppingToken);
                    LogConfig();
                    continue;
                }

                using var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                try
                {
                    List<ChatMessage> messages = [new ChatMessage(ChatRole.User, input)];
                    var executeTask = _agentRunner.ExecuteAsync(messages, session, source.Token);
                    var cancelTask = CancelAgentAsync(source);
                    var mainTask = Task.WhenAny(executeTask, cancelTask);
                    await AnsiConsole.Status().StartAsync("执行中（按 Esc 取消）", _ => mainTask);
                }
                finally
                {
                    source.Cancel();
                }
            }
            catch (Exception ex)
            {
                _outputCapture.WriteSystemMessage(ex.Message);
            }
            finally
            {
                await SafeSaveAsync(session, stoppingToken);
            }
        }
    }

    private void LogConfig()
    {
        Table table = new Table();
        table.AddColumn("配置项");
        table.AddColumn("值");
        if (!string.IsNullOrWhiteSpace(_chatClientOptions.Value.Provider))
        {
            table.AddRow(new Text("Provider"), new Text(_chatClientOptions.Value.Provider));
        }
        if (!string.IsNullOrWhiteSpace(_chatClientOptions.Value.BaseUrl))
        {
            table.AddRow(new Text("BaseUrl"), new Text(_chatClientOptions.Value.BaseUrl));
        }
        if (!string.IsNullOrWhiteSpace(_chatClientOptions.Value.ModelId))
        {
            table.AddRow(new Text("ModelId"), new Text(_chatClientOptions.Value.ModelId));
        }
        table.AddRow(new Text("Working"), new Text(_workingOptions.Value.BasePath));
        AnsiConsole.Write(table);
    }

    private static async Task CancelAgentAsync(CancellationTokenSource source)
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
}
