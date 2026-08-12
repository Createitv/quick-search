namespace QuickSearch.Core.Tests;

public sealed class EverythingInstallationPathsTests
{
    [Theory]
    [InlineData(
        "\"C:\\Program Files\\Everything\\Everything.exe\",0",
        "C:\\Program Files\\Everything\\Everything.exe")]
    [InlineData(
        "C:\\Program Files\\Everything\\Everything.exe",
        "C:\\Program Files\\Everything\\Everything.exe")]
    [InlineData("  ", null)]
    public void NormalizeExecutablePath_RemovesRegistryDecoration(
        string input,
        string? expected)
    {
        Assert.Equal(expected, EverythingInstallationPaths.NormalizeExecutablePath(input));
    }

    [Fact]
    public void BuildCandidates_DeduplicatesPathsIgnoringCase()
    {
        var candidates = EverythingInstallationPaths.BuildCandidates(
            [
                "C:\\Program Files\\Everything\\Everything.exe",
                "c:\\program files\\everything\\EVERYTHING.EXE"
            ],
            "C:\\Program Files",
            "C:\\Program Files (x86)");

        Assert.Equal(2, candidates.Count);
        Assert.Equal(
            "C:\\Program Files\\Everything\\Everything.exe",
            candidates[0]);
        Assert.Equal(
            "C:\\Program Files (x86)\\Everything\\Everything.exe",
            candidates[1]);
    }

    [Fact]
    public void BuildCandidates_IgnoresNonExecutableRegistryValues()
    {
        var candidates = EverythingInstallationPaths.BuildCandidates(
            ["C:\\Program Files\\Everything", "unrelated"],
            null,
            null);

        Assert.Empty(candidates);
    }
}
