using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages Presenter module settings persistence (presenter-settings.json).
/// </summary>
/// <remarks>
/// Consolidates the two previously-independent presenter-settings writers (the live
/// writer in PresenterIntegration and the dead static writer in MainWindow) into a
/// single file-backed owner of load + save. One canonical <see cref="JsonSerializerOptions"/>
/// (indented writes + case-insensitive reads + enums-as-strings) is a strict superset of
/// every prior serialization path, so observable behavior is preserved. Backing store is a
/// JSON file for now; it is NOT auto-created on a missing file (presenter settings never did).
/// </remarks>
public class PresenterSettingsService
{
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// The in-memory presenter settings. Writable so the live writer
    /// (PresenterIntegration) can hand the operator-mutated instance to the service
    /// before <see cref="SaveAsync"/> — the single persistence path.
    /// </summary>
    public PresenterSettings Settings { get; set; } = new();

    public PresenterSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Load settings from disk, or fall back to defaults.
    /// Missing or malformed files yield defaults and never throw.
    /// Unlike <see cref="SettingsService"/>, this does NOT auto-write a file on a missing path.
    /// </summary>
    public async Task LoadAsync()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                Settings = JsonSerializer.Deserialize<PresenterSettings>(json, _jsonOptions) ?? new PresenterSettings();
            }
            catch
            {
                Settings = new PresenterSettings();
            }
        }
        else
        {
            Settings = new PresenterSettings();
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
