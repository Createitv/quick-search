namespace QuickSearch.Core;

public sealed record NavigationFolder(
    Guid Id,
    Guid? ParentId,
    string Name,
    int SortOrder,
    DateTimeOffset UpdatedAtUtc);
