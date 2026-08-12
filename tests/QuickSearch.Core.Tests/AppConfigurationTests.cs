namespace QuickSearch.Core.Tests;

public sealed class AppConfigurationTests
{
    [Fact]
    public void AppSettings_DefaultsSchemaVersionToTwo()
    {
        Assert.Equal(2, new AppSettings().SchemaVersion);
    }

    [Fact]
    public void NavigationFolders_RejectCyclesButAllowSameNameInDifferentParents()
    {
        var configuration = new AppConfiguration();
        var customer = configuration.AddNavigationFolder("客户", parentId: null);
        var project = configuration.AddNavigationFolder("项目", customer.Id);
        var archive = configuration.AddNavigationFolder("归档", project.Id);
        var personal = configuration.AddNavigationFolder("个人", parentId: null);

        configuration.AddNavigationFolder("项目", personal.Id);

        Assert.Throws<InvalidOperationException>(() =>
            configuration.MoveNavigationFolder(customer.Id, archive.Id, 0));
        Assert.Throws<InvalidOperationException>(() =>
            configuration.AddNavigationFolder("项目", customer.Id));
    }

    [Fact]
    public void PinnedFolders_PreserveExplicitOrderWithoutDuplicates()
    {
        var configuration = new AppConfiguration();
        var customer = configuration.AddNavigationFolder("客户", parentId: null);
        var project = configuration.AddNavigationFolder("项目", parentId: null);

        configuration.PinFolder(customer.Id);
        configuration.PinFolder(project.Id);
        configuration.PinFolder(customer.Id);
        configuration.ReorderPinnedFolder(project.Id, 0);

        Assert.Equal([project.Id, customer.Id], configuration.PinnedFolderIds);
    }

    [Fact]
    public void AddRule_MergesAliasesForTheSamePhysicalPath()
    {
        var configuration = new AppConfiguration();
        var customer = configuration.AddNavigationFolder("客户", parentId: null);

        var first = configuration.AddRule(
            "文档",
            ["docs"],
            @"C:\Work\Docs",
            customer.Id);
        var merged = configuration.AddRule(
            "",
            ["manual", "DOCS"],
            @"c:\work\docs",
            customer.Id);

        Assert.Equal(first.Id, merged.Id);
        var rule = Assert.Single(configuration.Rules);
        Assert.Equal(["docs", "manual"], rule.Aliases);
        Assert.Single(configuration.FindMappings("docs"));
        Assert.Single(configuration.FindMappings("manual"));
    }

    [Fact]
    public void DirectConstruction_PreservesDifferentFoldersForSameNormalizedKeyword()
    {
        var configuration = new AppConfiguration
        {
            Mappings =
            [
                new FolderMapping(" Sales Team ", @"C:\Sales\Old"),
                new FolderMapping("SALES\tTEAM", @"D:\Sales\Current")
            ]
        };

        Assert.Equal(2, configuration.Mappings.Count);
        Assert.Equal(
            [@"C:\Sales\Old", @"D:\Sales\Current"],
            configuration.Mappings.Select(mapping => mapping.FolderPath));
    }

    [Fact]
    public void Mappings_ExposesAReadOnlyDefensiveView()
    {
        var suppliedMappings = new List<FolderMapping>
        {
            new("Sales Team", @"C:\Sales")
        };
        var configuration = new AppConfiguration
        {
            Mappings = suppliedMappings
        };

        suppliedMappings.Add(new FolderMapping("Support", @"C:\Support"));

        Assert.Equal(
            typeof(IReadOnlyList<FolderMapping>),
            typeof(AppConfiguration).GetProperty(nameof(AppConfiguration.Mappings))!.PropertyType);
        Assert.Single(configuration.Mappings);
        Assert.True(((ICollection<FolderMapping>)configuration.Mappings).IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<FolderMapping>)configuration.Mappings).Add(
                new FolderMapping("Finance", @"C:\Finance")));
    }

    [Fact]
    public void FindMapping_UsesExactNormalizedAlias()
    {
        var configuration = new AppConfiguration
        {
            Mappings =
            [
                new FolderMapping("Project Alpha", @"C:\Clients\Alpha")
            ]
        };

        var mapping = configuration.FindMapping("  PROJECT\talpha  ");

        Assert.NotNull(mapping);
        Assert.Equal(@"C:\Clients\Alpha", mapping.FolderPath);
        Assert.Null(configuration.FindMapping("Project Alpha Archive"));
    }

    [Fact]
    public void UpsertMapping_CreatesThenOverwritesSameNormalizedAlias()
    {
        var configuration = new AppConfiguration();

        configuration.UpsertMapping("Sales Team", @"C:\Sales\Old");
        var overwritten = configuration.UpsertMapping(
            "  SALES\tteam  ",
            @"D:\Sales\Current");

        Assert.Single(configuration.Mappings);
        Assert.Equal(@"D:\Sales\Current", overwritten.FolderPath);
        Assert.Equal(overwritten, configuration.FindMapping("sales team"));
    }

    [Fact]
    public void UpsertMapping_UsesDeterministicCreationAndUpdateMetadata()
    {
        var createdAtUtc = new DateTimeOffset(
            2026,
            7,
            10,
            1,
            2,
            3,
            TimeSpan.Zero);
        var updatedAtUtc = createdAtUtc.AddHours(2);
        var timeProvider = new SettableTimeProvider(createdAtUtc);
        var configuration = new AppConfiguration(timeProvider);

        var created = configuration.UpsertMapping(
            "Sales Team",
            @"C:\Sales\Old");

        Assert.Equal(createdAtUtc, created.CreatedAtUtc);
        Assert.Equal(createdAtUtc, created.UpdatedAtUtc);
        Assert.Null(created.LastUsedAtUtc);

        timeProvider.UtcNow = updatedAtUtc;
        var updated = configuration.UpsertMapping(
            "  SALES\tteam  ",
            @"D:\Sales\Current");

        Assert.Equal(createdAtUtc, updated.CreatedAtUtc);
        Assert.Equal(updatedAtUtc, updated.UpdatedAtUtc);
        Assert.Null(updated.LastUsedAtUtc);
    }

    [Fact]
    public void MarkMappingUsed_UpdatesLastUsedAndUpsertPreservesIt()
    {
        var createdAtUtc = new DateTimeOffset(
            2026,
            7,
            10,
            1,
            2,
            3,
            TimeSpan.Zero);
        var usedAtUtc = createdAtUtc.AddMinutes(15);
        var updatedAtUtc = createdAtUtc.AddHours(2);
        var timeProvider = new SettableTimeProvider(createdAtUtc);
        var configuration = new AppConfiguration(timeProvider);
        configuration.UpsertMapping("Sales Team", @"C:\Sales\Old");

        timeProvider.UtcNow = usedAtUtc;
        var used = configuration.MarkMappingUsed("  SALES\tteam  ");

        Assert.NotNull(used);
        Assert.Equal(createdAtUtc, used.CreatedAtUtc);
        Assert.Equal(createdAtUtc, used.UpdatedAtUtc);
        Assert.Equal(usedAtUtc, used.LastUsedAtUtc);
        Assert.Equal(used, configuration.FindMapping("sales team"));

        timeProvider.UtcNow = updatedAtUtc;
        var updated = configuration.UpsertMapping(
            "Sales Team",
            @"D:\Sales\Current");

        Assert.Equal(createdAtUtc, updated.CreatedAtUtc);
        Assert.Equal(updatedAtUtc, updated.UpdatedAtUtc);
        Assert.Equal(usedAtUtc, updated.LastUsedAtUtc);
    }

    [Fact]
    public void RemoveMapping_RemovesByNormalizedAliasWithoutExposingTheCollection()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("Sales Team", @"C:\Sales");

        var removed = configuration.RemoveMapping("  SALES\tteam  ");

        Assert.True(removed);
        Assert.Empty(configuration.Mappings);
        Assert.False(configuration.RemoveMapping("Sales Team"));
    }

    [Fact]
    public void GetMappingsForPath_ReturnsMultipleAliasesForOneFolder()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("Northwind Billing", @"C:\Clients\Northwind");
        configuration.UpsertMapping("Northwind Invoices", @"C:\Clients\Northwind");

        var mappings = configuration.GetMappingsForPath(
            @"c:\clients\NORTHWIND");

        Assert.Equal(2, mappings.Count);
        Assert.Equal(
            ["Northwind Billing", "Northwind Invoices"],
            mappings.Select(mapping => mapping.Alias));
    }

    [Fact]
    public void DirectConstruction_KeepsDifferentPathsForOneKeywordAndFoldsExactDuplicates()
    {
        var configuration = new AppConfiguration
        {
            Mappings =
            [
                new FolderMapping(" Sales Team ", @"C:\Sales\Current"),
                new FolderMapping("SALES\tTEAM", @"D:\Sales\Archive"),
                new FolderMapping("sales team", @"c:\sales\current")
            ]
        };

        var mappings = configuration.FindMappings(" sales team ");

        Assert.Equal(2, mappings.Count);
        Assert.Equal(
            [@"C:\Sales\Current", @"D:\Sales\Archive"],
            mappings.Select(mapping => mapping.FolderPath));
    }

    [Fact]
    public void AddMapping_AllowsOneKeywordToTargetMultipleFoldersWithoutExactDuplicates()
    {
        var configuration = new AppConfiguration();

        var current = configuration.AddMapping("project alpha", @"C:\Alpha");
        var archive = configuration.AddMapping(
            " PROJECT\tALPHA ",
            @"D:\Archive\Alpha");
        var duplicate = configuration.AddMapping(
            "project alpha",
            @"c:\alpha");

        Assert.Equal(2, configuration.FindMappings("project alpha").Count);
        Assert.Equal(current, duplicate);
        Assert.NotSame(current, archive);
    }

    [Fact]
    public void UpdateAndRemoveMapping_TargetOnlyOnePathForRepeatedKeyword()
    {
        var configuration = new AppConfiguration();
        configuration.AddMapping("sales", @"C:\Sales\Current");
        configuration.AddMapping("sales", @"D:\Sales\Archive");

        configuration.UpdateMapping(
            "sales",
            @"C:\Sales\Current",
            "sales",
            @"E:\Sales\Current");
        var removed = configuration.RemoveMapping(
            "sales",
            @"D:\Sales\Archive");

        Assert.True(removed);
        var mapping = Assert.Single(configuration.FindMappings("sales"));
        Assert.Equal(@"E:\Sales\Current", mapping.FolderPath);
    }

    [Fact]
    public void MarkMappingUsed_TargetsOnlyTheSuccessfullyOpenedPath()
    {
        var usedAtUtc = new DateTimeOffset(
            2026,
            7,
            10,
            6,
            30,
            0,
            TimeSpan.Zero);
        var timeProvider = new SettableTimeProvider(usedAtUtc.AddMinutes(-5));
        var configuration = new AppConfiguration(timeProvider);
        configuration.AddMapping("sales", @"C:\Sales\Current");
        configuration.AddMapping("sales", @"D:\Sales\Archive");
        timeProvider.UtcNow = usedAtUtc;

        configuration.MarkMappingUsed("sales", @"D:\Sales\Archive");

        var mappings = configuration.FindMappings("sales");
        Assert.Null(mappings.Single(mapping => mapping.FolderPath.StartsWith("C:")).LastUsedAtUtc);
        Assert.Equal(
            usedAtUtc,
            mappings.Single(mapping => mapping.FolderPath.StartsWith("D:")).LastUsedAtUtc);
    }

    private sealed class SettableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
