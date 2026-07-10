using System.Diagnostics;
using System.IO;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public sealed class ExplorerFolderOpener : IFolderOpener
{
    public PlatformOperationResult Open(string folderPath) =>
        PlatformBoundary.Capture(
            () =>
            {
                if (!Directory.Exists(folderPath))
                {
                    throw new DirectoryNotFoundException(folderPath);
                }

                if (Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    ArgumentList = { folderPath },
                    UseShellExecute = true
                }) is null)
                {
                    throw new InvalidOperationException("Explorer 未能启动。");
                }
            },
            "无法打开文件夹");
}
