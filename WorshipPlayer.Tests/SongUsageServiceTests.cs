using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for SongUsageService (SQLite-backed, app.db).
/// Tests usage logging, date range queries, CSV export format, and the one-time
/// JSON->SQLite import. Each test injects a temp app.db and clears SQLite connection
/// pools in teardown so the temp .db file can be deleted on Windows (mirrors
/// BibleDatabaseServiceTests discipline).
/// </summary>
public class SongUsageServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;
    private readonly SongUsageService _service;

    public SongUsageServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"UsageTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
        _service = new SongUsageService(_dbPath);
    }

    public void Dispose()
    {
        // SQLite pools keep a file handle open after connections are disposed; clear the
        // pools first so the temp .db can be deleted without an IOException.
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
    public async Task LogUsageAsync_Record_StoresSuccessfully()
    {
        var record = new SongUsageRecord
        {
            SongTitle = "Amazing Grace",
            SongId = "song-1",
            ServiceName = "Sunday AM"
        };

        await _service.LogUsageAsync(record);

        var all = await _service.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("Amazing Grace", all[0].SongTitle);
    }

    [Fact]
    public async Task LogUsageAsync_AssignsId()
    {
        var record = new SongUsageRecord
        {
            Id = "",
            SongTitle = "Test Song"
        };

        await _service.LogUsageAsync(record);

        var all = await _service.GetAllAsync();
        Assert.NotEmpty(all[0].Id);
    }

    [Fact]
    public async Task LogUsageAsync_SetsDatePlayed()
    {
        var record = new SongUsageRecord
        {
            SongTitle = "Test Song"
        };

        var before = DateTime.UtcNow;
        await _service.LogUsageAsync(record);

        var all = await _service.GetAllAsync();
        Assert.True(all[0].DatePlayed >= before.AddSeconds(-1));
    }

    [Fact]
    public async Task LogUsageAsync_MultipleRecords_StoresAll()
    {
        for (int i = 0; i < 5; i++)
        {
            await _service.LogUsageAsync(new SongUsageRecord
            {
                SongTitle = $"Song {i}",
                SongId = $"id-{i}"
            });
        }

        var all = await _service.GetAllAsync();
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public async Task LogUsageAsync_WithParameters_StoresCorrectly()
    {
        await _service.LogUsageAsync(
            "Test Title",
            "test-id",
            "Sunday Service",
            new DateTime(2024, 1, 7));

        var all = await _service.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("Test Title", all[0].SongTitle);
        Assert.Equal("test-id", all[0].SongId);
        Assert.Equal("Sunday Service", all[0].ServiceName);
    }

    [Fact]
    public async Task GetByDateRangeAsync_WithinRange_ReturnsResults()
    {
        await _service.LogUsageAsync(new SongUsageRecord
        {
            SongTitle = "In Range"
        });

        var results = await _service.GetByDateRangeAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.Single(results);
    }

    [Fact]
    public async Task GetByDateRangeAsync_OutsideRange_ReturnsEmpty()
    {
        await _service.LogUsageAsync(new SongUsageRecord
        {
            SongTitle = "Current Song"
        });

        // Query for a range in the past
        var results = await _service.GetByDateRangeAsync(
            new DateTime(2020, 1, 1),
            new DateTime(2020, 12, 31));

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetByDateRangeAsync_NoRecords_ReturnsEmpty()
    {
        var results = await _service.GetByDateRangeAsync(
            DateTime.UtcNow.AddDays(-30),
            DateTime.UtcNow);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetByDateRangeAsync_MultipleRecords_OrderedDescending()
    {
        await _service.LogUsageAsync(new SongUsageRecord { SongTitle = "First" });
        await Task.Delay(10); // Small delay to ensure different timestamps
        await _service.LogUsageAsync(new SongUsageRecord { SongTitle = "Second" });

        var results = await _service.GetByDateRangeAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1));

        Assert.Equal(2, results.Count);
        Assert.Equal("Second", results[0].SongTitle);
        Assert.Equal("First", results[1].SongTitle);
    }

    [Fact]
    public async Task ExportToCsvAsync_ProducesCsvFile()
    {
        await _service.LogUsageAsync(new SongUsageRecord
        {
            SongTitle = "Amazing Grace",
            SongId = "ag-1",
            Artist = "John Newton",
            CcliNumber = "12345",
            ServiceName = "Sunday Morning"
        });

        var outputPath = Path.Combine(_tempFolder, "export.csv");
        await _service.ExportToCsvAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1),
            outputPath);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Song Title,Song ID,Artist,CCLI Number,Service Name,Service Date,Date Played", content);
        Assert.Contains("Amazing Grace", content);
        Assert.Contains("12345", content);
    }

    [Fact]
    public async Task ExportToCsvAsync_EmptyRange_ProducesHeaderOnly()
    {
        var outputPath = Path.Combine(_tempFolder, "empty_export.csv");
        await _service.ExportToCsvAsync(
            new DateTime(2020, 1, 1),
            new DateTime(2020, 12, 31),
            outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines); // Only header line
    }

    [Fact]
    public async Task ExportToCsvAsync_EscapesQuotes()
    {
        await _service.LogUsageAsync(new SongUsageRecord
        {
            SongTitle = "He said \"hello\"",
            SongId = "test"
        });

        var outputPath = Path.Combine(_tempFolder, "quotes.csv");
        await _service.ExportToCsvAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(1),
            outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("\"\"hello\"\"", content);
    }

    [Fact]
    public async Task LogUsageAsync_PersistsAcrossServiceInstances()
    {
        await _service.LogUsageAsync(new SongUsageRecord
        {
            SongTitle = "Persisted Song"
        });

        // The database file is created, and a fresh service against the same app.db reads
        // the record back (DB-backed persistence, replaces the old JSON-file-contents check).
        Assert.True(File.Exists(_dbPath));

        var reopened = new SongUsageService(_dbPath);
        var all = await reopened.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("Persisted Song", all[0].SongTitle);
    }

    [Fact]
    public async Task Import_LegacyJson_ImportsOnceWithoutRestamping()
    {
        // Write a legacy song-usage.json beside the (not-yet-created) app.db BEFORE first use.
        var legacyJsonPath = Path.Combine(_tempFolder, "song-usage.json");
        var historicalDatePlayed = new DateTime(2023, 5, 1, 10, 30, 0, DateTimeKind.Utc);
        var legacy = new List<SongUsageRecord>
        {
            new()
            {
                Id = "fixed-id-1",
                SongTitle = "Legacy Song A",
                SongId = "legacy-a",
                Artist = "Artist A",
                CcliNumber = "111",
                ServiceName = "Legacy Service",
                ServiceDate = new DateTime(2023, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                DatePlayed = historicalDatePlayed
            },
            new()
            {
                Id = "fixed-id-2",
                SongTitle = "Legacy Song B",
                SongId = "legacy-b"
            }
        };
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllTextAsync(legacyJsonPath, JsonSerializer.Serialize(legacy, options));

        // First service instance imports the legacy records.
        var first = new SongUsageService(_dbPath);
        var afterImport = await first.GetAllAsync();
        Assert.Equal(2, afterImport.Count);

        var importedA = afterImport.Single(r => r.Id == "fixed-id-1");
        Assert.Equal("Legacy Song A", importedA.SongTitle);
        // Historical Id and DatePlayed are preserved (NOT re-stamped on import).
        Assert.Equal(historicalDatePlayed, importedA.DatePlayed);

        // A second service instance against the same app.db must NOT re-import (no duplicates).
        var second = new SongUsageService(_dbPath);
        var afterSecond = await second.GetAllAsync();
        Assert.Equal(2, afterSecond.Count);
    }

    [Fact]
    public async Task Import_MalformedLegacyJson_DoesNotThrowAndYieldsEmptyStore()
    {
        var legacyJsonPath = Path.Combine(_tempFolder, "song-usage.json");
        await File.WriteAllTextAsync(legacyJsonPath, "{ this is not valid json ]");

        var service = new SongUsageService(_dbPath);

        // A malformed legacy file must never throw; the store is simply empty.
        var all = await service.GetAllAsync();
        Assert.Empty(all);
    }
}
