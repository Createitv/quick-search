using System.Text.Json.Serialization;

namespace QuickSearch.Core;

public sealed record FolderMapping
{
    public FolderMapping(string alias, string folderPath)
        : this(alias, folderPath, default, default, null)
    {
    }

    [JsonConstructor]
    public FolderMapping(
        string alias,
        string folderPath,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? lastUsedAtUtc)
    {
        Alias = alias;
        FolderPath = folderPath;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        LastUsedAtUtc = lastUsedAtUtc?.ToUniversalTime();
    }

    public string Alias { get; init; }

    public string FolderPath { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? LastUsedAtUtc { get; init; }

    [JsonIgnore]
    public string NormalizedAlias => AliasNormalizer.Normalize(Alias);
}
