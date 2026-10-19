using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// CRUD store for Slides-library presentations (<see cref="SlidePresentation"/>), persisted as
/// rows in the shared SQLite application database (app.db, SlidePresentations table).
/// </summary>
/// <remarks>
/// Additive — only a new SlidePresentations table is created (CREATE TABLE IF NOT EXISTS); no
/// existing table or data file is touched and app.db is never reset. The DataJson-row +
/// promoted-columns shape and the UpsertAsync / ON CONFLICT(Id) DO UPDATE write path mirror
/// <see cref="MediaLibraryService"/>; the ISO-8601-UTC date-string storage (FormatDate/ParseDate),
/// the ORDER BY UpdatedAt DESC listing, the updatedat index, and the _initialized schema-init
/// guard mirror <see cref="SongUsageService"/>. Unlike those stores there is NO legacy-import
/// branch — Slides is a brand-new feature with no prior JSON store.
///
/// The service owns the invariant that the promoted SlideCount / UpdatedAt columns always match
/// the serialized object: Create/Update/Duplicate recompute SlideCount = Slides.Count and stamp
/// UpdatedAt = UtcNow before every upsert. The UI never writes these columns directly.
/// </remarks>
public class SlidePresentationService : ISlidePresentationService
{
    private readonly AppDatabase _database;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string CreateSchemaSql = @"
CREATE TABLE IF NOT EXISTS SlidePresentations (
    Id         TEXT PRIMARY KEY,
    Name       TEXT NOT NULL DEFAULT '',
    SlideCount INTEGER NOT NULL DEFAULT 0,
    CreatedAt  TEXT NOT NULL,
    UpdatedAt  TEXT NOT NULL,
    DataJson   TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_slidepresentations_updatedat ON SlidePresentations(UpdatedAt);
";

    private const string DefaultName = "New Presentation";

    /// <summary>
    /// Create the service against a database path (conceptually &lt;appFolder&gt;/app.db — the
    /// caller passes the path, exactly like <see cref="SongUsageService"/> and
    /// <see cref="MediaLibraryService"/>). When <paramref name="databasePath"/> is null the
    /// standard app-folder app.db (<see cref="AppDatabase.DefaultPath"/>) is used.
    /// </summary>
    public SlidePresentationService(string? databasePath = null)
    {
        _database = new AppDatabase(databasePath ?? AppDatabase.DefaultPath);
    }

    /// <inheritdoc />
    public async Task<List<SlidePresentation>> GetAllAsync()
    {
        await EnsureInitializedAsync();

        var results = new List<SlidePresentation>();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DataJson FROM SlidePresentations ORDER BY UpdatedAt DESC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            try
            {
                var presentation = JsonSerializer.Deserialize<SlidePresentation>(reader.GetString(0), JsonOptions);
                if (presentation != null)
                    results.Add(presentation);
            }
            catch
            {
                // Skip a single malformed DataJson row and continue (mirrors
                // MediaLibraryService.EnsureLoadedAsync).
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<SlidePresentation?> GetByIdAsync(string id)
    {
        await EnsureInitializedAsync();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DataJson FROM SlidePresentations WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id ?? "");

        var json = await cmd.ExecuteScalarAsync() as string;
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SlidePresentation>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SlidePresentation> CreateAsync(string name)
    {
        await EnsureInitializedAsync();

        var trimmed = name?.Trim();
        var now = DateTime.UtcNow;

        var presentation = new SlidePresentation
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.IsNullOrWhiteSpace(trimmed) ? DefaultName : trimmed!,
            CreatedAt = now,
            UpdatedAt = now,
            Slides = new List<PresentationSlide>()
        };

        await UpsertAsync(presentation);
        return presentation;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(SlidePresentation presentation)
    {
        await EnsureInitializedAsync();

        presentation.UpdatedAt = DateTime.UtcNow;
        await UpsertAsync(presentation);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id)
    {
        await EnsureInitializedAsync();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SlidePresentations WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id ?? "");
        await cmd.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task<SlidePresentation> DuplicateAsync(string id)
    {
        var original = await GetByIdAsync(id);
        if (original == null)
            throw new KeyNotFoundException($"No slide presentation found with id '{id}'.");

        // Deep-clone via JSON round-trip so the slides list is a fully independent copy.
        var clone = JsonSerializer.Deserialize<SlidePresentation>(
            JsonSerializer.Serialize(original, JsonOptions), JsonOptions)!;

        var now = DateTime.UtcNow;
        clone.Id = Guid.NewGuid().ToString();
        clone.Name = original.Name + " (Copy)";
        clone.CreatedAt = now;
        clone.UpdatedAt = now;
        foreach (var slide in clone.Slides)
            slide.Id = Guid.NewGuid().ToString();

        await UpsertAsync(clone);
        return clone;
    }

    /// <summary>
    /// Insert or update a presentation row. Promotes Id/Name/SlideCount/CreatedAt/UpdatedAt to
    /// columns and serializes the whole object to DataJson (parameterized). SlideCount is always
    /// re-derived here so the column can never drift from DataJson. Mirrors
    /// <see cref="MediaLibraryService"/>'s upsert shape.
    /// </summary>
    private async Task UpsertAsync(SlidePresentation presentation)
    {
        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SlidePresentations (Id, Name, SlideCount, CreatedAt, UpdatedAt, DataJson)
            VALUES (@Id, @Name, @SlideCount, @CreatedAt, @UpdatedAt, @DataJson)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                SlideCount = excluded.SlideCount,
                CreatedAt = excluded.CreatedAt,
                UpdatedAt = excluded.UpdatedAt,
                DataJson = excluded.DataJson";
        BindPresentation(cmd, presentation);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void BindPresentation(SqliteCommand cmd, SlidePresentation presentation)
    {
        cmd.Parameters.AddWithValue("@Id", presentation.Id);
        cmd.Parameters.AddWithValue("@Name", presentation.Name ?? "");
        cmd.Parameters.AddWithValue("@SlideCount", presentation.Slides?.Count ?? 0);
        cmd.Parameters.AddWithValue("@CreatedAt", FormatDate(presentation.CreatedAt));
        cmd.Parameters.AddWithValue("@UpdatedAt", FormatDate(presentation.UpdatedAt));
        cmd.Parameters.AddWithValue("@DataJson", JsonSerializer.Serialize(presentation, JsonOptions));
    }

    /// <summary>
    /// Create the SlidePresentations schema (idempotent). Runs once per process via the
    /// <see cref="_initialized"/> guard. No legacy-import branch — Slides is a brand-new feature.
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

        _initialized = true;
    }

    /// <summary>
    /// Store dates as ISO-8601 round-trip ("o") UTC text (mirrors
    /// <see cref="SongUsageService"/>). Every value is normalized to UTC first so lexicographic
    /// string ordering is chronological, making ORDER BY UpdatedAt DESC correct text-wise.
    /// </summary>
    private static string FormatDate(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return utc.ToString("o", CultureInfo.InvariantCulture);
    }
}
