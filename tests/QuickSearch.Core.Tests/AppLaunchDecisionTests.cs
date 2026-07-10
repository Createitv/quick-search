namespace QuickSearch.Core.Tests;

public sealed class AppLaunchDecisionTests
{
    [Theory]
    [InlineData(true, false, AppLaunchDisposition.StartPrimary)]
    [InlineData(true, true, AppLaunchDisposition.StartPrimary)]
    [InlineData(false, false, AppLaunchDisposition.NotifyExistingAndExit)]
    [InlineData(false, true, AppLaunchDisposition.ExitSilently)]
    public void Decide_BackgroundSecondaryExitsSilentlyWhileManualSecondaryActivatesPrimary(
        bool isPrimary,
        bool backgroundRequested,
        AppLaunchDisposition expected)
    {
        Assert.Equal(
            expected,
            AppLaunchDecision.Decide(isPrimary, backgroundRequested));
    }
}
