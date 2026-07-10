namespace QuickSearch.Core.Tests;

public sealed class HotkeySettingsControllerTests
{
    [Fact]
    public async Task ApplyAsync_KeepsActiveShortcutAndDoesNotPersistWhenCandidateRegistrationFails()
    {
        var registrar = new FakeHotkeyRegistrar(true, false);
        using var registration = new HotkeyRegistrationController(registrar);
        Assert.True(registration.TryReplace("Ctrl+Alt+F").Success);
        var store = new FakeMappingStore();
        var controller = new HotkeySettingsController(registration, store);
        var configuration = new AppConfiguration
        {
            Settings = new AppSettings { GlobalShortcut = "Ctrl+Alt+F" }
        };
        var candidate = configuration.Settings with
        {
            GlobalShortcut = "Ctrl+Alt+1"
        };

        var result = await controller.ApplyAsync(configuration, candidate);

        Assert.False(result.Success);
        Assert.Equal("Ctrl+Alt+F", registration.ActiveShortcut);
        Assert.Equal("Ctrl+Alt+F", configuration.Settings.GlobalShortcut);
        Assert.Equal(0, store.SaveCalls);
        Assert.Empty(registrar.UnregisteredIds);
    }

    [Fact]
    public async Task ApplyAsync_RegistersCandidateBeforeRemovingOldShortcutAndPersistsOnSuccess()
    {
        var registrar = new FakeHotkeyRegistrar(true, true);
        using var registration = new HotkeyRegistrationController(registrar);
        Assert.True(registration.TryReplace("Ctrl+Alt+F").Success);
        var store = new FakeMappingStore();
        var controller = new HotkeySettingsController(registration, store);
        var configuration = new AppConfiguration
        {
            Settings = new AppSettings { GlobalShortcut = "Ctrl+Alt+F" }
        };
        var candidate = configuration.Settings with
        {
            GlobalShortcut = "Ctrl+Alt+1"
        };

        var result = await controller.ApplyAsync(configuration, candidate);

        Assert.True(result.Success);
        Assert.Equal("Ctrl+Alt+1", registration.ActiveShortcut);
        Assert.Equal("Ctrl+Alt+1", configuration.Settings.GlobalShortcut);
        Assert.Equal(1, store.SaveCalls);
        Assert.Equal(
            ["register:Ctrl+Alt+F", "register:Ctrl+Alt+1", "unregister"],
            registrar.Operations);
    }

    private sealed class FakeHotkeyRegistrar(params bool[] registrationResults)
        : IHotkeyRegistrar
    {
        private readonly Queue<bool> _registrationResults = new(registrationResults);

        public List<int> UnregisteredIds { get; } = [];

        public List<string> Operations { get; } = [];

        public bool TryRegister(int registrationId, string shortcut)
        {
            Operations.Add($"register:{shortcut}");
            return _registrationResults.Dequeue();
        }

        public void Unregister(int registrationId)
        {
            UnregisteredIds.Add(registrationId);
            Operations.Add("unregister");
        }
    }

    private sealed class FakeMappingStore : IMappingStore
    {
        public int SaveCalls { get; private set; }

        public Task<AppConfiguration> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppConfiguration());

        public Task SaveAsync(
            AppConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }
}
