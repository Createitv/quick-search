namespace QuickSearch.Core.Tests;

public sealed class AliasNormalizerTests
{
    [Theory]
    [InlineData("  Alice.SMITH  ", "alice.smith")]
    [InlineData("  Sales.\tTeam \r\n WEST  ", "sales. team west")]
    public void Normalize_TrimsCollapsesWhitespaceAndIgnoresLatinCase(
        string alias,
        string expected)
    {
        Assert.Equal(expected, AliasNormalizer.Normalize(alias));
    }
}
