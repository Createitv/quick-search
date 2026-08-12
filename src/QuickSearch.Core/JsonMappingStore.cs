using System.Globalization;
using System.Text.Json;

namespace QuickSearch.Core;

public sealed class JsonMappingStore : IMappingStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _configPath;
    private readonly TimeProvider _timeProvider;

    public JsonMappingStore(string configPath)
        : this(configPath, TimeProvider.System)
    {
    }

    public JsonMappingStore(string configPath, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _configPath = Path.GetFullPath(configPath);
        _timeProvider = timeProvider;
    }

    public async Task<AppConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configPath))
        {
            return new AppConfiguration();
        }

        var json = await File.ReadAllTextAsync(_configPath, cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (HasProperty(document.RootElement, "rules"))
            {
                var configuration = JsonSerializer.Deserialize<AppConfiguration>(
                                        json,
                                        SerializerOptions)
                                    ?? throw new JsonException("Configuration JSON was null.");
                return ConfigurationMigrator.Upgrade(configuration);
            }

            var legacy = JsonSerializer.Deserialize<LegacyConfiguration>(
                             json,
                             SerializerOptions)
                         ?? throw new JsonException("Configuration JSON was null.");
            return ConfigurationMigrator.FromLegacy(
                legacy.Settings with { SchemaVersion = 1 },
                legacy.Mappings,
                _timeProvider);
        }
        catch (JsonException)
        {
            var corruptPath = GetCorruptPath();
            File.Move(_configPath, corruptPath);
            return new AppConfiguration();
        }
    }

    public async Task SaveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var directoryPath = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directoryPath);
        if (configuration.Settings.SchemaVersion < 3)
        {
            ConfigurationMigrator.Upgrade(configuration);
        }

        var json = JsonSerializer.Serialize(configuration, SerializerOptions);
        var temporaryPath = Path.Combine(
            directoryPath,
            $"{Path.GetFileName(_configPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                cancellationToken);
            if (File.Exists(_configPath))
            {
                File.Replace(
                    temporaryPath,
                    _configPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _configPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetCorruptPath()
    {
        var directoryPath = Path.GetDirectoryName(_configPath)!;
        var baseName = Path.GetFileNameWithoutExtension(_configPath);
        var extension = Path.GetExtension(_configPath);
        var timestamp = _timeProvider
            .GetUtcNow()
            .ToUniversalTime()
            .ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);

        return Path.Combine(
            directoryPath,
            $"{baseName}.corrupt-{timestamp}{extension}");
    }

    private static bool HasProperty(JsonElement element, string propertyName) =>
        element.EnumerateObject().Any(property => string.Equals(
            property.Name,
            propertyName,
            StringComparison.OrdinalIgnoreCase));

    private sealed class LegacyConfiguration
    {
        public AppSettings Settings { get; init; } = new() { SchemaVersion = 1 };

        public IReadOnlyList<FolderMapping> Mappings { get; init; } = [];
    }
}
