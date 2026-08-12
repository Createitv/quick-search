namespace QuickSearch.Core;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 2;

    public string GlobalShortcut { get; init; } = "Ctrl+Alt+F";

    public bool StartWithWindows { get; init; } = true;

    public bool AutomaticallyCheckForUpdates { get; init; } = true;

    public DateTimeOffset? LastUpdateCheckAtUtc { get; init; }
}
