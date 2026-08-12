namespace QuickSearch.Core;

public sealed class NavigationFolderNodeViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;

    public NavigationFolderNodeViewModel(
        NavigationFolder folder,
        IReadOnlyList<NavigationFolderNodeViewModel> children,
        bool isPinned)
    {
        Folder = folder;
        Children = children;
        IsPinned = isPinned;
    }

    public NavigationFolder Folder { get; }

    public Guid Id => Folder.Id;

    public string Name => Folder.Name;

    public IReadOnlyList<NavigationFolderNodeViewModel> Children { get; }

    public bool IsPinned { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
