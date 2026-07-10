namespace QuickSearch.Core;

public static class AliasNormalizer
{
    public static string Normalize(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);

        return string.Join(
                ' ',
                alias.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }
}
