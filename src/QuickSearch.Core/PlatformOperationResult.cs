namespace QuickSearch.Core;

public readonly record struct PlatformOperationResult(bool Success, string Message)
{
    public static PlatformOperationResult Succeeded() => new(true, string.Empty);

    public static PlatformOperationResult Failed(string message) => new(false, message);
}

public readonly record struct PlatformOperationResult<T>(
    bool Success,
    T? Value,
    string Message)
{
    public static PlatformOperationResult<T> Succeeded(T? value) =>
        new(true, value, string.Empty);

    public static PlatformOperationResult<T> Failed(string message) =>
        new(false, default, message);
}
