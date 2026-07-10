namespace QuickSearch.Core.Tests;

public sealed class LauncherViewModelTests
{
    [Fact]
    public async Task ActivateFromClipboard_ShowsExactMappingAndWaitsForConfirmation()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("sales", Path.Combine("clients", "Sales Team"));
        var store = new FakeMappingStore(configuration);
        var opener = new FakeFolderOpener();
        var viewModel = CreateViewModel(
            store,
            clipboard: new FakeClipboardTextReader(" sales "),
            opener: opener,
            folderExists: _ => true);
        await viewModel.InitializeAsync();

        viewModel.ActivateFromClipboard();

        Assert.Equal(" sales ", viewModel.Alias);
        Assert.Contains("sales", viewModel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sales Team", viewModel.Status);
        Assert.Contains(Path.Combine("clients", "Sales Team"), viewModel.Status);
        Assert.Equal(0, opener.OpenCalls);
        Assert.True(viewModel.CanConfirmOpen);
    }

    [Fact]
    public async Task ActivateFromClipboard_StaleMappingExposesEditableAliasAndRealFolderQuery()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("sales", "/missing-sales");
        var viewModel = CreateViewModel(
            new FakeMappingStore(configuration),
            clipboard: new FakeClipboardTextReader("sales"),
            folderExists: _ => false);
        await viewModel.InitializeAsync();

        viewModel.ActivateFromClipboard();
        Assert.Contains("重新搜索", viewModel.Status);
        viewModel.Alias = "sales-team";

        Assert.Equal("sales-team", viewModel.Alias);
        Assert.Equal("sales", viewModel.FolderQuery);
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
        configuration.Settings = configuration.Settings with
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
    public async Task InitializeAndActivation_ContainStoreAndClipboardExceptionsAsStatus()
    {
        var store = new FakeMappingStore(new AppConfiguration())
        {
            LoadException = new IOException("bad config")
        };
        var viewModel = CreateViewModel(
            store,
            clipboard: new FakeClipboardTextReader(
                exception: new InvalidOperationException("clipboard busy")));

        await viewModel.InitializeAsync();
        Assert.Contains("bad config", viewModel.Status);

        viewModel.ActivateFromClipboard();
        Assert.Contains("clipboard busy", viewModel.Status);
    }

    private static LauncherViewModel CreateViewModel(
        FakeMappingStore store,
        IFolderSearch? search = null,
        IClipboardTextReader? clipboard = null,
        IFolderOpener? opener = null,
        Func<string, bool>? folderExists = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null) =>
        new(
            store,
            search ?? new FakeFolderSearch(),
            clipboard ?? new FakeClipboardTextReader(null),
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

    private sealed class FakeClipboardTextReader(
        string? value = null,
        Exception? exception = null) : IClipboardTextReader
    {
        public PlatformOperationResult<string?> ReadText() =>
            exception is null
                ? PlatformOperationResult<string?>.Succeeded(value)
                : throw exception;
    }

    private sealed class FakeFolderOpener(
        PlatformOperationResult? result = null,
        List<string>? operations = null,
        Exception? exception = null) : IFolderOpener
    {
        public int OpenCalls { get; private set; }

        public PlatformOperationResult Open(string folderPath)
        {
            OpenCalls++;
            operations?.Add($"open:{folderPath}");
            if (exception is not null)
            {
                throw exception;
            }

            return result ?? PlatformOperationResult.Succeeded();
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
