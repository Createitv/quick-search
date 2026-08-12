namespace QuickSearch.Core;

public sealed class QuickLauncherViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(120);
    private readonly AppConfiguration _configuration;
    private readonly IMappingStore _store;
    private readonly IQuickLauncherSearch _search;
    private readonly IPathOpener _opener;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly IClipboardTextReader? _clipboard;
    private CancellationTokenSource? _searchCancellation;
    private string? _clipboardQuery;
    private string _searchText = string.Empty;
    private IReadOnlyList<QuickLauncherResult> _results = [];
    private QuickLauncherResult? _selectedResult;
    private string _statusText = "输入快捷导航名称，或搜索应用、文件和文件夹";
    private bool _isSearching;
    private bool _disposed;

    public QuickLauncherViewModel(
        AppConfiguration configuration,
        IMappingStore store,
        IQuickLauncherSearch search,
        IPathOpener opener,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        IClipboardTextReader? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(opener);
        _configuration = configuration;
        _store = store;
        _search = search;
        _opener = opener;
        _delayAsync = delayAsync ?? Task.Delay;
        _clipboard = clipboard;
    }

    public event EventHandler? HideRequested;

    public event EventHandler? SettingsRequested;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                QueueSearch();
            }
        }
    }

    public IReadOnlyList<QuickLauncherResult> Results
    {
        get => _results;
        private set
        {
            if (SetProperty(ref _results, value))
            {
                OnPropertyChanged(nameof(HasResults));
            }
        }
    }

    public QuickLauncherResult? SelectedResult
    {
        get => _selectedResult;
        set => SetProperty(ref _selectedResult, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public bool HasResults => Results.Count > 0;

    public string LauncherShortcutText =>
        $"{_configuration.Settings.QuickLauncherShortcut} 呼出启动器";

    public void Reset()
    {
        CancelSearch();
        _clipboardQuery = null;
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        Results = [];
        SelectedResult = null;
        IsSearching = false;
        StatusText = "输入快捷导航名称，或搜索应用、文件和文件夹";
        OnPropertyChanged(nameof(LauncherShortcutText));
    }

    public void PrefillFromClipboard()
    {
        if (_clipboard is null)
        {
            return;
        }

        PlatformOperationResult<string?> result;
        try
        {
            result = _clipboard.ReadText();
        }
        catch
        {
            return;
        }

        var query = result.Success ? result.Value?.Trim() ?? string.Empty : string.Empty;
        if (query.Length == 0)
        {
            return;
        }

        _clipboardQuery = query;
        SearchText = query;
    }

    public async Task<bool> OpenSelectedAsync()
    {
        if (SelectedResult is null)
        {
            return false;
        }

        PlatformOperationResult result;
        try
        {
            result = _opener.Open(SelectedResult.FullPath);
        }
        catch (Exception exception)
        {
            StatusText = $"无法打开项目：{exception.Message}";
            return false;
        }
        if (!result.Success)
        {
            StatusText = result.Message;
            return false;
        }

        if (SelectedResult.RuleId is Guid ruleId)
        {
            var rule = _configuration.Rules.FirstOrDefault(item => item.Id == ruleId);
            if (rule is not null)
            {
                var candidate = _configuration.Clone();
                candidate.MarkMappingUsed(rule.Aliases[0], rule.FolderPath);
                try
                {
                    await _store.SaveAsync(candidate);
                    _configuration.ReplaceWith(candidate);
                }
                catch (Exception exception)
                {
                    StatusText = $"项目已打开，但无法保存使用时间：{exception.Message}";
                }
            }
        }

        HideRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> OpenShortcutAsync(int number)
    {
        if (number is < 1 or > 9 || number > Results.Count)
        {
            return false;
        }

        SelectedResult = Results[number - 1];
        return await OpenSelectedAsync();
    }

    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void RequestHide() => HideRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelSearch();
    }

    private void QueueSearch()
    {
        CancelSearch();
        var query = SearchText.Trim();
        if (_clipboardQuery is not null
            && !string.Equals(_clipboardQuery, query, StringComparison.Ordinal))
        {
            _clipboardQuery = null;
        }

        if (query.Length == 0)
        {
            Results = [];
            SelectedResult = null;
            IsSearching = false;
            StatusText = "输入快捷导航名称，或搜索应用、文件和文件夹";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        _ = SearchAsync(query, cancellation.Token);
    }

    private async Task SearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            IsSearching = true;
            StatusText = "正在搜索…";
            await _delayAsync(SearchDebounce, cancellationToken);

            var local = BuildLocalResults(query);
            var usedEverything = local.Count == 0;
            var candidates = usedEverything
                ? (await _search.SearchLauncherAsync(query, cancellationToken))
                    .Select(item => new QuickLauncherResult(
                        item.Name,
                        item.FullPath,
                        GetKindLabel(item.Kind),
                        item.Kind))
                : local;
            cancellationToken.ThrowIfCancellationRequested();
            var combined = candidates
                .Take(20)
                .Select((item, index) => item with
                {
                    ShortcutText = index < 9 ? $"Ctrl+{index + 1}" : string.Empty
                })
                .ToArray();

            Results = combined;
            SelectedResult = combined.FirstOrDefault();
            if (combined.Length == 0 && IsClipboardQuery(query))
            {
                ClearClipboardQuery("剪贴板内容没有匹配项，请手动输入。");
                return;
            }

            if (IsClipboardQuery(query))
            {
                _clipboardQuery = null;
            }

            StatusText = combined.Length == 0
                ? "快捷导航和 Everything 都没有找到匹配项"
                : usedEverything
                    ? $"快捷导航无匹配 · Everything 找到 {combined.Length} 项 · Enter 打开"
                    : $"找到 {combined.Length} 个快捷导航 · Enter 打开 · Ctrl+1–9 快速打开";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (IsClipboardQuery(query))
            {
                ClearClipboardQuery("无法搜索剪贴板内容，请手动输入。");
                return;
            }

            Results = [];
            SelectedResult = null;
            StatusText = $"搜索失败：{exception.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsSearching = false;
            }
        }
    }

    private static string GetKindLabel(QuickLauncherItemKind kind) => kind switch
    {
        QuickLauncherItemKind.Folder => "文件夹",
        QuickLauncherItemKind.Application => "应用",
        QuickLauncherItemKind.File => "文件",
        _ => "我的快捷导航"
    };

    private IReadOnlyList<QuickLauncherResult> BuildLocalResults(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _configuration.Rules
            .Where(rule => tokens.All(token =>
                rule.DisplayTitle.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(rule => rule.LastUsedAtUtc)
            .ThenBy(rule => rule.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .Select(rule => new QuickLauncherResult(
                rule.DisplayTitle,
                rule.FolderPath,
                "我的快捷导航",
                QuickLauncherItemKind.Shortcut,
                rule.Id))
            .ToArray();
    }

    private void CancelSearch()
    {
        var previous = Interlocked.Exchange(ref _searchCancellation, null);
        previous?.Cancel();
        previous?.Dispose();
    }

    private bool IsClipboardQuery(string query) => string.Equals(
        _clipboardQuery,
        query,
        StringComparison.Ordinal);

    private void ClearClipboardQuery(string statusText)
    {
        _clipboardQuery = null;
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        Results = [];
        SelectedResult = null;
        StatusText = statusText;
    }
}
