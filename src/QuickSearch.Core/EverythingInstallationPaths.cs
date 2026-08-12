namespace QuickSearch.Core;

public static class EverythingInstallationPaths
{
    public static string? NormalizeExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.EndsWith(",0", StringComparison.Ordinal))
        {
            normalized = normalized[..^2].Trim();
        }

        normalized = normalized.Trim('"').Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    public static IReadOnlyList<string> BuildCandidates(
        IEnumerable<string?> registeredPaths,
        string? programFiles,
        string? programFilesX86)
    {
        ArgumentNullException.ThrowIfNull(registeredPaths);
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in registeredPaths)
        {
            Add(NormalizeExecutablePath(value));
        }

        AddProgramFilesCandidate(programFiles);
        AddProgramFilesCandidate(programFilesX86);
        return candidates;

        void AddProgramFilesCandidate(string? root)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                Add($"{root.TrimEnd('\\', '/')}\\Everything\\Everything.exe");
            }
        }

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
            {
                candidates.Add(path);
            }
        }
    }
}
