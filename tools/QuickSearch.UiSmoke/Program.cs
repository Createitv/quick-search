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
            AppContext.SetSwitch("QuickSearch.UiSmoke", true);
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
            var clipboard = new MutableClipboardTextReader("项目文件");
            var mainWindow = new MainWindow(
                launcher,
                store,
                search,
                new SuccessfulStartupRegistration(),
                bootstrap,
                update,
                launcherClipboard: clipboard);
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

            Thread.Sleep(250);
            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render);
            var launcherSearchBox = launcherWindow.FindName("LauncherSearchBox")
                as System.Windows.Controls.TextBox;
            var launcherResults = launcherWindow.FindName("ResultsList")
                as System.Windows.Controls.ListBox;
            if (launcherSearchBox?.Text != "项目文件"
                || launcherResults?.Items.Count != 1)
            {
                Console.Error.WriteLine("Clipboard match was not shown in the quick launcher.");
                return 2;
            }

            clipboard.Text = "没有任何结果";
            launcherWindow.Hide();
            mainWindow.ShowQuickLauncher(hideWhenDeactivated: false);
            Thread.Sleep(250);
            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render);
            if (launcherSearchBox.Text.Length != 0
                || launcherResults.Items.Count != 0)
            {
                var launcherState = launcherWindow.DataContext as QuickLauncherViewModel;
                Console.Error.WriteLine(
                    "Unmatched clipboard text was not cleared from the quick launcher. "
                    + $"TextBox='{launcherSearchBox.Text}', Items={launcherResults.Items.Count}, "
                    + $"ViewModelText='{launcherState?.SearchText}', "
                    + $"ViewModelItems={launcherState?.Results.Count}, "
                    + $"IsSearching={launcherState?.IsSearching}, Status='{launcherState?.StatusText}'.");
                return 2;
            }

            launcherWindow.UpdateLayout();
            launcherWindow.Hide();
            var windowCountBeforeSettings = application.Windows.Count;
            launcher.ShowSettingsCommand.Execute(null);
            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render);
            var settingsPage = mainWindow.FindName("GeneralSettingsPage")
                as System.Windows.FrameworkElement;
            if (settingsPage is null || settingsPage.Visibility != System.Windows.Visibility.Visible)
            {
                Console.Error.WriteLine("In-window general settings page did not become visible.");
                return 2;
            }

            if (application.Windows.Count != windowCountBeforeSettings)
            {
                Console.Error.WriteLine("Settings created an unexpected additional window.");
                return 2;
            }

            mainWindow.UpdateLayout();
            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render);

            var windowCountBeforeOnboarding = application.Windows.Count;
            if (!mainWindow.ShowFirstRunOnboarding(configurationExistedAtStartup: false))
            {
                Console.Error.WriteLine("First-run onboarding was not requested for a new configuration.");
                return 2;
            }

            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render);
            var onboardingPage = mainWindow.FindName("OnboardingPage")
                as System.Windows.FrameworkElement;
            if (onboardingPage is null
                || onboardingPage.Visibility != System.Windows.Visibility.Visible)
            {
                Console.Error.WriteLine("In-window first-run onboarding did not become visible.");
                return 2;
            }

            if (application.Windows.Count != windowCountBeforeOnboarding)
            {
                Console.Error.WriteLine("Onboarding created an unexpected additional window.");
                return 2;
            }

            if (onboardingPage.DataContext is not FirstRunOnboardingViewModel onboarding)
            {
                Console.Error.WriteLine("Onboarding did not receive its view model.");
                return 2;
            }

            onboarding.NextCommand.Execute(null);
            onboarding.NextCommand.Execute(null);
            onboarding.NextCommand.Execute(null);
            onboarding.NextCommand.Execute(null);
            application.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Background);
            if (onboardingPage.Visibility != System.Windows.Visibility.Collapsed
                || store.LastSavedConfiguration?.Settings.HasCompletedOnboarding != true)
            {
                Console.Error.WriteLine("Onboarding completion was not saved and dismissed.");
                return 2;
            }

            mainWindow.ExitApplication();
            Console.WriteLine("QuickSearch in-window settings and onboarding smoke checks passed.");
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
    public AppConfiguration? LastSavedConfiguration { get; private set; }

    public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(configuration.Clone());

    public Task SaveAsync(
        AppConfiguration candidate,
        CancellationToken cancellationToken = default)
    {
        LastSavedConfiguration = candidate.Clone();
        return Task.CompletedTask;
    }
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

sealed class MutableClipboardTextReader(string? text) : IClipboardTextReader
{
    public string? Text { get; set; } = text;

    public PlatformOperationResult<string?> ReadText() =>
        PlatformOperationResult<string?>.Succeeded(Text);
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
