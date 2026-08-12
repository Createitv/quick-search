namespace QuickSearch.Core;

public enum EverythingHealth
{
    Ready,
    DllMissing,
    NotReady,
    Unavailable,
    QueryFailed
}

public sealed class EverythingFolderSearch : IFolderSearch, IQuickLauncherSearch
{
    public const uint RequestFileName = 0x00000001;
    public const uint RequestPath = 0x00000002;

    private readonly IEverythingNative _native;
    private readonly object _queueSync = new();
    private NativeRequest? _pendingRequest;
    private bool _workerRunning;

    public EverythingFolderSearch(IEverythingNative native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    public EverythingHealth Health { get; private set; } = EverythingHealth.NotReady;

    public string? FailureMessage { get; private set; }

    public async Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var result = await QueueAsync(
            token => SearchOnWorker(query, token),
            cancellationToken);
        return (IReadOnlyList<FolderSearchResult>)result!;
    }

    public async Task<IReadOnlyList<QuickLauncherSearchItem>> SearchLauncherAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var result = await QueueAsync(
            token => SearchLauncherOnWorker(query, token),
            cancellationToken);
        return (IReadOnlyList<QuickLauncherSearchItem>)result!;
    }

    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        await QueueAsync(
            token =>
            {
                token.ThrowIfCancellationRequested();
                ProbeOnWorker();
                return null;
            },
            cancellationToken);
    }

    private Task<object?> QueueAsync(
        Func<CancellationToken, object?> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = new NativeRequest(operation, cancellationToken);
        NativeRequest? droppedRequest;
        var startWorker = false;
        lock (_queueSync)
        {
            droppedRequest = _pendingRequest;
            _pendingRequest = request;
            if (!_workerRunning)
            {
                _workerRunning = true;
                startWorker = true;
            }
        }

        droppedRequest?.Drop();
        if (startWorker)
        {
            _ = Task.Factory.StartNew(
                DrainQueue,
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }

        return request.Task;
    }

    private void DrainQueue()
    {
        while (true)
        {
            NativeRequest? request;
            lock (_queueSync)
            {
                request = _pendingRequest;
                _pendingRequest = null;
                if (request is null)
                {
                    _workerRunning = false;
                    return;
                }
            }

            request.Execute();
        }
    }

    private IReadOnlyList<FolderSearchResult> SearchOnWorker(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ProbeOnWorker())
        {
            return [];
        }

        try
        {
            _native.SetSearch(EverythingQueryBuilder.Build(query));
            _native.SetRequestFlags(RequestFileName | RequestPath);
            _native.SetMax(100);
            if (!_native.Query(wait: true))
            {
                var error = _native.GetLastError();
                Health = ClassifyNativeError(error);
                FailureMessage = Health == EverythingHealth.QueryFailed
                    ? $"Everything 查询失败（错误 {error}）。"
                    : null;
                return [];
            }

            var candidates = new List<FolderSearchResult>();
            var count = Math.Min(_native.GetNumResults(), 100u);
            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = _native.GetResultFileName(index);
                var parent = _native.GetResultPath(index);
                if (!string.IsNullOrWhiteSpace(name) && parent is not null)
                {
                    candidates.Add(new FolderSearchResult(
                        name,
                        Path.Combine(parent, name)));
                }
            }

            Health = EverythingHealth.Ready;
            FailureMessage = null;
            return FolderSearchRanker.Rank(candidates, query);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException and not ArgumentException)
        {
            SetPlatformFailure(exception);
            return [];
        }
    }

    private IReadOnlyList<QuickLauncherSearchItem> SearchLauncherOnWorker(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ProbeOnWorker())
        {
            return [];
        }

        try
        {
            _native.SetSearch(EverythingQueryBuilder.BuildLauncher(query));
            _native.SetRequestFlags(RequestFileName | RequestPath);
            _native.SetMax(60);
            if (!_native.Query(wait: true))
            {
                var error = _native.GetLastError();
                Health = ClassifyNativeError(error);
                FailureMessage = Health == EverythingHealth.QueryFailed
                    ? $"Everything 查询失败（错误 {error}）。"
                    : null;
                return [];
            }

            var results = new List<QuickLauncherSearchItem>();
            var count = Math.Min(_native.GetNumResults(), 60u);
            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = _native.GetResultFileName(index);
                var parent = _native.GetResultPath(index);
                if (string.IsNullOrWhiteSpace(name) || parent is null)
                {
                    continue;
                }

                var fullPath = Path.Combine(parent, name);
                results.Add(new QuickLauncherSearchItem(
                    name,
                    fullPath,
                    ClassifyLauncherItem(name, _native.IsFolderResult(index))));
            }

            Health = EverythingHealth.Ready;
            FailureMessage = null;
            return results;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException and not ArgumentException)
        {
            SetPlatformFailure(exception);
            return [];
        }
    }

    private static QuickLauncherItemKind ClassifyLauncherItem(string name, bool isFolder)
    {
        if (isFolder)
        {
            return QuickLauncherItemKind.Folder;
        }

        var extension = Path.GetExtension(name);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase)
            ? QuickLauncherItemKind.Application
            : QuickLauncherItemKind.File;
    }

    private bool ProbeOnWorker()
    {
        FailureMessage = null;
        try
        {
            if (_native.IsDatabaseLoaded())
            {
                Health = EverythingHealth.Ready;
                return true;
            }

            var error = _native.GetLastError();
            Health = ClassifyNativeError(error);
            FailureMessage = Health == EverythingHealth.QueryFailed
                ? $"Everything 就绪检查失败（错误 {error}）。"
                : null;
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetPlatformFailure(exception);
            return false;
        }
    }

    private void SetPlatformFailure(Exception exception)
    {
        if (exception is DllNotFoundException)
        {
            Health = EverythingHealth.DllMissing;
            FailureMessage = "缺少 Everything64.dll。";
        }
        else if (exception is EntryPointNotFoundException)
        {
            Health = EverythingHealth.DllMissing;
            FailureMessage = "Everything64.dll 版本不兼容。";
        }
        else
        {
            Health = EverythingHealth.QueryFailed;
            FailureMessage = $"Everything 平台调用失败：{exception.Message}";
        }
    }

    private static EverythingHealth ClassifyNativeError(uint error) => error switch
    {
        0 => EverythingHealth.NotReady,
        2 => EverythingHealth.Unavailable,
        _ => EverythingHealth.QueryFailed
    };

    private sealed class NativeRequest
    {
        private readonly Func<CancellationToken, object?> _operation;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<object?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public NativeRequest(
            Func<CancellationToken, object?> operation,
            CancellationToken cancellationToken)
        {
            _operation = operation;
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.Register(
                static state => ((NativeRequest)state!).Cancel(),
                this);
        }

        public Task<object?> Task => _completion.Task;

        public void Drop()
        {
            _completion.TrySetCanceled();
            _registration.Dispose();
        }

        public void Execute()
        {
            if (_completion.Task.IsCompleted)
            {
                _registration.Dispose();
                return;
            }

            try
            {
                _completion.TrySetResult(_operation(_cancellationToken));
            }
            catch (OperationCanceledException)
            {
                Cancel();
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
            finally
            {
                _registration.Dispose();
            }
        }

        private void Cancel() =>
            _completion.TrySetCanceled(_cancellationToken);
    }
}
