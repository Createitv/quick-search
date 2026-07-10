namespace QuickSearch.Core;

public enum EverythingHealth
{
    Ready,
    DllMissing,
    NotReady,
    Unavailable,
    QueryFailed
}

public sealed class EverythingFolderSearch : IFolderSearch
{
    public const uint RequestFileName = 0x00000001;
    public const uint RequestPath = 0x00000002;

    private readonly IEverythingNative _native;
    private readonly object _sync = new();

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
        cancellationToken.ThrowIfCancellationRequested();
        var worker = Task.Run(
            () => SearchOnWorker(query, cancellationToken),
            CancellationToken.None);
        try
        {
            return await worker.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = worker.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private IReadOnlyList<FolderSearchResult> SearchOnWorker(
        string query,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FailureMessage = null;
            try
            {
                if (!_native.IsDatabaseLoaded())
                {
                    var error = _native.GetLastError();
                    Health = ClassifyNativeError(error);
                    FailureMessage = Health == EverythingHealth.QueryFailed
                        ? $"Everything 就绪检查失败（错误 {error}）。"
                        : null;
                    return [];
                }

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
                return FolderSearchRanker.Rank(candidates, query);
            }
            catch (DllNotFoundException)
            {
                Health = EverythingHealth.DllMissing;
                FailureMessage = "缺少 Everything64.dll。";
                return [];
            }
            catch (EntryPointNotFoundException)
            {
                Health = EverythingHealth.DllMissing;
                FailureMessage = "Everything64.dll 版本不兼容。";
                return [];
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException and not ArgumentException)
            {
                Health = EverythingHealth.QueryFailed;
                FailureMessage = $"Everything 平台调用失败：{exception.Message}";
                return [];
            }
        }
    }

    private static EverythingHealth ClassifyNativeError(uint error) => error switch
    {
        0 => EverythingHealth.NotReady,
        2 => EverythingHealth.Unavailable,
        _ => EverythingHealth.QueryFailed
    };
}
