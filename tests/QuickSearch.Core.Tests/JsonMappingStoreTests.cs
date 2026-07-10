namespace QuickSearch.Core.Tests;

public sealed class JsonMappingStoreTests
{
    [Fact]
    public async Task LoadAsync_CanonicalizesDuplicateAliasesWithLastEntryWinning()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "settings": {},
              "mappings": [
                {
                  "alias": " Sales Team ",
                  "folderPath": "C:\\Sales\\Old"
                },
                {
                  "alias": "SALES\tTEAM",
                  "folderPath": "D:\\Sales\\Current"
                }
              ]
            }
            """);
        var store = new JsonMappingStore(configPath);

        var configuration = await store.LoadAsync();

        var mapping = Assert.Single(configuration.Mappings);
        Assert.Equal("SALES\tTEAM", mapping.Alias);
        Assert.Equal(@"D:\Sales\Current", mapping.FolderPath);
    }

    [Fact]
    public async Task LoadAsync_WhenConfigDoesNotExist_ReturnsDefaults()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "config.json");
        var store = new JsonMappingStore(configPath);

        var configuration = await store.LoadAsync();

        Assert.Equal("Ctrl+Alt+F", configuration.Settings.GlobalShortcut);
        Assert.True(configuration.Settings.StartWithWindows);
        Assert.Empty(configuration.Mappings);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsSettingsAndMappings()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "config.json");
        IMappingStore store = new JsonMappingStore(configPath);
        var configuration = new AppConfiguration
        {
            Settings = new AppSettings
            {
                GlobalShortcut = "Ctrl+Shift+G",
                StartWithWindows = false
            }
        };
        configuration.UpsertMapping("Northwind Billing", @"C:\Clients\Northwind");
        configuration.UpsertMapping("Contoso", @"D:\Clients\Contoso");

        await store.SaveAsync(configuration);
        var loaded = await store.LoadAsync();

        Assert.Equal("Ctrl+Shift+G", loaded.Settings.GlobalShortcut);
        Assert.False(loaded.Settings.StartWithWindows);
        Assert.Equal(configuration.Mappings, loaded.Mappings);
    }

    [Fact]
    public async Task SaveAsync_AtomicallyReplacesTheExistingConfigFile()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "config.json");
        var store = new JsonMappingStore(configPath);
        var original = new AppConfiguration();
        original.UpsertMapping("Original Alias", @"C:\Original");
        await store.SaveAsync(original);

        await using var originalFileHandle = new FileStream(
            configPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var replacement = new AppConfiguration();
        replacement.UpsertMapping("Replacement Alias", @"D:\Replacement");

        await store.SaveAsync(replacement);

        originalFileHandle.Position = 0;
        using var originalReader = new StreamReader(
            originalFileHandle,
            leaveOpen: true);
        var jsonFromOriginalHandle = await originalReader.ReadToEndAsync();
        var jsonAtConfigPath = await File.ReadAllTextAsync(configPath);
        Assert.Contains("Original Alias", jsonFromOriginalHandle);
        Assert.DoesNotContain("Replacement Alias", jsonFromOriginalHandle);
        Assert.Contains("Replacement Alias", jsonAtConfigPath);
        Assert.Empty(Directory.GetFiles(tempDirectory.Path, "*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_MovesCorruptJsonAsideAndReturnsDefaults()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "config.json");
        const string corruptJson = "{ definitely-not-json";
        await File.WriteAllTextAsync(configPath, corruptJson);
        var utcNow = new DateTimeOffset(
            2026,
            7,
            10,
            3,
            4,
            5,
            678,
            TimeSpan.Zero);
        var store = new JsonMappingStore(
            configPath,
            new FixedTimeProvider(utcNow));

        var configuration = await store.LoadAsync();

        Assert.Equal("Ctrl+Alt+F", configuration.Settings.GlobalShortcut);
        Assert.True(configuration.Settings.StartWithWindows);
        Assert.Empty(configuration.Mappings);
        Assert.False(File.Exists(configPath));
        var corruptPath = Path.Combine(
            tempDirectory.Path,
            "config.corrupt-20260710T030405678Z.json");
        Assert.Equal(corruptJson, await File.ReadAllTextAsync(corruptPath));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = Directory.CreateTempSubdirectory("QuickSearchTests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
