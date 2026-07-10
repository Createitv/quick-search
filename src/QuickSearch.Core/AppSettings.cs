namespace QuickSearch.Core;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;

    public string GlobalShortcut { get; init; } = "Ctrl+Alt+F";

    public bool StartWithWindows { get; init; } = true;
}
