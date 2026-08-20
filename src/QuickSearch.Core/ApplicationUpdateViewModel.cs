namespace QuickSearch.Core;

public enum ApplicationUpdateState
{
    Idle,
    Checking,
    Current,
    Available,
    Downloading,
    Installing,
    Failed
}

public sealed class ApplicationUpdateViewModel : ObservableObject
{
    private readonly IApplicationUpdateService _service;
    private readonly AppReleaseVersion _installedVersion;
    private ApplicationRelease? _availableRelease;
    private ApplicationUpdateState _state;
    private string _latestVersion = "—";
    private string _statusText = "尚未检查更新。";
    private double _downloadProgress;

    public ApplicationUpdateViewModel(
        IApplicationUpdateService service,
        string installedVersion)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _installedVersion = AppReleaseVersion.Parse(installedVersion);
        InstalledVersion = _installedVersion.ToString();
        CheckNowCommand = new AsyncRelayCommand(
            () => CheckNowAsync(),
            () => !IsBusy,
            HandleFailure);
        DownloadAndInstallCommand = new AsyncRelayCommand(
            () => DownloadAndInstallAsync(),
            () => CanDownloadAndInstall,
            HandleFailure);
    }

    public event EventHandler? ShutdownRequested;

    public string InstalledVersion { get; }

    public string LatestVersion
    {
        get => _latestVersion;
        private set => SetProperty(ref _latestVersion, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ApplicationUpdateState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanDownloadAndInstall));
                CheckNowCommand.NotifyCanExecuteChanged();
                DownloadAndInstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetProperty(ref _downloadProgress, value);
    }

    public bool IsBusy => State is
        ApplicationUpdateState.Checking
        or ApplicationUpdateState.Downloading
        or ApplicationUpdateState.Installing;

    public bool CanDownloadAndInstall =>
        State == ApplicationUpdateState.Available && _availableRelease is not null;

    public AsyncRelayCommand CheckNowCommand { get; }

    public AsyncRelayCommand DownloadAndInstallCommand { get; }

    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        State = ApplicationUpdateState.Checking;
        StatusText = "正在检查更新…";
        try
        {
            var result = await _service.CheckAsync(_installedVersion, cancellationToken);
            LatestVersion = result.LatestVersion;
            _availableRelease = result.Release;
            if (result.IsUpdateAvailable && result.Release is not null)
            {
                State = ApplicationUpdateState.Available;
                StatusText = $"发现新版本 {result.LatestVersion}。";
            }
            else
            {
                State = ApplicationUpdateState.Current;
                StatusText = "已是最新版本。";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = ApplicationUpdateState.Idle;
            StatusText = "更新检查已取消。";
        }
        catch (OperationCanceledException exception)
        {
            HandleConnectionFailure(exception);
        }
        catch (HttpRequestException exception)
        {
            HandleConnectionFailure(exception);
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
    }

    public async Task DownloadAndInstallAsync(CancellationToken cancellationToken = default)
    {
        if (_availableRelease is null || State != ApplicationUpdateState.Available)
        {
            return;
        }

        State = ApplicationUpdateState.Downloading;
        DownloadProgress = 0;
        StatusText = "正在下载安装包…";
        try
        {
            var progress = new InlineProgress(value =>
            {
                DownloadProgress = Math.Clamp(value, 0, 1);
                StatusText = $"正在下载安装包… {DownloadProgress:P0}";
            });
            var installerPath = await _service.DownloadAndVerifyAsync(
                _availableRelease,
                progress,
                cancellationToken);
            var launchResult = _service.LaunchInstaller(installerPath);
            if (!launchResult.Success)
            {
                throw new InvalidOperationException(launchResult.Message);
            }

            State = ApplicationUpdateState.Installing;
            StatusText = "安装程序已启动，QuickSearch 即将退出。";
            ShutdownRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = ApplicationUpdateState.Available;
            StatusText = "更新下载已取消。";
        }
        catch (OperationCanceledException exception)
        {
            HandleConnectionFailure(exception);
        }
        catch (HttpRequestException exception)
        {
            HandleConnectionFailure(exception);
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
    }

    private void HandleConnectionFailure(Exception exception)
    {
        State = ApplicationUpdateState.Failed;
        StatusText =
            "更新失败：无法连接更新服务器。请检查网络连接或本机代理设置，然后重试。" +
            $"详细信息：{exception.Message}";
    }

    private void HandleFailure(Exception exception)
    {
        State = ApplicationUpdateState.Failed;
        StatusText = $"更新失败：{exception.Message}";
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
