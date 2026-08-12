using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;
using QuickSearch.Core;

namespace QuickSearch.Windows;

internal sealed class EverythingInstallationManager : IEverythingInstallationManager
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IEverythingNative _native;
    private readonly string _applicationDirectory;

    public EverythingInstallationManager(
        IEverythingNative native,
        string applicationDirectory)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        _native = native;
        _applicationDirectory = applicationDirectory;
    }

    public async Task<EverythingBootstrapResult> CheckAndStartAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsDatabaseLoaded())
        {
            return Ready("Everything 已就绪。");
        }

        var executablePath = FindInstalledExecutable();
        if (executablePath is null)
        {
            return new EverythingBootstrapResult(
                EverythingBootstrapState.NotInstalled,
                "需要安装 Everything 才能搜索本地文件夹。");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(
                executablePath,
                "-startup")
            {
                UseShellExecute = true
            });
            if (process is null)
            {
                return StartFailed("无法启动 Everything，请重试或检查安装状态。");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return StartFailed($"无法启动 Everything：{exception.Message}");
        }

        return await WaitUntilReadyAsync(cancellationToken);
    }

    public async Task<EverythingBootstrapResult> InstallAndStartAsync(
        CancellationToken cancellationToken = default)
    {
        var dependencies = Path.Combine(_applicationDirectory, "dependencies");
        var installerPath = Path.Combine(dependencies, "Everything-Setup.exe");
        var hashPath = Path.Combine(dependencies, "Everything-Setup.sha256");
        if (!File.Exists(installerPath) || !File.Exists(hashPath))
        {
            return new EverythingBootstrapResult(
                EverythingBootstrapState.InstallerMissing,
                "QuickSearch 安装不完整，缺少 Everything 官方安装程序，请重新安装 QuickSearch。");
        }

        if (!await HasExpectedHashAsync(installerPath, hashPath, cancellationToken))
        {
            return new EverythingBootstrapResult(
                EverythingBootstrapState.InstallerInvalid,
                "Everything 安装文件校验失败。为保护您的电脑，QuickSearch 没有运行该文件。");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true
            });
            if (process is null)
            {
                return Failed("无法打开 Everything 官方安装程序，请重试。");
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed($"Everything 安装程序未能完成：{exception.Message}");
        }

        if (FindInstalledExecutable() is null && !IsDatabaseLoaded())
        {
            return Failed("未检测到 Everything，安装可能已取消。您可以重试安装。");
        }

        return await CheckAndStartAsync(cancellationToken);
    }

    private async Task<EverythingBootstrapResult> WaitUntilReadyAsync(
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDatabaseLoaded())
            {
                return Ready("Everything 已在后台启动，可以开始搜索。");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return StartFailed("Everything 已启动，但索引尚未就绪。请稍后重试。");
    }

    private bool IsDatabaseLoaded()
    {
        try
        {
            return _native.IsDatabaseLoaded();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private string? FindInstalledExecutable()
    {
        var registeredPaths = ReadRegisteredPaths();
        var candidates = EverythingInstallationPaths.BuildCandidates(
            registeredPaths,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string?> ReadRegisteredPaths()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? baseKey = null;
                RegistryKey? uninstallKey = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    uninstallKey = baseKey.OpenSubKey(UninstallKey);
                    if (uninstallKey is null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        using var productKey = uninstallKey.OpenSubKey(subKeyName);
                        var displayName = productKey?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName) ||
                            !displayName.StartsWith("Everything", StringComparison.OrdinalIgnoreCase) ||
                            displayName.Contains("Lite", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var displayIcon = productKey?.GetValue("DisplayIcon") as string;
                        if (!string.IsNullOrWhiteSpace(displayIcon))
                        {
                            yield return displayIcon;
                        }

                        var installLocation = productKey?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(installLocation))
                        {
                            yield return Path.Combine(installLocation, "Everything.exe");
                        }
                    }
                }
                finally
                {
                    uninstallKey?.Dispose();
                    baseKey?.Dispose();
                }
            }
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string installerPath,
        string hashPath,
        CancellationToken cancellationToken)
    {
        var expected = (await File.ReadAllTextAsync(hashPath, cancellationToken)).Trim();
        if (expected.Length != 64 || expected.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        await using var stream = File.OpenRead(installerPath);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(
            Convert.ToHexString(actual),
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static EverythingBootstrapResult Ready(string message) =>
        new(EverythingBootstrapState.Ready, message);

    private static EverythingBootstrapResult Failed(string message) =>
        new(EverythingBootstrapState.Failed, message);

    private static EverythingBootstrapResult StartFailed(string message) =>
        new(EverythingBootstrapState.StartFailed, message);
}
