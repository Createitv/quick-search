using System.Text.Json.Serialization;

namespace QuickSearch.Core;

public sealed record FolderRule(
    Guid Id,
    string Title,
    IReadOnlyList<string> Aliases,
    string FolderPath,
    Guid NavigationFolderId,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastUsedAtUtc)
{
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title.Trim();
            }

            var trimmedPath = FolderPath.Trim().TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar,
                '\\');
            var separatorIndex = trimmedPath.LastIndexOfAny(['\\', '/']);
            return separatorIndex >= 0 && separatorIndex < trimmedPath.Length - 1
                ? trimmedPath[(separatorIndex + 1)..]
                : trimmedPath;
        }
    }

    public bool MatchesAlias(string alias)
    {
        var normalized = AliasNormalizer.Normalize(alias);
        return Aliases.Any(candidate => string.Equals(
            AliasNormalizer.Normalize(candidate),
            normalized,
            StringComparison.Ordinal));
    }
}
