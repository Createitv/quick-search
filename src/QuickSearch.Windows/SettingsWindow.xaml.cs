using System.ComponentModel;
using System.Windows;
using QuickSearch.Core;
using MessageBox = System.Windows.MessageBox;

namespace QuickSearch.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _allowClose;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.HideRequested += ViewModel_HideRequested;
    }

    public void AllowApplicationExit()
    {
        if (_allowClose)
        {
            return;
        }

        _allowClose = true;
        _viewModel.HideRequested -= ViewModel_HideRequested;
        if (IsLoaded)
        {
            Close();
        }
    }

    private void ViewModel_HideRequested(object? sender, EventArgs e) => Hide();

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _viewModel.Organizer.SelectedFolder;
        if (folder is null)
        {
            return;
        }

        if (folder.Id == _viewModel.Organizer.UncategorizedFolderId)
        {
            MessageBox.Show("“未分类”不能删除。", "删除分类", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var choice = MessageBox.Show(
            "选择“是”：删除分类，子分类和规则上移一层。\n\n选择“否”：删除整棵分类树及其规则。",
            "删除分类",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }

        try
        {
            _viewModel.Organizer.DeleteFolder(
                folder.Id,
                choice == MessageBoxResult.Yes
                    ? FolderDeletionMode.MoveContentsToParent
                    : FolderDeletionMode.DeleteSubtree);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法删除分类", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _viewModel.Cancel();
    }
}
