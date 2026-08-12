using QuickSearch.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QuickSearch.Windows;

public sealed class NotifyIconAdapter : ITrayIcon
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public NotifyIconAdapter()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开文件夹管理器", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("打开快速启动器", null, (_, _) => LauncherRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "QuickSearch",
            Icon = LoadApplicationIcon(),
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? LauncherRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public PlatformOperationResult Show() => PlatformBoundary.Capture(
        () => { _notifyIcon.Visible = true; },
        "无法显示系统托盘图标");

    public void Dispose()
    {
        _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
        _notifyIcon.Visible = false;
        var icon = _notifyIcon.Icon;
        _notifyIcon.Icon = null;
        _notifyIcon.Dispose();
        icon?.Dispose();
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/QuickSearch.ico"))
            ?? throw new InvalidOperationException(
                "QuickSearch icon resource is unavailable.");
        using var icon = new Drawing.Icon(resource.Stream);
        return (Drawing.Icon)icon.Clone();
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);
}
