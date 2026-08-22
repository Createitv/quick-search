using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Shell;
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
                new SuccessfulFolderOpener(),
                _ => true);
            var bootstrap = new EverythingBootstrapViewModel(
                new ReadyEverythingInstallationManager());
            var mainWindow = new MainWindow(
                launcher,
                store,
                search,
                new SuccessfulStartupRegistration(),
                bootstrap);
            application.MainWindow = mainWindow;
            mainWindow.InitializeAsync().GetAwaiter().GetResult();
            mainWindow.Show();
            mainWindow.UpdateLayout();
            var mainWindowChrome = WindowChrome.GetWindowChrome(mainWindow);
            if (mainWindowChrome is null || mainWindowChrome.CaptionHeight != 44)
            {
                Console.Error.WriteLine("Main window does not expose its 44px title bar as a native drag region.");
                return 2;
            }

            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(application.Dispatcher));
            const string initialClipboardText = "QuickSearch 不应自动读取这段文字";
            System.Windows.Clipboard.SetText(initialClipboardText);
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

            var launcherSearchBox = launcherWindow.FindName("LauncherSearchBox")
                as System.Windows.Controls.TextBox;
            var launcherResults = launcherWindow.FindName("ResultsList")
                as System.Windows.Controls.ListBox;
            if (launcherSearchBox is null
                || launcherResults is null
                || !WaitUntil(
                    application.Dispatcher,
                    () => launcherSearchBox.Text.Length == 0
                        && launcherResults.Items.Count == 0))
            {
                Console.Error.WriteLine("Quick launcher should open empty for manual input.");
                return 2;
            }

            ApplicationCommands.Paste.Execute(null, launcherSearchBox);
            if (!WaitUntil(
                    application.Dispatcher,
                    () => launcherSearchBox.Text == initialClipboardText))
            {
                Console.Error.WriteLine("System paste did not insert clipboard text into the launcher search box.");
                return 2;
            }

            const string copiedText = "QuickSearch 系统复制剪切粘贴测试";
            launcherSearchBox.Text = copiedText;
            launcherSearchBox.SelectAll();
            ApplicationCommands.Copy.Execute(null, launcherSearchBox);
            if (System.Windows.Clipboard.GetText() != copiedText)
            {
                Console.Error.WriteLine("System copy did not update the Windows clipboard.");
                return 2;
            }

            ApplicationCommands.Cut.Execute(null, launcherSearchBox);
            if (launcherSearchBox.Text.Length != 0
                || System.Windows.Clipboard.GetText() != copiedText)
            {
                Console.Error.WriteLine("System cut did not preserve the expected clipboard text.");
                return 2;
            }

            ApplicationCommands.Paste.Execute(null, launcherSearchBox);
            if (!WaitUntil(
                    application.Dispatcher,
                    () => launcherSearchBox.Text == copiedText))
            {
                Console.Error.WriteLine("System paste did not restore the copied text.");
                return 2;
            }

            launcherSearchBox.Clear();

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

    private static bool WaitUntil(
        Dispatcher dispatcher,
        Func<bool> condition,
        int timeoutMilliseconds = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }

        return condition();
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
