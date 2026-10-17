using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Manages alert templates and alert display.
/// CRUD for alert templates persisted in the shared SQLite application database
/// (app.db, AlertTemplates table), migrated from the legacy alert-templates.json file.
/// Fires events when alerts are shown/dismissed for output rendering.
/// </summary>
/// <remarks>
/// Backing store migrated from a JSON file (alert-templates.json) to SQLite, mirroring
/// <see cref="SongUsageService"/>. The public <see cref="IAlertService"/> surface and all
/// observable behavior are preserved: <see cref="SaveTemplateAsync"/> assigns a GUID when
/// <see cref="AlertTemplate.Id"/> is empty and upserts in place, <see cref="GetTemplatesAsync"/>
/// returns a fresh copy, and <see cref="DeleteTemplateAsync"/> is a no-op for an absent id.
/// Display state (<see cref="CurrentAlert"/>, <see cref="IsAlertActive"/>) stays in-memory and
/// is NOT persisted. On first use, if a legacy alert-templates.json sits beside the database and
/// the AlertTemplates table is empty, its templates are imported once (defensively — never throws).
/// </remarks>
public class AlertService : IAlertService
{
    private readonly AppDatabase _database;
    private readonly string _legacyJsonPath;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string CreateSchemaSql = @"
CREATE TABLE IF NOT EXISTS AlertTemplates (
    Id              TEXT PRIMARY KEY,
    Name            TEXT NOT NULL DEFAULT '',
    OrganizationId  TEXT NOT NULL DEFAULT '',
    DataJson        TEXT NOT NULL
);
";

    /// <inheritdoc />
    public event EventHandler<AlertTemplate>? AlertShown;

    /// <inheritdoc />
    public event EventHandler? AlertDismissed;

    /// <summary>
    /// Whether an alert is currently being displayed.
    /// </summary>
    public bool IsAlertActive { get; private set; }

    /// <summary>
    /// The currently displayed alert (null if none).
    /// </summary>
    public AlertTemplate? CurrentAlert { get; private set; }

    /// <summary>
    /// Create the service against a database path (conceptually &lt;appFolder&gt;/app.db — the
    /// caller passes the path, exactly like <see cref="SongUsageService"/>). The legacy
    /// alert-templates.json is looked for as a sibling of the database file.
    /// </summary>
    public AlertService(string databasePath)
    {
        _database = new AppDatabase(databasePath);
        var dir = Path.GetDirectoryName(databasePath);
        _legacyJsonPath = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "alert-templates.json");
    }

    /// <inheritdoc />
    public Task ShowAlertAsync(AlertTemplate template)
    {
        CurrentAlert = template;
        IsAlertActive = true;
        AlertShown?.Invoke(this, template);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DismissAlertAsync()
    {
        CurrentAlert = null;
        IsAlertActive = false;
        AlertDismissed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<AlertTemplate> SaveTemplateAsync(AlertTemplate template)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrEmpty(template.Id))
            template.Id = Guid.NewGuid().ToString();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AlertTemplates (Id, Name, OrganizationId, DataJson)
            VALUES (@Id, @Name, @OrganizationId, @DataJson)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                OrganizationId = excluded.OrganizationId,
                DataJson = excluded.DataJson";
        BindTemplate(cmd, template);
        await cmd.ExecuteNonQueryAsync();

        return template;
    }

    /// <inheritdoc />
    public async Task<List<AlertTemplate>> GetTemplatesAsync()
    {
        await EnsureInitializedAsync();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DataJson FROM AlertTemplates";

        var results = new List<AlertTemplate>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var template = JsonSerializer.Deserialize<AlertTemplate>(reader.GetString(0), JsonOptions);
            if (template != null)
                results.Add(template);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task DeleteTemplateAsync(string templateId)
    {
        await EnsureInitializedAsync();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AlertTemplates WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", templateId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Create the AlertTemplates schema (idempotent) and run the one-time legacy JSON import.
    /// Runs once per process via the <see cref="_initialized"/> guard.
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
    /// Defensive one-time import: if the AlertTemplates table is empty AND a legacy
    /// alert-templates.json exists beside the database, import its templates once. Preserves
    /// historical ids (assigns a GUID only when missing). Tolerates a missing or malformed
    /// file — never throws. The empty-table guard makes this non-duplicating.
    /// </summary>
    private async Task ImportLegacyJsonIfNeededAsync(SqliteConnection connection)
    {
        try
        {
            if (!File.Exists(_legacyJsonPath))
                return;

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM AlertTemplates";
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
                if (count > 0)
                    return;
            }

            var json = await File.ReadAllTextAsync(_legacyJsonPath);
            var templates = JsonSerializer.Deserialize<List<AlertTemplate>>(json, JsonOptions);
            if (templates == null || templates.Count == 0)
                return;

            using var transaction = connection.BeginTransaction();
            foreach (var template in templates)
            {
                if (string.IsNullOrEmpty(template.Id))
                    template.Id = Guid.NewGuid().ToString();

                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT OR IGNORE INTO AlertTemplates (Id, Name, OrganizationId, DataJson)
                    VALUES (@Id, @Name, @OrganizationId, @DataJson)";
                BindTemplate(insertCmd, template);
                await insertCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            // A malformed/locked legacy file must never throw — matches the old
            // EnsureLoadedAsync catch behavior. Leave the table as-is (empty).
        }
    }

    private static void BindTemplate(SqliteCommand cmd, AlertTemplate template)
    {
        cmd.Parameters.AddWithValue("@Id", template.Id);
        cmd.Parameters.AddWithValue("@Name", template.Name ?? "");
        cmd.Parameters.AddWithValue("@OrganizationId", template.OrganizationId ?? "");
        cmd.Parameters.AddWithValue("@DataJson", JsonSerializer.Serialize(template, JsonOptions));
    }
}
