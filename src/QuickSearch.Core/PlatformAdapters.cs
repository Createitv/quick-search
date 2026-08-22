namespace QuickSearch.Core;

public interface IFolderOpener
{
    PlatformOperationResult Open(string folderPath);
}

public interface IPathOpener
{
    PlatformOperationResult Open(string path);
}

public interface IStartupRegistration
{
    PlatformOperationResult SetEnabled(bool enabled);
}

public static class PlatformBoundary
{
    public static PlatformOperationResult Capture(Action operation, string failurePrefix)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(failurePrefix);

        try
        {
            operation();
            return PlatformOperationResult.Succeeded();
        }
        catch (Exception exception)
        {
            return PlatformOperationResult.Failed($"{failurePrefix}：{exception.Message}");
        }
    }

    public static PlatformOperationResult<T> Capture<T>(
        Func<T> operation,
        string failurePrefix)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(failurePrefix);

        try
        {
            return PlatformOperationResult<T>.Succeeded(operation());
        }
        catch (Exception exception)
        {
            return PlatformOperationResult<T>.Failed(
                $"{failurePrefix}：{exception.Message}");
        }
    }
}
