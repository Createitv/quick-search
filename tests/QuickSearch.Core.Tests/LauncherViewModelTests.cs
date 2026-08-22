namespace QuickSearch.Core.Tests;

public sealed class LauncherViewModelTests
{
    [Fact]
    public async Task LocalSearchMatch_DoesNotExposeEverythingFallback()
    {
        var configuration = new AppConfiguration();
        var folder = configuration.AddNavigationFolder("客户项目", null);
        configuration.AddRule("销售资料", ["sales"], "/clients/sales", folder.Id);
        var search = new FakeFolderSearch();
        var viewModel = CreateViewModel(
            new FakeMappingStore(configuration),
            search: search);
        await viewModel.InitializeAsync();

        viewModel.Explorer.SearchText = "sales";

        Assert.True(viewModel.Explorer.HasLocalSearchResults);
        Assert.False(viewModel.CanSearchEverything);
        Assert.Equal(0, search.SearchCalls);
    }

    [Fact]
    public async Task SearchEverythingAsync_FillsExistingResultsAndPreservesExplorerText()
    {
        var search = new FakeFolderSearch
        {
            Results = [new FolderSearchResult("Sales", "/found/sales")]
        };
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            search: search);
        await viewModel.InitializeAsync();
        viewModel.Explorer.SearchText = "sales";

        await viewModel.SearchEverythingAsync();

        Assert.Equal("sales", viewModel.Explorer.SearchText);
        Assert.Equal("sales", viewModel.FolderQuery);
        Assert.Equal("/found/sales", viewModel.SelectedResult?.FullPath);
        Assert.True(viewModel.IsEverythingSearchVisible);
        Assert.Equal(1, search.SearchCalls);
    }

    [Fact]
    public async Task OpenSearchShortcutAsync_OpensMatchingLocalRule()
    {
        var configuration = new AppConfiguration();
        var folder = configuration.AddNavigationFolder("项目", null);
        configuration.AddRule("Alpha", ["quick"], "/quick/alpha", folder.Id);
        configuration.AddRule("Beta", ["quick"], "/quick/beta", folder.Id);
        var opener = new FakeFolderOpener();
        var viewModel = CreateViewModel(
            new FakeMappingStore(configuration),
            opener: opener,
            folderExists: _ => true);
        await viewModel.InitializeAsync();
        viewModel.Explorer.SearchText = "quick";

        var opened = await viewModel.OpenSearchShortcutAsync(2);

        Assert.True(opened);
        Assert.Equal(["/quick/beta"], opener.OpenedPaths);
    }

    [Fact]
    public async Task EverythingResults_AssignAndOpenCtrlNumberShortcuts()
    {
        var search = new FakeFolderSearch
        {
            Results =
            [
                new FolderSearchResult("One", "/one"),
                new FolderSearchResult("Two", "/two")
            ]
        };
        var opener = new FakeFolderOpener();
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            search: search,
            opener: opener);
        await viewModel.InitializeAsync();
        viewModel.Explorer.SearchText = "folder";
        await viewModel.SearchEverythingAsync();

        var opened = await viewModel.OpenSearchShortcutAsync(2);

        Assert.Equal("Ctrl+1", viewModel.Results[0].ShortcutText);
        Assert.Equal("Ctrl+2", viewModel.Results[1].ShortcutText);
        Assert.True(opened);
        Assert.Equal(["/two"], opener.OpenedPaths);
    }

    [Fact]
    public async Task OpenSearchShortcutAsync_IgnoresUnavailableNumbers()
    {
        var opener = new FakeFolderOpener();
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            opener: opener);
        await viewModel.InitializeAsync();

        Assert.False(await viewModel.OpenSearchShortcutAsync(0));
        Assert.False(await viewModel.OpenSearchShortcutAsync(10));
        Assert.Empty(opener.OpenedPaths);
    }

    [Fact]
    public async Task InitializeAsync_PopulatesExplorerFromLoadedConfiguration()
    {
        var configuration = new AppConfiguration();
        var folder = configuration.AddNavigationFolder("工作区", null);
        configuration.PinFolder(folder.Id);
        configuration.AddRule("项目文档", ["docs"], "/workspace/docs", folder.Id);
        var viewModel = CreateViewModel(new FakeMappingStore(configuration));

        await viewModel.InitializeAsync();
        viewModel.Explorer.NavigateToFolder(folder.Id);

        Assert.Contains(viewModel.Explorer.PinnedFolders, item => item.Id == folder.Id);
        Assert.Equal("工作区", viewModel.Explorer.CurrentFolder.Name);
        Assert.Equal("项目文档", Assert.Single(viewModel.Explorer.CurrentRules).DisplayTitle);
    }

    [Fact]
    public async Task OpenSelectedAsync_OpensSearchResultWithoutCreatingMapping()
    {
        var configuration = new AppConfiguration();
        var store = new FakeMappingStore(configuration);
        var opener = new FakeFolderOpener();
        var viewModel = CreateViewModel(store, opener: opener);
        await viewModel.InitializeAsync();
        viewModel.SelectedResult = new FolderSearchResult("Sales", "/found/sales");

        await viewModel.OpenSelectedAsync();

        Assert.Equal(["/found/sales"], opener.OpenedPaths);
        Assert.Empty(configuration.Mappings);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task AliasChangeToExactMapping_CancelsSearchAndIgnoresLateResults()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("sales", "/sales");
        var search = new DeferredFolderSearch();
        var viewModel = CreateViewModel(
            new FakeMappingStore(configuration),
            search: search,
            folderExists: path => path == "/sales",
            delayAsync: (_, _) => Task.CompletedTask);
        await viewModel.InitializeAsync();
        viewModel.Alias = "unknown";
        var oldRequest = await search.WaitForRequestAsync("unknown");

        viewModel.Alias = "sales";

        Assert.True(oldRequest.CancellationToken.IsCancellationRequested);
        Assert.False(viewModel.IsFolderSearchVisible);
        var exactStatus = viewModel.Status;
        oldRequest.Complete([new FolderSearchResult("Late", "/late")]);
        await oldRequest.Completed;
        await Task.Yield();

        Assert.Empty(viewModel.Results);
        Assert.Null(viewModel.SelectedResult);
        Assert.Equal(exactStatus, viewModel.Status);
        Assert.True(viewModel.CanConfirmOpen);
    }

    [Fact]
    public async Task FolderQueryChange_DebouncesForTwoHundredMilliseconds()
    {
        var delay = new ControlledDelay();
        var search = new FakeFolderSearch();
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            search: search,
            delayAsync: delay.DelayAsync);
        await viewModel.InitializeAsync();

        viewModel.FolderQuery = "Invoices";

        Assert.Equal(TimeSpan.FromMilliseconds(200), delay.LastDelay);
        Assert.Equal(0, search.SearchCalls);

        delay.Release();
        await search.WaitForCallsAsync(1);
    }

    [Fact]
    public async Task FolderQueryChange_CancelsPreviousRequestAndIgnoresItsLateResults()
    {
        var search = new DeferredFolderSearch();
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            search: search,
            delayAsync: (_, _) => Task.CompletedTask);
        await viewModel.InitializeAsync();

        viewModel.FolderQuery = "old";
        var oldRequest = await search.WaitForRequestAsync("old");
        viewModel.FolderQuery = "new";
        var newRequest = await search.WaitForRequestAsync("new");

        Assert.True(oldRequest.CancellationToken.IsCancellationRequested);
        newRequest.Complete([new FolderSearchResult("New", "/new")]);
        await EventuallyAsync(() => viewModel.Results.Count == 1);
        oldRequest.Complete([new FolderSearchResult("Old", "/old")]);
        await oldRequest.Completed;

        Assert.Equal("/new", Assert.Single(viewModel.Results).FullPath);
        Assert.Equal("/new", viewModel.SelectedResult?.FullPath);
    }

    [Fact]
    public async Task SearchNowAsync_ShowsAtMostTwentyFullPathsAndSelectsFirst()
    {
        var results = Enumerable.Range(1, 25)
            .Select(index => new FolderSearchResult($"Folder {index}", $"/root/folder-{index}"))
            .ToArray();
        var search = new FakeFolderSearch { Results = results };
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            search: search);
        await viewModel.InitializeAsync();
        viewModel.FolderQuery = "folder";

        await viewModel.SearchNowAsync();

        Assert.Equal(20, viewModel.Results.Count);
        Assert.All(viewModel.Results, result => Assert.StartsWith("/root/", result.FullPath));
        Assert.Same(viewModel.Results[0], viewModel.SelectedResult);
    }

    [Fact]
    public async Task SelectionCommands_MoveWithinSearchResultsWithoutWrapping()
    {
        var search = new FakeFolderSearch
        {
            Results =
            [
                new FolderSearchResult("One", "/one"),
                new FolderSearchResult("Two", "/two"),
                new FolderSearchResult("Three", "/three")
            ]
        };
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            search: search);
        await viewModel.InitializeAsync();
        viewModel.SearchText = "folder";
        await viewModel.SearchNowAsync();

        viewModel.SelectPreviousCommand.Execute(null);
        Assert.Equal("/one", viewModel.SelectedResult?.FullPath);

        viewModel.SelectNextCommand.Execute(null);
        viewModel.SelectNextCommand.Execute(null);
        viewModel.SelectNextCommand.Execute(null);
        Assert.Equal("/three", viewModel.SelectedResult?.FullPath);

        viewModel.SelectPreviousCommand.Execute(null);
        Assert.Equal("/two", viewModel.SelectedResult?.FullPath);
    }

    public static TheoryData<EverythingHealth, IReadOnlyList<FolderSearchResult>, string> SearchStatuses =>
        new()
        {
            { EverythingHealth.Ready, new[] { new FolderSearchResult("Invoices", "/Invoices") }, "找到 1 个候选文件夹" },
            { EverythingHealth.Ready, Array.Empty<FolderSearchResult>(), "没有找到匹配的文件夹" },
            { EverythingHealth.DllMissing, Array.Empty<FolderSearchResult>(), "缺少 Everything64.dll" },
            { EverythingHealth.NotReady, Array.Empty<FolderSearchResult>(), "索引数据库正在加载" },
            { EverythingHealth.Unavailable, Array.Empty<FolderSearchResult>(), "无法连接 Everything" },
            { EverythingHealth.QueryFailed, Array.Empty<FolderSearchResult>(), "native failed" }
        };

    [Theory]
    [MemberData(nameof(SearchStatuses))]
    public async Task SearchNowAsync_DistinguishesEveryHealthState(
        EverythingHealth health,
        IReadOnlyList<FolderSearchResult> results,
        string expectedStatus)
    {
        var search = new FakeFolderSearch
        {
            Health = health,
            FailureMessage = health == EverythingHealth.QueryFailed ? "native failed" : null,
            Results = results
        };
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            search: search);
        await viewModel.InitializeAsync();
        viewModel.FolderQuery = "Invoices";

        await viewModel.SearchNowAsync();

        Assert.Contains(expectedStatus, viewModel.Status);
    }

    [Fact]
    public async Task SearchNowAsync_ReportsCancellationAndUnexpectedFailure()
    {
        var search = new FakeFolderSearch
        {
            SearchException = new OperationCanceledException()
        };
        var viewModel = CreateViewModel(
            new FakeMappingStore(new AppConfiguration()),
            search: search);
        await viewModel.InitializeAsync();
        viewModel.FolderQuery = "Invoices";

        await viewModel.SearchNowAsync();
        Assert.Contains("搜索已取消", viewModel.Status);

        search.SearchException = new InvalidOperationException("search exploded");
        await viewModel.SearchNowAsync();
        Assert.Contains("search exploded", viewModel.Status);
    }

    [Fact]
    public async Task ConfirmAndOpenAsync_OpensBeforeSavingNewMapping()
    {
        var operations = new List<string>();
        var configuration = new AppConfiguration();
        var store = new FakeMappingStore(configuration, operations);
        var opener = new FakeFolderOpener(operations: operations);
        var viewModel = CreateViewModel(store, opener: opener);
        await viewModel.InitializeAsync();
        viewModel.Alias = "sales";
        viewModel.SelectedResult = new FolderSearchResult("Sales Team", "/new-sales");

        await viewModel.ConfirmAndOpenAsync();

        Assert.Equal(["open:/new-sales", "save"], operations);
        Assert.Equal("/new-sales", viewModel.Configuration.FindMapping("sales")?.FolderPath);
    }

    [Fact]
    public async Task ConfirmAndOpenAsync_FailedOpenDoesNotOverwritePriorMapping()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("sales", "/old-sales");
        var store = new FakeMappingStore(configuration);
        var opener = new FakeFolderOpener(
            PlatformOperationResult.Failed("Explorer failed"));
        var viewModel = CreateViewModel(
            store,
            opener: opener,
            folderExists: _ => false);
        await viewModel.InitializeAsync();
        viewModel.Alias = "sales";
        viewModel.SelectedResult = new FolderSearchResult("Sales Team", "/new-sales");

        await viewModel.ConfirmAndOpenAsync();

        Assert.Equal("/old-sales", viewModel.Configuration.FindMapping("sales")?.FolderPath);
        Assert.Equal(0, store.SaveCalls);
        Assert.Contains("Explorer failed", viewModel.Status);
    }

    [Fact]
    public async Task ConfirmAndOpenAsync_ExactOpenFailureDemotesToRepairWithoutPersisting()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("sales", "/sales");
        var store = new FakeMappingStore(configuration);
        var viewModel = CreateViewModel(
            store,
            opener: new FakeFolderOpener(
                PlatformOperationResult.Failed("Explorer failed")),
            folderExists: path => path == "/sales",
            delayAsync: (_, cancellationToken) =>
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        await viewModel.InitializeAsync();
        viewModel.Alias = "sales";
        Assert.False(viewModel.IsFolderSearchVisible);

        await viewModel.ConfirmAndOpenAsync();

        Assert.True(viewModel.IsFolderSearchVisible);
        Assert.False(viewModel.CanConfirmOpen);
        Assert.Equal("sales", viewModel.FolderQuery);
        Assert.Contains("重新搜索", viewModel.Status);
        Assert.Equal(StatusKind.Error, viewModel.StatusKind);
        Assert.Equal(string.Empty, viewModel.ResolvedPath);
        Assert.Equal("/sales", viewModel.Configuration.FindMapping("sales")?.FolderPath);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task ConfirmAndOpenAsync_ContainsThrownOpenExceptionWithoutSaving()
    {
        var configuration = new AppConfiguration();
        var store = new FakeMappingStore(configuration);
        var viewModel = CreateViewModel(
            store,
            opener: new FakeFolderOpener(
                exception: new InvalidOperationException("shell exploded")));
        await viewModel.InitializeAsync();
        viewModel.Alias = "sales";
        viewModel.SelectedResult = new FolderSearchResult("Sales", "/sales");

        await viewModel.ConfirmAndOpenAsync();

        Assert.Equal(0, store.SaveCalls);
        Assert.Contains("shell exploded", viewModel.Status);
    }

    [Fact]
    public async Task RefreshConfiguration_UpdatesShortcutInstruction()
    {
        var configuration = new AppConfiguration();
        var viewModel = CreateViewModel(new FakeMappingStore(configuration));
        await viewModel.InitializeAsync();
        viewModel.Configuration.Settings = viewModel.Configuration.Settings with
        {
            GlobalShortcut = "Ctrl+Shift+9"
        };

        viewModel.RefreshConfiguration();

        Assert.Contains("Ctrl+Shift+9", viewModel.ShortcutInstruction);
    }

    [Fact]
    public async Task ConfirmAndOpenAsync_UpdatesLastUsedAndPersistsSuccessfulSavedMappingOpen()
    {
        var createdAt = new DateTimeOffset(2026, 7, 10, 1, 0, 0, TimeSpan.Zero);
        var usedAt = createdAt.AddMinutes(30);
        var timeProvider = new SettableTimeProvider(createdAt);
        var configuration = new AppConfiguration(timeProvider);
        configuration.UpsertMapping("sales", "/sales");
        timeProvider.UtcNow = usedAt;
        var store = new FakeMappingStore(configuration);
        var viewModel = CreateViewModel(store, folderExists: _ => true);
        await viewModel.InitializeAsync();
        viewModel.Alias = "sales";

        await viewModel.ConfirmAndOpenAsync();

        Assert.Equal(usedAt, viewModel.Configuration.FindMapping("sales")?.LastUsedAtUtc);
        Assert.Equal(usedAt, store.LastSaved?.FindMapping("sales")?.LastUsedAtUtc);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task ConfirmAndOpenAsync_SurfacesPersistenceFailureWithoutMutatingLiveConfiguration()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("sales", "/old-sales");
        var store = new FakeMappingStore(configuration)
        {
            SaveException = new IOException("disk full")
        };
        var viewModel = CreateViewModel(
            store,
            folderExists: _ => false);
        await viewModel.InitializeAsync();
        viewModel.Alias = "sales";
        viewModel.SelectedResult = new FolderSearchResult("New Sales", "/new-sales");

        await viewModel.ConfirmAndOpenAsync();

        Assert.Equal("/old-sales", viewModel.Configuration.FindMapping("sales")?.FolderPath);
        Assert.Contains("disk full", viewModel.Status);
    }

    [Fact]
    public async Task InitializeAsync_ContainsStoreExceptionAsStatus()
    {
        var store = new FakeMappingStore(new AppConfiguration())
        {
            LoadException = new IOException("bad config")
        };
        var viewModel = CreateViewModel(store);

        await viewModel.InitializeAsync();
        Assert.Contains("bad config", viewModel.Status);
    }

    private static LauncherViewModel CreateViewModel(
        FakeMappingStore store,
        IFolderSearch? search = null,
        IFolderOpener? opener = null,
        Func<string, bool>? folderExists = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null) =>
        new(
            store,
            search ?? new FakeFolderSearch(),
            opener ?? new FakeFolderOpener(),
            folderExists,
            delayAsync);

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeMappingStore(
        AppConfiguration configuration,
        List<string>? operations = null) : IMappingStore
    {
        public Exception? LoadException { get; init; }

        public Exception? SaveException { get; init; }

        public int SaveCalls { get; private set; }

        public AppConfiguration? LastSaved { get; private set; }

        public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            LoadException is null
                ? Task.FromResult(configuration)
                : Task.FromException<AppConfiguration>(LoadException);

        public Task SaveAsync(
            AppConfiguration candidate,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            operations?.Add("save");
            LastSaved = candidate;
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }
    }

    private sealed class FakeFolderSearch : IFolderSearch
    {
        public EverythingHealth Health { get; set; } = EverythingHealth.Ready;

        public string? FailureMessage { get; set; }

        public IReadOnlyList<FolderSearchResult> Results { get; init; } = [];

        public Exception? SearchException { get; set; }

        public int SearchCalls { get; private set; }

        public Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return SearchException is null
                ? Task.FromResult(Results)
                : Task.FromException<IReadOnlyList<FolderSearchResult>>(SearchException);
        }

        public async Task WaitForCallsAsync(int count) =>
            await EventuallyAsync(() => SearchCalls >= count);
    }

    private sealed class DeferredFolderSearch : IFolderSearch
    {
        private readonly Dictionary<string, Request> _requests = [];

        public EverythingHealth Health => EverythingHealth.Ready;

        public string? FailureMessage => null;

        public Task<IReadOnlyList<FolderSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            var request = new Request(cancellationToken);
            lock (_requests)
            {
                _requests.Add(query, request);
            }

            return request.Task;
        }

        public async Task<Request> WaitForRequestAsync(string query)
        {
            Request? request = null;
            await EventuallyAsync(() =>
            {
                lock (_requests)
                {
                    return _requests.TryGetValue(query, out request);
                }
            });
            return request!;
        }

        public sealed class Request(CancellationToken cancellationToken)
        {
            private readonly TaskCompletionSource<IReadOnlyList<FolderSearchResult>> _source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public CancellationToken CancellationToken { get; } = cancellationToken;

            public Task<IReadOnlyList<FolderSearchResult>> Task => _source.Task;

            public Task Completed => _source.Task;

            public void Complete(IReadOnlyList<FolderSearchResult> results)
            {
                _source.TrySetResult(results);
            }
        }
    }

    private sealed class FakeFolderOpener(
        PlatformOperationResult? result = null,
        List<string>? operations = null,
        Exception? exception = null,
        Func<string, PlatformOperationResult>? resultForPath = null) : IFolderOpener
    {
        public int OpenCalls { get; private set; }

        public List<string> OpenedPaths { get; } = [];

        public PlatformOperationResult Open(string folderPath)
        {
            OpenCalls++;
            OpenedPaths.Add(folderPath);
            operations?.Add($"open:{folderPath}");
            if (exception is not null)
            {
                throw exception;
            }

            return resultForPath?.Invoke(folderPath)
                ?? result
                ?? PlatformOperationResult.Succeeded();
        }
    }

    private sealed class ControlledDelay
    {
        private readonly TaskCompletionSource _source =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TimeSpan LastDelay { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            LastDelay = delay;
            return _source.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _source.TrySetResult();
    }

    private sealed class SettableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
