namespace QuickSearch.Core;

public sealed class MappingGroupEditorViewModel : ObservableObject
{
    private static readonly char[] Separators = [',', '，', ';', '；', '\r', '\n'];

    private string _keywordsText;
    private string _folderPath;

    public MappingGroupEditorViewModel()
        : this(string.Empty, string.Empty)
    {
    }

    public MappingGroupEditorViewModel(
        string keywordsText,
        string folderPath)
    {
        _keywordsText = keywordsText ?? string.Empty;
        _folderPath = folderPath ?? string.Empty;
    }

    public MappingGroupEditorViewModel(
        IEnumerable<string> keywords,
        string folderPath)
        : this(
            string.Join("， ", keywords ?? []),
            folderPath)
    {
    }

    public string KeywordsText
    {
        get => _keywordsText;
        set => SetProperty(ref _keywordsText, value ?? string.Empty);
    }

    public string FolderPath
    {
        get => _folderPath;
        set => SetProperty(ref _folderPath, value ?? string.Empty);
    }

    public IReadOnlyList<string> ParseKeywords()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keywords = new List<string>();
        foreach (var value in KeywordsText.Split(
                     Separators,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var keyword = value.Trim();
            if (keyword.Length == 0)
            {
                continue;
            }

            if (seen.Add(AliasNormalizer.Normalize(keyword)))
            {
                keywords.Add(keyword);
            }
        }

        return keywords;
    }

    public bool Matches(string filter)
    {
        var value = filter?.Trim() ?? string.Empty;
        return value.Length == 0
            || KeywordsText.Contains(value, StringComparison.OrdinalIgnoreCase)
            || FolderPath.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}

