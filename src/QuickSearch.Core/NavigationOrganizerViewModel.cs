namespace QuickSearch.Core;

public enum FolderDeletionMode
{
    MoveContentsToParent,
    DeleteSubtree
}

public sealed class NavigationOrganizerViewModel : ObservableObject
{
    private AppConfiguration _candidate;
    private NavigationFolder? _selectedFolder;
    private FolderRuleEditorViewModel? _selectedRule;
    private IReadOnlyList<NavigationFolderNodeViewModel> _rootFolders = [];
    private IReadOnlyList<FolderRuleEditorViewModel> _currentRules = [];
    private List<FolderRuleEditorViewModel> _editors = [];
    private string _newFolderName = string.Empty;
    private string _renameFolderName = string.Empty;
    private string _newRuleTitle = string.Empty;
    private string _newRuleKeywords = string.Empty;
    private string _newRulePath = string.Empty;

    public NavigationOrganizerViewModel(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _candidate = configuration.Clone();
        SelectFolderCommand = new RelayCommand<NavigationFolder>(folder => SelectFolder(folder.Id));
        AddFolderCommand = new RelayCommand(AddFolderFromInput);
        RenameFolderCommand = new RelayCommand(RenameSelectedFolderFromInput);
        AddRuleCommand = new RelayCommand(AddRuleFromInput);
        DeleteRuleCommand = new RelayCommand(DeleteSelectedRule, () => SelectedRule is not null);
        BeginEdit(configuration);
    }

    public IReadOnlyList<NavigationFolder> Folders => _candidate.NavigationFolders;

    public Guid UncategorizedFolderId => _candidate.UncategorizedFolderId;

    public IReadOnlyList<NavigationFolderNodeViewModel> RootFolders
    {
        get => _rootFolders;
        private set => SetProperty(ref _rootFolders, value);
    }

    public NavigationFolder? SelectedFolder
    {
        get => _selectedFolder;
        private set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                RenameFolderName = value?.Name ?? string.Empty;
                RefreshCurrentRules();
            }
        }
    }

    public FolderRuleEditorViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                DeleteRuleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<FolderRuleEditorViewModel> CurrentRules
    {
        get => _currentRules;
        private set => SetProperty(ref _currentRules, value);
    }

    public string NewFolderName
    {
        get => _newFolderName;
        set => SetProperty(ref _newFolderName, value ?? string.Empty);
    }

    public string RenameFolderName
    {
        get => _renameFolderName;
        set => SetProperty(ref _renameFolderName, value ?? string.Empty);
    }

    public string NewRuleTitle
    {
        get => _newRuleTitle;
        set => SetProperty(ref _newRuleTitle, value ?? string.Empty);
    }

    public string NewRuleKeywords
    {
        get => _newRuleKeywords;
        set => SetProperty(ref _newRuleKeywords, value ?? string.Empty);
    }

    public string NewRulePath
    {
        get => _newRulePath;
        set => SetProperty(ref _newRulePath, value ?? string.Empty);
    }

    public RelayCommand<NavigationFolder> SelectFolderCommand { get; }

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand RenameFolderCommand { get; }

    public RelayCommand AddRuleCommand { get; }

    public RelayCommand DeleteRuleCommand { get; }

    public void BeginEdit(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _candidate = configuration.Clone();
        _editors = _candidate.Rules.Select(rule => new FolderRuleEditorViewModel(rule)).ToList();
        var selectedId = SelectedFolder?.Id;
        RefreshTree();
        SelectFolder(
            selectedId is Guid id && Folders.Any(folder => folder.Id == id)
                ? id
                : UncategorizedFolderId);
    }

    public AppConfiguration BuildCandidate()
    {
        CommitEditors();
        return _candidate.Clone();
    }

    public NavigationFolder CreateFolder(Guid? parentId, string name)
    {
        CommitEditors();
        var folder = _candidate.AddNavigationFolder(name, parentId);
        RefreshTree();
        SelectFolder(folder.Id);
        return folder;
    }

    public void RenameFolder(Guid folderId, string name)
    {
        CommitEditors();
        _candidate.RenameNavigationFolder(folderId, name);
        RefreshTree();
        SelectFolder(folderId);
    }

    public void MoveFolder(Guid folderId, Guid? parentId, int sortOrder)
    {
        CommitEditors();
        _candidate.MoveNavigationFolder(folderId, parentId, sortOrder);
        RefreshTree();
        SelectFolder(folderId);
    }

    public void DeleteFolder(Guid folderId, FolderDeletionMode mode)
    {
        CommitEditors();
        if (folderId == UncategorizedFolderId)
        {
            throw new InvalidOperationException("未分类文件夹不能删除。");
        }

        var folder = Folders.Single(item => item.Id == folderId);
        if (mode == FolderDeletionMode.MoveContentsToParent)
        {
            var targetId = folder.ParentId ?? UncategorizedFolderId;
            foreach (var rule in _candidate.Rules.Where(rule => rule.NavigationFolderId == folderId).ToArray())
            {
                _candidate.UpdateRule(rule.Id, rule.Title, rule.Aliases, rule.FolderPath, targetId);
            }

            foreach (var child in Folders.Where(item => item.ParentId == folderId).ToArray())
            {
                _candidate.MoveNavigationFolder(child.Id, targetId, child.SortOrder);
            }

            _candidate.RemoveNavigationFolder(folderId);
        }
        else
        {
            var subtree = GetSubtree(folderId).ToArray();
            foreach (var rule in _candidate.Rules.Where(rule => subtree.Contains(rule.NavigationFolderId)).ToArray())
            {
                _candidate.RemoveRule(rule.Id);
            }

            foreach (var id in subtree.Reverse())
            {
                _candidate.RemoveNavigationFolder(id);
            }
        }

        ReloadEditors();
        RefreshTree();
        SelectFolder(folder.ParentId ?? UncategorizedFolderId);
    }

    public void MoveRules(IEnumerable<Guid> ruleIds, Guid targetFolderId)
    {
        ArgumentNullException.ThrowIfNull(ruleIds);
        CommitEditors();
        foreach (var ruleId in ruleIds.Distinct())
        {
            var rule = _candidate.Rules.Single(item => item.Id == ruleId);
            _candidate.UpdateRule(rule.Id, rule.Title, rule.Aliases, rule.FolderPath, targetFolderId);
        }

        ReloadEditors();
        RefreshCurrentRules();
    }

    public void SelectFolder(Guid folderId)
    {
        if (Folders.FirstOrDefault(folder => folder.Id == folderId) is not { } folder)
        {
            return;
        }

        SelectedFolder = folder;
    }

    private void AddFolderFromInput()
    {
        CreateFolder(SelectedFolder?.Id, NewFolderName);
        NewFolderName = string.Empty;
    }

    private void RenameSelectedFolderFromInput()
    {
        if (SelectedFolder is not null)
        {
            RenameFolder(SelectedFolder.Id, RenameFolderName);
        }
    }

    private void AddRuleFromInput()
    {
        if (SelectedFolder is null)
        {
            return;
        }

        CommitEditors();
        var aliases = NewRuleKeywords
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim());
        var rule = _candidate.AddRule(NewRuleTitle, aliases, NewRulePath, SelectedFolder.Id);
        ReloadEditors();
        SelectedRule = _editors.Single(editor => editor.Id == rule.Id);
        RefreshCurrentRules();
        NewRuleTitle = string.Empty;
        NewRuleKeywords = string.Empty;
        NewRulePath = string.Empty;
    }

    private void DeleteSelectedRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        _candidate.RemoveRule(SelectedRule.Id);
        ReloadEditors();
        SelectedRule = null;
        RefreshCurrentRules();
    }

    private void CommitEditors()
    {
        foreach (var editor in _editors)
        {
            _candidate.UpdateRule(
                editor.Id,
                editor.Title,
                editor.ParseKeywords(),
                editor.FolderPath,
                editor.NavigationFolderId);
        }
    }

    private void ReloadEditors() =>
        _editors = _candidate.Rules.Select(rule => new FolderRuleEditorViewModel(rule)).ToList();

    private void RefreshTree()
    {
        OnPropertyChanged(nameof(Folders));
        OnPropertyChanged(nameof(UncategorizedFolderId));
        RootFolders = Folders
            .Where(folder => folder.ParentId is null)
            .OrderBy(folder => folder.SortOrder)
            .Select(BuildNode)
            .ToArray();
    }

    private NavigationFolderNodeViewModel BuildNode(NavigationFolder folder) => new(
        folder,
        Folders.Where(child => child.ParentId == folder.Id)
            .OrderBy(child => child.SortOrder)
            .Select(BuildNode)
            .ToArray(),
        false);

    private void RefreshCurrentRules()
    {
        CurrentRules = SelectedFolder is null
            ? []
            : _editors.Where(editor => editor.NavigationFolderId == SelectedFolder.Id).ToArray();
        SelectedRule = CurrentRules.FirstOrDefault(editor => editor.Id == SelectedRule?.Id);
    }

    private IEnumerable<Guid> GetSubtree(Guid rootId)
    {
        yield return rootId;
        foreach (var child in Folders.Where(folder => folder.ParentId == rootId).ToArray())
        {
            foreach (var descendant in GetSubtree(child.Id))
            {
                yield return descendant;
            }
        }
    }
}
