namespace QuickSearch.Core.Tests;

public sealed class TrayIconControllerTests
{
    [Fact]
    public void StartAndEvents_KeepTrayAdapterBehindRecoverableBoundary()
    {
        var trayIcon = new FakeTrayIcon();
        var operations = new List<string>();
        using var controller = new TrayIconController(
            trayIcon,
            () => operations.Add("open"),
            () => operations.Add("launcher"),
            () => throw new InvalidOperationException("settings exploded"),
            () => operations.Add("exit"));

        var start = controller.Start();
        trayIcon.RaiseOpen();
        trayIcon.RaiseLauncher();
        var settingsException = Record.Exception(trayIcon.RaiseSettings);
        trayIcon.RaiseExit();

        Assert.True(start.Success);
        Assert.Equal(1, trayIcon.ShowCalls);
        Assert.Equal(["open", "launcher", "exit"], operations);
        Assert.Null(settingsException);
        Assert.Contains("settings exploded", controller.LastFailure);
    }

    [Fact]
    public void Start_SurfacesAdapterFailureWithoutThrowing()
    {
        var trayIcon = new FakeTrayIcon
        {
            ShowResult = PlatformOperationResult.Failed("tray unavailable")
        };
        using var controller = new TrayIconController(
            trayIcon,
            () => { },
            () => { },
            () => { },
            () => { });

        var result = controller.Start();

        Assert.False(result.Success);
        Assert.Equal("tray unavailable", controller.LastFailure);
    }

    private sealed class FakeTrayIcon : ITrayIcon
    {
        public event EventHandler? OpenRequested;

        public event EventHandler? LauncherRequested;

        public event EventHandler? SettingsRequested;

        public event EventHandler? ExitRequested;

        public int ShowCalls { get; private set; }

        public PlatformOperationResult ShowResult { get; init; } =
            PlatformOperationResult.Succeeded();

        public PlatformOperationResult Show()
        {
            ShowCalls++;
            return ShowResult;
        }

        public void RaiseOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);

        public void RaiseLauncher() => LauncherRequested?.Invoke(this, EventArgs.Empty);

        public void RaiseSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

        public void RaiseExit() => ExitRequested?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
        }
    }
}
