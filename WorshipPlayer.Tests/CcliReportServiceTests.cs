using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for CcliReportService.
/// Tests report generation, CSV export format, and empty date range handling.
/// Uses a temp SQLite app.db behind SongUsageService (unchanged public API).
/// </summary>
public class CcliReportServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly SongUsageService _usageService;
    private readonly CcliReportService _service;

    public CcliReportServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"CcliTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);

        var dbPath = Path.Combine(_tempFolder, "app.db");
        _usageService = new SongUsageService(dbPath);
        _service = new CcliReportService(_usageService);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempFolder))
        {
            try
            {
                Directory.Delete(_tempFolder, true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp folder; ignore a transient OS lock.
            }
        }
    }

    [Fact]
    public async Task GetReportAsync_EmptyRange_ReturnsEmpty()
    {
        var report = await _service.GetReportAsync(
            new DateTime(2020, 1, 1),
            new DateTime(2020, 12, 31));

        Assert.Empty(report);
    }

    [Fact]
    public async Task GetReportAsync_NoUsage_ReturnsEmpty()
    {
        var report = await _service.GetReportAsync(
            DateTime.UtcNow.AddDays(-30),
            DateTime.UtcNow.AddDays(1));

        Assert.Empty(report);
    }

    [Fact]
    public async Task GetReportAsync_SingleUsage_ReturnsOneEntry()
    {
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");

        var report = await _service.GetReportAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.Single(report);
        Assert.Equal("Amazing Grace", report[0].SongTitle);
        Assert.Equal(1, report[0].PlayCount);
    }

    [Fact]
    public async Task GetReportAsync_MultiplePlays_AggregatesCount()
    {
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");

        var report = await _service.GetReportAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.Single(report);
        Assert.Equal(3, report[0].PlayCount);
    }

    [Fact]
    public async Task GetReportAsync_MultipleSongs_GroupsCorrectly()
    {
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");
        await LogTestUsage("10000 Reasons", "10k-1", "Matt Redman", "67890");

        var report = await _service.GetReportAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.Equal(2, report.Count);
    }

    [Fact]
    public async Task GetReportAsync_OrderedByPlayCountDescending()
    {
        await LogTestUsage("Less Popular", "lp-1", "Artist", "111");
        await LogTestUsage("Most Popular", "mp-1", "Artist", "222");
        await LogTestUsage("Most Popular", "mp-1", "Artist", "222");
        await LogTestUsage("Most Popular", "mp-1", "Artist", "222");

        var report = await _service.GetReportAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.Equal("Most Popular", report[0].SongTitle);
        Assert.Equal(3, report[0].PlayCount);
        Assert.Equal("Less Popular", report[1].SongTitle);
        Assert.Equal(1, report[1].PlayCount);
    }

    [Fact]
    public async Task GetReportAsync_IncludesArtistAndCcliNumber()
    {
        await LogTestUsage("Test Song", "ts-1", "Test Artist", "99999");

        var report = await _service.GetReportAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.Equal("Test Artist", report[0].Artist);
        Assert.Equal("99999", report[0].CcliNumber);
    }

    [Fact]
    public async Task GetReportAsync_IncludesLastPlayedDate()
    {
        await LogTestUsage("Test Song", "ts-1", "Artist", "123");

        var report = await _service.GetReportAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.True(report[0].LastPlayed >= DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task ExportCsvAsync_EmptyRange_ProducesHeaderOnly()
    {
        var outputPath = Path.Combine(_tempFolder, "empty_report.csv");

        await _service.ExportCsvAsync(
            new DateTime(2020, 1, 1),
            new DateTime(2020, 12, 31),
            outputPath);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Song Title,Artist,CCLI Number,Play Count,Last Played", content);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines); // Header only
    }

    [Fact]
    public async Task ExportCsvAsync_WithData_ProducesCsvRows()
    {
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");

        var outputPath = Path.Combine(_tempFolder, "report.csv");
        await _service.ExportCsvAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1),
            outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Amazing Grace", content);
        Assert.Contains("12345", content);
        Assert.Contains("2", content); // Play count
    }

    [Fact]
    public async Task ExportCsvAsync_CsvFormatIsValid()
    {
        await LogTestUsage("Song With, Comma", "sc-1", "Artist \"Quoted\"", "111");

        var outputPath = Path.Combine(_tempFolder, "format_test.csv");
        await _service.ExportCsvAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1),
            outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        // Values with commas/quotes should be properly escaped
        Assert.Contains("Song With, Comma", content);
        Assert.Contains("\"\"Quoted\"\"", content);
    }

    [Fact]
    public async Task ExportCsvAsync_CreatesDirectoryIfNeeded()
    {
        await LogTestUsage("Test", "t-1", "Artist", "123");

        var outputPath = Path.Combine(_tempFolder, "subdir", "report.csv");
        await _service.ExportCsvAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1),
            outputPath);

        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportCsvAsync_RespectsDateRange_ExcludesOutOfRangePlays()
    {
        // The service stamps DatePlayed = UtcNow when logging, so a window entirely in the past
        // must exclude the just-logged play.
        await LogTestUsage("In Window", "iw-1", "Artist", "111");

        var outputPath = Path.Combine(_tempFolder, "range_report.csv");
        await _service.ExportCsvAsync(
            new DateTime(2000, 1, 1),
            new DateTime(2000, 12, 31),
            outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("In Window", content);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines); // Header only — the play is outside the queried window.
    }

    [Fact]
    public async Task GetReportAsync_RespectsDateRange_ExcludesOutOfRangePlays()
    {
        await LogTestUsage("Recent", "r-1", "Artist", "222");

        var report = await _service.GetReportAsync(
            new DateTime(2000, 1, 1),
            new DateTime(2000, 12, 31));

        Assert.Empty(report);
    }

    [Fact]
    public async Task ExportCsvAsync_WithLicenseNumber_WritesLicenseHeaderLine()
    {
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");

        var outputPath = Path.Combine(_tempFolder, "licensed_report.csv");
        await _service.ExportCsvAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1),
            outputPath,
            "CCLI-55555");

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("CCLI License #", content);
        Assert.Contains("CCLI-55555", content);
        // Column header still present after the license line.
        Assert.Contains("Song Title,Artist,CCLI Number,Play Count,Last Played", content);
    }

    [Fact]
    public async Task ExportCsvAsync_WithoutLicenseNumber_OmitsLicenseHeaderLine()
    {
        await LogTestUsage("Amazing Grace", "ag-1", "John Newton", "12345");

        var outputPath = Path.Combine(_tempFolder, "unlicensed_report.csv");
        await _service.ExportCsvAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1),
            outputPath,
            "");

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("CCLI License #", content);
        // First non-empty line is the column header.
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Song Title,Artist,CCLI Number,Play Count,Last Played", lines[0]);
    }

    private async Task LogTestUsage(string title, string songId, string artist, string ccliNumber)
    {
        await _usageService.LogUsageAsync(new SongUsageRecord
        {
            SongTitle = title,
            SongId = songId,
            Artist = artist,
            CcliNumber = ccliNumber,
            ServiceName = "Sunday AM",
            ServiceDate = DateTime.UtcNow
        });
    }
}
