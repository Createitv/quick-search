namespace QuickSearch.Core.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void Cancel_DiscardsAllPendingSettingsAndMappingChanges()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore();
        var viewModel = CreateViewModel(configuration, store);
        var hidden = 0;
        viewModel.HideRequested += (_, _) => hidden++;
        viewModel.Shortcut = "Ctrl+Shift+9";
        viewModel.StartWithWindows = false;
        viewModel.Mappings[0].Alias = "changed";
        viewModel.Mappings[0].FolderPath = "/changed";

        viewModel.Cancel();

        Assert.Equal("Ctrl+Alt+F", viewModel.Shortcut);
        Assert.True(viewModel.StartWithWindows);
        var mapping = Assert.Single(viewModel.Mappings);
        Assert.Equal("sales", mapping.Alias);
        Assert.Equal("/sales", mapping.FolderPath);
        Assert.Equal("/sales", configuration.FindMapping("sales")?.FolderPath);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(1, hidden);
    }

    [Fact]
    public void MappingEditAndDelete_RemainPendingUntilSaveAndCancelRestoresBoth()
    {
        var configuration = CreateConfiguration();
        configuration.UpsertMapping("support", "/support");
        var store = new FakeMappingStore();
        var viewModel = CreateViewModel(configuration, store);
        viewModel.Mappings[0].FolderPath = "/new-sales";
        viewModel.SelectedMapping = viewModel.Mappings[1];

        viewModel.DeleteSelectedMapping();

        Assert.Equal("/sales", configuration.FindMapping("sales")?.FolderPath);
        Assert.NotNull(configuration.FindMapping("support"));
        Assert.Equal(0, store.SaveCalls);

        viewModel.Cancel();

        Assert.Equal(2, viewModel.Mappings.Count);
        Assert.Contains(viewModel.Mappings, mapping => mapping.Alias == "support");
        Assert.Contains(viewModel.Mappings, mapping => mapping.FolderPath == "/sales");
    }

    [Fact]
    public async Task SaveAsync_CreatesEditsAndDeletesMappingsTogether()
    {
        var configuration = CreateConfiguration();
        configuration.UpsertMapping("support", "/support");
        var store = new FakeMappingStore();
        var viewModel = CreateViewModel(configuration, store);
        viewModel.Mappings.Single(mapping => mapping.Alias == "sales").FolderPath = "/new-sales";
        viewModel.SelectedMapping = viewModel.Mappings.Single(mapping => mapping.Alias == "support");
        viewModel.DeleteSelectedMapping();
        viewModel.AddMapping();
        viewModel.Mappings[^1].Alias = "finance";
        viewModel.Mappings[^1].FolderPath = "/finance";

        await viewModel.SaveAsync();

        Assert.Equal("/new-sales", configuration.FindMapping("sales")?.FolderPath);
        Assert.Null(configuration.FindMapping("support"));
        Assert.Equal("/finance", configuration.FindMapping("finance")?.FolderPath);
        Assert.Equal(1, store.SaveCalls);
    }

    public static TheoryData<EverythingHealth, string?, string> HealthStatuses => new()
    {
        { EverythingHealth.Ready, null, "Everything 已就绪" },
        { EverythingHealth.DllMissing, null, "缺少 Everything64.dll" },
        { EverythingHealth.NotReady, null, "索引数据库正在加载" },
        { EverythingHealth.Unavailable, null, "无法连接 Everything" },
        { EverythingHealth.QueryFailed, "health failed", "health failed" }
    };

    [Theory]
    [MemberData(nameof(HealthStatuses))]
    public void EverythingHealthText_DisplaysCurrentHealth(
        EverythingHealth health,
        string? failureMessage,
        string expected)
    {
        var search = new FakeFolderSearch
        {
            Health = health,
            FailureMessage = failureMessage
        };

        var viewModel = CreateViewModel(
            CreateConfiguration(),
            new FakeMappingStore(),
            search: search);

        Assert.Contains(expected, viewModel.EverythingHealthText);
    }

    [Fact]
    public async Task BeginEditAsync_ProbesHealthOnEveryOpenAndPublishesTransitions()
    {
        var search = new FakeFolderSearch
        {
            ProbeResults = new Queue<(EverythingHealth Health, string? Message)>(
            [
                (EverythingHealth.Ready, null),
                (EverythingHealth.Unavailable, null)
            ])
        };
        var viewModel = CreateViewModel(
            CreateConfiguration(),
            new FakeMappingStore(),
            search: search);

        await viewModel.BeginEditAsync();
        Assert.Equal(EverythingHealth.Ready, viewModel.EverythingHealth);
        Assert.Contains("已就绪", viewModel.EverythingHealthText);

        await viewModel.BeginEditAsync();
        Assert.Equal(EverythingHealth.Unavailable, viewModel.EverythingHealth);
        Assert.Contains("无法连接", viewModel.EverythingHealthText);
        Assert.Equal(2, search.ProbeCalls);
    }

    [Fact]
    public void UnavailableHotkeyRegistration_AllowsUnchangedShortcutAndRejectsChange()
    {
        var hotkey = new UnavailableHotkeyRegistration(
            "Ctrl+Alt+F",
            "hotkey adapter unavailable");

        Assert.True(hotkey.TryReplace("Ctrl+Alt+F").Success);
        var changed = hotkey.TryReplace("Ctrl+Shift+9");

        Assert.False(changed.Success);
        Assert.Contains("hotkey adapter unavailable", changed.Message);
        Assert.Equal("Ctrl+Alt+F", hotkey.ActiveShortcut);
    }

    [Fact]
    public async Task SaveAsync_UnavailableHotkeyStillSavesUnrelatedChangesWhenShortcutUnchanged()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore();
        var hotkey = new UnavailableHotkeyRegistration(
            configuration.Settings.GlobalShortcut,
            "hotkey adapter unavailable");
        var viewModel = CreateViewModel(configuration, store, hotkey);
        viewModel.StartWithWindows = false;
        viewModel.Mappings[0].FolderPath = "/new-sales";

        await viewModel.SaveAsync();

        Assert.False(configuration.Settings.StartWithWindows);
        Assert.Equal("/new-sales", configuration.FindMapping("sales")?.FolderPath);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task SaveAsync_UnavailableHotkeySurfacesChangedShortcutFailure()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore();
        var hotkey = new UnavailableHotkeyRegistration(
            configuration.Settings.GlobalShortcut,
            "hotkey adapter unavailable");
        var viewModel = CreateViewModel(configuration, store, hotkey);
        viewModel.Shortcut = "Ctrl+Shift+9";

        await viewModel.SaveAsync();

        Assert.Equal("Ctrl+Alt+F", configuration.Settings.GlobalShortcut);
        Assert.Equal(0, store.SaveCalls);
        Assert.Contains("hotkey adapter unavailable", viewModel.Message);
    }

    [Fact]
    public async Task SaveAsync_InvalidShortcutKeepsDialogOpenAndDoesNotApplyAnything()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore();
        var hotkey = new FakeHotkeyRegistration("Ctrl+Alt+F");
        var startup = new FakeStartupRegistration(enabled: true);
        var viewModel = CreateViewModel(configuration, store, hotkey, startup);
        var hidden = 0;
        viewModel.HideRequested += (_, _) => hidden++;
        viewModel.Shortcut = "F";

        await viewModel.SaveAsync();

        Assert.Contains("快捷键", viewModel.Message);
        Assert.Empty(hotkey.Calls);
        Assert.Empty(startup.Calls);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public async Task SaveAsync_HotkeyFailureRestoresPriorShortcutWithoutTouchingStartupOrStore()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore();
        var hotkey = new FakeHotkeyRegistration("Ctrl+Alt+F")
        {
            FailWhenSetting = "Ctrl+Shift+9"
        };
        var startup = new FakeStartupRegistration(enabled: true);
        var viewModel = CreateViewModel(configuration, store, hotkey, startup);
        viewModel.Shortcut = "Ctrl+Shift+9";

        await viewModel.SaveAsync();

        Assert.Equal(["Ctrl+Shift+9", "Ctrl+Alt+F"], hotkey.Calls);
        Assert.Equal("Ctrl+Alt+F", hotkey.ActiveShortcut);
        Assert.Empty(startup.Calls);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal("Ctrl+Alt+F", configuration.Settings.GlobalShortcut);
        Assert.Contains("hotkey failed", viewModel.Message);
    }

    [Fact]
    public async Task SaveAsync_StartupFailureRollsBackHotkeyAndStartupAndKeepsConfiguration()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore();
        var hotkey = new FakeHotkeyRegistration("Ctrl+Alt+F");
        var startup = new FakeStartupRegistration(enabled: true)
        {
            FailWhenSetting = false
        };
        var viewModel = CreateViewModel(configuration, store, hotkey, startup);
        var hidden = 0;
        viewModel.HideRequested += (_, _) => hidden++;
        viewModel.Shortcut = "Ctrl+Shift+9";
        viewModel.StartWithWindows = false;

        await viewModel.SaveAsync();

        Assert.Equal(["Ctrl+Shift+9", "Ctrl+Alt+F"], hotkey.Calls);
        Assert.Equal([false, true], startup.Calls);
        Assert.Equal("Ctrl+Alt+F", hotkey.ActiveShortcut);
        Assert.True(startup.Enabled);
        Assert.Equal("Ctrl+Alt+F", configuration.Settings.GlobalShortcut);
        Assert.True(configuration.Settings.StartWithWindows);
        Assert.Equal(0, store.SaveCalls);
        Assert.Contains("startup failed", viewModel.Message);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public async Task SaveAsync_PersistenceFailureRestoresExternalStateConfigurationAndStoredSnapshot()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore { FailNextSave = true };
        var hotkey = new FakeHotkeyRegistration("Ctrl+Alt+F");
        var startup = new FakeStartupRegistration(enabled: true);
        var viewModel = CreateViewModel(configuration, store, hotkey, startup);
        viewModel.Shortcut = "Ctrl+Shift+9";
        viewModel.StartWithWindows = false;
        viewModel.Mappings[0].FolderPath = "/new-sales";

        await viewModel.SaveAsync();

        Assert.Equal(["Ctrl+Shift+9", "Ctrl+Alt+F"], hotkey.Calls);
        Assert.Equal([false, true], startup.Calls);
        Assert.Equal(2, store.SaveCalls);
        Assert.Equal("Ctrl+Alt+F", configuration.Settings.GlobalShortcut);
        Assert.True(configuration.Settings.StartWithWindows);
        Assert.Equal("/sales", configuration.FindMapping("sales")?.FolderPath);
        Assert.Equal("/sales", store.LastSaved?.FindMapping("sales")?.FolderPath);
        Assert.Contains("disk full", viewModel.Message);
    }

    [Fact]
    public async Task SaveAsync_ContainsThrownStartupExceptionAndRollsBack()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore();
        var hotkey = new FakeHotkeyRegistration("Ctrl+Alt+F");
        var startup = new FakeStartupRegistration(enabled: true)
        {
            ThrowWhenSetting = false
        };
        var viewModel = CreateViewModel(configuration, store, hotkey, startup);
        viewModel.Shortcut = "Ctrl+Shift+9";
        viewModel.StartWithWindows = false;

        await viewModel.SaveAsync();

        Assert.Equal("Ctrl+Alt+F", hotkey.ActiveShortcut);
        Assert.True(startup.Enabled);
        Assert.Equal("Ctrl+Alt+F", configuration.Settings.GlobalShortcut);
        Assert.Contains("registry exploded", viewModel.Message);
    }

    [Fact]
    public async Task SaveAsync_SuccessCommitsAllChangesAndRequestsHide()
    {
        var configuration = CreateConfiguration();
        var store = new FakeMappingStore();
        var hotkey = new FakeHotkeyRegistration("Ctrl+Alt+F");
        var startup = new FakeStartupRegistration(enabled: true);
        var viewModel = CreateViewModel(configuration, store, hotkey, startup);
        var hidden = 0;
        viewModel.HideRequested += (_, _) => hidden++;
        viewModel.Shortcut = "control + shift + 9";
        viewModel.StartWithWindows = false;

        await viewModel.SaveAsync();

        Assert.Equal("Ctrl+Shift+9", configuration.Settings.GlobalShortcut);
        Assert.False(configuration.Settings.StartWithWindows);
        Assert.Equal("Ctrl+Shift+9", hotkey.ActiveShortcut);
        Assert.False(startup.Enabled);
        Assert.Equal(1, store.SaveCalls);
        Assert.Equal(1, hidden);
    }

    private static AppConfiguration CreateConfiguration()
    {
        var configuration = new AppConfiguration
        {
            Settings = new AppSettings
            {
                GlobalShortcut = "Ctrl+Alt+F",
                StartWithWindows = true
            }
        };
        configuration.UpsertMapping("sales", "/sales");
        return configuration;
    }

    private static SettingsViewModel CreateViewModel(
        AppConfiguration configuration,
        FakeMappingStore store,
        IHotkeyRegistration? hotkey = null,
        IStartupRegistration? startup = null,
        IFolderSearch? search = null) =>
        new(
            configuration,
            store,
            hotkey ?? new FakeHotkeyRegistration(configuration.Settings.GlobalShortcut),
            startup ?? new FakeStartupRegistration(configuration.Settings.StartWithWindows),
            search ?? new FakeFolderSearch());

    private sealed class FakeMappingStore : IMappingStore
    {
        public bool FailNextSave { get; set; }

        public int SaveCalls { get; private set; }

        public AppConfiguration? LastSaved { get; private set; }

        public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppConfiguration());

        public Task SaveAsync(
            AppConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("disk full");
            }

            LastSaved = configuration.Clone();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHotkeyRegistration(string? activeShortcut) : IHotkeyRegistration
    {
        public string? ActiveShortcut { get; private set; } = activeShortcut;

        public List<string> Calls { get; } = [];

        public string? FailWhenSetting { get; init; }

        public PlatformOperationResult TryReplace(string shortcut)
        {
            Calls.Add(shortcut);
            if (shortcut == FailWhenSetting)
            {
                return PlatformOperationResult.Failed("hotkey failed");
            }

            ActiveShortcut = shortcut;
            return PlatformOperationResult.Succeeded();
        }
    }

    private sealed class FakeStartupRegistration(bool enabled) : IStartupRegistration
    {
        public bool Enabled { get; private set; } = enabled;

        public List<bool> Calls { get; } = [];

        public bool? FailWhenSetting { get; set; }

        public bool? ThrowWhenSetting { get; set; }

        public PlatformOperationResult SetEnabled(bool enabled)
        {
            Calls.Add(enabled);
            Enabled = enabled;
            if (enabled == ThrowWhenSetting)
            {
                ThrowWhenSetting = null;
                throw new InvalidOperationException("registry exploded");
            }

            if (enabled == FailWhenSetting)
            {
                FailWhenSetting = null;
                return PlatformOperationResult.Failed("startup failed");
            }

            return PlatformOperationResult.Succeeded();
        }
    }

    private sealed class FakeFolderSearch : IFolderSearch
    {
        public EverythingHealth Health { get; set; } = EverythingHealth.Ready;

        public string? FailureMessage { get; set; }

        public Queue<(EverythingHealth Health, string? Message)> ProbeResults { get; init; } = [];

        public int ProbeCalls { get; private set; }

        public Task ProbeAsync(CancellationToken cancellationToken = default)
        {
            ProbeCalls++;
            if (ProbeResults.TryDequeue(out var result))
            {
                Health = result.Health;
                FailureMessage = result.Message;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FolderSearchResult>>([]);
    }
}
