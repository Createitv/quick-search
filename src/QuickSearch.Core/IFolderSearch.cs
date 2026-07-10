namespace QuickSearch.Core;

public interface IFolderSearch
{
    EverythingHealth Health { get; }

    string? FailureMessage { get; }

    Task ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
