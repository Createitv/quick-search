namespace QuickSearch.Core;

public enum AppLaunchDisposition
{
    StartPrimary,
    NotifyExistingAndExit,
    ExitSilently
}

public static class AppLaunchDecision
{
    public static AppLaunchDisposition Decide(
        bool isPrimary,
        bool backgroundRequested) =>
        isPrimary
            ? AppLaunchDisposition.StartPrimary
            : backgroundRequested
                ? AppLaunchDisposition.ExitSilently
                : AppLaunchDisposition.NotifyExistingAndExit;
}
