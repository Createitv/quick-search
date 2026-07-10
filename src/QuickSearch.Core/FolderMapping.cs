using System.Text.Json.Serialization;

namespace QuickSearch.Core;

public sealed record FolderMapping(string Alias, string FolderPath)
{
    [JsonIgnore]
    public string NormalizedAlias => AliasNormalizer.Normalize(Alias);
}
