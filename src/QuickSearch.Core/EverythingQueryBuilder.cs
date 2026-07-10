namespace QuickSearch.Core;

public static class EverythingQueryBuilder
{
    public static string Build(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var value = query.Trim();
        if (!value.Contains('"'))
        {
            return $"folder: nowildcards:\"{value}\"";
        }

        var parts = value.Split('"');
        var encoded = new List<string>();
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 0)
            {
                encoded.Add($"\"{parts[index]}\"");
            }

            if (index < parts.Length - 1)
            {
                encoded.Add("#x22:");
            }
        }

        return $"folder: nowildcards:<{string.Join(' ', encoded)}>";
    }
}
