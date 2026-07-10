using System.IO;
using System.Windows.Interop;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public partial class App : System.Windows.Application
{
    private ISingleInstanceService? _singleInstance;
    private MainWindow? _mainWindow;

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
        var activationController = new SingleInstanceActivationController(_singleInstance);
        if (!_singleInstance.IsPrimary)
        {
            var secondaryResult = activationController.Start(() => { });
            if (!secondaryResult.Operation.Success)
            {
                System.Windows.MessageBox.Show(secondaryResult.Operation.Message, "QuickSearch");
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);
        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickSearch");
        var store = new JsonMappingStore(Path.Combine(configDirectory, "config.json"));
        _mainWindow = new MainWindow(
            store,
            new EverythingFolderSearch(new EverythingNativeAdapter()),
            new ClipboardTextReader(),
            new ExplorerFolderOpener(),
            new StartupRegistration());
        MainWindow = _mainWindow;
        new WindowInteropHelper(_mainWindow).EnsureHandle();
        await _mainWindow.InitializeAsync();

        var primaryResult = activationController.Start(
            () => Dispatcher.BeginInvoke(_mainWindow.ActivateFromClipboard));
        if (!primaryResult.Operation.Success)
        {
            System.Windows.MessageBox.Show(primaryResult.Operation.Message, "QuickSearch");
        }

        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            _mainWindow.ActivateFromClipboard();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _mainWindow?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
