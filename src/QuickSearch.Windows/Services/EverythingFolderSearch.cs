using System.Runtime.InteropServices;
using System.IO;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public enum EverythingHealth
{
    Ready,
    DllMissing,
    NotRunning,
    QueryFailed
}

public sealed class EverythingFolderSearch : IFolderSearch
{
    private readonly object _sync = new();

    public EverythingHealth Health { get; private set; } = EverythingHealth.NotRunning;

    public Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run<IReadOnlyList<FolderSearchResult>>(() =>
        {
            lock (_sync)
            {
                try
                {
                    if (!EverythingNative.IsDatabaseLoaded())
                    {
                        Health = EverythingHealth.NotRunning;
                        return [];
                    }

                    EverythingNative.SetSearch(EverythingQueryBuilder.Build(query));
                    EverythingNative.SetRequestFlags(
                        EverythingNative.RequestFileName | EverythingNative.RequestPath);
                    EverythingNative.SetMax(100);
                    if (!EverythingNative.Query(wait: true))
                    {
                        Health = EverythingHealth.QueryFailed;
                        return [];
                    }

                    var candidates = new List<FolderSearchResult>();
                    var count = Math.Min(EverythingNative.GetNumResults(), 100u);
                    for (uint index = 0; index < count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = Marshal.PtrToStringUni(EverythingNative.GetResultFileName(index));
                        var parent = Marshal.PtrToStringUni(EverythingNative.GetResultPath(index));
                        if (!string.IsNullOrWhiteSpace(name) && parent is not null)
                        {
                            candidates.Add(new FolderSearchResult(name, Path.Combine(parent, name)));
                        }
                    }

                    Health = EverythingHealth.Ready;
                    return FolderSearchRanker.Rank(candidates, query);
                }
                catch (DllNotFoundException)
                {
                    Health = EverythingHealth.DllMissing;
                    return [];
                }
                catch (EntryPointNotFoundException)
                {
                    Health = EverythingHealth.DllMissing;
                    return [];
                }
            }
        }, cancellationToken);
    }
}
