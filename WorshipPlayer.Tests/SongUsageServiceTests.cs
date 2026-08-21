using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for SongUsageService.
/// Tests usage logging, date range queries, and CSV export format.
/// </summary>
public class SongUsageServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _storagePath;
    private readonly SongUsageService _service;

    public SongUsageServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"UsageTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _storagePath = Path.Combine(_tempFolder, "song-usage.json");
        _service = new SongUsageService(_storagePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
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
    public async Task LogUsageAsync_PersistsToFile()
    {
        await _service.LogUsageAsync(new SongUsageRecord
        {
            SongTitle = "Persisted Song"
        });

        Assert.True(File.Exists(_storagePath));
        var json = await File.ReadAllTextAsync(_storagePath);
        Assert.Contains("Persisted Song", json);
    }
}
