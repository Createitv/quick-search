using QuickSearch.Core;

var directory = Path.Combine(
    Path.GetTempPath(),
    $"QuickSearch-Smoke-{Guid.NewGuid():N}");

try
{
    var store = new JsonMappingStore(Path.Combine(directory, "config.json"));
    var configuration = new AppConfiguration();
    configuration.UpsertMapping(" Smoke User ", @"C:\Smoke\Folder");
    await store.SaveAsync(configuration);

    var loaded = await store.LoadAsync();
    var mapping = loaded.FindMapping("smoke user");
    if (mapping?.FolderPath != @"C:\Smoke\Folder")
    {
        Console.Error.WriteLine("QuickSearch smoke check returned an unexpected mapping.");
        return 2;
    }

    Console.WriteLine("QuickSearch configuration smoke check passed.");
    return 0;
}
finally
{
    if (Directory.Exists(directory))
    {
        Directory.Delete(directory, recursive: true);
    }
}
