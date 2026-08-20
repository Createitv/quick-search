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
    public async Task CheckNowAsync_WhenConnectionFails_ExplainsNetworkRecovery()
    {
        var service = new FakeUpdateService
        {
            CheckException = new HttpRequestException("connection refused")
        };
        var viewModel = new ApplicationUpdateViewModel(service, "0.0.2");

        await viewModel.CheckNowAsync();

        Assert.Equal(ApplicationUpdateState.Failed, viewModel.State);
        Assert.Contains("更新服务器", viewModel.StatusText);
        Assert.Contains("connection refused", viewModel.StatusText);
    }

    [Fact]
    public async Task CheckNowAsync_WhenHttpClientTimesOut_DoesNotReportUserCancellation()
    {
        var service = new FakeUpdateService
        {
            CheckException = new TaskCanceledException("request timed out")
        };
        var viewModel = new ApplicationUpdateViewModel(service, "0.0.2");

        await viewModel.CheckNowAsync();

        Assert.Equal(ApplicationUpdateState.Failed, viewModel.State);
        Assert.Contains("更新服务器", viewModel.StatusText);
        Assert.DoesNotContain("已取消", viewModel.StatusText);
    }

    [Fact]
    public async Task CheckNowAsync_WhenCallerCancels_ReportsCancellation()
    {
        var service = new FakeUpdateService
        {
            CheckException = new TaskCanceledException("cancelled")
        };
        var viewModel = new ApplicationUpdateViewModel(service, "0.0.2");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await viewModel.CheckNowAsync(cancellation.Token);

        Assert.Equal(ApplicationUpdateState.Idle, viewModel.State);
        Assert.Contains("已取消", viewModel.StatusText);
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
    public async Task DownloadAndInstallAsync_WhenConnectionFails_ExplainsNetworkRecovery()
    {
        var service = new FakeUpdateService
        {
            CheckResult = ApplicationUpdateCheck.Available(CreateRelease("0.0.3")),
            DownloadException = new HttpRequestException("connection refused")
        };
        var viewModel = new ApplicationUpdateViewModel(service, "0.0.2");
        await viewModel.CheckNowAsync();

        await viewModel.DownloadAndInstallAsync();

        Assert.Equal(0, service.LaunchCalls);
        Assert.Equal(ApplicationUpdateState.Failed, viewModel.State);
        Assert.Contains("更新服务器", viewModel.StatusText);
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

        public Exception? CheckException { get; init; }

        public Exception? DownloadException { get; init; }

        public string DownloadedPath { get; init; } = string.Empty;

        public int LaunchCalls { get; private set; }

        public Task<ApplicationUpdateCheck> CheckAsync(
            AppReleaseVersion installedVersion,
            CancellationToken cancellationToken = default) =>
            CheckException is null
                ? Task.FromResult(CheckResult)
                : Task.FromException<ApplicationUpdateCheck>(CheckException);

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
