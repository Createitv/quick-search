namespace QuickSearch.Core;

public sealed class UnavailableHotkeyRegistration(
    string activeShortcut,
    string failureMessage) : IHotkeyRegistration
{
    public string? ActiveShortcut { get; } = activeShortcut;

    public PlatformOperationResult TryReplace(string shortcut) =>
        string.Equals(shortcut, ActiveShortcut, StringComparison.Ordinal)
            ? PlatformOperationResult.Succeeded()
            : PlatformOperationResult.Failed(failureMessage);
}
