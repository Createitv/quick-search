using System.IO;
using System.Windows;
using System.Windows.Controls;
using QuickSearch.Core;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace QuickSearch.Windows;

public partial class ShortcutEditorDialog : Window
{
    public ShortcutEditorDialog(string targetFolderPath, FolderRule? shortcut = null)
    {
        TargetFolderPath = targetFolderPath;
        InitializeComponent();
        if (shortcut is not null)
        {
            Title = "编辑快捷导航";
            HeadingText.Text = "编辑快捷导航";
            DescriptionText.Text = "修改名称或关联的真实文件夹";
            ConfirmIcon.Symbol = SymbolRegular.Edit20;
            ConfirmText.Text = "保存修改";
            TitleBox.Text = shortcut.DisplayTitle;
            PathBox.Text = shortcut.FolderPath;
        }

        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
            Validate();
        };
    }

    public string TargetFolderPath { get; }

    public string NavigationTitle => TitleBox.Text.Trim();

    public string FolderPath => PathBox.Text.Trim();

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择快捷导航对应的真实文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(FolderPath))
        {
            dialog.SelectedPath = FolderPath;
        }

        if (dialog.ShowDialog() != FormsDialogResult.OK)
        {
            return;
        }

        PathBox.Text = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            TitleBox.Text = new DirectoryInfo(dialog.SelectedPath).Name;
        }
    }

    private void Field_TextChanged(object sender, TextChangedEventArgs e) => Validate();

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (Validate())
        {
            DialogResult = true;
        }
    }

    private bool Validate()
    {
        if (ConfirmButton is null || ValidationText is null
            || TitleBox is null || PathBox is null)
        {
            return false;
        }

        string message;
        var valid = false;
        if (NavigationTitle.Length == 0)
        {
            message = "请输入快捷导航名称。";
        }
        else if (FolderPath.Length == 0)
        {
            message = "请选择或输入真实文件夹路径。";
        }
        else
        {
            valid = true;
            message = Directory.Exists(FolderPath)
                ? string.Empty
                : "这个路径当前无法访问，仍可保存并稍后使用。";
        }

        ConfirmButton.IsEnabled = valid;
        ValidationText.Text = message;
        ValidationText.SetResourceReference(
            TextBlock.ForegroundProperty,
            valid ? "SlateBrush" : "ErrorBrush");
        return valid;
    }
}
