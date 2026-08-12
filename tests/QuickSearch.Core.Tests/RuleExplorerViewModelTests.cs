namespace QuickSearch.Core.Tests;

public sealed class RuleExplorerViewModelTests
{
    [Fact]
    public void SearchText_MatchesRuleAliasAndRestoresPreviousFolderWhenCleared()
    {
        var configuration = CreateConfiguration();
        var store = new RecordingMappingStore(configuration);
        var viewModel = new RuleExplorerViewModel(
            configuration,
            store,
            new RecordingFolderOpener());
        var development = configuration.NavigationFolders.Single(folder =>
            folder.Name == "开发");
        viewModel.NavigateToFolder(development.Id);

        viewModel.SearchText = "fastapi";

        var result = Assert.Single(viewModel.SearchResults);
        Assert.Equal(ExplorerSearchResultKind.Rule, result.Kind);
        Assert.Contains("客户 › 小溪", result.CategoryPath);

        viewModel.SearchText = string.Empty;
        Assert.Equal("开发", viewModel.CurrentFolder.Name);
    }

    [Fact]
    public void SearchText_MatchesFullCategoryPathAndRulePath()
    {
        var configuration = CreateConfiguration();
        var viewModel = new RuleExplorerViewModel(
            configuration,
            new RecordingMappingStore(configuration),
            new RecordingFolderOpener());

        viewModel.SearchText = "客户 小溪";
        Assert.Contains(
            viewModel.SearchResults,
            result => result.Kind == ExplorerSearchResultKind.Folder
                      && result.Title == "开发");

        viewModel.SearchText = "quick-search";
        var rule = Assert.Single(viewModel.SearchResults);
        Assert.Equal(@"F:\Github\客户\小溪\quick-search", rule.FolderPath);
    }

    [Fact]
    public void SearchResults_AssignCtrlNumberShortcutsToFirstNineMatches()
    {
        var configuration = new AppConfiguration();
        var folder = configuration.AddNavigationFolder("命令", null);
        for (var index = 1; index <= 11; index++)
        {
            configuration.AddRule(
                $"命令 {index:00}",
                ["command"],
                $@"C:\Commands\{index:00}",
                folder.Id);
        }

        var viewModel = new RuleExplorerViewModel(
            configuration,
            new RecordingMappingStore(configuration),
            new RecordingFolderOpener());

        viewModel.SearchText = "command";

        Assert.Equal("Ctrl+1", viewModel.SearchResults[0].ShortcutText);
        Assert.Equal("Ctrl+9", viewModel.SearchResults[8].ShortcutText);
        Assert.Equal(string.Empty, viewModel.SearchResults[9].ShortcutText);
    }

    [Fact]
    public async Task PinFolderAsync_PersistsAndRefreshesOrderedPins()
    {
        var configuration = CreateConfiguration();
        var store = new RecordingMappingStore(configuration);
        var viewModel = new RuleExplorerViewModel(
            configuration,
            store,
            new RecordingFolderOpener());
        var customer = configuration.NavigationFolders.Single(folder =>
            folder.Name == "客户");

        await viewModel.PinFolderAsync(customer.Id);

        Assert.Equal([customer.Id], configuration.PinnedFolderIds);
        Assert.Equal(["客户"], viewModel.PinnedFolders.Select(folder => folder.Name));
        Assert.Single(store.SavedConfigurations);
    }

    [Fact]
    public async Task PinFolderAsync_WhenSaveFails_RestoresConfigurationAndReportsError()
    {
        var configuration = CreateConfiguration();
        var store = new RecordingMappingStore(configuration)
        {
            SaveException = new IOException("disk full")
        };
        var viewModel = new RuleExplorerViewModel(
            configuration,
            store,
            new RecordingFolderOpener());
        var messages = new List<ExplorerStatusEventArgs>();
        viewModel.StatusReported += (_, args) => messages.Add(args);
        var customer = configuration.NavigationFolders.Single(folder =>
            folder.Name == "客户");

        await viewModel.PinFolderAsync(customer.Id);

        Assert.Empty(configuration.PinnedFolderIds);
        Assert.Empty(viewModel.PinnedFolders);
        Assert.Contains(messages, message =>
            message.Kind == StatusKind.Error
            && message.Message.Contains("disk full", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReorderPinnedFolderAsync_PersistsRequestedPosition()
    {
        var configuration = CreateConfiguration();
        var customer = configuration.NavigationFolders.Single(folder => folder.Name == "客户");
        var personal = configuration.NavigationFolders.Single(folder => folder.Name == "个人");
        configuration.PinFolder(customer.Id);
        configuration.PinFolder(personal.Id);
        var viewModel = new RuleExplorerViewModel(
            configuration,
            new RecordingMappingStore(configuration),
            new RecordingFolderOpener());

        await viewModel.ReorderPinnedFolderAsync(personal.Id, 0);

        Assert.Equal([personal.Id, customer.Id], configuration.PinnedFolderIds);
        Assert.Equal(["个人", "客户"], viewModel.PinnedFolders.Select(folder => folder.Name));
    }

    [Fact]
    public async Task CreateAndRenameFolderAsync_RefreshTreeAndRejectSameParentDuplicate()
    {
        var configuration = CreateConfiguration();
        var viewModel = new RuleExplorerViewModel(
            configuration,
            new RecordingMappingStore(configuration),
            new RecordingFolderOpener());
        var customer = configuration.NavigationFolders.Single(folder => folder.Name == "客户");

        var created = await viewModel.CreateFolderAsync(customer.Id, "交付");
        await viewModel.RenameFolderAsync(created.Id, "客户交付");
        var duplicate = await viewModel.CreateFolderAsync(customer.Id, "小溪");

        Assert.Equal("客户交付", configuration.NavigationFolders.Single(folder =>
            folder.Id == created.Id).Name);
        Assert.Equal(Guid.Empty, duplicate.Id);
        Assert.Contains(viewModel.RootFolders, node => node.Name == "客户");
    }

    [Fact]
    public async Task OpenRuleAsync_OpensPathAndPersistsLastUsedTime()
    {
        var configuration = CreateConfiguration();
        var opener = new RecordingFolderOpener();
        var store = new RecordingMappingStore(configuration);
        var viewModel = new RuleExplorerViewModel(
            configuration,
            store,
            opener,
            _ => true);
        var rule = configuration.Rules.Single(candidate =>
            candidate.DisplayTitle == "Quick Search 代码");

        await viewModel.OpenRuleAsync(rule);

        Assert.Equal([rule.FolderPath], opener.Paths);
        Assert.NotNull(configuration.Rules.Single(candidate => candidate.Id == rule.Id).LastUsedAtUtc);
        Assert.Single(store.SavedConfigurations);
    }

    [Fact]
    public async Task OpenRuleAsync_WhenPathIsMissing_DoesNotCallOpener()
    {
        var configuration = CreateConfiguration();
        var opener = new RecordingFolderOpener();
        var viewModel = new RuleExplorerViewModel(
            configuration,
            new RecordingMappingStore(configuration),
            opener,
            _ => false);
        var messages = new List<ExplorerStatusEventArgs>();
        viewModel.StatusReported += (_, args) => messages.Add(args);

        await viewModel.OpenRuleAsync(configuration.Rules[0]);

        Assert.Empty(opener.Paths);
        Assert.Contains(messages, message =>
            message.Kind == StatusKind.Error
            && message.Message.Contains("失效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MoveRuleAsync_PersistsNewFolderAndRemovesRuleFromCurrentFolder()
    {
        var configuration = CreateConfiguration();
        var store = new RecordingMappingStore(configuration);
        var viewModel = new RuleExplorerViewModel(
            configuration,
            store,
            new RecordingFolderOpener());
        var development = configuration.NavigationFolders.Single(folder =>
            folder.Name == "开发");
        var customer = configuration.NavigationFolders.Single(folder =>
            folder.Name == "客户");
        configuration.AddRule("客户资料", ["customer"], @"F:\Customers", customer.Id);
        var rule = configuration.Rules.Single(candidate =>
            candidate.DisplayTitle == "Quick Search 代码");
        viewModel.NavigateToFolder(development.Id);

        await viewModel.MoveRuleAsync(rule.Id, customer.Id);

        var movedRule = configuration.Rules.Single(candidate => candidate.Id == rule.Id);
        Assert.Equal(customer.Id, movedRule.NavigationFolderId);
        Assert.Equal(1, movedRule.SortOrder);
        Assert.Empty(viewModel.CurrentRules);
        Assert.Single(store.SavedConfigurations);
    }

    [Fact]
    public async Task MoveRuleAsync_WhenSaveFails_RestoresOriginalFolder()
    {
        var configuration = CreateConfiguration();
        var originalFolderId = configuration.Rules.Single().NavigationFolderId;
        var store = new RecordingMappingStore(configuration)
        {
            SaveException = new IOException("disk full")
        };
        var viewModel = new RuleExplorerViewModel(
            configuration,
            store,
            new RecordingFolderOpener());
        var customer = configuration.NavigationFolders.Single(folder =>
            folder.Name == "客户");
        var rule = configuration.Rules.Single();

        await viewModel.MoveRuleAsync(rule.Id, customer.Id);

        Assert.Equal(
            originalFolderId,
            configuration.Rules.Single(candidate => candidate.Id == rule.Id).NavigationFolderId);
        Assert.Empty(store.SavedConfigurations);
    }

    [Fact]
    public async Task CreateRuleAsync_PersistsAndShowsRuleInCurrentFolder()
    {
        var configuration = CreateConfiguration();
        var store = new RecordingMappingStore(configuration);
        var viewModel = new RuleExplorerViewModel(
            configuration,
            store,
            new RecordingFolderOpener());
        var personal = configuration.NavigationFolders.Single(folder => folder.Name == "个人");
        viewModel.NavigateToFolder(personal.Id);

        var created = await viewModel.CreateRuleAsync(
            personal.Id,
            "设计资料",
            ["design", "ui"],
            @"D:\Design");

        Assert.NotNull(created);
        Assert.Equal(personal.Id, created.NavigationFolderId);
        Assert.Equal(["design", "ui"], created.Aliases);
        Assert.Equal(created.Id, Assert.Single(viewModel.CurrentRules).Id);
        Assert.Single(store.SavedConfigurations);
    }

    [Fact]
    public async Task CreateRuleAsync_WhenSaveFails_DoesNotChangeLiveRules()
    {
        var configuration = CreateConfiguration();
        var originalRuleCount = configuration.Rules.Count;
        var store = new RecordingMappingStore(configuration)
        {
            SaveException = new IOException("disk full")
        };
        var viewModel = new RuleExplorerViewModel(
            configuration,
            store,
            new RecordingFolderOpener());

        var created = await viewModel.CreateRuleAsync(
            viewModel.CurrentFolder.Id,
            "设计资料",
            ["design"],
            @"D:\Design");

        Assert.Null(created);
        Assert.Equal(originalRuleCount, configuration.Rules.Count);
        Assert.Empty(store.SavedConfigurations);
    }

    [Fact]
    public void NavigateFolderCommand_UsesFolderParameterAndRefreshesVisibleContent()
    {
        var configuration = CreateConfiguration();
        var viewModel = new RuleExplorerViewModel(
            configuration,
            new RecordingMappingStore(configuration),
            new RecordingFolderOpener());
        var target = configuration.NavigationFolders.Single(folder => folder.Name == "开发");

        viewModel.NavigateFolderCommand.Execute(target);

        Assert.Equal(target.Id, viewModel.CurrentFolder.Id);
        Assert.All(
            viewModel.CurrentRules,
            rule => Assert.Equal(target.Id, rule.NavigationFolderId));
        var customerNode = viewModel.RootFolders.Single(node => node.Name == "客户");
        var creekNode = customerNode.Children.Single(node => node.Name == "小溪");
        var targetNode = creekNode.Children.Single(node => node.Name == "开发");
        Assert.True(customerNode.IsExpanded);
        Assert.True(creekNode.IsExpanded);
        Assert.True(targetNode.IsSelected);
    }

    private static AppConfiguration CreateConfiguration()
    {
        var configuration = new AppConfiguration();
        var customer = configuration.AddNavigationFolder("客户", parentId: null);
        var creek = configuration.AddNavigationFolder("小溪", customer.Id);
        var development = configuration.AddNavigationFolder("开发", creek.Id);
        configuration.AddNavigationFolder("个人", parentId: null);
        configuration.AddRule(
            "Quick Search 代码",
            ["quick search", "fastapi"],
            @"F:\Github\客户\小溪\quick-search",
            development.Id);
        return configuration;
    }

    private sealed class RecordingMappingStore(AppConfiguration configuration) : IMappingStore
    {
        public Exception? SaveException { get; init; }

        public List<AppConfiguration> SavedConfigurations { get; } = [];

        public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration.Clone());

        public Task SaveAsync(
            AppConfiguration candidate,
            CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedConfigurations.Add(candidate.Clone());
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFolderOpener : IFolderOpener
    {
        public List<string> Paths { get; } = [];

        public PlatformOperationResult Open(string folderPath)
        {
            Paths.Add(folderPath);
            return PlatformOperationResult.Succeeded();
        }
    }
}
