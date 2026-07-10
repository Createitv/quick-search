using System.Collections.ObjectModel;

namespace QuickSearch.Core;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppConfiguration _configuration;
    private readonly IMappingStore _store;
    private readonly IHotkeyRegistration _hotkey;
    private readonly IStartupRegistration _startup;
    private readonly IFolderSearch _search;
    private string _shortcut = string.Empty;
    private bool _startWithWindows;
    private string _everythingHealthText = string.Empty;
    private EverythingHealth _everythingHealth;
    private string _message = string.Empty;
    private MappingEditorViewModel? _selectedMapping;
    private bool _isSaving;

    public SettingsViewModel(
        AppConfiguration configuration,
        IMappingStore store,
        IHotkeyRegistration hotkey,
        IStartupRegistration startup,
        IFolderSearch search)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(search);
        _configuration = configuration;
        _store = store;
        _hotkey = hotkey;
        _startup = startup;
        _search = search;

        AddMappingCommand = new RelayCommand(AddMapping);
        DeleteMappingCommand = new RelayCommand(
            DeleteSelectedMapping,
            () => SelectedMapping is not null);
        CancelCommand = new RelayCommand(Cancel);
        SaveCommand = new AsyncRelayCommand(
            SaveAsync,
            () => !IsSaving,
            exception => Message = $"无法保存设置：{exception.Message}");
        BeginEdit();
    }

    public event EventHandler? HideRequested;

    public event EventHandler? Saved;

    public ObservableCollection<MappingEditorViewModel> Mappings { get; } = [];

    public string Shortcut
    {
        get => _shortcut;
        set => SetProperty(ref _shortcut, value ?? string.Empty);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
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

    public MappingEditorViewModel? SelectedMapping
    {
        get => _selectedMapping;
        set
        {
            if (SetProperty(ref _selectedMapping, value))
            {
                DeleteMappingCommand.NotifyCanExecuteChanged();
            }
        }
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

    public void BeginEdit()
    {
        Shortcut = _configuration.Settings.GlobalShortcut;
        StartWithWindows = _configuration.Settings.StartWithWindows;
        Mappings.Clear();
        foreach (var mapping in _configuration.Mappings.OrderBy(
                     mapping => mapping.Alias,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            Mappings.Add(new MappingEditorViewModel(mapping));
        }

        SelectedMapping = null;
        Message = string.Empty;
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
        var mapping = new MappingEditorViewModel();
        Mappings.Add(mapping);
        SelectedMapping = mapping;
    }

    public void DeleteSelectedMapping()
    {
        if (SelectedMapping is null)
        {
            return;
        }

        var index = Mappings.IndexOf(SelectedMapping);
        Mappings.Remove(SelectedMapping);
        SelectedMapping = Mappings.Count == 0
            ? null
            : Mappings[Math.Min(index, Mappings.Count - 1)];
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
        var startupAttempted = false;
        var persistenceAttempted = false;
        try
        {
            var shortcut = CanonicalizeShortcut(Shortcut);
            var candidate = BuildCandidate(shortcut);

            hotkeyAttempted = true;
            var hotkeyResult = _hotkey.TryReplace(shortcut);
            if (!hotkeyResult.Success)
            {
                throw new SettingsTransactionException(hotkeyResult.Message);
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

    private AppConfiguration BuildCandidate(string shortcut)
    {
        ValidateMappings();
        var candidate = _configuration.Clone();
        candidate.Settings = candidate.Settings with
        {
            GlobalShortcut = shortcut,
            StartWithWindows = StartWithWindows
        };

        var retainedOriginalAliases = Mappings
            .Select(mapping => mapping.OriginalAlias)
            .Where(alias => alias is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var mapping in candidate.Mappings.ToArray())
        {
            if (!retainedOriginalAliases.Contains(mapping.Alias))
            {
                candidate.RemoveMapping(mapping.Alias);
            }
        }

        foreach (var mapping in Mappings)
        {
            var alias = mapping.Alias.Trim();
            var path = mapping.FolderPath.Trim();
            if (mapping.OriginalAlias is null)
            {
                candidate.UpsertMapping(alias, path);
            }
            else
            {
                candidate.UpdateMapping(mapping.OriginalAlias, alias, path);
            }
        }

        return candidate;
    }

    private void ValidateMappings()
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Alias)
                || string.IsNullOrWhiteSpace(mapping.FolderPath))
            {
                throw new SettingsTransactionException(
                    "映射的邮箱别名和文件夹路径不能为空。");
            }

            if (!aliases.Add(AliasNormalizer.Normalize(mapping.Alias)))
            {
                throw new SettingsTransactionException($"映射别名重复：{mapping.Alias}");
            }
        }
    }

    private async Task<string> RollBackAsync(
        AppConfiguration previous,
        bool hotkeyAttempted,
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
