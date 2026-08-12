namespace QuickSearch.Core.Tests;

public sealed class ConfigurationMigratorTests
{
    [Fact]
    public void FromLegacy_GroupsAliasesByPathWithoutChangingKeywordTargets()
    {
        var created = new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero);
        var configuration = ConfigurationMigrator.FromLegacy(
            new AppSettings { SchemaVersion = 1 },
            [
                new FolderMapping("docs", @"C:\Work\Docs", created, created, null),
                new FolderMapping(
                    "manual",
                    @"c:\work\docs",
                    created.AddMinutes(1),
                    created.AddMinutes(2),
                    null),
                new FolderMapping("docs", @"D:\Archive\Docs", created, created, null)
            ],
            new FixedTimeProvider(created.AddDays(1)));

        Assert.Equal(2, configuration.Settings.SchemaVersion);
        Assert.Equal(2, configuration.Rules.Count);
        Assert.Equal(2, configuration.FindMappings("docs").Count);
        Assert.Equal(
            ["docs", "manual"],
            configuration.Rules.Single(rule =>
                rule.FolderPath.StartsWith("C:", StringComparison.OrdinalIgnoreCase)).Aliases);
        Assert.All(
            configuration.Rules,
            rule => Assert.Equal(
                configuration.UncategorizedFolderId,
                rule.NavigationFolderId));
    }

    [Fact]
    public void FromLegacy_AggregatesRuleMetadataWithoutLosingLastUsedTime()
    {
        var earliest = new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero);
        var latest = earliest.AddDays(2);
        var lastUsed = latest.AddHours(3);

        var configuration = ConfigurationMigrator.FromLegacy(
            new AppSettings { SchemaVersion = 1 },
            [
                new FolderMapping("first", @"C:\Docs", earliest, earliest, null),
                new FolderMapping("second", @"C:\Docs", latest, latest, lastUsed)
            ],
            new FixedTimeProvider(latest));

        var rule = Assert.Single(configuration.Rules);
        Assert.Equal(earliest, rule.CreatedAtUtc);
        Assert.Equal(latest, rule.UpdatedAtUtc);
        Assert.Equal(lastUsed, rule.LastUsedAtUtc);
        Assert.Equal("Docs", rule.DisplayTitle);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
