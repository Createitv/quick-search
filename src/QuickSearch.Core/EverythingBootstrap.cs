namespace QuickSearch.Core;

public enum EverythingBootstrapState
{
    Checking,
    Ready,
    NotInstalled,
    Starting,
    Installing,
    InstallerMissing,
    InstallerInvalid,
    StartFailed,
    Failed
}

public sealed record EverythingBootstrapResult(
    EverythingBootstrapState State,
    string Message);

public interface IEverythingInstallationManager
{
    Task<EverythingBootstrapResult> CheckAndStartAsync(
        CancellationToken cancellationToken = default);

    Task<EverythingBootstrapResult> InstallAndStartAsync(
        CancellationToken cancellationToken = default);
}

public sealed class EverythingBootstrapViewModel : ObservableObject
{
    private readonly IEverythingInstallationManager _manager;
    private readonly AsyncRelayCommand _installCommand;
    private EverythingBootstrapState _state = EverythingBootstrapState.Checking;
    private string _statusText = "正在检查 Everything...";
    private bool _installationRunning;

    public EverythingBootstrapViewModel(IEverythingInstallationManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _installCommand = new AsyncRelayCommand(
            () => InstallAsync(),
            () => IsInstallPanelVisible && !_installationRunning,
            exception => Apply(new EverythingBootstrapResult(
                EverythingBootstrapState.Failed,
                $"无法安装 Everything：{exception.Message}")));
    }

    public EverythingBootstrapState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSearch));
            OnPropertyChanged(nameof(IsBootstrapPanelVisible));
            OnPropertyChanged(nameof(IsInstallPanelVisible));
            OnPropertyChanged(nameof(IsInstallActionVisible));
            OnPropertyChanged(nameof(PanelTitle));
            OnPropertyChanged(nameof(InstallButtonText));
            _installCommand.NotifyCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool CanSearch => State == EverythingBootstrapState.Ready;

    public bool IsBootstrapPanelVisible => State != EverythingBootstrapState.Ready;

    public bool IsInstallPanelVisible => State is
        EverythingBootstrapState.NotInstalled or
        EverythingBootstrapState.Installing or
        EverythingBootstrapState.InstallerMissing or
        EverythingBootstrapState.InstallerInvalid or
        EverythingBootstrapState.Failed;

    public bool IsInstallActionVisible => IsInstallPanelVisible &&
        State != EverythingBootstrapState.Installing;

    public string PanelTitle => State == EverythingBootstrapState.StartFailed
        ? "Everything 无法启动"
        : "需要安装 Everything";

    public string InstallButtonText =>
        State == EverythingBootstrapState.Failed ? "重试安装" : "安装 Everything";

    public AsyncRelayCommand InstallCommand => _installCommand;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        State = EverythingBootstrapState.Checking;
        StatusText = "正在检查 Everything...";
        try
        {
            Apply(await _manager.CheckAndStartAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Apply(new EverythingBootstrapResult(
                EverythingBootstrapState.Failed,
                $"无法检查 Everything：{exception.Message}"));
        }
    }

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        if (_installationRunning)
        {
            return;
        }

        _installationRunning = true;
        State = EverythingBootstrapState.Installing;
        StatusText = "正在等待 Everything 安装完成...";
        _installCommand.NotifyCanExecuteChanged();
        try
        {
            Apply(await _manager.InstallAndStartAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Apply(new EverythingBootstrapResult(
                EverythingBootstrapState.Failed,
                $"无法安装 Everything：{exception.Message}"));
        }
        finally
        {
            _installationRunning = false;
            _installCommand.NotifyCanExecuteChanged();
        }
    }

    private void Apply(EverythingBootstrapResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        State = result.State;
        StatusText = result.Message;
    }
}
