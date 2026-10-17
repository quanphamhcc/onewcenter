using System.Text;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Generates CCLI song usage reports from SongUsageService data.
/// Aggregates play counts and provides CSV export for CCLI license compliance reporting.
/// </summary>
public class CcliReportService
{
    private readonly SongUsageService _usageService;

    public CcliReportService(SongUsageService usageService)
    {
        _usageService = usageService;
    }

    /// <summary>
    /// Get an aggregated CCLI report for the given date range.
    /// Groups songs by title and returns play counts.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <returns>Aggregated report entries sorted by play count (descending).</returns>
    public async Task<List<CcliReportEntry>> GetReportAsync(DateTime from, DateTime to)
    {
        var records = await _usageService.GetByDateRangeAsync(from, to);

        var grouped = records
            .GroupBy(r => new { r.SongTitle, r.SongId, r.CcliNumber, r.Artist })
            .Select(g => new CcliReportEntry
            {
                SongTitle = g.Key.SongTitle,
                SongId = g.Key.SongId,
                CcliNumber = g.Key.CcliNumber,
                Artist = g.Key.Artist,
                PlayCount = g.Count(),
                LastPlayed = g.Max(r => r.DatePlayed)
            })
            .OrderByDescending(e => e.PlayCount)
            .ThenBy(e => e.SongTitle)
            .ToList();

        return grouped;
    }

    /// <summary>
    /// Export a CCLI report to a CSV file.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <param name="outputPath">Output CSV file path.</param>
    public Task ExportCsvAsync(DateTime from, DateTime to, string outputPath)
        => ExportCsvAsync(from, to, outputPath, null);

    /// <summary>
    /// Export a CCLI report to a CSV file, optionally prefixed with the church's CCLI license number.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <param name="outputPath">Output CSV file path.</param>
    /// <param name="ccliLicenseNumber">
    /// Optional CCLI license number. When non-empty, a leading "CCLI License #,&lt;value&gt;" line is
    /// written before the column header; when null/empty, the output is identical to the 3-arg overload.
    /// </param>
    public async Task ExportCsvAsync(DateTime from, DateTime to, string outputPath, string? ccliLicenseNumber)
    {
        var report = await GetReportAsync(from, to);

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ccliLicenseNumber))
            sb.AppendLine($"CCLI License #,\"{EscapeCsv(ccliLicenseNumber)}\"");
        sb.AppendLine("Song Title,Artist,CCLI Number,Play Count,Last Played");

        foreach (var entry in report)
        {
            sb.AppendLine($"\"{EscapeCsv(entry.SongTitle)}\",\"{EscapeCsv(entry.Artist)}\",\"{EscapeCsv(entry.CcliNumber)}\",{entry.PlayCount},\"{entry.LastPlayed:yyyy-MM-dd}\"");
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        return value?.Replace("\"", "\"\"") ?? "";
    }
}

/// <summary>
/// Represents a single entry in the CCLI usage report.
/// </summary>
public class CcliReportEntry
{
    /// <summary>
    /// Song title.
    /// </summary>
    public string SongTitle { get; set; } = "";

    /// <summary>
    /// Internal song identifier.
    /// </summary>
    public string SongId { get; set; } = "";

    /// <summary>
    /// CCLI song number.
    /// </summary>
    public string CcliNumber { get; set; } = "";

    /// <summary>
    /// Song artist/author.
    /// </summary>
    public string Artist { get; set; } = "";

    /// <summary>
    /// Number of times the song was played in the date range.
    /// </summary>
    public int PlayCount { get; set; }

    /// <summary>
    /// Date the song was last played.
    /// </summary>
    public DateTime LastPlayed { get; set; }
}
