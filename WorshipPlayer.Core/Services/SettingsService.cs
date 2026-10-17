using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages application settings persistence.
/// </summary>
/// <remarks>
/// Backing store migrated from a JSON file (settings.json) to the shared SQLite application
/// database (app.db), reusing <see cref="AppDatabase"/> (no second db helper, no EF; bible.db
/// stays separate). The whole <see cref="AppSettings"/> object is serialized to a single JSON
/// text column in a single-row table (AppSettings, Id=1). The public API
/// (<see cref="Settings"/>, <see cref="LoadAsync"/>, <see cref="SaveAsync"/>) and all observable
/// behavior are preserved, including auto-creation of a defaults record on first use. On first
/// use, if the row is absent and a legacy settings.json sits beside the database, it is imported
/// once (defensively — never throws, non-duplicating). After migration the JSON file is no longer
/// written and is left on disk. Enums are serialized as their string names inside the JSON column.
/// </remarks>
public class SettingsService
{
    private readonly AppDatabase _database;
    private readonly string _legacyJsonPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _initialized;

    public AppSettings Settings { get; private set; } = new();

    private const string CreateSchemaSql =
        "CREATE TABLE IF NOT EXISTS AppSettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), DataJson TEXT NOT NULL);";

    /// <summary>
    /// Create the service against a database path (defaults to
    /// <see cref="AppDatabase.DefaultPath"/> when null — mirrors
    /// <see cref="MediaLibraryService"/>). The legacy settings.json is looked for as a sibling
    /// of the database file.
    /// </summary>
    public SettingsService(string? databasePath = null)
    {
        var path = databasePath ?? AppDatabase.DefaultPath;
        _database = new AppDatabase(path);
        var dir = Path.GetDirectoryName(path);
        _legacyJsonPath = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "settings.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Load settings from the database, or create defaults.
    /// </summary>
    public async Task LoadAsync()
    {
        await EnsureInitializedAsync();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DataJson FROM AppSettings WHERE Id = 1";
        var json = await cmd.ExecuteScalarAsync() as string;

        if (!string.IsNullOrEmpty(json))
        {
            try
            {
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
            "INSERT INTO AppSettings (Id, DataJson) VALUES (1, @j) ON CONFLICT(Id) DO UPDATE SET DataJson = @j";
        cmd.Parameters.AddWithValue("@j", json);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Create the AppSettings schema (idempotent), run the one-time legacy JSON import, and
    /// preserve the former auto-create-on-missing behavior by writing a defaults row when neither
    /// a row nor a legacy file exists. Runs once per process via the <see cref="_initialized"/>
    /// guard.
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

        // Preserve the old auto-create-on-missing behavior: if no row exists (neither a migrated
        // legacy file nor a prior save), write a defaults row so the store is always present.
        if (!await RowExistsAsync(connection))
            await WriteRowAsync(connection, JsonSerializer.Serialize(new AppSettings(), _jsonOptions));

        _initialized = true;
    }

    /// <summary>
    /// Defensive one-time import: if the AppSettings row is absent AND a legacy settings.json
    /// exists beside the database, deserialize it and write the single row once. Tolerates a
    /// missing or malformed file — never throws. The empty-row guard makes this non-duplicating.
    /// </summary>
    private async Task ImportLegacyJsonIfNeededAsync(SqliteConnection connection)
    {
        try
        {
            if (!File.Exists(_legacyJsonPath))
                return;

            if (await RowExistsAsync(connection))
                return;

            var json = await File.ReadAllTextAsync(_legacyJsonPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            if (settings == null)
                return;

            await WriteRowAsync(connection, JsonSerializer.Serialize(settings, _jsonOptions));
        }
        catch
        {
            // A malformed/locked legacy file must never throw — matches the old LoadAsync catch
            // behavior. Leave the row absent so defaults are written below.
        }
    }

    private static async Task<bool> RowExistsAsync(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AppSettings WHERE Id = 1";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task WriteRowAsync(SqliteConnection connection, string json)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO AppSettings (Id, DataJson) VALUES (1, @j) ON CONFLICT(Id) DO UPDATE SET DataJson = @j";
        cmd.Parameters.AddWithValue("@j", json);
        await cmd.ExecuteNonQueryAsync();
    }
}
