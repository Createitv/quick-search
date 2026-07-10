using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using QuickSearch.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QuickSearch.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly IMappingStore _store;
    private readonly EverythingFolderSearch _search;
    private readonly ExplorerFolderOpener _opener;
    private readonly StartupRegistration _startup;
    private readonly Forms.NotifyIcon _trayIcon;
    private AppConfiguration _configuration = new();
    private GlobalHotkey? _hotkey;
    private string? _confirmedPath;
    private bool _allowClose;
    private bool _suppressAliasChange;

    public MainWindow(
        IMappingStore store,
        EverythingFolderSearch search,
        ExplorerFolderOpener opener,
        StartupRegistration startup)
    {
        _store = store;
        _search = search;
        _opener = opener;
        _startup = startup;
        InitializeComponent();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => ShowLauncher());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "QuickSearch",
            Icon = Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowLauncher();
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    public async void ActivateFromClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                _suppressAliasChange = true;
                AliasBox.Text = System.Windows.Clipboard.GetText().Trim();
                _suppressAliasChange = false;
                UpdateMappingState();
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"无法读取剪贴板：{exception.Message}";
        }

        ShowLauncher();
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _hotkey?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _configuration = await _store.LoadAsync();
        _startup.SetEnabled(_configuration.Settings.StartWithWindows);
        RegisterConfiguredHotkey();
        UpdateMappingState();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hotkey = new GlobalHotkey(this);
        _hotkey.Pressed += (_, _) => Dispatcher.Invoke(ActivateFromClipboard);
    }

    private void RegisterConfiguredHotkey()
    {
        if (_hotkey is null)
        {
            return;
        }

        try
        {
            if (!_hotkey.Register(_configuration.Settings.GlobalShortcut))
            {
                StatusText.Text = $"快捷键 {_configuration.Settings.GlobalShortcut} 已被其他程序占用。";
            }
        }
        catch (FormatException exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void AliasBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_suppressAliasChange)
        {
            UpdateMappingState();
        }
    }

    private void UpdateMappingState()
    {
        _confirmedPath = null;
        ResultsList.ItemsSource = null;
        SelectedPathText.Text = string.Empty;
        var alias = AliasBox.Text.Trim();
        if (alias.Length == 0)
        {
            StatusText.Text = "请先复制或输入邮箱别名。";
            return;
        }

        var mapping = _configuration.FindMapping(alias);
        if (mapping is not null && Directory.Exists(mapping.FolderPath))
        {
            _confirmedPath = mapping.FolderPath;
            SelectedPathText.Text = mapping.FolderPath;
            StatusText.Text = $"已找到保存的映射：{mapping.Alias} → {Path.GetFileName(mapping.FolderPath)}";
            return;
        }

        if (mapping is not null)
        {
            StatusText.Text = "保存的文件夹已经不存在，请重新搜索并修复映射。";
        }
        else
        {
            StatusText.Text = "首次使用这个别名，请输入真实文件夹名称并搜索。";
        }

        if (string.IsNullOrWhiteSpace(FolderQueryBox.Text))
        {
            FolderQueryBox.Text = alias;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        await SearchFoldersAsync();
    }

    private async Task SearchFoldersAsync()
    {
        var query = FolderQueryBox.Text.Trim();
        if (query.Length == 0)
        {
            StatusText.Text = "请输入文件夹名称。";
            return;
        }

        StatusText.Text = "正在通过 Everything 搜索…";
        try
        {
            var results = await _search.SearchAsync(query);
            ResultsList.ItemsSource = results;
            ResultsList.SelectedIndex = results.Count > 0 ? 0 : -1;
            StatusText.Text = _search.Health switch
            {
                EverythingHealth.Ready when results.Count > 0 => $"找到 {results.Count} 个候选文件夹。",
                EverythingHealth.Ready => "没有找到匹配的文件夹。",
                EverythingHealth.DllMissing => "缺少 Everything64.dll，请重新安装 QuickSearch。",
                EverythingHealth.NotRunning => "Everything 未运行或索引尚未就绪，请安装并启动普通版 Everything 1.4。",
                _ => "Everything 查询失败，请确认普通版 Everything 正在运行。"
            };
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "搜索已取消。";
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        await ConfirmAndOpenAsync();
    }

    private async Task ConfirmAndOpenAsync()
    {
        var path = _confirmedPath;
        if (path is null && ResultsList.SelectedItem is FolderSearchResult selected)
        {
            path = selected.FullPath;
            var alias = AliasBox.Text.Trim();
            if (alias.Length == 0)
            {
                StatusText.Text = "请输入邮箱别名。";
                return;
            }

            _configuration.UpsertMapping(alias, path);
            await _store.SaveAsync(_configuration);
        }

        if (path is null)
        {
            StatusText.Text = "请先搜索并选择一个文件夹。";
            return;
        }

        try
        {
            _opener.Open(path);
            Hide();
        }
        catch (DirectoryNotFoundException)
        {
            StatusText.Text = "文件夹已经不存在，请重新搜索。";
            _confirmedPath = null;
        }
    }

    private async void FolderQueryBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SearchFoldersAsync();
        }
    }

    private async void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await ConfirmAndOpenAsync();
    }

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
        else if (e.Key == Key.Enter && ResultsList.SelectedItem is not null)
        {
            await ConfirmAndOpenAsync();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        var window = new SettingsWindow(_configuration, _store, _startup)
        {
            Owner = this
        };
        if (window.ShowDialog() == true)
        {
            RegisterConfiguredHotkey();
            UpdateMappingState();
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private void ShowLauncher()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        AliasBox.Focus();
        AliasBox.SelectAll();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
