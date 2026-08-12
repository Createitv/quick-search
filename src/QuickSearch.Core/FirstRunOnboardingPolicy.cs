namespace QuickSearch.Core;

public static class FirstRunOnboardingPolicy
{
    public static bool ShouldShow(
        bool configurationExistedAtStartup,
        bool hasCompletedOnboarding) =>
        !configurationExistedAtStartup && !hasCompletedOnboarding;
}
