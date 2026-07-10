namespace QuickSearch.Core;

public sealed class AppConfiguration
{
    private readonly List<FolderMapping> _mappings = [];
    private readonly TimeProvider _timeProvider;

    public AppConfiguration()
        : this(TimeProvider.System)
    {
    }

    public AppConfiguration(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public AppSettings Settings { get; set; } = new();

    public IReadOnlyList<FolderMapping> Mappings
    {
        get => _mappings.AsReadOnly();
        init
        {
            _mappings.Clear();
            var mappingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in value ?? [])
            {
                if (mappingKeys.Add(GetMappingKey(
                        mapping.NormalizedAlias,
                        mapping.FolderPath)))
                {
                    _mappings.Add(mapping);
                }
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

    public IReadOnlyList<FolderMapping> FindMappings(string alias)
    {
        var normalizedAlias = AliasNormalizer.Normalize(alias);

        return _mappings
            .Where(mapping => string.Equals(
                mapping.NormalizedAlias,
                normalizedAlias,
                StringComparison.Ordinal))
            .ToArray();
    }

    public FolderMapping AddMapping(string alias, string folderPath)
    {
        var existing = _mappings.FirstOrDefault(mapping =>
            IsSameMapping(mapping, alias, folderPath));
        if (existing is not null)
        {
            return existing;
        }

        var utcNow = _timeProvider.GetUtcNow().ToUniversalTime();
        var mapping = new FolderMapping(
            alias,
            folderPath,
            utcNow,
            utcNow,
            null);
        _mappings.Add(mapping);
        return mapping;
    }

    public FolderMapping UpsertMapping(string alias, string folderPath)
    {
        var normalizedAlias = AliasNormalizer.Normalize(alias);
        var existingIndex = _mappings.FindIndex(existing =>
            string.Equals(
                existing.NormalizedAlias,
                normalizedAlias,
                StringComparison.Ordinal));
        var utcNow = _timeProvider.GetUtcNow().ToUniversalTime();
        FolderMapping mapping;

        if (existingIndex >= 0)
        {
            var existing = _mappings[existingIndex];
            mapping = new FolderMapping(
                alias,
                folderPath,
                existing.CreatedAtUtc,
                utcNow,
                existing.LastUsedAtUtc);
            _mappings[existingIndex] = mapping;
        }
        else
        {
            mapping = new FolderMapping(
                alias,
                folderPath,
                utcNow,
                utcNow,
                null);
            _mappings.Add(mapping);
        }

        return mapping;
    }

    public FolderMapping? MarkMappingUsed(string alias)
    {
        var normalizedAlias = AliasNormalizer.Normalize(alias);
        var existingIndex = _mappings.FindIndex(existing =>
            string.Equals(
                existing.NormalizedAlias,
                normalizedAlias,
                StringComparison.Ordinal));

        if (existingIndex < 0)
        {
            return null;
        }

        var mapping = _mappings[existingIndex] with
        {
            LastUsedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime()
        };
        _mappings[existingIndex] = mapping;
        return mapping;
    }

    public FolderMapping? MarkMappingUsed(string alias, string folderPath)
    {
        var existingIndex = _mappings.FindIndex(mapping =>
            IsSameMapping(mapping, alias, folderPath));
        if (existingIndex < 0)
        {
            return null;
        }

        var mapping = _mappings[existingIndex] with
        {
            LastUsedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime()
        };
        _mappings[existingIndex] = mapping;
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

    public bool RemoveMapping(string alias, string folderPath) =>
        _mappings.RemoveAll(mapping =>
            IsSameMapping(mapping, alias, folderPath)) > 0;

    public FolderMapping UpdateMapping(
        string originalAlias,
        string alias,
        string folderPath)
    {
        var original = FindMapping(originalAlias);
        if (original is null)
        {
            return UpsertMapping(alias, folderPath);
        }

        return UpdateMapping(
            originalAlias,
            original.FolderPath,
            alias,
            folderPath);
    }

    public FolderMapping UpdateMapping(
        string originalAlias,
        string originalFolderPath,
        string alias,
        string folderPath)
    {
        var originalIndex = _mappings.FindIndex(mapping =>
            IsSameMapping(mapping, originalAlias, originalFolderPath));
        if (originalIndex < 0)
        {
            return AddMapping(alias, folderPath);
        }

        var conflictingIndex = _mappings.FindIndex(mapping =>
            IsSameMapping(mapping, alias, folderPath));
        if (conflictingIndex >= 0 && conflictingIndex != originalIndex)
        {
            throw new InvalidOperationException($"映射已存在：{alias} → {folderPath}");
        }

        var original = _mappings[originalIndex];
        var updated = new FolderMapping(
            alias,
            folderPath,
            original.CreatedAtUtc,
            _timeProvider.GetUtcNow().ToUniversalTime(),
            original.LastUsedAtUtc);
        _mappings[originalIndex] = updated;
        return updated;
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

    public AppConfiguration Clone() => new(_timeProvider)
    {
        Settings = Settings with { },
        Mappings = _mappings.Select(mapping => mapping with { }).ToArray()
    };

    public void ReplaceWith(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Settings = configuration.Settings with { };
        _mappings.Clear();
        _mappings.AddRange(configuration._mappings.Select(mapping => mapping with { }));
    }

    private static bool IsSameMapping(
        FolderMapping mapping,
        string alias,
        string folderPath) =>
        string.Equals(
            mapping.NormalizedAlias,
            AliasNormalizer.Normalize(alias),
            StringComparison.Ordinal)
        && string.Equals(
            mapping.FolderPath.Trim(),
            folderPath.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static string GetMappingKey(string normalizedAlias, string folderPath) =>
        $"{normalizedAlias}\u001F{folderPath.Trim()}";
}
