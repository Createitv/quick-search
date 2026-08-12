namespace QuickSearch.Core.Tests;

public sealed class UpdateCheckPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldCheckAutomatically_ReturnsFalseWhenDisabled()
    {
        Assert.False(UpdateCheckPolicy.ShouldCheckAutomatically(
            enabled: false,
            lastCheckedAtUtc: null,
            Now));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("2026-08-11T09:59:59Z", true)]
    [InlineData("2026-08-11T10:00:01Z", false)]
    [InlineData("2026-08-12T09:59:59Z", false)]
    public void ShouldCheckAutomatically_RequiresTwentyFourHoursBetweenChecks(
        string? lastChecked,
        bool expected)
    {
        var parsed = lastChecked is null
            ? (DateTimeOffset?)null
            : DateTimeOffset.Parse(lastChecked);

        Assert.Equal(
            expected,
            UpdateCheckPolicy.ShouldCheckAutomatically(true, parsed, Now));
    }
}
