namespace QuickSearch.Core;

public static class FolderSearchRanker
{
    private const int ResultLimit = 20;

    public static IReadOnlyList<FolderSearchResult> Rank(
        IEnumerable<FolderSearchResult> candidates,
        string query)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(query);

        var searchTerm = query.Trim();
        if (searchTerm.Length == 0)
        {
            return [];
        }

        return candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Rank = GetMatchRank(candidate.Name, searchTerm)
            })
            .Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank)
            .ThenBy(
                match => match.Candidate.FullPath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                match => match.Candidate.FullPath,
                StringComparer.Ordinal)
            .Take(ResultLimit)
            .Select(match => match.Candidate)
            .ToArray();
    }

    private static int GetMatchRank(string name, string searchTerm)
    {
        if (string.Equals(name, searchTerm, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
            ? 2
            : -1;
    }
}
