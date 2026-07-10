namespace QuickSearch.Core.Tests;

public sealed class FolderSearchRankerTests
{
    [Fact]
    public void Rank_OrdersExactPrefixAndSubstringMatchesWithStablePathTieBreak()
    {
        FolderSearchResult[] candidates =
        [
            new("Archived Invoices", @"C:\Archive\Archived Invoices"),
            new("Invoices 2025", @"D:\Clients\Invoices 2025"),
            new("Invoices", @"D:\Clients\Invoices"),
            new("Unrelated", @"A:\Unrelated"),
            new("invoices", @"C:\Clients\Invoices"),
            new("Invoices 2026", @"C:\Clients\Invoices 2026")
        ];

        var ranked = FolderSearchRanker.Rank(candidates, "  INVOICES  ");

        Assert.Equal(
            [
                @"C:\Clients\Invoices",
                @"D:\Clients\Invoices",
                @"C:\Clients\Invoices 2026",
                @"D:\Clients\Invoices 2025",
                @"C:\Archive\Archived Invoices"
            ],
            ranked.Select(result => result.FullPath));
    }

    [Fact]
    public void Rank_ReturnsAtMostTwentyResults()
    {
        var candidates = Enumerable.Range(0, 25)
            .Reverse()
            .Select(index => new FolderSearchResult(
                "Invoices",
                $@"C:\Clients\{index:00}"));

        var ranked = FolderSearchRanker.Rank(candidates, "Invoices");

        Assert.Equal(20, ranked.Count);
        Assert.Equal(@"C:\Clients\00", ranked[0].FullPath);
        Assert.Equal(@"C:\Clients\19", ranked[^1].FullPath);
    }
}
