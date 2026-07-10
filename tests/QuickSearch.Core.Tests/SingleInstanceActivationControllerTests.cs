namespace QuickSearch.Core.Tests;

public sealed class SingleInstanceActivationControllerTests
{
    [Fact]
    public void Start_SecondaryInstanceNotifiesExistingInstance()
    {
        var service = new FakeSingleInstanceService(isPrimary: false);
        var controller = new SingleInstanceActivationController(service);

        var result = controller.Start(() => { });

        Assert.False(result.IsPrimary);
        Assert.True(result.Operation.Success);
        Assert.Equal(1, service.NotifyCalls);
        Assert.Equal(0, service.ListenCalls);
    }

    [Fact]
    public void Start_PrimaryInstanceForwardsActivationSignalToForegroundCallback()
    {
        var service = new FakeSingleInstanceService(isPrimary: true);
        var controller = new SingleInstanceActivationController(service);
        var activations = 0;

        var result = controller.Start(() => activations++);
        service.SignalActivation();

        Assert.True(result.IsPrimary);
        Assert.True(result.Operation.Success);
        Assert.Equal(1, activations);
        Assert.Equal(1, service.ListenCalls);
        Assert.Equal(0, service.NotifyCalls);
    }

    private sealed class FakeSingleInstanceService(bool isPrimary)
        : ISingleInstanceService
    {
        private Action? _activation;

        public bool IsPrimary { get; } = isPrimary;

        public int ListenCalls { get; private set; }

        public int NotifyCalls { get; private set; }

        public PlatformOperationResult Listen(Action activation)
        {
            ListenCalls++;
            _activation = activation;
            return PlatformOperationResult.Succeeded();
        }

        public PlatformOperationResult NotifyExisting()
        {
            NotifyCalls++;
            return PlatformOperationResult.Succeeded();
        }

        public void SignalActivation() => _activation?.Invoke();

        public void Dispose()
        {
        }
    }
}
