namespace QuickSearch.Core.Tests;

public sealed class AppReleaseVersionTests
{
    [Theory]
    [InlineData("v0.0.2", "0.0.2")]
    [InlineData("1.4.12", "1.4.12")]
    [InlineData("2.0.0+build.7", "2.0.0")]
    public void Parse_AcceptsReleaseTagsAndNormalizesDisplayVersion(
        string input,
        string expected)
    {
        var version = AppReleaseVersion.Parse(input);

        Assert.Equal(expected, version.ToString());
    }

    [Fact]
    public void Parse_RejectsPrereleaseVersionsThatMustNotBeAutoInstalled()
    {
        Assert.Throws<FormatException>(() => AppReleaseVersion.Parse("v1.0.0-beta.1"));
    }

    [Theory]
    [InlineData("0.0.3", "0.0.2", true)]
    [InlineData("0.0.2", "0.0.2", false)]
    [InlineData("0.0.1", "0.0.2", false)]
    [InlineData("1.0.0", "0.99.99", true)]
    public void IsNewerThan_UsesNumericVersionOrdering(
        string candidate,
        string installed,
        bool expected)
    {
        Assert.Equal(
            expected,
            AppReleaseVersion.Parse(candidate).IsNewerThan(
                AppReleaseVersion.Parse(installed)));
    }
}
