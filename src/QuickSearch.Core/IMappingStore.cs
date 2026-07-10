namespace QuickSearch.Core;

public interface IMappingStore
{
    Task<AppConfiguration> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken = default);
}
