using System.Globalization;
using System.Text.Json;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Cross-platform (net8.0, Core-only, no WPF) tests for the service name + US-format
/// date/time capability: ServiceDateFormatter round-trips, date-only backward
/// compatibility, the name+date+time flow through CreateNew/SaveAsync/LoadAsync, and
/// US output under a non-US (vi-VN) thread culture.
/// </summary>
public class ServiceDateNameTests : IDisposable
{
    private readonly string _tempFolder;

    public ServiceDateNameTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ServiceDateNameTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public void FormatUs_WithTime_ReturnsUsDateTimeWithAmPm()
    {
        var start = new DateTime(2026, 10, 18, 8, 0, 0);
        var result = ServiceDateFormatter.FormatUs("2026-10-18", start);
        Assert.Equal("10/18/2026 08:00 AM", result);
    }

    [Fact]
    public void FormatUs_WithoutTime_ReturnsUsDateOnly()
    {
        var result = ServiceDateFormatter.FormatUs("2026-10-18", null);
        Assert.Equal("10/18/2026", result);
    }

    [Fact]
    public void TryParseUs_DateAndTime_YieldsNormalizedDateAndStartTime()
    {
        var ok = ServiceDateFormatter.TryParseUs("10/18/2026 08:00 AM", out var date, out var startTime);

        Assert.True(ok);
        Assert.Equal("2026-10-18", date);
        Assert.NotNull(startTime);
        Assert.Equal(new DateTime(2026, 10, 18, 8, 0, 0), startTime!.Value);
    }

    [Fact]
    public void TryParseUs_DateOnly_YieldsNormalizedDateAndNullTime()
    {
        var ok = ServiceDateFormatter.TryParseUs("10/18/2026", out var date, out var startTime);

        Assert.True(ok);
        Assert.Equal("2026-10-18", date);
        Assert.Null(startTime);
    }

    [Fact]
    public void TryParseUs_PmTime_RoundTripsThroughFormatUs()
    {
        var ok = ServiceDateFormatter.TryParseUs("12/25/2026 05:30 PM", out var date, out var startTime);
        Assert.True(ok);

        var formatted = ServiceDateFormatter.FormatUs(date, startTime);
        Assert.Equal("12/25/2026 05:30 PM", formatted);
    }

    [Fact]
    public void TryParseUs_Garbage_ReturnsFalse()
    {
        var ok = ServiceDateFormatter.TryParseUs("not a date", out var date, out var startTime);
        Assert.False(ok);
        Assert.Equal(string.Empty, date);
        Assert.Null(startTime);
    }

    [Fact]
    public void DateOnlyLegacyJson_LoadsWithNullStartTime()
    {
        // A legacy file that only carries "date" (no "startTime").
        const string legacyJson = """
        {
          "id": "legacy-001",
          "name": "Sunday Morning",
          "date": "2026-08-03",
          "serviceType": "Sunday Morning",
          "items": []
        }
        """;
        var options = new JsonSerializerOptions();
        var order = JsonSerializer.Deserialize<ServiceOrder>(legacyJson, options);

        Assert.NotNull(order);
        Assert.Null(order!.StartTime);
        Assert.Equal("2026-08-03", order.Date);
        Assert.Equal("10/18/2026".Length, "MM/dd/yyyy".Length); // format sanity
    }

    [Fact]
    public async Task NameDateTime_FlowsThroughCreateSaveLoad()
    {
        ServiceDateFormatter.TryParseUs("10/18/2026 08:00 AM", out var date, out var startTime);

        var manager = new ServiceOrderManager(_tempFolder);
        var order = manager.CreateNew("Easter Service", date);
        order.StartTime = startTime;
        await manager.SaveAsync();

        // Restart: new manager loads the written file.
        var files = Directory.GetFiles(_tempFolder, "*.json");
        Assert.Single(files);

        var reloaded = new ServiceOrderManager(_tempFolder);
        var loaded = await reloaded.LoadAsync(files[0]);

        Assert.NotNull(loaded);
        Assert.Equal("Easter Service", loaded!.Name);
        Assert.Equal("2026-10-18", loaded.Date);
        Assert.NotNull(loaded.StartTime);
        Assert.Equal(new DateTime(2026, 10, 18, 8, 0, 0), loaded.StartTime!.Value);
        Assert.Equal("10/18/2026 08:00 AM", ServiceDateFormatter.FormatUs(loaded.Date, loaded.StartTime));
    }

    [Fact]
    public async Task DateOnlyService_SavesAndReloadsWithoutStartTime()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("No Time Service", "2026-08-03");
        await manager.SaveAsync();

        var files = Directory.GetFiles(_tempFolder, "*.json");
        var json = await File.ReadAllTextAsync(files[0]);
        // WhenWritingNull means a date-only service must not write a startTime key.
        Assert.DoesNotContain("startTime", json);

        var reloaded = new ServiceOrderManager(_tempFolder);
        var loaded = await reloaded.LoadAsync(files[0]);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.StartTime);
        Assert.Equal("08/03/2026", ServiceDateFormatter.FormatUs(loaded.Date, loaded.StartTime));
    }

    [Fact]
    public void UsFormatting_IsCultureIndependent_UnderViVn()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");

            var start = new DateTime(2026, 10, 18, 8, 0, 0);
            Assert.Equal("10/18/2026 08:00 AM", ServiceDateFormatter.FormatUs("2026-10-18", start));

            var ok = ServiceDateFormatter.TryParseUs("10/18/2026 08:00 AM", out var date, out var parsed);
            Assert.True(ok);
            Assert.Equal("2026-10-18", date);
            Assert.Equal(start, parsed!.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
