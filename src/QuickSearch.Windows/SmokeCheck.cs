using System.IO;
using QuickSearch.Core;

namespace QuickSearch.Windows;

internal static class SmokeCheck
{
    internal static int Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"QuickSearch-Smoke-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonMappingStore(Path.Combine(directory, "config.json"));
            var configuration = new AppConfiguration();
            configuration.UpsertMapping(" Smoke User ", @"C:\Smoke\Folder");
            store.SaveAsync(configuration).GetAwaiter().GetResult();
            var loaded = store.LoadAsync().GetAwaiter().GetResult();
            var mapping = loaded.FindMapping("smoke user");
            return mapping?.FolderPath == @"C:\Smoke\Folder" ? 0 : 2;
        }
        catch
        {
            return 1;
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
