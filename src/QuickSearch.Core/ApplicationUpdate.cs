namespace QuickSearch.Core;

public sealed record ApplicationRelease(
    string Version,
    Uri ReleasePageUri,
    Uri InstallerUri,
    Uri ChecksumUri);

public sealed record ApplicationUpdateCheck(
    bool IsUpdateAvailable,
    string LatestVersion,
    ApplicationRelease? Release)
{
    public static ApplicationUpdateCheck Available(ApplicationRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new ApplicationUpdateCheck(true, release.Version, release);
    }

    public static ApplicationUpdateCheck Current(string latestVersion) =>
        new(false, latestVersion, null);
}

public interface IApplicationUpdateService
{
    Task<ApplicationUpdateCheck> CheckAsync(
        AppReleaseVersion installedVersion,
        CancellationToken cancellationToken = default);

    Task<string> DownloadAndVerifyAsync(
        ApplicationRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    PlatformOperationResult LaunchInstaller(string installerPath);
}

public static class UpdateCheckPolicy
{
    public static bool ShouldCheckAutomatically(bool enabled) => enabled;
}
