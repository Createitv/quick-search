namespace QuickSearch.Core;

public sealed class HotkeySettingsController
{
    private readonly IHotkeyRegistration _registration;
    private readonly IMappingStore _store;

    public HotkeySettingsController(
        IHotkeyRegistration registration,
        IMappingStore store)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(store);
        _registration = registration;
        _store = store;
    }

    public async Task<PlatformOperationResult> ApplyAsync(
        AppConfiguration configuration,
        AppSettings candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(candidate);

        var registrationResult = _registration.TryReplace(candidate.GlobalShortcut);
        if (!registrationResult.Success)
        {
            return registrationResult;
        }

        var previous = configuration.Settings;
        configuration.Settings = candidate;
        try
        {
            await _store.SaveAsync(configuration, cancellationToken);
            return PlatformOperationResult.Succeeded();
        }
        catch (Exception exception)
        {
            configuration.Settings = previous;
            _registration.TryReplace(previous.GlobalShortcut);
            return PlatformOperationResult.Failed(
                $"无法保存设置：{exception.Message}");
        }
    }
}
