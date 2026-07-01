using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;
using WesleyCode.Web.Interfaces;

namespace WesleyCode.Web.Services;

public sealed class ChatWorkspaceService : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebOutputCaptureState _outputState;
    private readonly ILogger<ChatWorkspaceService> _logger;
    private readonly string _workspacePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _channelId = Guid.NewGuid().ToString("N");
    private AgentSession? _session;
    private bool _initialized;

    public ChatWorkspaceService(
        IServiceProvider serviceProvider,
        IWebOutputCaptureState outputState,
        IOptions<WorkingOptions> workingOptions,
        ILogger<ChatWorkspaceService> logger)
    {
        _serviceProvider = serviceProvider;
        _outputState = outputState;
        _logger = logger;
        _workspacePath = workingOptions.Value.BasePath;
        _outputState.ChannelChanged += OnChannelChanged;
    }

    public event Action? Changed;

    public bool IsBusy { get; private set; }

    public IReadOnlyList<ChatMessage> Messages => _outputState.GetMessages(_channelId);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            _outputState.Reset(_channelId);
            try
            {
                using (_outputState.BeginChannel(_channelId))
                {
                    var sessionStore = GetSessionStore();
                    _session = await sessionStore.LoadAsync(cancellationToken);
                    GetAgentRunner().RestartSession(_session);
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "初始化对话工作区失败。");
                _outputState.AddSystemMessage(_channelId, $"初始化失败：{ex.Message}");
            }

            NotifyChanged();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SendAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        await InitializeAsync(cancellationToken);
        if (!_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;
            NotifyChanged();

            _outputState.AddUserMessage(_channelId, input);
            using (_outputState.BeginChannel(_channelId))
            {
                await GetAgentRunner().ExecuteAsync(input, _session!, cancellationToken);
            }

            await GetSessionStore().SaveAsync(_session!, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行智能体请求失败。");
            _outputState.AddSystemMessage(_channelId, ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
            _gate.Release();
        }
    }

    public async Task CreateNewConversationAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;
            NotifyChanged();

            await GetSessionStore().ClearAsync(cancellationToken);
            this.ClearWorkspaceContents();
            _outputState.Reset(_channelId);
            try
            {
                _session = await GetAgentRunner().CreateSessionAsync(cancellationToken);
                _initialized = true;
                _outputState.AddSystemMessage(_channelId, "已创建新的空白会话，工作区已清空。");
            }
            catch (Exception ex)
            {
                _initialized = false;
                _logger.LogWarning(ex, "创建新会话失败。");
                _outputState.AddSystemMessage(_channelId, $"创建会话失败：{ex.Message}");
            }
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _outputState.ChannelChanged -= OnChannelChanged;
        _gate.Dispose();
    }

    private void ClearWorkspaceContents()
    {
        if (string.IsNullOrWhiteSpace(_workspacePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(_workspacePath);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
            return;
        }

        foreach (var directory in Directory.GetDirectories(fullPath))
        {
            Directory.Delete(directory, recursive: true);
        }

        foreach (var file in Directory.GetFiles(fullPath))
        {
            File.Delete(file);
        }
    }

    private IAgentRunner GetAgentRunner() => _serviceProvider.GetRequiredService<IAgentRunner>();

    private ISessionStore GetSessionStore() => _serviceProvider.GetRequiredService<ISessionStore>();

    private void OnChannelChanged(string channelId)
    {
        if (channelId == _channelId)
        {
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }
}