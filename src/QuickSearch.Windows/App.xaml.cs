using System.IO;
using System.Threading;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private MainWindow? _mainWindow;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "Local\\QuickSearch.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
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
            new EverythingFolderSearch(),
            new ExplorerFolderOpener(),
            new StartupRegistration());
        MainWindow = _mainWindow;
        _mainWindow.Show();
        _mainWindow.ActivateFromClipboard();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _mainWindow?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
