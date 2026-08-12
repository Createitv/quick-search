using System.IO;
using System.Windows;
using System.Windows.Controls;
using QuickSearch.Core;
using Wpf.Ui.Controls;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using FormsDialogResult = System.Windows.Forms.DialogResult;

namespace QuickSearch.Windows;

public partial class ShortcutEditorDialog : Window
{
    public ShortcutEditorDialog(string targetFolderPath, FolderRule? shortcut = null)
    {
        TargetFolderPath = targetFolderPath;
        InitializeComponent();
        if (shortcut is not null)
        {
            Title = "编辑快捷方式";
            HeadingText.Text = "编辑快捷方式";
            DescriptionText.Text = "修改名称、搜索关键词或真实文件夹路径";
            ConfirmIcon.Symbol = SymbolRegular.Edit20;
            ConfirmText.Text = "保存修改";
            TitleBox.Text = shortcut.Title;
            KeywordsBox.Text = string.Join(", ", shortcut.Aliases);
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

    public string ShortcutTitle => TitleBox.Text.Trim();

    public string FolderPath => PathBox.Text.Trim();

    public IReadOnlyList<string> Keywords => KeywordsBox.Text
        .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
        .Select(keyword => keyword.Trim())
        .Where(keyword => keyword.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择快捷方式对应的真实文件夹",
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
            || KeywordsBox is null || PathBox is null)
        {
            return false;
        }

        string message;
        var valid = false;
        if (Keywords.Count == 0)
        {
            message = "至少输入一个搜索关键词。";
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
