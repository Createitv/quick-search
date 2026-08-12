namespace QuickSearch.Core;

public interface ITrayIcon : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? LauncherRequested;

    event EventHandler? SettingsRequested;

    event EventHandler? ExitRequested;

    PlatformOperationResult Show();
}

public sealed class TrayIconController : IDisposable
{
    private readonly ITrayIcon _trayIcon;
    private readonly Action _open;
    private readonly Action _launcher;
    private readonly Action _settings;
    private readonly Action _exit;
    private bool _disposed;

    public TrayIconController(
        ITrayIcon trayIcon,
        Action open,
        Action launcher,
        Action settings,
        Action exit)
    {
        ArgumentNullException.ThrowIfNull(trayIcon);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(exit);
        _trayIcon = trayIcon;
        _open = open;
        _launcher = launcher;
        _settings = settings;
        _exit = exit;
        _trayIcon.OpenRequested += TrayIcon_OpenRequested;
        _trayIcon.LauncherRequested += TrayIcon_LauncherRequested;
        _trayIcon.SettingsRequested += TrayIcon_SettingsRequested;
        _trayIcon.ExitRequested += TrayIcon_ExitRequested;
    }

    public event Action<string>? FailureReported;

    public string? LastFailure { get; private set; }

    public PlatformOperationResult Start()
    {
        try
        {
            var result = _trayIcon.Show();
            if (!result.Success)
            {
                ReportFailure(result.Message);
            }

            return result;
        }
        catch (Exception exception)
        {
            var message = $"无法显示系统托盘图标：{exception.Message}";
            ReportFailure(message);
            return PlatformOperationResult.Failed(message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon.OpenRequested -= TrayIcon_OpenRequested;
        _trayIcon.LauncherRequested -= TrayIcon_LauncherRequested;
        _trayIcon.SettingsRequested -= TrayIcon_SettingsRequested;
        _trayIcon.ExitRequested -= TrayIcon_ExitRequested;
        try
        {
            _trayIcon.Dispose();
        }
        catch (Exception exception)
        {
            ReportFailure($"无法释放系统托盘图标：{exception.Message}");
        }
    }

    private void TrayIcon_OpenRequested(object? sender, EventArgs e) =>
        InvokeBoundary(_open, "无法打开启动器");

    private void TrayIcon_LauncherRequested(object? sender, EventArgs e) =>
        InvokeBoundary(_launcher, "无法打开快速启动器");

    private void TrayIcon_SettingsRequested(object? sender, EventArgs e) =>
        InvokeBoundary(_settings, "无法打开设置");

    private void TrayIcon_ExitRequested(object? sender, EventArgs e) =>
        InvokeBoundary(_exit, "无法退出 QuickSearch");

    private void InvokeBoundary(Action operation, string failurePrefix)
    {
        try
        {
            operation();
        }
        catch (Exception exception)
        {
            ReportFailure($"{failurePrefix}：{exception.Message}");
        }
    }

    private void ReportFailure(string message)
    {
        LastFailure = message;
        try
        {
            FailureReported?.Invoke(message);
        }
        catch
        {
            // A diagnostic listener must never tear down the tray callback boundary.
        }
    }
}
