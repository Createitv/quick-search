namespace QuickSearch.Core;

public sealed class AppConfiguration
{
    public AppSettings Settings { get; init; } = new();

    public List<FolderMapping> Mappings { get; init; } = [];

    public FolderMapping? FindMapping(string alias)
    {
        var normalizedAlias = AliasNormalizer.Normalize(alias);

        return Mappings.FirstOrDefault(mapping =>
            string.Equals(
                mapping.NormalizedAlias,
                normalizedAlias,
                StringComparison.Ordinal));
    }

    public FolderMapping UpsertMapping(string alias, string folderPath)
    {
        var mapping = new FolderMapping(alias, folderPath);
        var existingIndex = Mappings.FindIndex(existing =>
            string.Equals(
                existing.NormalizedAlias,
                mapping.NormalizedAlias,
                StringComparison.Ordinal));

        if (existingIndex >= 0)
        {
            Mappings[existingIndex] = mapping;
        }
        else
        {
            Mappings.Add(mapping);
        }

        return mapping;
    }

    public IReadOnlyList<FolderMapping> GetMappingsForPath(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);

        return Mappings
            .Where(mapping => string.Equals(
                mapping.FolderPath,
                folderPath,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
