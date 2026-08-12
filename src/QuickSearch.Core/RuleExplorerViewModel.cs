namespace QuickSearch.Core;

public sealed class RuleExplorerViewModel : ObservableObject
{
    private readonly AppConfiguration _configuration;
    private readonly IMappingStore _store;
    private readonly IFolderOpener _opener;
    private readonly Func<string, bool> _folderExists;
    private string _searchText = string.Empty;
    private Guid _currentFolderId;
    private Guid? _folderBeforeSearch;
    private IReadOnlyList<NavigationFolderNodeViewModel> _rootFolders = [];
    private IReadOnlyList<NavigationFolder> _pinnedFolders = [];
    private IReadOnlyList<NavigationFolder> _breadcrumbs = [];
    private IReadOnlyList<NavigationFolder> _childFolders = [];
    private IReadOnlyList<FolderRule> _currentRules = [];
    private IReadOnlyList<ExplorerSearchResult> _searchResults = [];

    public RuleExplorerViewModel(
        AppConfiguration configuration,
        IMappingStore store,
        IFolderOpener opener,
        Func<string, bool>? folderExists = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(opener);
        _configuration = configuration;
        _store = store;
        _opener = opener;
        _folderExists = folderExists ?? Directory.Exists;
        _currentFolderId = configuration.UncategorizedFolderId;

        NavigateFolderCommand = new RelayCommand<NavigationFolder>(folder =>
            NavigateToFolder(folder.Id));
        OpenRuleCommand = new AsyncRelayCommand<FolderRule>(OpenRuleAsync);
        PinFolderCommand = new AsyncRelayCommand<NavigationFolder>(folder =>
            PinFolderAsync(folder.Id));
        UnpinFolderCommand = new AsyncRelayCommand<NavigationFolder>(folder =>
            UnpinFolderAsync(folder.Id));
        ReorderPinnedFolderCommand = new AsyncRelayCommand<PinMoveRequest>(request =>
            ReorderPinnedFolderRelativeAsync(
                request.FolderId,
                request.TargetFolderId,
                request.PlaceAfterTarget));
        MoveRuleCommand = new AsyncRelayCommand<RuleMoveRequest>(request =>
            MoveRuleAsync(request.RuleId, request.TargetFolderId));
        CreateFolderCommand = new AsyncRelayCommand<FolderNameRequest>(request =>
            CreateFolderAsync(request.ParentId, request.Name));
        RenameFolderCommand = new AsyncRelayCommand<FolderNameRequest>(request =>
            request.FolderId is null
                ? Task.CompletedTask
                : RenameFolderAsync(request.FolderId.Value, request.Name));
        ClearSearchCommand = new RelayCommand(ClearSearch);
        RefreshFromConfiguration();
    }

    public event EventHandler<ExplorerStatusEventArgs>? StatusReported;

    public event EventHandler? ConfigurationChanged;

    public IReadOnlyList<NavigationFolderNodeViewModel> RootFolders
    {
        get => _rootFolders;
        private set => SetProperty(ref _rootFolders, value);
    }

    public IReadOnlyList<NavigationFolder> PinnedFolders
    {
        get => _pinnedFolders;
        private set => SetProperty(ref _pinnedFolders, value);
    }

    public NavigationFolder CurrentFolder =>
        _configuration.NavigationFolders.Single(folder => folder.Id == _currentFolderId);

    public IReadOnlyList<NavigationFolder> Breadcrumbs
    {
        get => _breadcrumbs;
        private set => SetProperty(ref _breadcrumbs, value);
    }

    public IReadOnlyList<NavigationFolder> ChildFolders
    {
        get => _childFolders;
        private set => SetProperty(ref _childFolders, value);
    }

    public IReadOnlyList<FolderRule> CurrentRules
    {
        get => _currentRules;
        private set => SetProperty(ref _currentRules, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? string.Empty;
            if (!SetProperty(ref _searchText, next))
            {
                return;
            }

            if (next.Trim().Length > 0 && _folderBeforeSearch is null)
            {
                _folderBeforeSearch = _currentFolderId;
            }

            if (next.Trim().Length == 0 && _folderBeforeSearch is Guid folderId)
            {
                _folderBeforeSearch = null;
                if (_configuration.NavigationFolders.Any(folder => folder.Id == folderId))
                {
                    _currentFolderId = folderId;
                }
            }

            RefreshSearchResults();
            RefreshVisibleContent();
            OnPropertyChanged(nameof(IsSearching));
            OnPropertyChanged(nameof(HasLocalSearchResults));
        }
    }

    public IReadOnlyList<ExplorerSearchResult> SearchResults
    {
        get => _searchResults;
        private set => SetProperty(ref _searchResults, value);
    }

    public bool IsSearching => SearchText.Trim().Length > 0;

    public bool HasLocalSearchResults => SearchResults.Count > 0;

    public RelayCommand<NavigationFolder> NavigateFolderCommand { get; }

    public AsyncRelayCommand<FolderRule> OpenRuleCommand { get; }

    public AsyncRelayCommand<NavigationFolder> PinFolderCommand { get; }

    public AsyncRelayCommand<NavigationFolder> UnpinFolderCommand { get; }

    public AsyncRelayCommand<PinMoveRequest> ReorderPinnedFolderCommand { get; }

    public AsyncRelayCommand<RuleMoveRequest> MoveRuleCommand { get; }

    public AsyncRelayCommand<FolderNameRequest> CreateFolderCommand { get; }

    public AsyncRelayCommand<FolderNameRequest> RenameFolderCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    public void NavigateToFolder(Guid folderId)
    {
        if (_configuration.NavigationFolders.All(folder => folder.Id != folderId))
        {
            Report("文件夹不存在。", StatusKind.Error);
            return;
        }

        _currentFolderId = folderId;
        if (IsSearching)
        {
            _searchText = string.Empty;
            _folderBeforeSearch = null;
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(IsSearching));
        }

        RefreshFromConfiguration();
    }

    public async Task PinFolderAsync(Guid folderId) =>
        await ApplyImmediateAsync(
            candidate => candidate.PinFolder(folderId),
            "已收藏文件夹。",
            "无法收藏文件夹");

    public async Task UnpinFolderAsync(Guid folderId) =>
        await ApplyImmediateAsync(
            candidate => candidate.UnpinFolder(folderId),
            "已取消收藏。",
            "无法取消收藏");

    public async Task ReorderPinnedFolderAsync(Guid folderId, int targetIndex) =>
        await ApplyImmediateAsync(
            candidate => candidate.ReorderPinnedFolder(folderId, targetIndex),
            "收藏顺序已保存。",
            "无法保存收藏顺序");

    public async Task ReorderPinnedFolderRelativeAsync(
        Guid folderId,
        Guid targetFolderId,
        bool placeAfterTarget) =>
        await ApplyImmediateAsync(
            candidate => candidate.ReorderPinnedFolderRelative(
                folderId,
                targetFolderId,
                placeAfterTarget),
            "收藏顺序已保存。",
            "无法保存收藏顺序");

    public async Task MoveRuleAsync(Guid ruleId, Guid targetFolderId)
    {
        if (_configuration.Rules.FirstOrDefault(rule => rule.Id == ruleId) is not { } rule
            || _configuration.NavigationFolders.FirstOrDefault(folder =>
                folder.Id == targetFolderId) is not { } targetFolder)
        {
            Report("快捷方式或目标文件夹不存在。", StatusKind.Error);
            return;
        }

        if (rule.NavigationFolderId == targetFolderId)
        {
            Report($"“{rule.DisplayTitle}”已经在“{targetFolder.Name}”中。", StatusKind.Neutral);
            return;
        }

        await ApplyImmediateAsync(
            candidate => candidate.MoveRule(ruleId, targetFolderId),
            $"已将“{rule.DisplayTitle}”移动到“{targetFolder.Name}”。",
            "无法移动快捷方式");
    }

    public async Task<NavigationFolder> CreateFolderAsync(Guid? parentId, string name)
    {
        NavigationFolder created = EmptyFolder();
        var success = await ApplyImmediateAsync(
            candidate => created = candidate.AddNavigationFolder(name, parentId),
            "文件夹已创建。",
            "无法创建文件夹");
        return success
            ? _configuration.NavigationFolders.Single(folder => folder.Id == created.Id)
            : EmptyFolder();
    }

    public async Task RenameFolderAsync(Guid folderId, string name) =>
        await ApplyImmediateAsync(
            candidate => candidate.RenameNavigationFolder(folderId, name),
            "文件夹已重命名。",
            "无法重命名文件夹");

    public async Task OpenRuleAsync(FolderRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!_folderExists(rule.FolderPath))
        {
            Report($"路径失效：{rule.FolderPath}", StatusKind.Error);
            return;
        }

        PlatformOperationResult result;
        try
        {
            result = _opener.Open(rule.FolderPath);
        }
        catch (Exception exception)
        {
            Report($"无法打开文件夹：{exception.Message}", StatusKind.Error);
            return;
        }

        if (!result.Success)
        {
            Report(result.Message, StatusKind.Error);
            return;
        }

        var candidate = _configuration.Clone();
        candidate.MarkMappingUsed(rule.Aliases[0], rule.FolderPath);
        try
        {
            await _store.SaveAsync(candidate);
            _configuration.ReplaceWith(candidate);
            RefreshFromConfiguration();
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
            Report($"已打开：{rule.FolderPath}", StatusKind.Success);
        }
        catch (Exception exception)
        {
            Report($"文件夹已打开，但无法保存使用时间：{exception.Message}", StatusKind.Error);
        }
    }

    public void RefreshFromConfiguration()
    {
        if (_configuration.NavigationFolders.All(folder => folder.Id != _currentFolderId))
        {
            _currentFolderId = _configuration.UncategorizedFolderId;
        }

        var currentPathIds = BuildFolderPath(_currentFolderId)
            .Select(folder => folder.Id)
            .ToHashSet();
        RootFolders = _configuration.NavigationFolders
            .Where(folder => folder.ParentId is null)
            .OrderBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .Select(folder => BuildNode(folder, currentPathIds))
            .ToArray();
        PinnedFolders = _configuration.PinnedFolderIds
            .Select(folderId => _configuration.NavigationFolders.FirstOrDefault(folder =>
                folder.Id == folderId))
            .Where(folder => folder is not null)
            .Cast<NavigationFolder>()
            .ToArray();
        RefreshSearchResults();
        RefreshVisibleContent();
    }

    private NavigationFolderNodeViewModel BuildNode(
        NavigationFolder folder,
        IReadOnlySet<Guid> currentPathIds)
    {
        var node = new NavigationFolderNodeViewModel(
            folder,
            _configuration.NavigationFolders
                .Where(candidate => candidate.ParentId == folder.Id)
                .OrderBy(candidate => candidate.SortOrder)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .Select(child => BuildNode(child, currentPathIds))
                .ToArray(),
            _configuration.PinnedFolderIds.Contains(folder.Id))
        {
            IsSelected = folder.Id == _currentFolderId,
            IsExpanded = currentPathIds.Contains(folder.Id)
        };
        return node;
    }

    private void RefreshVisibleContent()
    {
        OnPropertyChanged(nameof(CurrentFolder));
        Breadcrumbs = BuildFolderPath(_currentFolderId);
        ChildFolders = _configuration.NavigationFolders
            .Where(folder => folder.ParentId == _currentFolderId)
            .OrderBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CurrentRules = _configuration.Rules
            .Where(rule => rule.NavigationFolderId == _currentFolderId)
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void RefreshSearchResults()
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            SearchResults = [];
            return;
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<ExplorerSearchResult>();
        foreach (var folder in _configuration.NavigationFolders)
        {
            var path = string.Join(" › ", BuildFolderPath(folder.Id).Select(item => item.Name));
            if (MatchesTokens($"{folder.Name} {path}", tokens))
            {
                results.Add(new ExplorerSearchResult(
                    ExplorerSearchResultKind.Folder,
                    folder.Id,
                    folder.Name,
                    path,
                    string.Empty,
                    folder,
                    null));
            }
        }

        foreach (var rule in _configuration.Rules)
        {
            var path = string.Join(
                " › ",
                BuildFolderPath(rule.NavigationFolderId).Select(item => item.Name));
            var searchable = string.Join(
                ' ',
                rule.DisplayTitle,
                string.Join(' ', rule.Aliases),
                rule.FolderPath,
                path);
            if (MatchesTokens(searchable, tokens))
            {
                results.Add(new ExplorerSearchResult(
                    ExplorerSearchResultKind.Rule,
                    rule.Id,
                    rule.DisplayTitle,
                    path,
                    rule.FolderPath,
                    null,
                    rule));
            }
        }

        SearchResults = results
            .OrderBy(result => result.Kind)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<NavigationFolder> BuildFolderPath(Guid folderId)
    {
        var path = new List<NavigationFolder>();
        var current = _configuration.NavigationFolders.FirstOrDefault(folder =>
            folder.Id == folderId);
        var visited = new HashSet<Guid>();
        while (current is not null && visited.Add(current.Id))
        {
            path.Add(current);
            current = current.ParentId is Guid parentId
                ? _configuration.NavigationFolders.FirstOrDefault(folder =>
                    folder.Id == parentId)
                : null;
        }

        path.Reverse();
        return path;
    }

    private async Task<bool> ApplyImmediateAsync(
        Action<AppConfiguration> apply,
        string successMessage,
        string failurePrefix)
    {
        var before = _configuration.Clone();
        var candidate = _configuration.Clone();
        try
        {
            apply(candidate);
            await _store.SaveAsync(candidate);
            _configuration.ReplaceWith(candidate);
            RefreshFromConfiguration();
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
            Report(successMessage, StatusKind.Success);
            return true;
        }
        catch (Exception exception)
        {
            _configuration.ReplaceWith(before);
            RefreshFromConfiguration();
            Report($"{failurePrefix}：{exception.Message}", StatusKind.Error);
            return false;
        }
    }

    private void ClearSearch() => SearchText = string.Empty;

    private void Report(string message, StatusKind kind) =>
        StatusReported?.Invoke(this, new ExplorerStatusEventArgs(message, kind));

    private static bool MatchesTokens(string searchable, IEnumerable<string> tokens) =>
        tokens.All(token => searchable.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static NavigationFolder EmptyFolder() => new(
        Guid.Empty,
        null,
        string.Empty,
        0,
        default);
}
