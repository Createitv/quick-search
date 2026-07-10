namespace QuickSearch.Core.Tests;

public sealed class AppConfigurationTests
{
    [Fact]
    public void FindMapping_UsesExactNormalizedAlias()
    {
        var configuration = new AppConfiguration
        {
            Mappings =
            [
                new FolderMapping("Project Alpha", @"C:\Clients\Alpha")
            ]
        };

        var mapping = configuration.FindMapping("  PROJECT\talpha  ");

        Assert.NotNull(mapping);
        Assert.Equal(@"C:\Clients\Alpha", mapping.FolderPath);
        Assert.Null(configuration.FindMapping("Project Alpha Archive"));
    }

    [Fact]
    public void UpsertMapping_CreatesThenOverwritesSameNormalizedAlias()
    {
        var configuration = new AppConfiguration();

        configuration.UpsertMapping("Sales Team", @"C:\Sales\Old");
        var overwritten = configuration.UpsertMapping(
            "  SALES\tteam  ",
            @"D:\Sales\Current");

        Assert.Single(configuration.Mappings);
        Assert.Equal(@"D:\Sales\Current", overwritten.FolderPath);
        Assert.Same(overwritten, configuration.FindMapping("sales team"));
    }

    [Fact]
    public void GetMappingsForPath_ReturnsMultipleAliasesForOneFolder()
    {
        var configuration = new AppConfiguration();
        configuration.UpsertMapping("Northwind Billing", @"C:\Clients\Northwind");
        configuration.UpsertMapping("Northwind Invoices", @"C:\Clients\Northwind");

        var mappings = configuration.GetMappingsForPath(
            @"c:\clients\NORTHWIND");

        Assert.Equal(2, mappings.Count);
        Assert.Equal(
            ["Northwind Billing", "Northwind Invoices"],
            mappings.Select(mapping => mapping.Alias));
    }
}
