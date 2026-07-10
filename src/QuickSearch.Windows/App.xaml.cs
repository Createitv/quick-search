using System.IO;
using System.Windows.Interop;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public partial class App : System.Windows.Application
{
    private ISingleInstanceService? _singleInstance;
    private MainWindow? _mainWindow;
    private TrayIconController? _trayController;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        var serviceResult = PlatformBoundary.Capture<ISingleInstanceService>(
            () => new NamedEventSingleInstance(),
            "无法初始化单实例服务");
        if (!serviceResult.Success || serviceResult.Value is null)
        {
            System.Windows.MessageBox.Show(serviceResult.Message, "QuickSearch");
            Shutdown();
            return;
        }

        _singleInstance = serviceResult.Value;
        var backgroundRequested = e.Args.Contains(
            "--background",
            StringComparer.OrdinalIgnoreCase);
        var disposition = AppLaunchDecision.Decide(
            _singleInstance.IsPrimary,
            backgroundRequested);
        if (disposition != AppLaunchDisposition.StartPrimary)
        {
            if (disposition == AppLaunchDisposition.NotifyExistingAndExit)
            {
                var secondaryResult = new SingleInstanceActivationController(
                    _singleInstance).Start(() => { });
                if (!secondaryResult.Operation.Success)
                {
                    System.Windows.MessageBox.Show(
                        secondaryResult.Operation.Message,
                        "QuickSearch");
                }
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);
        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickSearch");
        var store = new JsonMappingStore(Path.Combine(configDirectory, "config.json"));
        var search = new EverythingFolderSearch(new EverythingNativeAdapter());
        var startup = new StartupRegistration();
        var launcherViewModel = new LauncherViewModel(
            store,
            search,
            new ClipboardTextReader(),
            new ExplorerFolderOpener());
        _mainWindow = new MainWindow(
            launcherViewModel,
            store,
            search,
            startup);
        MainWindow = _mainWindow;
        new WindowInteropHelper(_mainWindow).EnsureHandle();
        await _mainWindow.InitializeAsync();

        var activationController = new SingleInstanceActivationController(_singleInstance);
        var primaryResult = activationController.Start(
            () => Dispatcher.BeginInvoke(async () =>
                await _mainWindow.ActivateFromClipboardAsync()));
        if (!primaryResult.Operation.Success)
        {
            _mainWindow.ReportStatus(primaryResult.Operation.Message);
        }

        InitializeTray();
        if (!backgroundRequested)
        {
            await _mainWindow.ActivateFromClipboardAsync();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayController?.Dispose();
        _mainWindow?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void InitializeTray()
    {
        if (_mainWindow is null)
        {
            return;
        }

        var trayResult = PlatformBoundary.Capture<ITrayIcon>(
            () => new NotifyIconAdapter(),
            "无法初始化系统托盘图标");
        if (!trayResult.Success || trayResult.Value is null)
        {
            _mainWindow.ReportStatus(trayResult.Message);
            return;
        }

        _trayController = new TrayIconController(
            trayResult.Value,
            () => Dispatcher.Invoke(_mainWindow.ShowLauncher),
            () => Dispatcher.Invoke(_mainWindow.ShowSettings),
            () => Dispatcher.Invoke(_mainWindow.ExitApplication));
        _trayController.FailureReported += message =>
            Dispatcher.BeginInvoke(() => _mainWindow.ReportStatus(message));
        var showResult = _trayController.Start();
        if (!showResult.Success)
        {
            _mainWindow.ReportStatus(showResult.Message);
        }
    }
}
