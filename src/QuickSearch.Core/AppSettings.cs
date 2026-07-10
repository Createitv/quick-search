namespace QuickSearch.Core;

public sealed record AppSettings
{
    public string GlobalShortcut { get; init; } = "Ctrl+Alt+F";

    public bool StartWithWindows { get; init; } = true;
}
