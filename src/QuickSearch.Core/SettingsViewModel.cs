using System.Collections.ObjectModel;
using System.ComponentModel;

namespace QuickSearch.Core;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppConfiguration _configuration;
    private readonly IMappingStore _store;
    private readonly IHotkeyRegistration _hotkey;
    private readonly IHotkeyRegistration _launcherHotkey;
    private readonly IStartupRegistration _startup;
    private readonly IFolderSearch _search;
    private string _shortcut = string.Empty;
    private string _launcherShortcut = string.Empty;
    private bool _startWithWindows;
    private int _quickLauncherResultColumns;
    private double _uiFontSize;
    private string _everythingHealthText = string.Empty;
    private EverythingHealth _everythingHealth;
    private string _message = string.Empty;
    private string _mappingFilter = string.Empty;
    private IReadOnlyList<MappingGroupEditorViewModel> _filteredMappingGroups = [];
    private MappingGroupEditorViewModel? _selectedMappingGroup;
    private bool _isSaving;
    private bool _mappingGroupsDirty;

    public SettingsViewModel(
        AppConfiguration configuration,
        IMappingStore store,
        IHotkeyRegistration hotkey,
        IStartupRegistration startup,
        IFolderSearch search,
        IHotkeyRegistration? launcherHotkey = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(search);
        _configuration = configuration;
        _store = store;
        _hotkey = hotkey;
        _launcherHotkey = launcherHotkey ?? new UnavailableHotkeyRegistration(
            configuration.Settings.QuickLauncherShortcut,
            "启动器快捷键服务尚未就绪。");
        _startup = startup;
        _search = search;
        Organizer = new NavigationOrganizerViewModel(configuration);

        AddMappingCommand = new RelayCommand(AddMapping);
        DeleteMappingCommand = new RelayCommand(
            DeleteSelectedMapping,
            () => SelectedMapping is not null);
        CancelCommand = new RelayCommand(Cancel);
        SaveCommand = new AsyncRelayCommand(
            SaveAsync,
            () => !IsSaving,
            exception => Message = $"无法保存设置：{exception.Message}");
        OpenQuickLauncherCommand = new RelayCommand(
            () => QuickLauncherRequested?.Invoke(this, EventArgs.Empty));
        BeginEdit();
    }

    public event EventHandler? HideRequested;

    public event EventHandler? Saved;

    public event EventHandler? QuickLauncherRequested;

    public ObservableCollection<MappingGroupEditorViewModel> MappingGroups { get; } = [];

    public ObservableCollection<MappingGroupEditorViewModel> Mappings => MappingGroups;

    public NavigationOrganizerViewModel Organizer { get; }

    public IReadOnlyList<MappingGroupEditorViewModel> FilteredMappingGroups
    {
        get => _filteredMappingGroups;
        private set
        {
            if (SetProperty(ref _filteredMappingGroups, value))
            {
                OnPropertyChanged(nameof(MappingCountText));
            }
        }
    }

    public string MappingFilter
    {
        get => _mappingFilter;
        set
        {
            if (SetProperty(ref _mappingFilter, value ?? string.Empty))
            {
                RefreshFilteredMappingGroups();
            }
        }
    }

    public string MappingCountText =>
        $"当前显示 {FilteredMappingGroups.Count} 条 / 共 {MappingGroups.Count} 条";

    public string Shortcut
    {
        get => _shortcut;
        set => SetProperty(ref _shortcut, value ?? string.Empty);
    }

    public string LauncherShortcut
    {
        get => _launcherShortcut;
        set => SetProperty(ref _launcherShortcut, value ?? string.Empty);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public int QuickLauncherResultColumns
    {
        get => _quickLauncherResultColumns;
        set => SetProperty(ref _quickLauncherResultColumns, Math.Clamp(value, 1, 3));
    }

    public double UiFontSize
    {
        get => _uiFontSize;
        set => SetProperty(ref _uiFontSize, Math.Clamp(value, 11, 22));
    }

    public string EverythingHealthText
    {
        get => _everythingHealthText;
        private set => SetProperty(ref _everythingHealthText, value);
    }

    public EverythingHealth EverythingHealth
    {
        get => _everythingHealth;
        private set => SetProperty(ref _everythingHealth, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public MappingGroupEditorViewModel? SelectedMappingGroup
    {
        get => _selectedMappingGroup;
        set
        {
            if (SetProperty(ref _selectedMappingGroup, value))
            {
                OnPropertyChanged(nameof(SelectedMapping));
                DeleteMappingCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public MappingGroupEditorViewModel? SelectedMapping
    {
        get => SelectedMappingGroup;
        set => SelectedMappingGroup = value;
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand AddMappingCommand { get; }

    public RelayCommand DeleteMappingCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand OpenQuickLauncherCommand { get; }

    public void BeginEdit()
    {
        Shortcut = _configuration.Settings.GlobalShortcut;
        LauncherShortcut = _configuration.Settings.QuickLauncherShortcut;
        StartWithWindows = _configuration.Settings.StartWithWindows;
        QuickLauncherResultColumns = Math.Clamp(
            _configuration.Settings.QuickLauncherResultColumns,
            1,
            3);
        UiFontSize = Math.Clamp(_configuration.Settings.UiFontSize, 11, 22);
        Organizer.BeginEdit(_configuration);
        foreach (var group in MappingGroups)
        {
            group.PropertyChanged -= MappingGroup_PropertyChanged;
        }

        MappingGroups.Clear();
        var groupsByPath = new Dictionary<string, (string Path, List<string> Keywords)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in _configuration.Mappings)
        {
            var path = mapping.FolderPath.Trim();
            if (!groupsByPath.TryGetValue(path, out var group))
            {
                group = (mapping.FolderPath, []);
                groupsByPath.Add(path, group);
            }

            group.Keywords.Add(mapping.Alias);
        }

        foreach (var group in groupsByPath.Values)
        {
            AddGroup(new MappingGroupEditorViewModel(group.Keywords, group.Path));
        }

        MappingFilter = string.Empty;
        SelectedMappingGroup = null;
        Message = string.Empty;
        _mappingGroupsDirty = false;
        RefreshFilteredMappingGroups();
        RefreshEverythingHealth();
    }

    public async Task BeginEditAsync(CancellationToken cancellationToken = default)
    {
        BeginEdit();
        try
        {
            await _search.ProbeAsync(cancellationToken);
            RefreshEverythingHealth();
        }
        catch (OperationCanceledException)
        {
            EverythingHealth = EverythingHealth.NotReady;
            EverythingHealthText = "Everything 状态检查已取消。";
        }
        catch (Exception exception)
        {
            EverythingHealth = EverythingHealth.QueryFailed;
            EverythingHealthText = $"Everything 状态检查失败：{exception.Message}";
        }
    }

    public void AddMapping()
    {
        _mappingGroupsDirty = true;
        MappingFilter = string.Empty;
        var group = new MappingGroupEditorViewModel();
        group.PropertyChanged += MappingGroup_PropertyChanged;
        MappingGroups.Insert(0, group);
        RefreshFilteredMappingGroups();
        SelectedMappingGroup = group;
    }

    public void DeleteSelectedMapping()
    {
        if (SelectedMappingGroup is null)
        {
            return;
        }

        var selected = SelectedMappingGroup;
        _mappingGroupsDirty = true;
        var index = FilteredMappingGroups.ToList().IndexOf(selected);
        selected.PropertyChanged -= MappingGroup_PropertyChanged;
        MappingGroups.Remove(selected);
        RefreshFilteredMappingGroups();
        SelectedMappingGroup = FilteredMappingGroups.Count == 0
            ? null
            : FilteredMappingGroups[Math.Min(
                Math.Max(index, 0),
                FilteredMappingGroups.Count - 1)];
    }

    public void Cancel()
    {
        BeginEdit();
        RaiseHideRequested();
    }

    public async Task SaveAsync()
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        var previous = _configuration.Clone();
        var hotkeyAttempted = false;
        var launcherHotkeyAttempted = false;
        var startupAttempted = false;
        var persistenceAttempted = false;
        try
        {
            var shortcut = CanonicalizeShortcut(Shortcut);
            var launcherShortcut = CanonicalizeShortcut(LauncherShortcut);
            if (string.Equals(shortcut, launcherShortcut, StringComparison.OrdinalIgnoreCase))
            {
                throw new SettingsTransactionException("两个全局快捷键不能相同。");
            }

            var candidate = BuildCandidate(shortcut, launcherShortcut);

            hotkeyAttempted = true;
            var hotkeyResult = _hotkey.TryReplace(shortcut);
            if (!hotkeyResult.Success)
            {
                throw new SettingsTransactionException(hotkeyResult.Message);
            }

            launcherHotkeyAttempted = true;
            var launcherHotkeyResult = _launcherHotkey.TryReplace(launcherShortcut);
            if (!launcherHotkeyResult.Success)
            {
                throw new SettingsTransactionException(launcherHotkeyResult.Message);
            }

            startupAttempted = true;
            var startupResult = _startup.SetEnabled(StartWithWindows);
            if (!startupResult.Success)
            {
                throw new SettingsTransactionException(startupResult.Message);
            }

            persistenceAttempted = true;
            await _store.SaveAsync(candidate);
            _configuration.ReplaceWith(candidate);
            Shortcut = shortcut;
            LauncherShortcut = launcherShortcut;
            Message = "设置已保存。";
            RaiseSaved();
            RaiseHideRequested();
        }
        catch (FormatException exception)
        {
            Message = $"快捷键无效：{exception.Message}";
        }
        catch (Exception exception)
        {
            var rollbackMessage = await RollBackAsync(
                previous,
                hotkeyAttempted,
                launcherHotkeyAttempted,
                startupAttempted,
                persistenceAttempted);
            Message = $"无法保存设置：{exception.Message}{rollbackMessage}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void RefreshEverythingHealth()
    {
        EverythingHealth = _search.Health;
        EverythingHealthText = _search.Health switch
        {
            EverythingHealth.Ready => "Everything 已就绪。",
            EverythingHealth.DllMissing => "缺少 Everything64.dll，请重新安装 QuickSearch。",
            EverythingHealth.NotReady => "Everything 索引数据库正在加载。",
            EverythingHealth.Unavailable => "无法连接 Everything，请确认 Everything 1.4 正在运行。",
            _ => _search.FailureMessage ?? "Everything 查询失败。"
        };
    }

    private AppConfiguration BuildCandidate(string shortcut, string launcherShortcut)
    {
        ValidateMappingGroups();
        var candidate = Organizer.BuildCandidate();
        candidate.Settings = candidate.Settings with
        {
            GlobalShortcut = shortcut,
            QuickLauncherShortcut = launcherShortcut,
            StartWithWindows = StartWithWindows,
            QuickLauncherResultColumns = QuickLauncherResultColumns,
            UiFontSize = UiFontSize
        };

        if (!_mappingGroupsDirty)
        {
            return candidate;
        }

        var desiredMappings = new Dictionary<string, (string Alias, string Path)>(
            StringComparer.Ordinal);
        foreach (var group in MappingGroups)
        {
            var path = group.FolderPath.Trim();
            foreach (var keyword in group.ParseKeywords())
            {
                desiredMappings.TryAdd(
                    GetMappingKey(keyword, path),
                    (keyword, path));
            }
        }

        foreach (var mapping in candidate.Mappings.ToArray())
        {
            if (!desiredMappings.ContainsKey(GetMappingKey(
                    mapping.Alias,
                    mapping.FolderPath)))
            {
                candidate.RemoveMapping(mapping.Alias, mapping.FolderPath);
            }
        }

        foreach (var desired in desiredMappings.Values)
        {
            if (!candidate.FindMappings(desired.Alias).Any(mapping =>
                    string.Equals(
                        mapping.FolderPath.Trim(),
                        desired.Path,
                        StringComparison.OrdinalIgnoreCase)))
            {
                candidate.AddMapping(desired.Alias, desired.Path);
            }
        }

        return candidate;
    }

    private void ValidateMappingGroups()
    {
        var folderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in MappingGroups)
        {
            if (group.ParseKeywords().Count == 0
                || string.IsNullOrWhiteSpace(group.FolderPath))
            {
                throw new SettingsTransactionException(
                    "映射的关键词和文件夹路径不能为空。");
            }

            if (!folderPaths.Add(group.FolderPath.Trim()))
            {
                throw new SettingsTransactionException(
                    $"文件夹路径重复，请将关键词合并到同一条映射：{group.FolderPath}");
            }
        }
    }

    private void AddGroup(MappingGroupEditorViewModel group)
    {
        group.PropertyChanged += MappingGroup_PropertyChanged;
        MappingGroups.Add(group);
    }

    private void MappingGroup_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        _mappingGroupsDirty = true;
        RefreshFilteredMappingGroups();
    }

    private void RefreshFilteredMappingGroups()
    {
        FilteredMappingGroups = MappingGroups
            .Where(group => group.Matches(MappingFilter))
            .ToArray();
        OnPropertyChanged(nameof(MappingCountText));
    }

    private static string GetMappingKey(string alias, string folderPath) =>
        $"{AliasNormalizer.Normalize(alias)}\u001F{folderPath.Trim().ToUpperInvariant()}";

    private async Task<string> RollBackAsync(
        AppConfiguration previous,
        bool hotkeyAttempted,
        bool launcherHotkeyAttempted,
        bool startupAttempted,
        bool persistenceAttempted)
    {
        var failures = new List<string>();
        if (persistenceAttempted)
        {
            try
            {
                await _store.SaveAsync(previous);
            }
            catch (Exception exception)
            {
                failures.Add($"恢复配置失败：{exception.Message}");
            }
        }

        if (startupAttempted)
        {
            try
            {
                var result = _startup.SetEnabled(previous.Settings.StartWithWindows);
                if (!result.Success)
                {
                    failures.Add($"恢复开机启动失败：{result.Message}");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"恢复开机启动失败：{exception.Message}");
            }
        }

        if (hotkeyAttempted)
        {
            try
            {
                var result = _hotkey.TryReplace(previous.Settings.GlobalShortcut);
                if (!result.Success)
                {
                    failures.Add($"恢复快捷键失败：{result.Message}");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"恢复快捷键失败：{exception.Message}");
            }
        }

        if (launcherHotkeyAttempted)
        {
            try
            {
                var result = _launcherHotkey.TryReplace(
                    previous.Settings.QuickLauncherShortcut);
                if (!result.Success)
                {
                    failures.Add($"恢复启动器快捷键失败：{result.Message}");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"恢复启动器快捷键失败：{exception.Message}");
            }
        }

        return failures.Count == 0
            ? string.Empty
            : $"（{string.Join("；", failures)}）";
    }

    private static string CanonicalizeShortcut(string shortcut)
    {
        var gesture = ShortcutGesture.Parse(shortcut);
        var parts = new List<string>();
        if (gesture.Control) parts.Add("Ctrl");
        if (gesture.Alt) parts.Add("Alt");
        if (gesture.Shift) parts.Add("Shift");
        if (gesture.Windows) parts.Add("Win");
        parts.Add(gesture.Key);
        return string.Join('+', parts);
    }

    private void RaiseSaved()
    {
        try
        {
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Message = $"设置已保存，但界面刷新失败：{exception.Message}";
        }
    }

    private void RaiseHideRequested()
    {
        try
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Message = $"无法隐藏设置窗口：{exception.Message}";
        }
    }

    private sealed class SettingsTransactionException(string message) : Exception(message);
}
