namespace QuickSearch.Core;

public enum QuickLauncherItemKind
{
    Shortcut,
    Folder,
    Application,
    File
}

public sealed record QuickLauncherSearchItem(
    string Name,
    string FullPath,
    QuickLauncherItemKind Kind);

public interface IQuickLauncherSearch
{
    Task<IReadOnlyList<QuickLauncherSearchItem>> SearchLauncherAsync(
        string query,
        CancellationToken cancellationToken = default);
}

public sealed record QuickLauncherResult(
    string Title,
    string FullPath,
    string Detail,
    QuickLauncherItemKind Kind,
    Guid? RuleId = null)
{
    public string ShortcutText { get; init; } = string.Empty;
}
