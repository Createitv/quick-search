namespace QuickSearch.Core;

public sealed class QuickLauncherViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(120);
    private const int MinimumResultColumns = 1;
    private const int MaximumResultColumns = 3;
    private const double MinimumUiFontSize = 11;
    private const double MaximumUiFontSize = 22;
    private readonly AppConfiguration _configuration;
    private readonly IMappingStore _store;
    private readonly IQuickLauncherSearch _search;
    private readonly IPathOpener _opener;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private CancellationTokenSource? _searchCancellation;
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
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
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

    public int ResultColumns => ClampResultColumns(
        _configuration.Settings.QuickLauncherResultColumns);

    public double UiFontSize => ClampUiFontSize(_configuration.Settings.UiFontSize);

    public double SearchFontSize => UiFontSize + 8;

    public double ResultTitleFontSize => UiFontSize;

    public double ResultDetailFontSize => Math.Max(10, UiFontSize - 2.5);

    public void Reset()
    {
        CancelSearch();
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        Results = [];
        SelectedResult = null;
        IsSearching = false;
        StatusText = "输入快捷导航名称，或搜索应用、文件和文件夹";
        OnPropertyChanged(nameof(LauncherShortcutText));
        RefreshSettings();
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

    public async Task SetResultColumnsAsync(int columns)
    {
        var normalized = ClampResultColumns(columns);
        if (normalized == ResultColumns)
        {
            return;
        }

        var candidate = _configuration.Clone();
        candidate.Settings = candidate.Settings with
        {
            QuickLauncherResultColumns = normalized
        };
        await _store.SaveAsync(candidate);
        _configuration.ReplaceWith(candidate);
        RefreshSettings();
    }

    public void ReorderResult(QuickLauncherResult source, QuickLauncherResult target)
    {
        var items = Results.ToList();
        var sourceIndex = items.IndexOf(source);
        var targetIndex = items.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        items.RemoveAt(sourceIndex);
        targetIndex = items.IndexOf(target);
        items.Insert(targetIndex, source);
        Results = items
            .Select((item, index) => item with
            {
                ShortcutText = index < 9 ? $"Ctrl+{index + 1}" : string.Empty
            })
            .ToArray();
        SelectedResult = source;
    }

    public void RefreshSettings()
    {
        OnPropertyChanged(nameof(LauncherShortcutText));
        OnPropertyChanged(nameof(ResultColumns));
        OnPropertyChanged(nameof(UiFontSize));
        OnPropertyChanged(nameof(SearchFontSize));
        OnPropertyChanged(nameof(ResultTitleFontSize));
        OnPropertyChanged(nameof(ResultDetailFontSize));
    }

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

    private static int ClampResultColumns(int columns) =>
        Math.Clamp(columns, MinimumResultColumns, MaximumResultColumns);

    private static double ClampUiFontSize(double fontSize) =>
        Math.Clamp(fontSize, MinimumUiFontSize, MaximumUiFontSize);
}
