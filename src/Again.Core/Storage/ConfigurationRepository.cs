using System.Text.Json;
using Again.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Again.Core.Storage;

public interface IConfigurationRepository
{
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
}

public class ConfigurationRepository : IConfigurationRepository
{
    private readonly string _settingsFilePath;
    private readonly ILogger<ConfigurationRepository>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigurationRepository(string settingsFilePath, ILogger<ConfigurationRepository>? logger = null)
    {
        _settingsFilePath = settingsFilePath;
        _logger = logger;
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaultSettings = new AppSettings();
                await SaveInternalAsync(defaultSettings);
                return defaultSettings;
            }

            var json = await File.ReadAllTextAsync(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings from {Path}, returning defaults.", _settingsFilePath);
            return new AppSettings();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _semaphore.WaitAsync();
        try
        {
            await SaveInternalAsync(settings);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SaveInternalAsync(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = _settingsFilePath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, _settingsFilePath, overwrite: true);
        _logger?.LogInformation("Saved application settings to {Path}", _settingsFilePath);
    }
}
