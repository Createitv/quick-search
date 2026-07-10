using System.Diagnostics;
using System.IO;

namespace QuickSearch.Windows;

public sealed class ExplorerFolderOpener
{
    public void Open(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException(folderPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { folderPath },
            UseShellExecute = true
        });
    }
}
