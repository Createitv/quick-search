namespace QuickSearch.Core.Tests;

public sealed class UpdateCheckPolicyTests
{
    [Fact]
    public void ShouldCheckAutomatically_ReturnsFalseWhenDisabled()
    {
        Assert.False(UpdateCheckPolicy.ShouldCheckAutomatically(enabled: false));
    }

    [Fact]
    public void ShouldCheckAutomatically_ChecksOnEveryStartupWhenEnabled()
    {
        Assert.True(UpdateCheckPolicy.ShouldCheckAutomatically(enabled: true));
    }
}
