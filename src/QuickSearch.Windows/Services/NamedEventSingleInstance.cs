using QuickSearch.Core;

namespace QuickSearch.Windows;

internal sealed class NamedEventSingleInstance : ISingleInstanceService
{
    private const string MutexName = @"Local\QuickSearch.SingleInstance";
    private const string ActivationEventName = @"Local\QuickSearch.Activate";

    private readonly EventWaitHandle _activationEvent;
    private readonly Mutex _mutex;
    private RegisteredWaitHandle? _registeredWait;
    private bool _disposed;

    public NamedEventSingleInstance()
    {
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _mutex = new Mutex(true, MutexName, out var isPrimary);
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public PlatformOperationResult Listen(Action activation) =>
        PlatformBoundary.Capture(
            () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!IsPrimary)
                {
                    throw new InvalidOperationException("只有主实例可以监听激活信号。");
                }

                _registeredWait ??= ThreadPool.RegisterWaitForSingleObject(
                    _activationEvent,
                    (_, _) => activation(),
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);
            },
            "无法监听 QuickSearch 激活信号");

    public PlatformOperationResult NotifyExisting() =>
        PlatformBoundary.Capture(
            () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _activationEvent.Set();
            },
            "无法激活已运行的 QuickSearch");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registeredWait?.Unregister(null);
        _activationEvent.Dispose();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
