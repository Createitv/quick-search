namespace QuickSearch.Core;

public interface ISingleInstanceService : IDisposable
{
    bool IsPrimary { get; }

    PlatformOperationResult Listen(Action activation);

    PlatformOperationResult NotifyExisting();
}

public readonly record struct SingleInstanceStartResult(
    bool IsPrimary,
    PlatformOperationResult Operation);

public sealed class SingleInstanceActivationController(
    ISingleInstanceService service)
{
    public SingleInstanceStartResult Start(Action activation)
    {
        ArgumentNullException.ThrowIfNull(activation);

        return service.IsPrimary
            ? new SingleInstanceStartResult(true, service.Listen(activation))
            : new SingleInstanceStartResult(false, service.NotifyExisting());
    }
}
