namespace QuickSearch.Core;

public enum ExplorerSearchResultKind
{
    Folder,
    Rule
}

public sealed record ExplorerSearchResult(
    ExplorerSearchResultKind Kind,
    Guid Id,
    string Title,
    string CategoryPath,
    string FolderPath,
    NavigationFolder? Folder,
    FolderRule? Rule)
{
    public string ShortcutText { get; init; } = string.Empty;
}

public sealed class ExplorerStatusEventArgs(
    string message,
    StatusKind kind) : EventArgs
{
    public string Message { get; } = message;

    public StatusKind Kind { get; } = kind;
}

public sealed record PinMoveRequest(
    Guid FolderId,
    Guid TargetFolderId,
    bool PlaceAfterTarget);

public sealed record RuleMoveRequest(Guid RuleId, Guid TargetFolderId);

public sealed record FolderNameRequest(Guid? ParentId, Guid? FolderId, string Name);
