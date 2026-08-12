using System.Windows.Threading;
using QuickSearch.Core;
using QuickSearch.Windows;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        try
        {
            var application = new App();
            application.InitializeComponent();
            var configuration = CreateConfiguration();
            var store = new MemoryMappingStore(configuration);
            var search = new ReadyFolderSearch();
            var launcher = new LauncherViewModel(
                store,
                search,
                new EmptyClipboardReader(),
                new SuccessfulFolderOpener(),
                _ => true);
            var bootstrap = new EverythingBootstrapViewModel(
                new ReadyEverythingInstallationManager());
            var update = new ApplicationUpdateViewModel(
                new CurrentApplicationUpdateService(),
                "0.0.4");
            var mainWindow = new MainWindow(
                launcher,
                store,
                search,
                new SuccessfulStartupRegistration(),
                bootstrap,
                update);
            application.MainWindow = mainWindow;
            mainWindow.InitializeAsync().GetAwaiter().GetResult();
            mainWindow.Show();
            mainWindow.UpdateLayout();
            mainWindow.ShowQuickLauncher(hideWhenDeactivated: false);
            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render);
            var launcherWindow = application.Windows
                .OfType<QuickLauncherWindow>()
                .SingleOrDefault();
            if (launcherWindow is null || !launcherWindow.IsVisible)
            {
                Console.Error.WriteLine("Quick launcher window did not become visible.");
                return 2;
            }

            launcherWindow.UpdateLayout();
            launcherWindow.Hide();
            launcher.ShowSettingsCommand.Execute(null);
            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render);
            var window = application.Windows
                .OfType<SettingsWindow>()
                .SingleOrDefault();
            if (window is null || !window.IsVisible)
            {
                Console.Error.WriteLine("Settings window did not become visible.");
                return 2;
            }

            window.UpdateLayout();
            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render);
            window.AllowApplicationExit();
            mainWindow.ExitApplication();
            Console.WriteLine("QuickSearch settings window smoke check passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static AppConfiguration CreateConfiguration()
    {
        var configuration = new AppConfiguration();
        var projects = configuration.AddNavigationFolder("项目", null);
        var customer = configuration.AddNavigationFolder("客户", projects.Id);
        var delivery = configuration.AddNavigationFolder("交付", customer.Id);
        configuration.AddRule("项目文件", ["项目", "客户"], @"C:\Projects", delivery.Id);
        configuration.PinFolder(projects.Id);
        return configuration;
    }
}

sealed class MemoryMappingStore(AppConfiguration configuration) : IMappingStore
{
    public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(configuration.Clone());

    public Task SaveAsync(
        AppConfiguration candidate,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class SuccessfulStartupRegistration : IStartupRegistration
{
    public PlatformOperationResult SetEnabled(bool enabled) =>
        PlatformOperationResult.Succeeded();
}

sealed class ReadyFolderSearch : IFolderSearch
{
    public EverythingHealth Health => EverythingHealth.Ready;

    public string? FailureMessage => null;

    public Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FolderSearchResult>>([]);
}

sealed class EmptyClipboardReader : IClipboardTextReader
{
    public PlatformOperationResult<string?> ReadText() =>
        PlatformOperationResult<string?>.Succeeded(string.Empty);
}

sealed class SuccessfulFolderOpener : IFolderOpener
{
    public PlatformOperationResult Open(string folderPath) =>
        PlatformOperationResult.Succeeded();
}

sealed class ReadyEverythingInstallationManager : IEverythingInstallationManager
{
    public Task<EverythingBootstrapResult> CheckAndStartAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new EverythingBootstrapResult(
            EverythingBootstrapState.Ready,
            "Everything 已就绪。"));

    public Task<EverythingBootstrapResult> InstallAndStartAsync(
        CancellationToken cancellationToken = default) =>
        CheckAndStartAsync(cancellationToken);
}

sealed class CurrentApplicationUpdateService : IApplicationUpdateService
{
    public Task<ApplicationUpdateCheck> CheckAsync(
        AppReleaseVersion installedVersion,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ApplicationUpdateCheck.Current(installedVersion.ToString()));

    public Task<string> DownloadAndVerifyAsync(
        ApplicationRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public PlatformOperationResult LaunchInstaller(string installerPath) =>
        PlatformOperationResult.Failed("Not available in UI smoke test.");
}
