namespace QuickSearch.Core;

public interface IFolderSearch
{
    Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
