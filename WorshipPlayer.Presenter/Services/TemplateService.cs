using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Manages slide templates with CRUD operations.
/// Provides 3 built-in templates that cannot be deleted:
/// "Default Centered", "Lower Third", and "Trendy Line".
/// Persists CUSTOM templates in the shared SQLite application database (app.db,
/// SlideTemplates table), migrated from the legacy Templates folder of *.json files.
/// </summary>
/// <remarks>
/// Backing store migrated from a Templates/*.json folder to SQLite, mirroring
/// <see cref="SongUsageService"/>. The built-in templates stay code-seeded via
/// <see cref="GetBuiltInTemplates"/> and are NEVER stored as rows nor imported from legacy
/// files. <see cref="GetAllAsync"/> merges the code-seeded built-ins with custom rows,
/// dedupes by id, and forces <see cref="SlideTemplate.IsBuiltIn"/> false on every custom row.
/// On first use, if a legacy Templates folder sits beside the database and the SlideTemplates
/// table is empty, its custom templates are imported once (defensively — never throws;
/// built-in ids excluded).
/// </remarks>
public class TemplateService : ITemplateService
{
    private readonly AppDatabase _database;
    private readonly string _legacyTemplatesFolder;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // IDs for built-in templates (stable across sessions)
    private const string DefaultCenteredId = "builtin-default-centered";
    private const string LowerThirdId = "builtin-lower-third";
    private const string TrendyLineId = "builtin-trendy-line";

    private static readonly HashSet<string> BuiltInIds = new()
    {
        DefaultCenteredId,
        LowerThirdId,
        TrendyLineId
    };

    private const string CreateSchemaSql = @"
CREATE TABLE IF NOT EXISTS SlideTemplates (
    Id               TEXT PRIMARY KEY,
    Name             TEXT NOT NULL DEFAULT '',
    TemplateCategory TEXT NOT NULL DEFAULT 'General',
    OrganizationId   TEXT NOT NULL DEFAULT '',
    DataJson         TEXT NOT NULL
);
";

    /// <summary>
    /// Create the service against a database path (conceptually &lt;appFolder&gt;/app.db — the
    /// caller passes the path, exactly like <see cref="SongUsageService"/>). The legacy
    /// Templates folder is looked for as a sibling of the database file.
    /// </summary>
    public TemplateService(string databasePath)
    {
        _database = new AppDatabase(databasePath);
        var dir = Path.GetDirectoryName(databasePath);
        _legacyTemplatesFolder = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "Templates");
    }

    /// <inheritdoc />
    public async Task<List<SlideTemplate>> GetAllAsync()
    {
        await EnsureInitializedAsync();

        // Always start with the code-seeded built-ins, then append custom rows (deduped by id,
        // IsBuiltIn forced false) — mirrors the old EnsureLoadedAsync merge order.
        var result = GetBuiltInTemplates();
        var ids = new HashSet<string>(result.Select(t => t.Id));

        foreach (var custom in await ReadCustomTemplatesAsync())
        {
            if (ids.Add(custom.Id))
                result.Add(custom);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<SlideTemplate?> GetByIdAsync(string templateId)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(t => t.Id == templateId);
    }

    /// <inheritdoc />
    public async Task<List<SlideTemplate>> GetByCategoryAsync(TemplateCategory category)
    {
        var all = await GetAllAsync();
        return all.Where(t => t.TemplateCategory == category).ToList();
    }

    /// <inheritdoc />
    public async Task<SlideTemplate> CreateAsync(SlideTemplate template)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrEmpty(template.Id))
            template.Id = Guid.NewGuid().ToString();

        template.IsBuiltIn = false;

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SlideTemplates (Id, Name, TemplateCategory, OrganizationId, DataJson)
            VALUES (@Id, @Name, @TemplateCategory, @OrganizationId, @DataJson)";
        BindTemplate(cmd, template);
        await cmd.ExecuteNonQueryAsync();

        return template;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(SlideTemplate template)
    {
        await EnsureInitializedAsync();

        // Built-in → preserve the built-in flag and do NOT write to the DB (matches the old
        // "only save when !isBuiltIn" behavior).
        if (BuiltInIds.Contains(template.Id))
        {
            template.IsBuiltIn = true;
            return;
        }

        template.IsBuiltIn = false;

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE SlideTemplates SET
                Name = @Name,
                TemplateCategory = @TemplateCategory,
                OrganizationId = @OrganizationId,
                DataJson = @DataJson
            WHERE Id = @Id";
        BindTemplate(cmd, template);
        var affected = await cmd.ExecuteNonQueryAsync();

        // Neither a built-in nor an existing custom row → preserve the old "not found → throw".
        if (affected == 0)
            throw new InvalidOperationException($"Template '{template.Id}' not found.");
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string templateId)
    {
        await EnsureInitializedAsync();

        // Built-in templates cannot be deleted.
        if (BuiltInIds.Contains(templateId))
            return false;

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SlideTemplates WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", templateId);
        var affected = await cmd.ExecuteNonQueryAsync();

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<SlideTemplate> DuplicateAsync(string templateId, string newName)
    {
        await EnsureInitializedAsync();

        var source = await GetByIdAsync(templateId);
        if (source == null)
            throw new InvalidOperationException($"Template '{templateId}' not found.");

        // Deep copy via serialization
        var json = JsonSerializer.Serialize(source, JsonOptions);
        var duplicate = JsonSerializer.Deserialize<SlideTemplate>(json, JsonOptions)!;

        duplicate.Id = Guid.NewGuid().ToString();
        duplicate.Name = newName;
        duplicate.IsBuiltIn = false;

        return await CreateAsync(duplicate);
    }

    /// <inheritdoc />
    public async Task<List<SlideTemplate>> GetBuiltInTemplatesAsync()
    {
        await EnsureInitializedAsync();
        return GetBuiltInTemplates();
    }

    /// <inheritdoc />
    public async Task ApplyToItemAsync(string templateId, string itemId)
    {
        await EnsureInitializedAsync();

        var template = await GetByIdAsync(templateId);
        if (template == null)
            throw new InvalidOperationException($"Template '{templateId}' not found.");

        // The actual application to an item would be handled by the caller
        // using the template data. This validates the template exists.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Get the 3 built-in templates.
    /// </summary>
    public static List<SlideTemplate> GetBuiltInTemplates()
    {
        return new List<SlideTemplate>
        {
            new SlideTemplate
            {
                Id = DefaultCenteredId,
                Name = "Default Centered",
                IsBuiltIn = true,
                FontFamily = "Segoe UI",
                FontSize = 60,
                FontWeight = "Bold",
                FontColor = "#FFFFFF",
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HasShadow = true,
                ShadowColor = "#000000",
                ShadowBlur = 4.0,
                HasOutline = false,
                MarginTop = 40,
                MarginLeft = 60,
                MarginRight = 60,
                MarginBottom = 40,
                LineHeight = 1.3,
                TemplateCategory = TemplateCategory.General
            },
            new SlideTemplate
            {
                Id = LowerThirdId,
                Name = "Lower Third",
                IsBuiltIn = true,
                FontFamily = "Segoe UI",
                FontSize = 48,
                FontWeight = "Bold",
                FontColor = "#FFFFFF",
                HorizontalAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                HasShadow = true,
                ShadowColor = "#000000",
                ShadowBlur = 6.0,
                HasOutline = false,
                MarginTop = 40,
                MarginLeft = 60,
                MarginRight = 60,
                MarginBottom = 80,
                LineHeight = 1.2,
                LineBackgroundEnabled = false,
                TemplateCategory = TemplateCategory.LowerThird
            },
            new SlideTemplate
            {
                Id = TrendyLineId,
                Name = "Trendy Line",
                IsBuiltIn = true,
                FontFamily = "Segoe UI",
                FontSize = 56,
                FontWeight = "Bold",
                FontColor = "#FFFFFF",
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HasShadow = false,
                HasOutline = false,
                LineBackgroundEnabled = true,
                LineBackgroundColor = "#000000",
                LineBackgroundOpacity = 0.5,
                MarginTop = 40,
                MarginLeft = 80,
                MarginRight = 80,
                MarginBottom = 40,
                LineHeight = 1.6,
                TemplateCategory = TemplateCategory.General
            }
        };
    }

    /// <summary>
    /// Read all CUSTOM slide templates from the SlideTemplates table. Each row's
    /// <see cref="SlideTemplate.IsBuiltIn"/> is forced false (custom rows are never built-ins).
    /// </summary>
    private async Task<List<SlideTemplate>> ReadCustomTemplatesAsync()
    {
        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DataJson FROM SlideTemplates";

        var results = new List<SlideTemplate>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var template = JsonSerializer.Deserialize<SlideTemplate>(reader.GetString(0), JsonOptions);
            if (template != null)
            {
                template.IsBuiltIn = false;
                results.Add(template);
            }
        }

        return results;
    }

    /// <summary>
    /// Create the SlideTemplates schema (idempotent) and run the one-time legacy folder import.
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

        await ImportLegacyTemplatesIfNeededAsync(connection);

        _initialized = true;
    }

    /// <summary>
    /// Defensive one-time import: if the SlideTemplates table is empty AND a legacy Templates
    /// folder exists beside the database, import its custom *.json templates once. Built-in ids
    /// are skipped (built-ins are never rows), <see cref="SlideTemplate.IsBuiltIn"/> is forced
    /// false, and the folder is NOT created here. Tolerates missing/malformed files — never
    /// throws. The empty-table guard makes this non-duplicating.
    /// </summary>
    private async Task ImportLegacyTemplatesIfNeededAsync(SqliteConnection connection)
    {
        try
        {
            if (!Directory.Exists(_legacyTemplatesFolder))
                return;

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM SlideTemplates";
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
                if (count > 0)
                    return;
            }

            var files = Directory.GetFiles(_legacyTemplatesFolder, "*.json");
            using var transaction = connection.BeginTransaction();
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var template = JsonSerializer.Deserialize<SlideTemplate>(json, JsonOptions);
                    if (template == null)
                        continue;

                    // Built-ins are never imported as rows.
                    if (BuiltInIds.Contains(template.Id))
                        continue;

                    template.IsBuiltIn = false;

                    using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"
                        INSERT OR IGNORE INTO SlideTemplates (Id, Name, TemplateCategory, OrganizationId, DataJson)
                        VALUES (@Id, @Name, @TemplateCategory, @OrganizationId, @DataJson)";
                    BindTemplate(insertCmd, template);
                    await insertCmd.ExecuteNonQueryAsync();
                }
                catch
                {
                    // Skip an individual invalid template file (mirrors the old per-file catch).
                }
            }

            transaction.Commit();
        }
        catch
        {
            // The whole import must never throw. Leave the table as-is (empty).
        }
    }

    private static void BindTemplate(SqliteCommand cmd, SlideTemplate template)
    {
        cmd.Parameters.AddWithValue("@Id", template.Id);
        cmd.Parameters.AddWithValue("@Name", template.Name ?? "");
        cmd.Parameters.AddWithValue("@TemplateCategory", template.TemplateCategory.ToString());
        cmd.Parameters.AddWithValue("@OrganizationId", template.OrganizationId ?? "");
        cmd.Parameters.AddWithValue("@DataJson", JsonSerializer.Serialize(template, JsonOptions));
    }
}
