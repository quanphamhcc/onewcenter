using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for ExportService.
/// Tests export with/without lyrics and empty service order handling.
/// </summary>
public class ExportServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly ExportService _service;

    public ExportServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ExportTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _service = new ExportService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public async Task ExportServiceOrderTextAsync_WithLyrics_ContainsLyricText()
    {
        var order = CreateTestServiceOrder();
        var outputPath = Path.Combine(_tempFolder, "export_lyrics.txt");

        await _service.ExportServiceOrderTextAsync(order, includeLyrics: true, outputPath);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Amazing Grace how sweet the sound", content);
    }

    [Fact]
    public async Task ExportServiceOrderTextAsync_WithoutLyrics_DoesNotContainLyricText()
    {
        var order = CreateTestServiceOrder();
        var outputPath = Path.Combine(_tempFolder, "export_no_lyrics.txt");

        await _service.ExportServiceOrderTextAsync(order, includeLyrics: false, outputPath);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("Amazing Grace how sweet the sound", content);
    }

    [Fact]
    public async Task ExportServiceOrderTextAsync_ContainsServiceName()
    {
        var order = CreateTestServiceOrder();
        var outputPath = Path.Combine(_tempFolder, "export_name.txt");

        await _service.ExportServiceOrderTextAsync(order, includeLyrics: false, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Sunday Morning Worship", content);
    }

    [Fact]
    public async Task ExportServiceOrderTextAsync_ContainsItemTitles()
    {
        var order = CreateTestServiceOrder();
        var outputPath = Path.Combine(_tempFolder, "export_titles.txt");

        await _service.ExportServiceOrderTextAsync(order, includeLyrics: false, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Amazing Grace", content);
        Assert.Contains("Opening Prayer", content);
    }

    [Fact]
    public async Task ExportServiceOrderTextAsync_EmptyOrder_ProducesValidOutput()
    {
        var order = new ServiceOrder
        {
            Name = "Empty Service",
            Date = "2024-01-01",
            Items = new List<ServiceItem>()
        };
        var outputPath = Path.Combine(_tempFolder, "export_empty.txt");

        await _service.ExportServiceOrderTextAsync(order, includeLyrics: true, outputPath);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Empty Service", content);
        Assert.Contains("Total Items: 0", content);
    }

    [Fact]
    public async Task ExportServiceOrderTextAsync_WithNotes_IncludesNotes()
    {
        var order = CreateTestServiceOrder();
        order.Notes = "Remember to dim lights during prayer";
        var outputPath = Path.Combine(_tempFolder, "export_notes.txt");

        await _service.ExportServiceOrderTextAsync(order, includeLyrics: false, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Remember to dim lights during prayer", content);
    }

    [Fact]
    public async Task GeneratePrintableContentAsync_WithLyrics_ReturnsContent()
    {
        var order = CreateTestServiceOrder();

        var content = await _service.GeneratePrintableContentAsync(order, includeLyrics: true);

        Assert.NotNull(content);
        Assert.Contains("Amazing Grace", content);
        Assert.Contains("Amazing Grace how sweet the sound", content);
    }

    [Fact]
    public async Task GeneratePrintableContentAsync_WithoutLyrics_OmitsLyricText()
    {
        var order = CreateTestServiceOrder();

        var content = await _service.GeneratePrintableContentAsync(order, includeLyrics: false);

        Assert.Contains("Amazing Grace", content); // Title still present
        Assert.DoesNotContain("Amazing Grace how sweet the sound", content);
    }

    [Fact]
    public async Task ExportUsageReportCsvAsync_ProducesCsv()
    {
        var records = new List<SongUsageRecord>
        {
            new SongUsageRecord
            {
                SongTitle = "Amazing Grace",
                SongId = "song-1",
                Artist = "John Newton",
                CcliNumber = "12345",
                ServiceName = "Sunday AM",
                ServiceDate = new DateTime(2024, 1, 7),
                DatePlayed = new DateTime(2024, 1, 7, 9, 30, 0)
            }
        };
        var outputPath = Path.Combine(_tempFolder, "usage.csv");

        await _service.ExportUsageReportCsvAsync(records, outputPath);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Song Title,Song ID,Artist,CCLI Number", content);
        Assert.Contains("Amazing Grace", content);
        Assert.Contains("12345", content);
    }

    [Fact]
    public async Task ExportUsageReportCsvAsync_EmptyRecords_ProducesHeaderOnly()
    {
        var records = new List<SongUsageRecord>();
        var outputPath = Path.Combine(_tempFolder, "empty_usage.csv");

        await _service.ExportUsageReportCsvAsync(records, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines); // Only header
    }

    [Fact]
    public async Task ExportServiceOrderTextAsync_ScriptureItem_ShowsReference()
    {
        var order = new ServiceOrder
        {
            Name = "Test",
            Date = "2024-01-01"
        };
        order.Items.Add(new ServiceItem
        {
            Type = ServiceItemType.Scripture,
            Title = "Scripture Reading",
            ScriptureReference = "John 3:16",
            ScriptureTranslation = "NIV"
        });
        var outputPath = Path.Combine(_tempFolder, "scripture_export.txt");

        await _service.ExportServiceOrderTextAsync(order, includeLyrics: false, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("John 3:16", content);
        Assert.Contains("NIV", content);
    }

    private ServiceOrder CreateTestServiceOrder()
    {
        var order = new ServiceOrder
        {
            Name = "Sunday Morning Worship",
            Date = "2024-01-07",
            ServiceType = "Sunday Morning"
        };

        var songItem = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Amazing Grace",
            Subtitle = "John Newton",
            DurationMinutes = 4,
            Slides = new List<Slide>
            {
                new Slide
                {
                    Content = new SlideContent { Text = "Amazing Grace how sweet the sound" }
                },
                new Slide
                {
                    Content = new SlideContent { Text = "That saved a wretch like me" }
                }
            }
        };

        var prayerItem = new ServiceItem
        {
            Type = ServiceItemType.Prayer,
            Title = "Opening Prayer",
            Notes = "Pastor leads"
        };

        order.Items.Add(songItem);
        order.Items.Add(prayerItem);

        return order;
    }
}
