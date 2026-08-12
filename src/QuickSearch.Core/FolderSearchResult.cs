namespace QuickSearch.Core;

public sealed record FolderSearchResult(string Name, string FullPath)
{
    public string ShortcutText { get; init; } = string.Empty;
}
