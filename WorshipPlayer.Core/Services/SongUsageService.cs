using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Tracks song usage records for CCLI reporting and analytics.
/// Stores data in a local JSON file (song-usage.json).
/// </summary>
public class SongUsageService
{
    private readonly string _storagePath;
    private List<SongUsageRecord> _records = new();
    private bool _loaded = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SongUsageService(string storagePath)
    {
        _storagePath = storagePath;
    }

    /// <summary>
    /// Log a song usage event.
    /// </summary>
    /// <param name="record">The usage record to log.</param>
    public async Task LogUsageAsync(SongUsageRecord record)
    {
        await EnsureLoadedAsync();

        if (string.IsNullOrEmpty(record.Id))
            record.Id = Guid.NewGuid().ToString();

        record.DatePlayed = DateTime.UtcNow;
        _records.Add(record);

        await SaveAsync();
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
    /// <returns>List of matching usage records.</returns>
    public async Task<List<SongUsageRecord>> GetByDateRangeAsync(DateTime from, DateTime to)
    {
        await EnsureLoadedAsync();

        return _records
            .Where(r => r.DatePlayed >= from && r.DatePlayed <= to)
            .OrderByDescending(r => r.DatePlayed)
            .ToList();
    }

    /// <summary>
    /// Get all usage records.
    /// </summary>
    public async Task<List<SongUsageRecord>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return new List<SongUsageRecord>(_records);
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

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        if (File.Exists(_storagePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_storagePath);
                _records = JsonSerializer.Deserialize<List<SongUsageRecord>>(json, JsonOptions) ?? new();
            }
            catch
            {
                _records = new List<SongUsageRecord>();
            }
        }

        _loaded = true;
    }

    private async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_records, JsonOptions);
        await File.WriteAllTextAsync(_storagePath, json);
    }

    private static string EscapeCsv(string value)
    {
        return value?.Replace("\"", "\"\"") ?? "";
    }
}
