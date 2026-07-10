namespace QuickSearch.Core;

public sealed class LauncherViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(200);

    private readonly IMappingStore _store;
    private readonly IFolderSearch _search;
    private readonly IClipboardTextReader _clipboard;
    private readonly IFolderOpener _opener;
    private readonly Func<string, bool> _folderExists;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private CancellationTokenSource? _searchCancellation;
    private long _searchGeneration;
    private AppConfiguration _configuration = new();
    private string _alias = string.Empty;
    private string _folderQuery = string.Empty;
    private string _status = "正在初始化…";
    private IReadOnlyList<FolderSearchResult> _results = [];
    private FolderSearchResult? _selectedResult;
    private FolderMapping? _exactMapping;
    private bool _disposed;

    public LauncherViewModel(
        IMappingStore store,
        IFolderSearch search,
        IClipboardTextReader clipboard,
        IFolderOpener opener,
        Func<string, bool>? folderExists = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(opener);
        _store = store;
        _search = search;
        _clipboard = clipboard;
        _opener = opener;
        _folderExists = folderExists ?? Directory.Exists;
        _delayAsync = delayAsync ?? Task.Delay;

        SearchCommand = new AsyncRelayCommand(
            SearchNowAsync,
            () => !string.IsNullOrWhiteSpace(FolderQuery),
            exception => Status = $"搜索失败：{exception.Message}");
        ConfirmOpenCommand = new AsyncRelayCommand(
            ConfirmAndOpenAsync,
            () => CanConfirmOpen,
            exception => Status = $"无法打开文件夹：{exception.Message}");
        HideCommand = new RelayCommand(() => HideRequested?.Invoke(this, EventArgs.Empty));
        ShowSettingsCommand = new RelayCommand(
            () => ShowSettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? HideRequested;

    public event EventHandler? ShowSettingsRequested;

    public AppConfiguration Configuration
    {
        get => _configuration;
        private set
        {
            if (SetProperty(ref _configuration, value))
            {
                OnPropertyChanged(nameof(ShortcutInstruction));
            }
        }
    }

    public string Alias
    {
        get => _alias;
        set
        {
            if (SetProperty(ref _alias, value ?? string.Empty))
            {
                RefreshAliasState();
            }
        }
    }

    public string FolderQuery
    {
        get => _folderQuery;
        set
        {
            if (SetProperty(ref _folderQuery, value ?? string.Empty))
            {
                SearchCommand.NotifyCanExecuteChanged();
                QueueDebouncedSearch();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public IReadOnlyList<FolderSearchResult> Results
    {
        get => _results;
        private set => SetProperty(ref _results, value);
    }

    public FolderSearchResult? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (SetProperty(ref _selectedResult, value))
            {
                NotifyConfirmStateChanged();
            }
        }
    }

    public string ShortcutInstruction =>
        $"复制邮箱别名后按 {Configuration.Settings.GlobalShortcut}";

    public bool CanConfirmOpen =>
        !string.IsNullOrWhiteSpace(Alias)
        && (_exactMapping is not null || SelectedResult is not null);

    public AsyncRelayCommand SearchCommand { get; }

    public AsyncRelayCommand ConfirmOpenCommand { get; }

    public RelayCommand HideCommand { get; }

    public RelayCommand ShowSettingsCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Configuration = await _store.LoadAsync(cancellationToken);
            RefreshAliasState();
        }
        catch (OperationCanceledException)
        {
            Status = "配置加载已取消。";
        }
        catch (Exception exception)
        {
            Status = $"无法加载配置：{exception.Message}";
        }
    }

    public void ActivateFromClipboard()
    {
        try
        {
            var result = _clipboard.ReadText();
            if (!result.Success)
            {
                Status = result.Message;
                return;
            }

            if (string.IsNullOrWhiteSpace(result.Value))
            {
                Status = "剪贴板中没有可用的邮箱别名，请手动输入。";
                return;
            }

            Alias = result.Value;
        }
        catch (Exception exception)
        {
            Status = $"无法读取剪贴板：{exception.Message}";
        }
    }

    public Task SearchNowAsync()
    {
        ThrowIfDisposed();
        var query = FolderQuery.Trim();
        var generation = BeginSearch(out var cancellation);
        if (query.Length == 0)
        {
            Results = [];
            SelectedResult = null;
            Status = "请输入文件夹名称。";
            return Task.CompletedTask;
        }

        return ExecuteSearchAsync(query, generation, cancellation.Token);
    }

    public void CancelSearch()
    {
        Interlocked.Increment(ref _searchGeneration);
        CancelCurrentSearch();
        Status = "搜索已取消。";
    }

    public async Task ConfirmAndOpenAsync()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _searchGeneration);
        CancelCurrentSearch();
        var alias = Alias.Trim();
        if (alias.Length == 0)
        {
            Status = "请输入邮箱别名。";
            return;
        }

        var path = _exactMapping?.FolderPath ?? SelectedResult?.FullPath;
        if (path is null)
        {
            Status = "请先搜索并选择一个文件夹。";
            return;
        }

        PlatformOperationResult openResult;
        try
        {
            openResult = _opener.Open(path);
        }
        catch (Exception exception)
        {
            Status = $"无法打开文件夹：{exception.Message}";
            return;
        }

        if (!openResult.Success)
        {
            Status = openResult.Message;
            return;
        }

        var candidate = Configuration.Clone();
        if (_exactMapping is null)
        {
            candidate.UpsertMapping(alias, path);
        }

        candidate.MarkMappingUsed(alias);
        try
        {
            await _store.SaveAsync(candidate);
        }
        catch (Exception exception)
        {
            Status = $"文件夹已打开，但无法保存映射：{exception.Message}";
            return;
        }

        Configuration.ReplaceWith(candidate);
        OnPropertyChanged(nameof(ShortcutInstruction));
        HideRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshConfiguration()
    {
        OnPropertyChanged(nameof(ShortcutInstruction));
        RefreshAliasState();
    }

    public void ReportStatus(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Status = message;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelCurrentSearch();
    }

    private void RefreshAliasState()
    {
        _exactMapping = null;
        Results = [];
        SelectedResult = null;
        var alias = Alias.Trim();
        if (alias.Length == 0)
        {
            Status = "请先复制或输入邮箱别名。";
            NotifyConfirmStateChanged();
            return;
        }

        var mapping = Configuration.FindMapping(alias);
        if (mapping is not null && _folderExists(mapping.FolderPath))
        {
            _exactMapping = mapping;
            var folderName = Path.GetFileName(
                mapping.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Status = $"已找到保存的映射：{mapping.Alias} → {folderName}\n{mapping.FolderPath}\n请确认后打开。";
            NotifyConfirmStateChanged();
            return;
        }

        Status = mapping is null
            ? "首次使用这个别名，请输入真实文件夹名称并搜索。"
            : "保存的文件夹已经不存在，请重新搜索并修复映射。";
        if (string.IsNullOrWhiteSpace(FolderQuery))
        {
            FolderQuery = alias;
        }

        NotifyConfirmStateChanged();
    }

    private void QueueDebouncedSearch()
    {
        var query = FolderQuery.Trim();
        var generation = BeginSearch(out var cancellation);
        if (query.Length == 0)
        {
            Results = [];
            SelectedResult = null;
            return;
        }

        _ = DebounceAndSearchAsync(query, generation, cancellation.Token);
    }

    private async Task DebounceAndSearchAsync(
        string query,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await _delayAsync(SearchDebounce, cancellationToken);
            await ExecuteSearchAsync(query, generation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (IsCurrentSearch(generation))
            {
                Status = "搜索已取消。";
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentSearch(generation))
            {
                Status = $"Everything 查询失败：{exception.Message}";
            }
        }
    }

    private async Task ExecuteSearchAsync(
        string query,
        long generation,
        CancellationToken cancellationToken)
    {
        if (IsCurrentSearch(generation))
        {
            Status = "正在通过 Everything 搜索…";
        }

        try
        {
            var results = await _search.SearchAsync(query, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentSearch(generation))
            {
                return;
            }

            Results = results.Take(20).ToArray();
            SelectedResult = Results.FirstOrDefault();
            Status = FormatSearchStatus(_search.Health, Results.Count, _search.FailureMessage);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentSearch(generation))
            {
                Status = "搜索已取消。";
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentSearch(generation))
            {
                Results = [];
                SelectedResult = null;
                Status = $"Everything 查询失败：{exception.Message}";
            }
        }
    }

    internal static string FormatSearchStatus(
        EverythingHealth health,
        int resultCount,
        string? failureMessage) => health switch
        {
            EverythingHealth.Ready when resultCount > 0 => $"找到 {resultCount} 个候选文件夹。",
            EverythingHealth.Ready => "没有找到匹配的文件夹。",
            EverythingHealth.DllMissing => "缺少 Everything64.dll，请重新安装 QuickSearch。",
            EverythingHealth.NotReady => "Everything 索引数据库正在加载，请稍后重试。",
            EverythingHealth.Unavailable => "无法连接 Everything，请安装并启动普通版 Everything 1.4。",
            _ => failureMessage ?? "Everything 查询失败，请重试。"
        };

    private long BeginSearch(out CancellationTokenSource cancellation)
    {
        CancelCurrentSearch();
        cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        return Interlocked.Increment(ref _searchGeneration);
    }

    private bool IsCurrentSearch(long generation) =>
        Interlocked.Read(ref _searchGeneration) == generation;

    private void CancelCurrentSearch()
    {
        var cancellation = Interlocked.Exchange(ref _searchCancellation, null);
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void NotifyConfirmStateChanged()
    {
        OnPropertyChanged(nameof(CanConfirmOpen));
        ConfirmOpenCommand.NotifyCanExecuteChanged();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
