using System.Windows.Threading;
using QuickSearch.Core;
using QuickSearch.Windows;

return RunSettingsWindowSmoke();

[STAThread]
static int RunSettingsWindowSmoke()
{
    try
    {
        var application = new App();
        application.InitializeComponent();
        var configuration = new AppConfiguration();
        var settings = new SettingsViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            new UnavailableHotkeyRegistration("Ctrl+Alt+F", "UI smoke"),
            new SuccessfulStartupRegistration(),
            new ReadyFolderSearch());
        var window = new SettingsWindow(settings);
        window.Show();
        window.UpdateLayout();
        application.Dispatcher.Invoke(
            () => { },
            DispatcherPriority.Render);
        if (!window.IsVisible)
        {
            Console.Error.WriteLine("Settings window did not become visible.");
            return 2;
        }

        window.AllowApplicationExit();
        Console.WriteLine("QuickSearch settings window smoke check passed.");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
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
