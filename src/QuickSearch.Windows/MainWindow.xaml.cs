using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using QuickSearch.Core;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

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
    private Point _pinDragStart;
    private NavigationFolder? _draggedPin;
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            FocusSearch();
            e.Handled = true;
        }
    }

    private void PinnedFolder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinDragStart = e.GetPosition(this);
        _draggedPin = (sender as FrameworkElement)?.DataContext as NavigationFolder;
    }

    private void PinnedFolder_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedPin is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _pinDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _pinDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _draggedPin, DragDropEffects.Move);
    }

    private void PinnedFolder_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(NavigationFolder))
            || e.Data.GetData(typeof(NavigationFolder)) is not NavigationFolder source
            || (sender as FrameworkElement)?.DataContext is not NavigationFolder target)
        {
            return;
        }

        var targetIndex = _viewModel.Explorer.PinnedFolders
            .Select((folder, index) => (folder, index))
            .FirstOrDefault(item => item.folder.Id == target.Id)
            .index;
        _viewModel.Explorer.ReorderPinnedFolderCommand.Execute(
            new PinMoveRequest(source.Id, targetIndex));
        _draggedPin = null;
        e.Handled = true;
    }

    private async void Folder_NewChild_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NavigationFolderNodeViewModel node)
        {
            return;
        }

        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "输入子文件夹名称：",
            "新建子文件夹");
        if (!string.IsNullOrWhiteSpace(name))
        {
            await _viewModel.Explorer.CreateFolderAsync(node.Id, name);
        }
    }

    private async void Folder_Rename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NavigationFolderNodeViewModel node)
        {
            return;
        }

        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "输入新名称：",
            "重命名文件夹",
            node.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await _viewModel.Explorer.RenameFolderAsync(node.Id, name);
        }
    }

    private void Folder_Pin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NavigationFolderNodeViewModel node)
        {
            _viewModel.Explorer.PinFolderCommand.Execute(node.Folder);
        }
    }

    private void Folder_Unpin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NavigationFolderNodeViewModel node)
        {
            _viewModel.Explorer.UnpinFolderCommand.Execute(node.Folder);
        }
    }

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
