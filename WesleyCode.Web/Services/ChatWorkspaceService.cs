using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;
using WesleyCode.Web.Interfaces;

namespace WesleyCode.Web.Services;

public sealed class ChatWorkspaceService : IDisposable
{
    private readonly IAgentRunner _agentRunner;
    private readonly ISessionStore _sessionStore;
    private readonly IWebOutputCaptureState _outputState;
    private readonly ILogger<ChatWorkspaceService> _logger;
    private readonly string? _workspacePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _channelId = Guid.NewGuid().ToString("N");
    private readonly FileSystemWatcher _workspaceWatcher;
    private AgentSession? _session;
    private CancellationTokenSource? _generationCancellation;
    private IReadOnlyList<WorkspaceEntryNode>? _workspaceEntries;
    private bool _initialized;

    public ChatWorkspaceService(
        IAgentRunner agentRunner,
        ISessionStore sessionStore,
        IWebOutputCaptureState outputState,
        IOptions<WorkingOptions> workingOptions,
        ILogger<ChatWorkspaceService> logger
    )
    {
        _agentRunner = agentRunner;
        _sessionStore = sessionStore;
        _outputState = outputState;
        _logger = logger;
        var workspacePath = workingOptions.Value.BasePath;
        _workspacePath = string.IsNullOrWhiteSpace(workspacePath) ? null : Path.GetFullPath(workspacePath);
        _outputState.ChannelChanged += OnChannelChanged;
        _workspaceWatcher = CreateWorkspaceWatcher();
    }

    public event Action? Changed;

    public bool IsBusy { get; private set; }

    public IReadOnlyList<ChatMessage> Messages => _outputState.GetMessages(_channelId);

    public IReadOnlyList<WorkspaceEntryNode> WorkspaceEntries
    {
        get
        {
            var entries = Volatile.Read(ref _workspaceEntries);
            return entries ?? CacheWorkspaceEntries();
        }
    }

    public void CancelGeneration()
    {
        try
        {
            Volatile.Read(ref _generationCancellation)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 生成任务恰好结束时，取消令牌可能已被释放。
        }
    }

    public void RefreshWorkspaceEntries()
    {
        Interlocked.Exchange(ref _workspaceEntries, null);
        NotifyChanged();
    }

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
                    _session = await _sessionStore.LoadAsync(cancellationToken);
                    await _agentRunner.RestartSessionAsync(_session, cancellationToken);
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

        if (_session is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        using var generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            IsBusy = true;
            Volatile.Write(ref _generationCancellation, generationCancellation);

            NotifyChanged();

            _outputState.AddUserMessage(_channelId, input);
            using (_outputState.BeginChannel(_channelId))
            {
                await _agentRunner.ExecuteAsync([new ChatMessage(ChatRole.User, input)], _session, generationCancellation.Token);
            }

            await _sessionStore.SaveAsync(_session, generationCancellation.Token);
        }
        catch (OperationCanceledException) when (generationCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _outputState.AddSystemMessage(_channelId, "已中断当前生成。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行智能体请求失败。");
            _outputState.AddSystemMessage(_channelId, ex.Message);
        }
        finally
        {
            Interlocked.CompareExchange(ref _generationCancellation, null, generationCancellation);

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

            await _sessionStore.ClearAsync(cancellationToken);
            this.ClearWorkspaceContents();
            RefreshWorkspaceEntries();
            _outputState.Reset(_channelId);
            try
            {
                _session = await _agentRunner.CreateSessionAsync(cancellationToken);
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
        if (_workspaceWatcher is not null)
        {
            _workspaceWatcher.Created -= OnWorkspaceFilesChanged;
            _workspaceWatcher.Changed -= OnWorkspaceFilesChanged;
            _workspaceWatcher.Deleted -= OnWorkspaceFilesChanged;
            _workspaceWatcher.Renamed -= OnWorkspaceFilesRenamed;
            _workspaceWatcher.Dispose();
        }
        _gate.Dispose();
    }

    private void ClearWorkspaceContents()
    {
        if (_workspacePath is null)
        {
            return;
        }

        if (!Directory.Exists(_workspacePath))
        {
            Directory.CreateDirectory(_workspacePath);
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(_workspacePath))
        {
            Directory.Delete(directory, recursive: true);
        }

        foreach (var file in Directory.EnumerateFiles(_workspacePath))
        {
            File.Delete(file);
        }
    }

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

    private FileSystemWatcher CreateWorkspaceWatcher()
    {
        if (_workspacePath is null)
        {
            throw new InvalidOperationException("工作区路径未配置，无法创建文件系统监视器。");
        }

        Directory.CreateDirectory(_workspacePath);

        var watcher = new FileSystemWatcher(_workspacePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };

        watcher.Created += OnWorkspaceFilesChanged;
        watcher.Changed += OnWorkspaceFilesChanged;
        watcher.Deleted += OnWorkspaceFilesChanged;
        watcher.Renamed += OnWorkspaceFilesRenamed;
        return watcher;
    }

    private void OnWorkspaceFilesChanged(object? sender, FileSystemEventArgs e)
    {
        RefreshWorkspaceEntries();
    }

    private void OnWorkspaceFilesRenamed(object? sender, RenamedEventArgs e)
    {
        RefreshWorkspaceEntries();
    }

    private IReadOnlyList<WorkspaceEntryNode> GetWorkspaceEntries()
    {
        if (_workspacePath is null || !Directory.Exists(_workspacePath))
        {
            return [];
        }

        return BuildWorkspaceEntries(_workspacePath, _workspacePath);
    }

    private IReadOnlyList<WorkspaceEntryNode> CacheWorkspaceEntries()
    {
        var entries = GetWorkspaceEntries();
        return Interlocked.CompareExchange(ref _workspaceEntries, entries, null) ?? entries;
    }

    private IReadOnlyList<WorkspaceEntryNode> BuildWorkspaceEntries(string rootPath, string currentPath)
    {
        List<WorkspaceEntryNode> entries = [];

        if (!Directory.Exists(currentPath))
        {
            return entries;
        }

        try
        {
            foreach (var directoryPath in Directory.EnumerateDirectories(currentPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var directoryName = Path.GetFileName(directoryPath);
                var relativePath = Path.GetRelativePath(rootPath, directoryPath).Replace('\\', '/');
                entries.Add(new WorkspaceEntryNode(directoryName, relativePath, true, BuildWorkspaceEntries(rootPath, directoryPath)));
            }

            foreach (var filePath in Directory.EnumerateFiles(currentPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(filePath);
                var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
                entries.Add(new WorkspaceEntryNode(fileName, relativePath, false, []));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取工作区目录失败：{DirectoryPath}", currentPath);
        }

        return entries;
    }

    public sealed record WorkspaceEntryNode(string Name, string RelativePath, bool IsDirectory, IReadOnlyList<WorkspaceEntryNode> Children);
}
