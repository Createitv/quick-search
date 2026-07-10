namespace QuickSearch.Core;

public sealed class AppConfiguration
{
    private readonly List<FolderMapping> _mappings = [];

    public AppSettings Settings { get; set; } = new();

    public IReadOnlyList<FolderMapping> Mappings
    {
        get => _mappings.AsReadOnly();
        init
        {
            _mappings.Clear();
            foreach (var mapping in value ?? [])
            {
                UpsertMapping(mapping.Alias, mapping.FolderPath);
            }
        }
    }

    public FolderMapping? FindMapping(string alias)
    {
        var normalizedAlias = AliasNormalizer.Normalize(alias);

        return _mappings.FirstOrDefault(mapping =>
            string.Equals(
                mapping.NormalizedAlias,
                normalizedAlias,
                StringComparison.Ordinal));
    }

    public FolderMapping UpsertMapping(string alias, string folderPath)
    {
        var mapping = new FolderMapping(alias, folderPath);
        var existingIndex = _mappings.FindIndex(existing =>
            string.Equals(
                existing.NormalizedAlias,
                mapping.NormalizedAlias,
                StringComparison.Ordinal));

        if (existingIndex >= 0)
        {
            _mappings[existingIndex] = mapping;
        }
        else
        {
            _mappings.Add(mapping);
        }

        return mapping;
    }

    public bool RemoveMapping(string alias)
    {
        var normalizedAlias = AliasNormalizer.Normalize(alias);
        return _mappings.RemoveAll(mapping => string.Equals(
            mapping.NormalizedAlias,
            normalizedAlias,
            StringComparison.Ordinal)) > 0;
    }

    public IReadOnlyList<FolderMapping> GetMappingsForPath(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);

        return _mappings
            .Where(mapping => string.Equals(
                mapping.FolderPath,
                folderPath,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
