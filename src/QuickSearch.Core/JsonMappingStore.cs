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
            return JsonSerializer.Deserialize<AppConfiguration>(
                       json,
                       SerializerOptions)
                   ?? throw new JsonException("Configuration JSON was null.");
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
            File.Move(temporaryPath, _configPath, overwrite: true);
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
}
