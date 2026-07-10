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
        menu.Items.Add("打开", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("设置", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "QuickSearch",
            Icon = Drawing.SystemIcons.Application,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public PlatformOperationResult Show() => PlatformBoundary.Capture(
        () => { _notifyIcon.Visible = true; },
        "无法显示系统托盘图标");

    public void Dispose()
    {
        _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);
}
