namespace QuickSearch.Core.Tests;

public sealed class EverythingQueryBuilderTests
{
    [Fact]
    public void Build_TrimsAndQuotesLiteralFolderQuery()
    {
        Assert.Equal(
            "folder: nowildcards:\"Annual Reports\"",
            EverythingQueryBuilder.Build("  Annual Reports  "));
    }

    [Fact]
    public void Build_EncodesQuotesInsteadOfAllowingSearchSyntaxInjection()
    {
        Assert.Equal(
            "folder: nowildcards:<\"Alice\" #x22: \" OR file:\">",
            EverythingQueryBuilder.Build("Alice\" OR file:"));
    }

    [Fact]
    public void Build_RejectsEmptyQuery()
    {
        Assert.Throws<ArgumentException>(() => EverythingQueryBuilder.Build("  "));
    }
}
