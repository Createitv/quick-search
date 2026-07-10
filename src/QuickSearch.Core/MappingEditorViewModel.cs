namespace QuickSearch.Core;

public sealed class MappingEditorViewModel : ObservableObject
{
    private string _alias;
    private string _folderPath;

    internal MappingEditorViewModel(FolderMapping mapping)
    {
        OriginalAlias = mapping.Alias;
        OriginalFolderPath = mapping.FolderPath;
        _alias = mapping.Alias;
        _folderPath = mapping.FolderPath;
    }

    internal MappingEditorViewModel()
    {
        _alias = string.Empty;
        _folderPath = string.Empty;
    }

    internal string? OriginalAlias { get; }

    internal string? OriginalFolderPath { get; }

    public string Alias
    {
        get => _alias;
        set => SetProperty(ref _alias, value ?? string.Empty);
    }

    public string FolderPath
    {
        get => _folderPath;
        set => SetProperty(ref _folderPath, value ?? string.Empty);
    }
}
