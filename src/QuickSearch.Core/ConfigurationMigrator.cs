using System.Security.Cryptography;
using System.Text;

namespace QuickSearch.Core;

public static class ConfigurationMigrator
{
    public static AppConfiguration FromLegacy(
        AppSettings settings,
        IReadOnlyList<FolderMapping> mappings,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(mappings);
        var clock = timeProvider ?? TimeProvider.System;
        var utcNow = clock.GetUtcNow().ToUniversalTime();
        var uncategorized = new NavigationFolder(
            AppConfiguration.DefaultUncategorizedFolderId,
            null,
            "未分类",
            0,
            utcNow);
        var rules = mappings
            .GroupBy(
                mapping => mapping.FolderPath.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => CreateRule(group.ToArray(), index, utcNow))
            .ToArray();

        return new AppConfiguration(clock)
        {
            Settings = settings with { SchemaVersion = 2 },
            NavigationFolders = [uncategorized],
            Rules = rules,
            PinnedFolderIds = []
        };
    }

    private static FolderRule CreateRule(
        IReadOnlyList<FolderMapping> mappings,
        int sortOrder,
        DateTimeOffset utcNow)
    {
        var first = mappings[0];
        var aliases = new List<string>();
        var seenAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            var alias = mapping.Alias.Trim();
            if (alias.Length > 0 && seenAliases.Add(AliasNormalizer.Normalize(alias)))
            {
                aliases.Add(alias);
            }
        }

        var createdValues = mappings
            .Select(mapping => mapping.CreatedAtUtc)
            .Where(value => value != default)
            .ToArray();
        var updatedValues = mappings
            .Select(mapping => mapping.UpdatedAtUtc)
            .Where(value => value != default)
            .ToArray();
        var lastUsedValues = mappings
            .Select(mapping => mapping.LastUsedAtUtc)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        var path = first.FolderPath.Trim();

        return new FolderRule(
            CreateDeterministicId($"rule:{path.ToUpperInvariant()}"),
            string.Empty,
            aliases,
            path,
            AppConfiguration.DefaultUncategorizedFolderId,
            sortOrder,
            createdValues.Length == 0 ? utcNow : createdValues.Min(),
            updatedValues.Length == 0 ? utcNow : updatedValues.Max(),
            lastUsedValues.Length == 0 ? null : lastUsedValues.Max());
    }

    private static Guid CreateDeterministicId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
