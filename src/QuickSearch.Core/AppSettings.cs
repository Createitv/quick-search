namespace QuickSearch.Core;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 3;

    public string GlobalShortcut { get; init; } = "Ctrl+Alt+F";

    public string QuickLauncherShortcut { get; init; } = "Alt+K";

    public bool StartWithWindows { get; init; } = true;

    public bool AutomaticallyCheckForUpdates { get; init; } = true;

    public bool HasCompletedOnboarding { get; init; }

    public DateTimeOffset? LastUpdateCheckAtUtc { get; init; }

    public int QuickLauncherResultColumns { get; init; } = 1;

    public double UiFontSize { get; init; } = 14;
}
