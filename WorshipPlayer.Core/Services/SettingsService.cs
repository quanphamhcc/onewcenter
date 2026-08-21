using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages application settings persistence.
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public AppSettings Settings { get; private set; } = new();

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Load settings from disk, or create defaults.
    /// </summary>
    public async Task LoadAsync()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            }
            catch
            {
                Settings = new AppSettings();
            }
        }
        else
        {
            Settings = new AppSettings();
            await SaveAsync();
        }
    }

    /// <summary>
    /// Save current settings to disk.
    /// </summary>
    public async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(Settings, _jsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}
