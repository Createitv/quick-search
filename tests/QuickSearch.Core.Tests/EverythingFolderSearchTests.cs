namespace QuickSearch.Core.Tests;

public sealed class EverythingFolderSearchTests
{
    [Fact]
    public async Task SearchAsync_SetsNameAndPathRequestFlagsAndMaximumOneHundred()
    {
        var native = new FakeEverythingNative();
        var search = new EverythingFolderSearch(native);

        await search.SearchAsync("Invoices");

        Assert.Equal(
            EverythingFolderSearch.RequestFileName | EverythingFolderSearch.RequestPath,
            native.RequestFlags);
        Assert.Equal(100u, native.MaximumResults);
    }

    [Fact]
    public async Task SearchAsync_ConstructsFullPathFromNativePathAndFileName()
    {
        var native = new FakeEverythingNative
        {
            Results = [("Invoices", Path.Combine("clients", "acme"))]
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        var result = Assert.Single(results);
        Assert.Equal("Invoices", result.Name);
        Assert.Equal(
            Path.Combine("clients", "acme", "Invoices"),
            result.FullPath);
    }

    [Fact]
    public async Task SearchAsync_RanksNativeResultsByMatchQuality()
    {
        var native = new FakeEverythingNative
        {
            Results =
            [
                ("Archived Invoices", "archive"),
                ("Invoices 2026", "clients"),
                ("Invoices", "clients")
            ]
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Equal(
            ["Invoices", "Invoices 2026", "Archived Invoices"],
            results.Select(result => result.Name));
    }

    [Fact]
    public async Task SearchAsync_ReportsNotReadyWhenDatabaseIsStillLoading()
    {
        var native = new FakeEverythingNative
        {
            DatabaseLoaded = false,
            LastError = 0
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Empty(results);
        Assert.Equal(EverythingHealth.NotReady, search.Health);
        Assert.True(native.GetLastErrorCalled);
        Assert.False(native.QueryCalled);
    }

    [Fact]
    public async Task SearchAsync_ReportsUnavailableForNativeIpcError()
    {
        var native = new FakeEverythingNative
        {
            DatabaseLoaded = false,
            LastError = 2
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Empty(results);
        Assert.Equal(EverythingHealth.Unavailable, search.Health);
        Assert.True(native.GetLastErrorCalled);
    }

    [Fact]
    public async Task SearchAsync_ReportsQueryFailedForReadinessNativeError()
    {
        var native = new FakeEverythingNative
        {
            DatabaseLoaded = false,
            LastError = 7
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Empty(results);
        Assert.Equal(EverythingHealth.QueryFailed, search.Health);
    }

    [Fact]
    public async Task SearchAsync_ReportsDllMissingWhenNativeLibraryCannotLoad()
    {
        var native = new FakeEverythingNative
        {
            DatabaseException = new DllNotFoundException("Everything64.dll")
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Empty(results);
        Assert.Equal(EverythingHealth.DllMissing, search.Health);
    }

    [Fact]
    public async Task SearchAsync_ReportsQueryFailedForNonIpcNativeQueryError()
    {
        var native = new FakeEverythingNative
        {
            QueryResult = false,
            LastError = 7
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Empty(results);
        Assert.Equal(EverythingHealth.QueryFailed, search.Health);
        Assert.True(native.GetLastErrorCalled);
    }

    [Fact]
    public async Task SearchAsync_ReportsUnavailableWhenQueryCannotReachEverythingIpc()
    {
        var native = new FakeEverythingNative
        {
            QueryResult = false,
            LastError = 2
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Empty(results);
        Assert.Equal(EverythingHealth.Unavailable, search.Health);
        Assert.True(native.GetLastErrorCalled);
    }

    [Fact]
    public async Task SearchAsync_ReportsNotReadyWhenQueryReturnsNativeErrorZero()
    {
        var native = new FakeEverythingNative
        {
            QueryResult = false,
            LastError = 0
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Empty(results);
        Assert.Equal(EverythingHealth.NotReady, search.Health);
    }

    [Fact]
    public async Task SearchAsync_HonorsCancellationBeforeCallingNativeApi()
    {
        var native = new FakeEverythingNative();
        var search = new EverythingFolderSearch(native);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => search.SearchAsync("Invoices", cancellation.Token));

        Assert.False(native.QueryCalled);
    }

    [Fact]
    public async Task SearchAsync_HonorsCancellationWhileReadingNativeResults()
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeEverythingNative
        {
            Results = [("Invoices", "one"), ("Invoices", "two")],
            ResultRead = index =>
            {
                if (index == 0)
                {
                    cancellation.Cancel();
                }
            }
        };
        var search = new EverythingFolderSearch(native);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => search.SearchAsync("Invoices", cancellation.Token));
    }

    [Fact]
    public async Task SearchAsync_ReturnsExplicitFailureWhenNativeAdapterThrowsUnexpectedly()
    {
        var native = new FakeEverythingNative
        {
            QueryException = new InvalidOperationException("native adapter exploded")
        };
        var search = new EverythingFolderSearch(native);

        var results = await search.SearchAsync("Invoices");

        Assert.Empty(results);
        Assert.Equal(EverythingHealth.QueryFailed, search.Health);
        Assert.Contains("native adapter exploded", search.FailureMessage);
    }

    [Fact]
    public async Task SearchAsync_ReturnsControlBeforeBlockingNativeQueryCompletes()
    {
        var native = new FakeEverythingNative { BlockQuery = true };
        var search = new EverythingFolderSearch(native);
        Task<IReadOnlyList<FolderSearchResult>>? pendingSearch = null;
        var callReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() =>
        {
            pendingSearch = search.SearchAsync("Invoices");
            callReturned.TrySetResult();
        });
        await native.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await callReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotNull(pendingSearch);
            Assert.False(pendingSearch.IsCompleted);
        }
        finally
        {
            native.ReleaseQuery();
        }

        await pendingSearch!;
    }

    [Fact]
    public async Task SearchAsync_CancelsCallerWhileBlockingNativeQueryFinishesInWorker()
    {
        var native = new FakeEverythingNative { BlockQuery = true };
        var search = new EverythingFolderSearch(native);
        using var cancellation = new CancellationTokenSource();
        Task<IReadOnlyList<FolderSearchResult>>? pendingSearch = null;
        var callReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() =>
        {
            pendingSearch = search.SearchAsync("Invoices", cancellation.Token);
            callReturned.TrySetResult();
        });
        await native.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        try
        {
            await callReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotNull(pendingSearch);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pendingSearch.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            native.ReleaseQuery();
        }
    }

    [Fact]
    public async Task SearchAsync_CoalescesBlockedRequestsToOneLatestPendingQuery()
    {
        var native = new FakeEverythingNative { BlockQuery = true };
        var search = new EverythingFolderSearch(native);
        var active = search.SearchAsync("one");
        await native.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var droppedTwo = search.SearchAsync("two");
        var droppedThree = search.SearchAsync("three");
        var latest = search.SearchAsync("latest");

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => droppedTwo.WaitAsync(TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => droppedThree.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(latest.IsCompleted);
            Assert.Equal(1, native.QueryCallCount);
        }
        finally
        {
            native.ReleaseQuery();
        }

        await active;
        await latest;

        Assert.Equal(2, native.QueryCallCount);
        Assert.Equal(
            [EverythingQueryBuilder.Build("one"), EverythingQueryBuilder.Build("latest")],
            native.Searches);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsControlAndReportsUnavailableWithoutRunningQuery()
    {
        var native = new FakeEverythingNative
        {
            BlockDatabaseCheck = true,
            DatabaseLoaded = false,
            LastError = 2
        };
        var search = new EverythingFolderSearch(native);
        Task? pendingProbe = null;
        var callReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() =>
        {
            pendingProbe = search.ProbeAsync();
            callReturned.TrySetResult();
        });
        await native.DatabaseCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await callReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotNull(pendingProbe);
            Assert.False(pendingProbe.IsCompleted);
        }
        finally
        {
            native.ReleaseDatabaseCheck();
        }

        await pendingProbe!;
        Assert.Equal(EverythingHealth.Unavailable, search.Health);
        Assert.False(native.QueryCalled);
    }

    private sealed class FakeEverythingNative : IEverythingNative
    {
        public uint RequestFlags { get; private set; }

        public uint MaximumResults { get; private set; }

        public IReadOnlyList<(string Name, string Path)> Results { get; init; } = [];

        public Action<uint>? ResultRead { get; init; }

        public bool DatabaseLoaded { get; init; } = true;

        public bool BlockDatabaseCheck { get; init; }

        public TaskCompletionSource DatabaseCheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ManualResetEventSlim DatabaseCheckRelease { get; } = new(initialState: false);

        public uint LastError { get; init; }

        public bool QueryResult { get; init; } = true;

        public Exception? QueryException { get; init; }

        public bool QueryCalled { get; private set; }

        public int QueryCallCount { get; private set; }

        public List<string> Searches { get; } = [];

        public bool BlockQuery { get; init; }

        public TaskCompletionSource QueryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ManualResetEventSlim QueryRelease { get; } = new(initialState: false);

        public bool GetLastErrorCalled { get; private set; }

        public Exception? DatabaseException { get; init; }

        public bool IsDatabaseLoaded()
        {
            DatabaseCheckStarted.TrySetResult();
            if (BlockDatabaseCheck)
            {
                DatabaseCheckRelease.Wait();
            }

            if (DatabaseException is not null)
            {
                throw DatabaseException;
            }

            return DatabaseLoaded;
        }

        public void ReleaseDatabaseCheck() => DatabaseCheckRelease.Set();

        public void SetSearch(string search)
        {
            Searches.Add(search);
        }

        public void SetRequestFlags(uint flags) => RequestFlags = flags;

        public void SetMax(uint maximumResults) => MaximumResults = maximumResults;

        public bool Query(bool wait)
        {
            QueryCalled = true;
            QueryCallCount++;
            QueryStarted.TrySetResult();
            if (BlockQuery)
            {
                QueryRelease.Wait();
            }

            if (QueryException is not null)
            {
                throw QueryException;
            }

            return QueryResult;
        }

        public void ReleaseQuery() => QueryRelease.Set();

        public uint GetNumResults() => (uint)Results.Count;

        public string? GetResultFileName(uint index)
        {
            ResultRead?.Invoke(index);
            return Results[(int)index].Name;
        }

        public string? GetResultPath(uint index) => Results[(int)index].Path;

        public uint GetLastError()
        {
            GetLastErrorCalled = true;
            return LastError;
        }
    }
}
