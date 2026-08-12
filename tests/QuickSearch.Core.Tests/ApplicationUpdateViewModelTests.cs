namespace QuickSearch.Core.Tests;

public sealed class ApplicationUpdateViewModelTests
{
    [Fact]
    public async Task CheckNowAsync_ExposesAvailableReleaseWhenLatestIsNewer()
    {
        var service = new FakeUpdateService
        {
            CheckResult = ApplicationUpdateCheck.Available(CreateRelease("0.0.3"))
        };
        var viewModel = new ApplicationUpdateViewModel(service, "0.0.2");

        await viewModel.CheckNowAsync();

        Assert.Equal(ApplicationUpdateState.Available, viewModel.State);
        Assert.Equal("0.0.3", viewModel.LatestVersion);
        Assert.True(viewModel.CanDownloadAndInstall);
    }

    [Fact]
    public async Task CheckNowAsync_ReportsCurrentWhenReleaseIsNotNewer()
    {
        var service = new FakeUpdateService
        {
            CheckResult = ApplicationUpdateCheck.Current("0.0.2")
        };
        var viewModel = new ApplicationUpdateViewModel(service, "0.0.2");

        await viewModel.CheckNowAsync();

        Assert.Equal(ApplicationUpdateState.Current, viewModel.State);
        Assert.Equal("已是最新版本。", viewModel.StatusText);
        Assert.False(viewModel.CanDownloadAndInstall);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_DoesNotLaunchWhenChecksumVerificationFails()
    {
        var service = new FakeUpdateService
        {
            CheckResult = ApplicationUpdateCheck.Available(CreateRelease("0.0.3")),
            DownloadException = new InvalidDataException("安装包校验失败。")
        };
        var viewModel = new ApplicationUpdateViewModel(service, "0.0.2");
        await viewModel.CheckNowAsync();

        await viewModel.DownloadAndInstallAsync();

        Assert.Equal(0, service.LaunchCalls);
        Assert.Equal(ApplicationUpdateState.Failed, viewModel.State);
        Assert.Contains("校验失败", viewModel.StatusText);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_RequestsShutdownOnlyAfterInstallerLaunches()
    {
        var service = new FakeUpdateService
        {
            CheckResult = ApplicationUpdateCheck.Available(CreateRelease("0.0.3")),
            DownloadedPath = @"C:\updates\QuickSearch-Setup-v0.0.3.exe"
        };
        var viewModel = new ApplicationUpdateViewModel(service, "0.0.2");
        var shutdownRequests = 0;
        viewModel.ShutdownRequested += (_, _) => shutdownRequests++;
        await viewModel.CheckNowAsync();

        await viewModel.DownloadAndInstallAsync();

        Assert.Equal(1, service.LaunchCalls);
        Assert.Equal(1, shutdownRequests);
        Assert.Equal(ApplicationUpdateState.Installing, viewModel.State);
    }

    private static ApplicationRelease CreateRelease(string version) => new(
        version,
        new Uri($"https://github.com/Createitv/quick-search/releases/tag/v{version}"),
        new Uri($"https://github.com/Createitv/quick-search/releases/download/v{version}/QuickSearch-Setup-v{version}.exe"),
        new Uri($"https://github.com/Createitv/quick-search/releases/download/v{version}/QuickSearch-Setup-v{version}.exe.sha256"));

    private sealed class FakeUpdateService : IApplicationUpdateService
    {
        public ApplicationUpdateCheck CheckResult { get; init; } =
            ApplicationUpdateCheck.Current("0.0.2");

        public Exception? DownloadException { get; init; }

        public string DownloadedPath { get; init; } = string.Empty;

        public int LaunchCalls { get; private set; }

        public Task<ApplicationUpdateCheck> CheckAsync(
            AppReleaseVersion installedVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CheckResult);

        public Task<string> DownloadAndVerifyAsync(
            ApplicationRelease release,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (DownloadException is not null)
            {
                return Task.FromException<string>(DownloadException);
            }

            progress?.Report(1);
            return Task.FromResult(DownloadedPath);
        }

        public PlatformOperationResult LaunchInstaller(string installerPath)
        {
            LaunchCalls++;
            return PlatformOperationResult.Succeeded();
        }
    }
}
