using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace QuickSearch.Windows;

public partial class FolderEditorDialog : Window
{
    public FolderEditorDialog(
        string title,
        string description,
        string confirmText,
        string initialName = "")
    {
        InitializeComponent();
        Title = title;
        DialogTitle.Text = title;
        DialogDescription.Text = description;
        ConfirmButtonText.Text = confirmText;
        FolderIcon.Symbol = string.IsNullOrWhiteSpace(initialName)
            ? SymbolRegular.FolderAdd24
            : SymbolRegular.Edit24;
        NameBox.Text = initialName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        Validate();
    }

    public string FolderName => NameBox.Text.Trim();

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => Validate();

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate())
        {
            return;
        }

        DialogResult = true;
    }

    private bool Validate()
    {
        if (ConfirmButton is null || ValidationText is null || NameBox is null)
        {
            return false;
        }

        var valid = !string.IsNullOrWhiteSpace(NameBox.Text);
        ConfirmButton.IsEnabled = valid;
        ValidationText.Text = valid ? string.Empty : "请输入文件夹名称。";
        return valid;
    }
}
