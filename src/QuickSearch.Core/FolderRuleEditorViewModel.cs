namespace QuickSearch.Core;

public sealed class FolderRuleEditorViewModel : ObservableObject
{
    private string _title;
    private string _keywords;
    private string _folderPath;
    private Guid _navigationFolderId;

    public FolderRuleEditorViewModel(FolderRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Id = rule.Id;
        _title = rule.Title;
        _keywords = string.Join(", ", rule.Aliases);
        _folderPath = rule.FolderPath;
        _navigationFolderId = rule.NavigationFolderId;
    }

    public Guid Id { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    public string Keywords
    {
        get => _keywords;
        set => SetProperty(ref _keywords, value ?? string.Empty);
    }

    public string FolderPath
    {
        get => _folderPath;
        set => SetProperty(ref _folderPath, value ?? string.Empty);
    }

    public Guid NavigationFolderId
    {
        get => _navigationFolderId;
        set => SetProperty(ref _navigationFolderId, value);
    }

    public IReadOnlyList<string> ParseKeywords() => Keywords
        .Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(keyword => keyword.Trim())
        .Where(keyword => keyword.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
