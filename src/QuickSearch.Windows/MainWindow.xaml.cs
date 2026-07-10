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
    private GlobalHotkey? _hotkey;
    private SettingsViewModel? _settingsViewModel;
    private SettingsWindow? _settingsWindow;
    private bool _allowClose;

    public MainWindow(
        LauncherViewModel viewModel,
        IMappingStore store,
        IFolderSearch search,
        IStartupRegistration startup)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(startup);
        _viewModel = viewModel;
        _store = store;
        _search = search;
        _startup = startup;
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.HideRequested += ViewModel_HideRequested;
        _viewModel.ShowSettingsRequested += ViewModel_ShowSettingsRequested;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    public async Task InitializeAsync()
    {
        await _viewModel.InitializeAsync();
        ApplyInitialPlatformSettings();
    }

    public void ActivateFromClipboard()
    {
        _viewModel.ActivateFromClipboard();
        ShowLauncher();
    }

    public void ShowLauncher()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        FocusAlias();
    }

    public void ShowSettings()
    {
        if (_hotkey is null)
        {
            _viewModel.ReportStatus("快捷键服务尚未就绪。");
            ShowLauncher();
            return;
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

        _settingsViewModel!.BeginEdit();
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
        if (_hotkey is not null)
        {
            _hotkey.Pressed -= Hotkey_Pressed;
            _hotkey.Dispose();
        }

        _viewModel.Dispose();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _hotkey = new GlobalHotkey(this);
            _hotkey.Pressed += Hotkey_Pressed;
        }
        catch (Exception exception)
        {
            _viewModel.ReportStatus($"无法初始化快捷键服务：{exception.Message}");
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

    private void Hotkey_Pressed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(ActivateFromClipboard);

    private void ViewModel_HideRequested(object? sender, EventArgs e) => Hide();

    private void ViewModel_ShowSettingsRequested(object? sender, EventArgs e) => ShowSettings();

    private void SettingsViewModel_Saved(object? sender, EventArgs e) =>
        _viewModel.RefreshConfiguration();

    private void Window_Activated(object sender, EventArgs e) => FocusAlias();

    private void FocusAlias()
    {
        AliasBox.Focus();
        AliasBox.SelectAll();
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
