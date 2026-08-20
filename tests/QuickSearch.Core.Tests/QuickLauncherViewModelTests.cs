namespace QuickSearch.Core.Tests;

public sealed class QuickLauncherViewModelTests
{
    [Fact]
    public async Task Search_LocalNavigationNameMatchSkipsEverything()
    {
        var configuration = new AppConfiguration();
        configuration.AddRule(
            "项目资料",
            ["project"],
            @"C:\Work\Projects",
            configuration.UncategorizedFolderId);
        var indexed = Enumerable.Range(1, 10)
            .Select(index => new QuickLauncherSearchItem(
                $"project-{index}",
                $@"C:\Indexed\project-{index}",
                QuickLauncherItemKind.Folder))
            .ToArray();
        var search = new FakeQuickLauncherSearch(indexed);
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            search,
            new FakePathOpener(),
            (_, _) => Task.CompletedTask);

        viewModel.SearchText = "项目";
        await EventuallyAsync(() => !viewModel.IsSearching && viewModel.Results.Count == 1);

        Assert.Equal(QuickLauncherItemKind.Shortcut, viewModel.Results[0].Kind);
        Assert.Equal("Ctrl+1", viewModel.Results[0].ShortcutText);
        Assert.Equal("项目资料", viewModel.SelectedResult?.Title);
        Assert.Equal(0, search.CallCount);
        Assert.Contains("快捷导航", viewModel.StatusText);
        Assert.Equal("Alt+K 呼出启动器", viewModel.LauncherShortcutText);
    }

    [Fact]
    public async Task Search_NoLocalNavigationNameMatchFallsBackToEverything()
    {
        var configuration = new AppConfiguration();
        configuration.AddRule(
            "项目资料",
            ["project"],
            @"C:\Work\Projects",
            configuration.UncategorizedFolderId);
        var search = new FakeQuickLauncherSearch(
        [
            new("project-folder", @"C:\Indexed\project-folder", QuickLauncherItemKind.Folder)
        ]);
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            search,
            new FakePathOpener(),
            (_, _) => Task.CompletedTask);

        viewModel.SearchText = "project";
        await EventuallyAsync(() => !viewModel.IsSearching && viewModel.Results.Count == 1);

        Assert.Equal(1, search.CallCount);
        Assert.Equal(QuickLauncherItemKind.Folder, viewModel.Results[0].Kind);
        Assert.Contains("Everything", viewModel.StatusText);
    }

    [Fact]
    public async Task ClipboardText_WithMatchingNavigation_IsPrefilledAndShowsResults()
    {
        var configuration = new AppConfiguration();
        configuration.AddRule(
            "项目资料",
            ["项目资料"],
            @"C:\Work\Projects",
            configuration.UncategorizedFolderId);
        var search = new FakeQuickLauncherSearch([]);
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            search,
            new FakePathOpener(),
            (_, _) => Task.CompletedTask,
            new FakeClipboardTextReader(" 项目资料 "));

        viewModel.PrefillFromClipboard();
        await EventuallyAsync(() => !viewModel.IsSearching && viewModel.Results.Count == 1);

        Assert.Equal("项目资料", viewModel.SearchText);
        Assert.Equal("项目资料", viewModel.Results[0].Title);
        Assert.Equal(0, search.CallCount);
    }

    [Fact]
    public async Task ClipboardText_WithoutAnyMatch_IsRemovedForManualInput()
    {
        var configuration = new AppConfiguration();
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            new FakeQuickLauncherSearch([]),
            new FakePathOpener(),
            (_, _) => Task.CompletedTask,
            new FakeClipboardTextReader("没有任何结果"));

        viewModel.PrefillFromClipboard();
        await EventuallyAsync(() => !viewModel.IsSearching);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Empty(viewModel.Results);
        Assert.Contains("手动输入", viewModel.StatusText);
    }

    [Fact]
    public async Task ManualInput_DuringClipboardSearch_IsNotClearedByOldClipboardResult()
    {
        var configuration = new AppConfiguration();
        var search = new DeferredQuickLauncherSearch();
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            search,
            new FakePathOpener(),
            (_, _) => Task.CompletedTask,
            new FakeClipboardTextReader("剪贴板文字"));

        viewModel.PrefillFromClipboard();
        var clipboardRequest = await search.WaitForRequestAsync("剪贴板文字");
        viewModel.SearchText = "手动搜索";
        var manualRequest = await search.WaitForRequestAsync("手动搜索");
        manualRequest.Complete(
        [
            new("手动搜索结果", @"C:\Manual", QuickLauncherItemKind.Folder)
        ]);
        clipboardRequest.Complete([]);
        await EventuallyAsync(() => !viewModel.IsSearching && viewModel.Results.Count == 1);

        Assert.Equal("手动搜索", viewModel.SearchText);
        Assert.Equal("手动搜索结果", viewModel.Results[0].Title);
    }

    [Fact]
    public async Task OpenShortcutAsync_OpensNumberedResultAndRequestsHide()
    {
        var configuration = new AppConfiguration();
        var opener = new FakePathOpener();
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            new FakeQuickLauncherSearch(
            [
                new("one", @"C:\one", QuickLauncherItemKind.Folder),
                new("two.exe", @"C:\two.exe", QuickLauncherItemKind.Application)
            ]),
            opener,
            (_, _) => Task.CompletedTask);
        var hidden = false;
        viewModel.HideRequested += (_, _) => hidden = true;
        viewModel.SearchText = "two";
        await EventuallyAsync(() => !viewModel.IsSearching && viewModel.Results.Count == 2);

        var opened = await viewModel.OpenShortcutAsync(2);

        Assert.True(opened);
        Assert.True(hidden);
        Assert.Equal([@"C:\two.exe"], opener.OpenedPaths);
    }

    [Fact]
    public async Task SetResultColumnsAsync_PersistsClampedLayout()
    {
        var configuration = new AppConfiguration();
        var store = new MemoryMappingStore(configuration);
        var viewModel = new QuickLauncherViewModel(
            configuration,
            store,
            new FakeQuickLauncherSearch([]),
            new FakePathOpener(),
            (_, _) => Task.CompletedTask);

        await viewModel.SetResultColumnsAsync(3);

        Assert.Equal(3, viewModel.ResultColumns);
        Assert.Equal(3, configuration.Settings.QuickLauncherResultColumns);
    }

    [Fact]
    public async Task ReorderResult_UpdatesVisibleOrderAndShortcuts()
    {
        var configuration = new AppConfiguration();
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            new FakeQuickLauncherSearch(
            [
                new("one", @"C:\one", QuickLauncherItemKind.Folder),
                new("two", @"C:\two", QuickLauncherItemKind.Folder),
                new("three", @"C:\three", QuickLauncherItemKind.Folder)
            ]),
            new FakePathOpener(),
            (_, _) => Task.CompletedTask);
        viewModel.SearchText = "item";
        await EventuallyAsync(() => !viewModel.IsSearching && viewModel.Results.Count == 3);

        viewModel.ReorderResult(viewModel.Results[2], viewModel.Results[0]);

        Assert.Equal(["three", "one", "two"], viewModel.Results.Select(result => result.Title));
        Assert.Equal("Ctrl+1", viewModel.Results[0].ShortcutText);
        Assert.Equal("three", viewModel.SelectedResult?.Title);
    }

    [Fact]
    public async Task ShowWithoutClipboardPrefill_RemainsEmptyUntilUserTypes()
    {
        var configuration = new AppConfiguration();
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            new FakeQuickLauncherSearch([]),
            new FakePathOpener(),
            (_, _) => Task.CompletedTask,
            new FakeClipboardTextReader("剪贴板文字"));

        viewModel.Reset();
        await Task.Delay(1);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Empty(viewModel.Results);
    }

    [Fact]
    public async Task NewSearch_CancelsPreviousSearchAndKeepsLatestResults()
    {
        var configuration = new AppConfiguration();
        var search = new DeferredQuickLauncherSearch();
        var viewModel = new QuickLauncherViewModel(
            configuration,
            new MemoryMappingStore(configuration),
            search,
            new FakePathOpener(),
            (_, _) => Task.CompletedTask);

        viewModel.SearchText = "old";
        var oldRequest = await search.WaitForRequestAsync("old");
        viewModel.SearchText = "new";
        var newRequest = await search.WaitForRequestAsync("new");
        newRequest.Complete([new("new", @"C:\new", QuickLauncherItemKind.Folder)]);
        await EventuallyAsync(() => !viewModel.IsSearching && viewModel.Results.Count == 1);

        Assert.True(oldRequest.CancellationToken.IsCancellationRequested);
        Assert.Equal("new", viewModel.Results[0].Title);
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class MemoryMappingStore(AppConfiguration configuration) : IMappingStore
    {
        public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration.Clone());

        public Task SaveAsync(
            AppConfiguration candidate,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeQuickLauncherSearch(
        IReadOnlyList<QuickLauncherSearchItem> results) : IQuickLauncherSearch
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<QuickLauncherSearchItem>> SearchLauncherAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(results);
        }
    }

    private sealed class FakePathOpener : IPathOpener
    {
        public List<string> OpenedPaths { get; } = [];

        public PlatformOperationResult Open(string path)
        {
            OpenedPaths.Add(path);
            return PlatformOperationResult.Succeeded();
        }
    }

    private sealed class FakeClipboardTextReader(string? text) : IClipboardTextReader
    {
        public PlatformOperationResult<string?> ReadText() =>
            PlatformOperationResult<string?>.Succeeded(text);
    }

    private sealed class DeferredQuickLauncherSearch : IQuickLauncherSearch
    {
        private readonly Dictionary<string, Request> _requests = [];

        public Task<IReadOnlyList<QuickLauncherSearchItem>> SearchLauncherAsync(
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
            private readonly TaskCompletionSource<IReadOnlyList<QuickLauncherSearchItem>> _source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public CancellationToken CancellationToken { get; } = cancellationToken;

            public Task<IReadOnlyList<QuickLauncherSearchItem>> Task => _source.Task;

            public void Complete(IReadOnlyList<QuickLauncherSearchItem> results) =>
                _source.TrySetResult(results);
        }
    }
}
