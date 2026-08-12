using System.Diagnostics;
using System.IO;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public sealed class ShellPathOpener : IPathOpener
{
    public PlatformOperationResult Open(string path) => PlatformBoundary.Capture(
        () =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("路径不存在。", path);
            }

            if (Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            }) is null)
            {
                throw new InvalidOperationException("Windows 未能打开此项目。");
            }
        },
        "无法打开项目");
}
