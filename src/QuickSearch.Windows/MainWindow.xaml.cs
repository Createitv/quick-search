using System.ComponentModel;
using System.Windows;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly LauncherViewModel _viewModel;
    private readonly IMappingStore _store;
    private readonly IFolderSearch _search;
    private readonly IStartupRegistration _startup;
    private readonly EverythingBootstrapViewModel _bootstrap;
    private IHotkeyRegistration? _hotkey;
    private GlobalHotkey? _globalHotkey;
    private string? _hotkeyInitializationFailure;
    private SettingsViewModel? _settingsViewModel;
    private SettingsWindow? _settingsWindow;
    private bool _allowClose;

    public MainWindow(
        LauncherViewModel viewModel,
        IMappingStore store,
        IFolderSearch search,
        IStartupRegistration startup,
        EverythingBootstrapViewModel bootstrap)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(bootstrap);
        _viewModel = viewModel;
        _store = store;
        _search = search;
        _startup = startup;
        _bootstrap = bootstrap;
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.HideRequested += ViewModel_HideRequested;
        _viewModel.ShowSettingsRequested += ViewModel_ShowSettingsRequested;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    public EverythingBootstrapViewModel Bootstrap => _bootstrap;

    public async Task InitializeAsync()
    {
        await _viewModel.InitializeAsync();
        ApplyInitialPlatformSettings();
    }

    public async Task ActivateFromClipboardAsync()
    {
        if (!_bootstrap.CanSearch)
        {
            ShowLauncher();
            return;
        }

        var disposition = await _viewModel.ActivateFromClipboardAsync();
        if (disposition == LauncherActivationDisposition.ShowLauncher)
        {
            ShowLauncher();
        }
        else
        {
            Hide();
        }
    }

    public void ShowLauncher()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        FocusSearch();
    }

    public void ShowSettings()
    {
        if (_hotkey is null)
        {
            _hotkey = new UnavailableHotkeyRegistration(
                _viewModel.Configuration.Settings.GlobalShortcut,
                _hotkeyInitializationFailure ?? "快捷键服务尚未就绪。");
        }

        if (_settingsWindow is null)
        {
            _settingsViewModel = new SettingsViewModel(
                _viewModel.Configuration,
                _store,
                _hotkey,
                _startup,
                _search);
            _settingsViewModel.Saved += SettingsViewModel_Saved;
            _settingsWindow = new SettingsWindow(_settingsViewModel)
            {
                Owner = this
            };
        }

        _ = _settingsViewModel!.BeginEditAsync();
        _settingsWindow.Show();
        _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    public void ReportStatus(string message) => _viewModel.ReportStatus(message);

    public void ExitApplication()
    {
        _allowClose = true;
        _settingsWindow?.AllowApplicationExit();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _viewModel.HideRequested -= ViewModel_HideRequested;
        _viewModel.ShowSettingsRequested -= ViewModel_ShowSettingsRequested;
        if (_settingsViewModel is not null)
        {
            _settingsViewModel.Saved -= SettingsViewModel_Saved;
        }

        _settingsWindow?.AllowApplicationExit();
        if (_globalHotkey is not null)
        {
            _globalHotkey.Pressed -= Hotkey_Pressed;
            _globalHotkey.Dispose();
        }

        _viewModel.Dispose();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _globalHotkey = new GlobalHotkey(this);
            _globalHotkey.Pressed += Hotkey_Pressed;
            _hotkey = _globalHotkey;
        }
        catch (Exception exception)
        {
            _hotkeyInitializationFailure = $"无法初始化快捷键服务：{exception.Message}";
            _viewModel.ReportStatus(_hotkeyInitializationFailure);
        }
    }

    private void ApplyInitialPlatformSettings()
    {
        try
        {
            var startupResult = _startup.SetEnabled(
                _viewModel.Configuration.Settings.StartWithWindows);
            if (!startupResult.Success)
            {
                _viewModel.ReportStatus(startupResult.Message);
            }
        }
        catch (Exception exception)
        {
            _viewModel.ReportStatus($"无法应用开机启动设置：{exception.Message}");
        }

        if (_hotkey is null)
        {
            _hotkey = new UnavailableHotkeyRegistration(
                _viewModel.Configuration.Settings.GlobalShortcut,
                _hotkeyInitializationFailure ?? "快捷键服务尚未就绪。");
            return;
        }

        try
        {
            var hotkeyResult = _hotkey.TryReplace(
                _viewModel.Configuration.Settings.GlobalShortcut);
            if (!hotkeyResult.Success)
            {
                _viewModel.ReportStatus(hotkeyResult.Message);
            }
        }
        catch (Exception exception)
        {
            _viewModel.ReportStatus($"无法注册快捷键：{exception.Message}");
        }
    }

    private async void Hotkey_Pressed(object? sender, EventArgs e) =>
        await ActivateFromClipboardAsync();

    private void ViewModel_HideRequested(object? sender, EventArgs e) => Hide();

    private void ViewModel_ShowSettingsRequested(object? sender, EventArgs e) => ShowSettings();

    private void SettingsViewModel_Saved(object? sender, EventArgs e) =>
        _viewModel.RefreshConfiguration();

    private void Window_Activated(object sender, EventArgs e) => FocusSearch();

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
