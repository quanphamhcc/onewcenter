using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Tracks song usage records for CCLI reporting and analytics.
/// Stores data in the shared SQLite application database (app.db), SongUsage table.
/// </summary>
/// <remarks>
/// Backing store migrated from a JSON file (song-usage.json) to SQLite. The public API
/// and all observable behavior are preserved: empty <see cref="SongUsageRecord.Id"/> is
/// assigned a GUID on log, <see cref="SongUsageRecord.DatePlayed"/> is stamped at
/// <see cref="DateTime.UtcNow"/> on log, <see cref="GetByDateRangeAsync"/> is inclusive
/// and descending by <c>DatePlayed</c>, and <see cref="ExportToCsvAsync"/> keeps its exact
/// CSV format. On first use, if a legacy song-usage.json sits beside the database and the
/// SongUsage table is empty, its records are imported once (defensively — never throws).
/// </remarks>
public class SongUsageService
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
CREATE TABLE IF NOT EXISTS SongUsage (
    Id             TEXT PRIMARY KEY,
    SongTitle      TEXT NOT NULL DEFAULT '',
    SongId         TEXT NOT NULL DEFAULT '',
    ServiceName    TEXT NOT NULL DEFAULT '',
    ServiceDate    TEXT NOT NULL,
    DatePlayed     TEXT NOT NULL,
    CcliNumber     TEXT NOT NULL DEFAULT '',
    Artist         TEXT NOT NULL DEFAULT '',
    OrganizationId TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_songusage_dateplayed ON SongUsage(DatePlayed);
";

    private const string SelectColumns =
        "Id, SongTitle, SongId, ServiceName, ServiceDate, DatePlayed, CcliNumber, Artist, OrganizationId";

    /// <summary>
    /// Create the service against a database path (default conceptually
    /// &lt;appFolder&gt;/app.db — the caller passes the path, exactly like
    /// <see cref="Bible.BibleDatabaseService"/>). The legacy song-usage.json is looked
    /// for as a sibling of the database file.
    /// </summary>
    public SongUsageService(string databasePath)
    {
        _database = new AppDatabase(databasePath);
        var dir = Path.GetDirectoryName(databasePath);
        _legacyJsonPath = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "song-usage.json");
    }

    /// <summary>
    /// Log a song usage event.
    /// </summary>
    /// <param name="record">The usage record to log.</param>
    public async Task LogUsageAsync(SongUsageRecord record)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrEmpty(record.Id))
            record.Id = Guid.NewGuid().ToString();

        record.DatePlayed = DateTime.UtcNow;

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SongUsage
                (Id, SongTitle, SongId, ServiceName, ServiceDate, DatePlayed, CcliNumber, Artist, OrganizationId)
            VALUES (@Id, @SongTitle, @SongId, @ServiceName, @ServiceDate, @DatePlayed, @CcliNumber, @Artist, @OrganizationId)";
        BindRecord(cmd, record);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Log a usage event with individual parameters.
    /// </summary>
    public async Task LogUsageAsync(string songTitle, string songId, string serviceName, DateTime serviceDate)
    {
        var record = new SongUsageRecord
        {
            SongTitle = songTitle,
            SongId = songId,
            ServiceName = serviceName,
            ServiceDate = serviceDate,
            DatePlayed = DateTime.UtcNow
        };

        await LogUsageAsync(record);
    }

    /// <summary>
    /// Get all usage records within a date range.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <returns>List of matching usage records, ordered by DatePlayed descending.</returns>
    public async Task<List<SongUsageRecord>> GetByDateRangeAsync(DateTime from, DateTime to)
    {
        await EnsureInitializedAsync();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT {SelectColumns}
            FROM SongUsage
            WHERE DatePlayed >= @from AND DatePlayed <= @to
            ORDER BY DatePlayed DESC";
        cmd.Parameters.AddWithValue("@from", FormatDate(from));
        cmd.Parameters.AddWithValue("@to", FormatDate(to));

        return await ReadRecordsAsync(cmd);
    }

    /// <summary>
    /// Get all usage records.
    /// </summary>
    public async Task<List<SongUsageRecord>> GetAllAsync()
    {
        await EnsureInitializedAsync();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM SongUsage";

        return await ReadRecordsAsync(cmd);
    }

    /// <summary>
    /// Export usage records within a date range to a CSV file.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <param name="outputPath">Path to write the CSV file.</param>
    public async Task ExportToCsvAsync(DateTime from, DateTime to, string outputPath)
    {
        var records = await GetByDateRangeAsync(from, to);

        var sb = new StringBuilder();
        sb.AppendLine("Song Title,Song ID,Artist,CCLI Number,Service Name,Service Date,Date Played");

        foreach (var record in records)
        {
            sb.AppendLine($"\"{EscapeCsv(record.SongTitle)}\",\"{EscapeCsv(record.SongId)}\",\"{EscapeCsv(record.Artist)}\",\"{EscapeCsv(record.CcliNumber)}\",\"{EscapeCsv(record.ServiceName)}\",\"{record.ServiceDate:yyyy-MM-dd}\",\"{record.DatePlayed:yyyy-MM-dd HH:mm:ss}\"");
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    /// <summary>
    /// Create the SongUsage schema (idempotent) and run the one-time legacy JSON import.
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
    /// Defensive one-time import: if the SongUsage table is empty AND a legacy
    /// song-usage.json exists beside the database, import its records once. Preserves
    /// historical Id and DatePlayed values (does not re-stamp). Tolerates a missing or
    /// malformed file — never throws. The empty-table guard makes this non-duplicating.
    /// </summary>
    private async Task ImportLegacyJsonIfNeededAsync(SqliteConnection connection)
    {
        try
        {
            if (!File.Exists(_legacyJsonPath))
                return;

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM SongUsage";
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
                if (count > 0)
                    return;
            }

            var json = await File.ReadAllTextAsync(_legacyJsonPath);
            var records = JsonSerializer.Deserialize<List<SongUsageRecord>>(json, JsonOptions);
            if (records == null || records.Count == 0)
                return;

            using var transaction = connection.BeginTransaction();
            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.Id))
                    record.Id = Guid.NewGuid().ToString();

                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT OR IGNORE INTO SongUsage
                        (Id, SongTitle, SongId, ServiceName, ServiceDate, DatePlayed, CcliNumber, Artist, OrganizationId)
                    VALUES (@Id, @SongTitle, @SongId, @ServiceName, @ServiceDate, @DatePlayed, @CcliNumber, @Artist, @OrganizationId)";
                BindRecord(insertCmd, record);
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

    private static async Task<List<SongUsageRecord>> ReadRecordsAsync(SqliteCommand cmd)
    {
        var results = new List<SongUsageRecord>();

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new SongUsageRecord
            {
                Id = reader.GetString(0),
                SongTitle = reader.GetString(1),
                SongId = reader.GetString(2),
                ServiceName = reader.GetString(3),
                ServiceDate = ParseDate(reader.GetString(4)),
                DatePlayed = ParseDate(reader.GetString(5)),
                CcliNumber = reader.GetString(6),
                Artist = reader.GetString(7),
                OrganizationId = reader.GetString(8)
            });
        }

        return results;
    }

    private static void BindRecord(SqliteCommand cmd, SongUsageRecord record)
    {
        cmd.Parameters.AddWithValue("@Id", record.Id);
        cmd.Parameters.AddWithValue("@SongTitle", record.SongTitle);
        cmd.Parameters.AddWithValue("@SongId", record.SongId);
        cmd.Parameters.AddWithValue("@ServiceName", record.ServiceName);
        cmd.Parameters.AddWithValue("@ServiceDate", FormatDate(record.ServiceDate));
        cmd.Parameters.AddWithValue("@DatePlayed", FormatDate(record.DatePlayed));
        cmd.Parameters.AddWithValue("@CcliNumber", record.CcliNumber);
        cmd.Parameters.AddWithValue("@Artist", record.Artist);
        cmd.Parameters.AddWithValue("@OrganizationId", record.OrganizationId);
    }

    /// <summary>
    /// Store dates as ISO-8601 round-trip ("o") UTC text. Every value is normalized to
    /// UTC first so all stored/compared strings share the same "...Z" shape; lexicographic
    /// ordering of these strings is then chronological, so text range filters and ORDER BY
    /// match the former in-memory DateTime comparisons regardless of each input's Kind.
    /// </summary>
    private static string FormatDate(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Treat Unspecified as UTC (matches the old in-memory comparison, which did a
            // raw DateTime compare without any zone conversion).
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return utc.ToString("o", CultureInfo.InvariantCulture);
    }

    private static DateTime ParseDate(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string EscapeCsv(string value)
    {
        return value?.Replace("\"", "\"\"") ?? "";
    }
}
