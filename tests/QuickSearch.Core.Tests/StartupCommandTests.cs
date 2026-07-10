namespace QuickSearch.Core.Tests;

public sealed class StartupCommandTests
{
    [Fact]
    public void Build_QuotesExecutableAndAddsBackgroundArgument()
    {
        var command = StartupCommand.Build(
            @"C:\Program Files\QuickSearch\QuickSearch.exe");

        Assert.Equal(
            "\"C:\\Program Files\\QuickSearch\\QuickSearch.exe\" --background",
            command);
    }
}
