using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages Presenter module settings persistence.
/// </summary>
/// <remarks>
/// Consolidates the two previously-independent presenter-settings writers (the live writer in
/// PresenterIntegration and the dead static writer in MainWindow) into a single owner of load +
/// save. One canonical <see cref="JsonSerializerOptions"/> (indented writes + case-insensitive
/// reads + enums-as-strings) is a strict superset of every prior serialization path, so observable
/// behavior is preserved. Backing store migrated from a JSON file (presenter-settings.json) to the
/// shared SQLite application database (app.db), reusing <see cref="AppDatabase"/> (no second db
/// helper, no EF; bible.db stays separate). The whole <see cref="PresenterSettings"/> object is
/// serialized to a single JSON text column in a single-row table (PresenterSettings, Id=1). On
/// first use, if the row is absent and a legacy presenter-settings.json sits beside the database,
/// it is imported once (defensively — never throws, non-duplicating). Unlike
/// <see cref="SettingsService"/>, this does NOT auto-create a record when both the row and the
/// legacy file are absent (presenter settings never auto-created one). After migration the JSON
/// file is no longer written and is left on disk.
/// </remarks>
public class PresenterSettingsService
{
    private readonly AppDatabase _database;
    private readonly string _legacyJsonPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _initialized;

    /// <summary>
    /// The in-memory presenter settings. Writable so the live writer
    /// (PresenterIntegration) can hand the operator-mutated instance to the service
    /// before <see cref="SaveAsync"/> — the single persistence path.
    /// </summary>
    public PresenterSettings Settings { get; set; } = new();

    private const string CreateSchemaSql =
        "CREATE TABLE IF NOT EXISTS PresenterSettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), DataJson TEXT NOT NULL);";

    /// <summary>
    /// Create the service against a database path (defaults to
    /// <see cref="AppDatabase.DefaultPath"/> when null — mirrors
    /// <see cref="MediaLibraryService"/>). The legacy presenter-settings.json is looked for as a
    /// sibling of the database file.
    /// </summary>
    public PresenterSettingsService(string? databasePath = null)
    {
        var path = databasePath ?? AppDatabase.DefaultPath;
        _database = new AppDatabase(path);
        var dir = Path.GetDirectoryName(path);
        _legacyJsonPath = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "presenter-settings.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Load settings from the database, or fall back to defaults.
    /// A missing row or malformed data yields defaults and never throws.
    /// Unlike <see cref="SettingsService"/>, this does NOT auto-write a record on a missing row.
    /// </summary>
    public async Task LoadAsync()
    {
        await EnsureInitializedAsync();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DataJson FROM PresenterSettings WHERE Id = 1";
        var json = await cmd.ExecuteScalarAsync() as string;

        if (!string.IsNullOrEmpty(json))
        {
            try
            {
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
    /// Save current settings to the database (single-row upsert).
    /// </summary>
    public async Task SaveAsync()
    {
        await EnsureInitializedAsync();

        var json = JsonSerializer.Serialize(Settings, _jsonOptions);

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO PresenterSettings (Id, DataJson) VALUES (1, @j) ON CONFLICT(Id) DO UPDATE SET DataJson = @j";
        cmd.Parameters.AddWithValue("@j", json);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Create the PresenterSettings schema (idempotent) and run the one-time legacy JSON import.
    /// Runs once per process via the <see cref="_initialized"/> guard. Does NOT auto-write a
    /// defaults row (presenter settings never auto-created one).
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        _database.EnsureDirectory();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText = CreateSchemaSql;
            await schemaCmd.ExecuteNonQueryAsync();
        }

        await ImportLegacyJsonIfNeededAsync(connection);

        _initialized = true;
    }

    /// <summary>
    /// Defensive one-time import: if the PresenterSettings row is absent AND a legacy
    /// presenter-settings.json exists beside the database, deserialize it and write the single
    /// row once. Tolerates a missing or malformed file — never throws. The empty-row guard makes
    /// this non-duplicating.
    /// </summary>
    private async Task ImportLegacyJsonIfNeededAsync(SqliteConnection connection)
    {
        try
        {
            if (!File.Exists(_legacyJsonPath))
                return;

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM PresenterSettings WHERE Id = 1";
                if (Convert.ToInt64(await countCmd.ExecuteScalarAsync()) > 0)
                    return;
            }

            var json = await File.ReadAllTextAsync(_legacyJsonPath);
            var settings = JsonSerializer.Deserialize<PresenterSettings>(json, _jsonOptions);
            if (settings == null)
                return;

            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
                "INSERT OR IGNORE INTO PresenterSettings (Id, DataJson) VALUES (1, @j)";
            insertCmd.Parameters.AddWithValue("@j", JsonSerializer.Serialize(settings, _jsonOptions));
            await insertCmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // A malformed/locked legacy file must never throw — matches the old LoadAsync catch
            // behavior. Leave the row absent so defaults are used.
        }
    }
}
