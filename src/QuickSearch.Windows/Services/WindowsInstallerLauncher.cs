using System.Diagnostics;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public sealed class WindowsInstallerLauncher : IInstallerLauncher
{
    public PlatformOperationResult Launch(string installerPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            {
                return PlatformOperationResult.Failed("找不到已下载的安装包。");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /UPDATED",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)!
            });
            return PlatformOperationResult.Succeeded();
        }
        catch (Exception exception)
        {
            return PlatformOperationResult.Failed($"无法启动安装程序：{exception.Message}");
        }
    }
}
