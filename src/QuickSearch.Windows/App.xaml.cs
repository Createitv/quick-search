using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows.Interop;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public partial class App : System.Windows.Application
{
    private ISingleInstanceService? _singleInstance;
    private MainWindow? _mainWindow;
    private TrayIconController? _trayController;
    private HttpClient? _updateHttpClient;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (AppContext.TryGetSwitch("QuickSearch.UiSmoke", out var isUiSmoke)
            && isUiSmoke)
        {
            base.OnStartup(e);
            return;
        }

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
        var updatedRequested = e.Args.Contains(
            "--updated",
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
        FontSizeResources.Apply(FontSizeResources.DefaultFontSize);
        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickSearch");
        var configPath = Path.Combine(configDirectory, "config.json");
        var configurationExistedAtStartup = File.Exists(configPath);
        var store = new JsonMappingStore(configPath);
        _updateHttpClient = UpdateHttpClientFactory.Create();
        var updateService = new ManifestUpdateService(
            _updateHttpClient,
            new Uri("https://quick-search-updates.xfy150150.workers.dev/latest.json"),
            Path.Combine(configDirectory, "updates"),
            new WindowsInstallerLauncher());
        var installedVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        var update = new ApplicationUpdateViewModel(
            updateService,
            installedVersion is null
                ? "0.0.2"
                : $"{installedVersion.Major}.{installedVersion.Minor}.{Math.Max(installedVersion.Build, 0)}");
        update.ShutdownRequested += Update_ShutdownRequested;
        var native = new EverythingNativeAdapter();
        var search = new EverythingFolderSearch(native);
        var bootstrap = new EverythingBootstrapViewModel(
            new EverythingInstallationManager(native, AppContext.BaseDirectory));
        var startup = new StartupRegistration();
        var clipboard = new ClipboardTextReader();
        var launcherViewModel = new LauncherViewModel(
            store,
            search,
            clipboard,
            new ExplorerFolderOpener());
        var pathOpener = new ShellPathOpener();
        _mainWindow = new MainWindow(
            launcherViewModel,
            store,
            search,
            startup,
            bootstrap,
            update,
            search,
            pathOpener,
            clipboard);
        MainWindow = _mainWindow;
        new WindowInteropHelper(_mainWindow).EnsureHandle();
        await _mainWindow.InitializeAsync();
        FontSizeResources.Apply(launcherViewModel.Configuration.Settings.UiFontSize);
        if (updatedRequested)
        {
            _mainWindow.ReportStatus("QuickSearch 已更新完成。");
        }
        _ = _mainWindow.CheckForUpdatesOnStartupAsync();
        await bootstrap.InitializeAsync();

        var activationController = new SingleInstanceActivationController(_singleInstance);
        var primaryResult = activationController.Start(
            () => Dispatcher.BeginInvoke((Action)(() =>
                _mainWindow.ShowLauncher())));
        if (!primaryResult.Operation.Success)
        {
            _mainWindow.ReportStatus(primaryResult.Operation.Message);
        }

        InitializeTray();
        if (_mainWindow.ShowFirstRunOnboarding(configurationExistedAtStartup))
        {
            return;
        }

        if (!bootstrap.CanSearch)
        {
            _mainWindow.ShowLauncher();
        }
        else if (updatedRequested)
        {
            _mainWindow.ShowLauncher();
        }
        else if (!backgroundRequested)
        {
            _mainWindow.ShowLauncher();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayController?.Dispose();
        _updateHttpClient?.Dispose();
        _mainWindow?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void Update_ShutdownRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => Shutdown());

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
            () => Dispatcher.Invoke(() => _mainWindow.ShowQuickLauncher()),
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
