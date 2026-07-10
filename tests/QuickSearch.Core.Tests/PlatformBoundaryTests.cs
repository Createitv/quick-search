namespace QuickSearch.Core.Tests;

public sealed class PlatformBoundaryTests
{
    [Fact]
    public void Capture_ReturnsExplicitFailureWithUnexpectedExceptionMessage()
    {
        var result = PlatformBoundary.Capture(
            () =>
            {
                throw new InvalidOperationException("adapter exploded");
            },
            "无法执行平台操作");

        Assert.False(result.Success);
        Assert.Contains("无法执行平台操作", result.Message);
        Assert.Contains("adapter exploded", result.Message);
    }

    [Fact]
    public void CaptureValue_ReturnsAdapterValueOnSuccess()
    {
        var result = PlatformBoundary.Capture(() => "clipboard text", "无法读取");

        Assert.True(result.Success);
        Assert.Equal("clipboard text", result.Value);
        Assert.Equal(string.Empty, result.Message);
    }
}
