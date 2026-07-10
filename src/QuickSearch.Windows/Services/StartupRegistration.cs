using Microsoft.Win32;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public sealed class StartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickSearch";

    private readonly string? _executablePath;

    public StartupRegistration(string? executablePath = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath;
    }

    public PlatformOperationResult SetEnabled(bool enabled) =>
        PlatformBoundary.Capture(
            () =>
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (enabled)
                {
                    var executablePath = _executablePath
                        ?? throw new InvalidOperationException("无法确定应用程序路径。");
                    key.SetValue(ValueName, StartupCommand.Build(executablePath));
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            },
            "无法更新开机启动设置");
}
