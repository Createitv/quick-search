using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using QuickSearch.Core;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Wpf.Ui.Controls;

namespace QuickSearch.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly LauncherViewModel _viewModel;
    private readonly IMappingStore _store;
    private readonly IFolderSearch _search;
    private readonly IStartupRegistration _startup;
    private readonly EverythingBootstrapViewModel _bootstrap;
    private readonly ApplicationUpdateViewModel _update;
    private IHotkeyRegistration? _hotkey;
    private GlobalHotkey? _globalHotkey;
    private IHotkeyRegistration? _launcherHotkey;
    private GlobalHotkey? _globalLauncherHotkey;
    private string? _hotkeyInitializationFailure;
    private SettingsViewModel? _settingsViewModel;
    private readonly QuickLauncherViewModel _quickLauncherViewModel;
    private QuickLauncherWindow? _quickLauncherWindow;
    private Point _pinDragStart;
    private NavigationFolder? _draggedPin;
    private Point _ruleDragStart;
    private FolderRule? _draggedRule;
    private Point _folderDragStart;
    private NavigationFolder? _draggedFolder;
    private bool _allowClose;

    public MainWindow(
        LauncherViewModel viewModel,
        IMappingStore store,
        IFolderSearch search,
        IStartupRegistration startup,
        EverythingBootstrapViewModel bootstrap,
        ApplicationUpdateViewModel update,
        IQuickLauncherSearch? launcherSearch = null,
        IPathOpener? pathOpener = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(update);
        _viewModel = viewModel;
        _store = store;
        _search = search;
        _startup = startup;
        _bootstrap = bootstrap;
        _update = update;
        _quickLauncherViewModel = new QuickLauncherViewModel(
            _viewModel.Configuration,
            _store,
            launcherSearch ?? new EmptyQuickLauncherSearch(),
            pathOpener ?? new UnavailablePathOpener());
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.HideRequested += ViewModel_HideRequested;
        _viewModel.ShowSettingsRequested += ViewModel_ShowSettingsRequested;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    public EverythingBootstrapViewModel Bootstrap => _bootstrap;

    public ApplicationUpdateViewModel Update => _update;

    public async Task InitializeAsync()
    {
        await _viewModel.InitializeAsync();
        ApplyInitialPlatformSettings();
    }

    public async Task CheckForUpdatesOnStartupAsync()
    {
        var settings = _viewModel.Configuration.Settings;
        if (!UpdateCheckPolicy.ShouldCheckAutomatically(
                settings.AutomaticallyCheckForUpdates))
        {
            return;
        }

        await _update.CheckNowAsync();
        if (_update.State == ApplicationUpdateState.Available)
        {
            _viewModel.ReportStatus(
                $"发现 QuickSearch {_update.LatestVersion}，可在设置中安装。");
        }
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
        ShowExplorerPage();
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

        if (_settingsViewModel is null)
        {
            if (_launcherHotkey is null)
            {
                _launcherHotkey = new UnavailableHotkeyRegistration(
                    _viewModel.Configuration.Settings.QuickLauncherShortcut,
                    _hotkeyInitializationFailure ?? "启动器快捷键服务尚未就绪。");
            }

            _settingsViewModel = new SettingsViewModel(
                _viewModel.Configuration,
                _store,
                _hotkey,
                _startup,
                _search,
                _update,
                _launcherHotkey);
            _settingsViewModel.Saved += SettingsViewModel_Saved;
            _settingsViewModel.HideRequested += SettingsViewModel_HideRequested;
            _settingsViewModel.QuickLauncherRequested += SettingsViewModel_QuickLauncherRequested;
            GeneralSettingsPage.DataContext = _settingsViewModel;
        }

        _ = _settingsViewModel!.BeginEditAsync();
        ExplorerPage.Visibility = Visibility.Collapsed;
        GeneralSettingsPage.Visibility = Visibility.Visible;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowQuickLauncher(bool hideWhenDeactivated = true)
    {
        if (_quickLauncherWindow is null)
        {
            _quickLauncherWindow = new QuickLauncherWindow(_quickLauncherViewModel);
            _quickLauncherWindow.SettingsRequested += QuickLauncherWindow_SettingsRequested;
        }

        _quickLauncherWindow.ShowLauncher(hideWhenDeactivated);
    }

    public void ReportStatus(string message) => _viewModel.ReportStatus(message);

    public void ExitApplication()
    {
        _allowClose = true;
        _quickLauncherWindow?.AllowApplicationExit();
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
            _settingsViewModel.HideRequested -= SettingsViewModel_HideRequested;
            _settingsViewModel.QuickLauncherRequested -= SettingsViewModel_QuickLauncherRequested;
        }

        if (_quickLauncherWindow is not null)
        {
            _quickLauncherWindow.SettingsRequested -= QuickLauncherWindow_SettingsRequested;
            _quickLauncherWindow.AllowApplicationExit();
            _quickLauncherWindow.Dispose();
        }
        if (_globalHotkey is not null)
        {
            _globalHotkey.Pressed -= Hotkey_Pressed;
            _globalHotkey.Dispose();
        }

        if (_globalLauncherHotkey is not null)
        {
            _globalLauncherHotkey.Pressed -= LauncherHotkey_Pressed;
            _globalLauncherHotkey.Dispose();
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
            _globalLauncherHotkey = new GlobalHotkey(this, 0x5150);
            _globalLauncherHotkey.Pressed += LauncherHotkey_Pressed;
            _launcherHotkey = _globalLauncherHotkey;
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


        if (_launcherHotkey is null)
        {
            _launcherHotkey = new UnavailableHotkeyRegistration(
                _viewModel.Configuration.Settings.QuickLauncherShortcut,
                _hotkeyInitializationFailure ?? "启动器快捷键服务尚未就绪。");
            return;
        }

        try
        {
            var launcherHotkeyResult = _launcherHotkey.TryReplace(
                _viewModel.Configuration.Settings.QuickLauncherShortcut);
            if (!launcherHotkeyResult.Success)
            {
                _viewModel.ReportStatus(launcherHotkeyResult.Message);
            }
        }
        catch (Exception exception)
        {
            _viewModel.ReportStatus($"无法注册启动器快捷键：{exception.Message}");
        }
    }

    private async void Hotkey_Pressed(object? sender, EventArgs e) =>
        await ActivateFromClipboardAsync();

    private void LauncherHotkey_Pressed(object? sender, EventArgs e) => ShowQuickLauncher();

    private void ViewModel_HideRequested(object? sender, EventArgs e) => Hide();

    private void ViewModel_ShowSettingsRequested(object? sender, EventArgs e) => ShowSettings();

    private void SettingsViewModel_Saved(object? sender, EventArgs e) =>
        _viewModel.RefreshConfiguration();

    private void SettingsViewModel_HideRequested(object? sender, EventArgs e) =>
        ShowExplorerPage();

    private void SettingsViewModel_QuickLauncherRequested(object? sender, EventArgs e)
    {
        ShowExplorerPage();
        ShowQuickLauncher();
    }

    private void QuickLauncherWindow_SettingsRequested(object? sender, EventArgs e) =>
        ShowSettings();

    private void Window_Activated(object sender, EventArgs e)
    {
        if (ExplorerPage.Visibility == Visibility.Visible)
        {
            FocusSearch();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Hide();

    private void QuickLauncher_Click(object sender, RoutedEventArgs e) => ShowQuickLauncher();

    private void CollapseSidebar_Click(object sender, RoutedEventArgs e)
    {
        Sidebar.Visibility = Visibility.Collapsed;
        CollapsedSidebar.Visibility = Visibility.Visible;
        SidebarColumn.Width = new GridLength(48);
    }

    private void ExpandSidebar_Click(object sender, RoutedEventArgs e)
    {
        SidebarColumn.Width = new GridLength(210);
        CollapsedSidebar.Visibility = Visibility.Collapsed;
        Sidebar.Visibility = Visibility.Visible;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (GeneralSettingsPage.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            _settingsViewModel?.Cancel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control
            && GetSearchShortcutNumber(e.Key) is int shortcutNumber
            && await _viewModel.OpenSearchShortcutAsync(shortcutNumber))
        {
            e.Handled = true;
        }
    }

    private static int? GetSearchShortcutNumber(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => null
    };

    private sealed class EmptyQuickLauncherSearch : IQuickLauncherSearch
    {
        public Task<IReadOnlyList<QuickLauncherSearchItem>> SearchLauncherAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QuickLauncherSearchItem>>([]);
    }

    private sealed class UnavailablePathOpener : IPathOpener
    {
        public PlatformOperationResult Open(string path) =>
            PlatformOperationResult.Failed("启动器路径打开服务尚未就绪。");
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

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, _draggedPin, DragDropEffects.Move);
        }
        finally
        {
            _draggedPin = null;
        }
    }

    private void PinnedFolder_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Button targetButton
            || targetButton.DataContext is not NavigationFolder target
            || e.Data.GetData(typeof(NavigationFolder)) is not NavigationFolder source
            || source.Id == target.Id)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var placeAfterTarget = e.GetPosition(targetButton).X >= targetButton.ActualWidth / 2;
        targetButton.BorderBrush = FindResource("RouteBlueBrush") as System.Windows.Media.Brush;
        targetButton.BorderThickness = placeAfterTarget
            ? new Thickness(1, 1, 3, 1)
            : new Thickness(3, 1, 1, 1);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void PinnedFolder_DragLeave(object sender, DragEventArgs e)
    {
        ResetPinnedFolderDropIndicator(sender as Button);
    }

    private void PinnedFolder_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(NavigationFolder))
            || e.Data.GetData(typeof(NavigationFolder)) is not NavigationFolder source
            || (sender as FrameworkElement)?.DataContext is not NavigationFolder target)
        {
            return;
        }

        var targetButton = sender as Button;
        var placeAfterTarget = targetButton is not null
            && e.GetPosition(targetButton).X >= targetButton.ActualWidth / 2;
        ResetPinnedFolderDropIndicator(targetButton);
        _viewModel.Explorer.ReorderPinnedFolderCommand.Execute(
            new PinMoveRequest(source.Id, target.Id, placeAfterTarget));
        _draggedPin = null;
        e.Handled = true;
    }

    private static void ResetPinnedFolderDropIndicator(Button? button)
    {
        if (button is null)
        {
            return;
        }

        button.ClearValue(Border.BorderBrushProperty);
        button.ClearValue(Border.BorderThicknessProperty);
    }

    private void Rule_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ruleDragStart = e.GetPosition(this);
        _draggedRule = ResolveFolderRule((sender as FrameworkElement)?.DataContext);
    }

    private void Rule_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedRule is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _ruleDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _ruleDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, _draggedRule, DragDropEffects.Move);
        }
        finally
        {
            _draggedRule = null;
        }
    }

    private void RuleContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu
            || contextMenu.PlacementTarget is not FrameworkElement placementTarget
            || ResolveFolderRule(placementTarget.DataContext) is not { } rule)
        {
            return;
        }

        contextMenu.DataContext = rule;
        var moveRoot = contextMenu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag as string,
                "MoveRuleRoot",
                StringComparison.Ordinal));
        if (moveRoot is null)
        {
            return;
        }

        moveRoot.Items.Clear();
        foreach (var rootFolder in _viewModel.Explorer.RootFolders)
        {
            moveRoot.Items.Add(CreateMoveRuleMenuItem(rootFolder, rule));
        }

        moveRoot.IsEnabled = moveRoot.Items.Count > 0;
    }

    private MenuItem CreateMoveRuleMenuItem(
        NavigationFolderNodeViewModel node,
        FolderRule rule)
    {
        var isCurrentFolder = node.Id == rule.NavigationFolderId;
        var item = new MenuItem
        {
            Header = node.Name,
            Icon = new SymbolIcon
            {
                Symbol = isCurrentFolder
                    ? SymbolRegular.Checkmark20
                    : SymbolRegular.Folder20
            }
        };

        if (node.Children.Count == 0)
        {
            ConfigureMoveRuleAction(item, node.Id, rule.Id, isCurrentFolder);
            return item;
        }

        var chooseFolder = new MenuItem
        {
            Header = isCurrentFolder ? "当前位置" : $"移动到“{node.Name}”",
            Icon = new SymbolIcon
            {
                Symbol = isCurrentFolder
                    ? SymbolRegular.Checkmark20
                    : SymbolRegular.FolderArrowRight20
            }
        };
        ConfigureMoveRuleAction(chooseFolder, node.Id, rule.Id, isCurrentFolder);
        item.Items.Add(chooseFolder);
        item.Items.Add(new System.Windows.Controls.Separator());
        foreach (var child in node.Children)
        {
            item.Items.Add(CreateMoveRuleMenuItem(child, rule));
        }

        return item;
    }

    private void ConfigureMoveRuleAction(
        MenuItem item,
        Guid folderId,
        Guid ruleId,
        bool isCurrentFolder)
    {
        item.IsEnabled = !isCurrentFolder;
        item.Tag = new RuleMoveRequest(ruleId, folderId);
        item.Click += MoveRuleMenuItem_Click;
    }

    private void MoveRuleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is RuleMoveRequest request)
        {
            _viewModel.Explorer.MoveRuleCommand.Execute(request);
        }
    }

    private void RuleContextOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveFolderRule((sender as FrameworkElement)?.DataContext) is { } rule)
        {
            _viewModel.Explorer.OpenRuleCommand.Execute(rule);
        }
    }

    private async void RuleContextEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveFolderRule((sender as FrameworkElement)?.DataContext) is not { } rule)
        {
            return;
        }

        var targetPath = string.Join(
            " › ",
            BuildFolderPath(rule.NavigationFolderId));
        var dialog = new ShortcutEditorDialog(targetPath, rule)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.Explorer.UpdateRuleAsync(
                rule.Id,
                dialog.ShortcutTitle,
                dialog.Keywords,
                dialog.FolderPath);
        }
    }

    private async void RuleContextDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveFolderRule((sender as FrameworkElement)?.DataContext) is not { } rule)
        {
            return;
        }

        var choice = System.Windows.MessageBox.Show(
            $"确定删除快捷方式“{rule.DisplayTitle}”吗？\n\n只会删除快捷方式，不会删除真实文件夹。",
            "删除快捷方式",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (choice == System.Windows.MessageBoxResult.Yes)
        {
            await _viewModel.Explorer.DeleteRuleAsync(rule.Id);
        }
    }

    private static FolderRule? ResolveFolderRule(object? dataContext) => dataContext switch
    {
        FolderRule rule => rule,
        ExplorerSearchResult { Rule: { } rule } => rule,
        _ => null
    };

    private void Folder_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Button targetButton
            || targetButton.DataContext is not NavigationFolderNodeViewModel target)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var canDropRule = e.Data.GetData(typeof(FolderRule)) is FolderRule rule
                          && rule.NavigationFolderId != target.Id;
        var canDropFolder = e.Data.GetData(typeof(NavigationFolder)) is NavigationFolder folder
                            && CanNestFolder(folder.Id, target.Id);
        if (!canDropRule && !canDropFolder)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        targetButton.Background = FindResource("SoftBlueBrush") as System.Windows.Media.Brush;
        targetButton.BorderBrush = FindResource("RouteBlueBrush") as System.Windows.Media.Brush;
        targetButton.BorderThickness = new Thickness(2);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Folder_DragLeave(object sender, DragEventArgs e) =>
        ResetFolderDropIndicator(sender as Button);

    private void Folder_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NavigationFolderNodeViewModel target)
        {
            return;
        }

        ResetFolderDropIndicator(sender as Button);
        if (e.Data.GetData(typeof(FolderRule)) is FolderRule rule)
        {
            _viewModel.Explorer.MoveRuleCommand.Execute(new RuleMoveRequest(rule.Id, target.Id));
            _draggedRule = null;
        }
        else if (e.Data.GetData(typeof(NavigationFolder)) is NavigationFolder folder
                 && CanNestFolder(folder.Id, target.Id))
        {
            _ = _viewModel.Explorer.MoveFolderAsync(folder.Id, target.Id);
            _draggedFolder = null;
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private void Folder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _folderDragStart = e.GetPosition(this);
        _draggedFolder = (sender as FrameworkElement)?.DataContext
            is NavigationFolderNodeViewModel node
            ? node.Folder
            : null;
    }

    private void Folder_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedFolder is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _folderDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _folderDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        try
        {
            DragDrop.DoDragDrop(
                (DependencyObject)sender,
                _draggedFolder,
                DragDropEffects.Move);
        }
        finally
        {
            _draggedFolder = null;
        }
    }

    private bool CanNestFolder(Guid folderId, Guid targetParentId)
    {
        if (folderId == _viewModel.Configuration.UncategorizedFolderId
            || folderId == targetParentId)
        {
            return false;
        }

        var currentId = (Guid?)targetParentId;
        var visited = new HashSet<Guid>();
        while (currentId is Guid id && visited.Add(id))
        {
            if (id == folderId)
            {
                return false;
            }

            currentId = _viewModel.Configuration.NavigationFolders
                .FirstOrDefault(folder => folder.Id == id)
                ?.ParentId;
        }

        return true;
    }

    private static void ResetFolderDropIndicator(Button? button)
    {
        if (button is null)
        {
            return;
        }

        button.ClearValue(Button.BackgroundProperty);
        button.ClearValue(Border.BorderBrushProperty);
        button.ClearValue(Border.BorderThicknessProperty);
    }

    private async void Sidebar_NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var currentFolder = _viewModel.Explorer.CurrentFolder;
        var dialog = new FolderEditorDialog(
            "新建子文件夹",
            $"将创建在“{currentFolder.Name}”中",
            "创建")
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.Explorer.CreateFolderAsync(
                currentFolder.Id,
                dialog.FolderName);
        }
    }

    private async void CurrentFolder_NewRule_Click(object sender, RoutedEventArgs e)
    {
        var targetFolder = _viewModel.Explorer.CurrentFolder;
        var targetPath = string.Join(
            " › ",
            _viewModel.Explorer.Breadcrumbs.Select(folder => folder.Name));
        var dialog = new ShortcutEditorDialog(targetPath)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.Explorer.CreateRuleAsync(
                targetFolder.Id,
                dialog.ShortcutTitle,
                dialog.Keywords,
                dialog.FolderPath);
        }
    }

    private async void Folder_NewChild_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NavigationFolderNodeViewModel node)
        {
            return;
        }

        var dialog = new FolderEditorDialog(
            "新建子文件夹",
            $"将创建在“{node.Name}”中",
            "创建")
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.Explorer.CreateFolderAsync(node.Id, dialog.FolderName);
        }
    }

    private async void Folder_Rename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NavigationFolderNodeViewModel node)
        {
            return;
        }

        var dialog = new FolderEditorDialog(
            "重命名文件夹",
            "修改后，内部的子文件夹和快捷方式不会改变",
            "保存",
            node.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true
            && !string.Equals(dialog.FolderName, node.Name, StringComparison.Ordinal))
        {
            await _viewModel.Explorer.RenameFolderAsync(node.Id, dialog.FolderName);
        }
    }

    private async void Folder_Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NavigationFolderNodeViewModel node)
        {
            return;
        }

        if (node.Id == _viewModel.Configuration.UncategorizedFolderId)
        {
            System.Windows.MessageBox.Show(
                "“未分类”不能删除。",
                "删除文件夹",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var choice = System.Windows.MessageBox.Show(
            "选择“是”：删除当前文件夹，子文件夹和快捷方式上移一层。\n\n"
            + "选择“否”：删除整棵文件夹树及其中的所有快捷方式。\n\n"
            + "真实磁盘文件夹不会被删除。",
            $"删除“{node.Name}”",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Warning);
        if (choice == System.Windows.MessageBoxResult.Cancel)
        {
            return;
        }

        await _viewModel.Explorer.DeleteFolderAsync(
            node.Id,
            choice == System.Windows.MessageBoxResult.Yes
                ? FolderDeletionMode.MoveContentsToParent
                : FolderDeletionMode.DeleteSubtree);
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

    private IEnumerable<string> BuildFolderPath(Guid folderId)
    {
        var path = new List<string>();
        var current = _viewModel.Configuration.NavigationFolders.FirstOrDefault(folder =>
            folder.Id == folderId);
        var visited = new HashSet<Guid>();
        while (current is not null && visited.Add(current.Id))
        {
            path.Add(current.Name);
            current = current.ParentId is Guid parentId
                ? _viewModel.Configuration.NavigationFolders.FirstOrDefault(folder =>
                    folder.Id == parentId)
                : null;
        }

        path.Reverse();
        return path;
    }

    private void ShowExplorerPage()
    {
        GeneralSettingsPage.Visibility = Visibility.Collapsed;
        ExplorerPage.Visibility = Visibility.Visible;
        if (IsVisible)
        {
            FocusSearch();
        }
    }

    private async void Settings_DownloadAndInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel?.Update is null
            || !_settingsViewModel.Update.CanDownloadAndInstall)
        {
            return;
        }

        var choice = System.Windows.MessageBox.Show(
            $"将下载并安装 QuickSearch {_settingsViewModel.Update.LatestVersion}。\n\n"
            + "安装包通过 SHA-256 校验后，QuickSearch 会退出并完成更新。是否继续？",
            "安装更新",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Information);
        if (choice == System.Windows.MessageBoxResult.Yes)
        {
            await _settingsViewModel.Update.DownloadAndInstallAsync();
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
