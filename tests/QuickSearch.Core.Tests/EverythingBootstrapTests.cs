namespace QuickSearch.Core.Tests;

public sealed class EverythingBootstrapTests
{
    [Fact]
    public async Task InitializeAsync_WhenEverythingIsReady_EnablesSearch()
    {
        var manager = new FakeInstallationManager(
            new EverythingBootstrapResult(EverythingBootstrapState.Ready, "Everything 已就绪。"));
        var viewModel = new EverythingBootstrapViewModel(manager);

        await viewModel.InitializeAsync();

        Assert.Equal(EverythingBootstrapState.Ready, viewModel.State);
        Assert.True(viewModel.CanSearch);
        Assert.False(viewModel.IsInstallPanelVisible);
        Assert.Equal("Everything 已就绪。", viewModel.StatusText);
    }

    [Fact]
    public async Task InitializeAsync_WhenEverythingIsMissing_ShowsInstaller()
    {
        var manager = new FakeInstallationManager(
            new EverythingBootstrapResult(
                EverythingBootstrapState.NotInstalled,
                "需要安装 Everything。"));
        var viewModel = new EverythingBootstrapViewModel(manager);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsInstallPanelVisible);
        Assert.False(viewModel.CanSearch);
        Assert.Equal(EverythingBootstrapState.NotInstalled, viewModel.State);
    }

    [Fact]
    public async Task InitializeAsync_WhenInstalledEverythingCannotStart_DoesNotOfferInstaller()
    {
        var manager = new FakeInstallationManager(
            new EverythingBootstrapResult(
                EverythingBootstrapState.StartFailed,
                "无法启动 Everything。"));
        var viewModel = new EverythingBootstrapViewModel(manager);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsBootstrapPanelVisible);
        Assert.False(viewModel.IsInstallActionVisible);
        Assert.Equal("Everything 无法启动", viewModel.PanelTitle);
    }

    [Fact]
    public async Task InstallAsync_WhenInstallationSucceeds_EnablesSearch()
    {
        var manager = new FakeInstallationManager(
            checkResult: new EverythingBootstrapResult(
                EverythingBootstrapState.NotInstalled,
                "需要安装 Everything。"),
            installResult: new EverythingBootstrapResult(
                EverythingBootstrapState.Ready,
                "Everything 已安装并就绪。"));
        var viewModel = new EverythingBootstrapViewModel(manager);
        await viewModel.InitializeAsync();

        await viewModel.InstallAsync();

        Assert.Equal(EverythingBootstrapState.Ready, viewModel.State);
        Assert.True(viewModel.CanSearch);
        Assert.False(viewModel.IsInstallPanelVisible);
        Assert.Equal("Everything 已安装并就绪。", viewModel.StatusText);
    }

    [Fact]
    public async Task InstallAsync_WhenInstallationFails_RemainsRetryable()
    {
        var manager = new FakeInstallationManager(
            checkResult: new EverythingBootstrapResult(
                EverythingBootstrapState.NotInstalled,
                "需要安装 Everything。"),
            installResult: new EverythingBootstrapResult(
                EverythingBootstrapState.Failed,
                "未检测到 Everything，您可以重试安装。"));
        var viewModel = new EverythingBootstrapViewModel(manager);
        await viewModel.InitializeAsync();

        await viewModel.InstallAsync();

        Assert.Equal(EverythingBootstrapState.Failed, viewModel.State);
        Assert.True(viewModel.IsInstallPanelVisible);
        Assert.False(viewModel.CanSearch);
        Assert.True(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task InstallAsync_WhileInstallationIsRunning_DoesNotStartAnotherInstaller()
    {
        var completion = new TaskCompletionSource<EverythingBootstrapResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new FakeInstallationManager(
            new EverythingBootstrapResult(
                EverythingBootstrapState.NotInstalled,
                "需要安装 Everything。"),
            completion.Task);
        var viewModel = new EverythingBootstrapViewModel(manager);
        await viewModel.InitializeAsync();

        var first = viewModel.InstallAsync();
        var second = viewModel.InstallAsync();
        completion.SetResult(new EverythingBootstrapResult(
            EverythingBootstrapState.Ready,
            "Everything 已就绪。"));
        await Task.WhenAll(first, second);

        Assert.Equal(1, manager.InstallCalls);
        Assert.Equal(EverythingBootstrapState.Ready, viewModel.State);
    }

    private sealed class FakeInstallationManager : IEverythingInstallationManager
    {
        private readonly EverythingBootstrapResult _checkResult;
        private readonly Task<EverythingBootstrapResult> _installResult;

        public FakeInstallationManager(EverythingBootstrapResult checkResult)
            : this(
                checkResult,
                Task.FromResult(new EverythingBootstrapResult(
                    EverythingBootstrapState.Ready,
                    "Everything 已就绪。")))
        {
        }

        public FakeInstallationManager(
            EverythingBootstrapResult checkResult,
            EverythingBootstrapResult installResult)
            : this(checkResult, Task.FromResult(installResult))
        {
        }

        public FakeInstallationManager(
            EverythingBootstrapResult checkResult,
            Task<EverythingBootstrapResult> installResult)
        {
            _checkResult = checkResult;
            _installResult = installResult;
        }

        public int InstallCalls { get; private set; }

        public Task<EverythingBootstrapResult> CheckAndStartAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_checkResult);

        public Task<EverythingBootstrapResult> InstallAndStartAsync(
            CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            return _installResult;
        }
    }
}
