namespace QuickSearch.Core;

public interface IHotkeyRegistrar
{
    bool TryRegister(int registrationId, string shortcut);

    void Unregister(int registrationId);
}

public interface IHotkeyRegistration
{
    string? ActiveShortcut { get; }

    PlatformOperationResult TryReplace(string shortcut);
}

public sealed class HotkeyRegistrationController : IHotkeyRegistration, IDisposable
{
    private const int FirstRegistrationId = 0x5146;
    private const int SecondRegistrationId = 0x5147;

    private readonly IHotkeyRegistrar _registrar;
    private int? _activeRegistrationId;

    public HotkeyRegistrationController(IHotkeyRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        _registrar = registrar;
    }

    public string? ActiveShortcut { get; private set; }

    public bool IsActiveRegistration(int registrationId) =>
        _activeRegistrationId == registrationId;

    public PlatformOperationResult TryReplace(string shortcut)
    {
        if (string.Equals(ActiveShortcut, shortcut, StringComparison.Ordinal))
        {
            return PlatformOperationResult.Succeeded();
        }

        var candidateId = _activeRegistrationId == FirstRegistrationId
            ? SecondRegistrationId
            : FirstRegistrationId;

        try
        {
            if (!_registrar.TryRegister(candidateId, shortcut))
            {
                return PlatformOperationResult.Failed(
                    $"快捷键 {shortcut} 已被其他程序占用。");
            }

            var previousId = _activeRegistrationId;
            _activeRegistrationId = candidateId;
            ActiveShortcut = shortcut;
            if (previousId is not null)
            {
                _registrar.Unregister(previousId.Value);
            }

            return PlatformOperationResult.Succeeded();
        }
        catch (Exception exception)
        {
            return PlatformOperationResult.Failed(
                $"无法注册快捷键：{exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_activeRegistrationId is not null)
        {
            _registrar.Unregister(_activeRegistrationId.Value);
            _activeRegistrationId = null;
            ActiveShortcut = null;
        }
    }
}
