using System.Text.Json;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the J4 log-site mapping (Song → SongUsageRecord) and the CCLI license number
/// persistence on PresenterSettings. These cover the testable Core pieces of the CCLI report path;
/// the WPF dialog/section wiring lives in net8.0-windows projects the Tests project cannot reference.
/// </summary>
public class SongUsageMappingTests
{
    [Fact]
    public void FromSong_CarriesArtistAndCcliNumber()
    {
        var song = new Song
        {
            Title = "Amazing Grace",
            Artist = "John Newton",
            Ccli = "12345",
            FolderPath = @"C:\Songs\amazing-grace"
        };

        var record = SongUsageMapper.FromSong(song, "Sunday AM", new DateTime(2024, 1, 7));

        Assert.Equal("Amazing Grace", record.SongTitle);
        Assert.Equal("John Newton", record.Artist);
        Assert.Equal("12345", record.CcliNumber);
    }

    [Fact]
    public void FromSong_MapsSongIdToFolderPathAndServiceContext()
    {
        var song = new Song
        {
            Title = "10000 Reasons",
            Artist = "Matt Redman",
            Ccli = "67890",
            FolderPath = @"C:\Songs\10k-reasons"
        };
        var serviceDate = new DateTime(2024, 3, 10);

        var record = SongUsageMapper.FromSong(song, "Easter Service", serviceDate);

        Assert.Equal(@"C:\Songs\10k-reasons", record.SongId);
        Assert.Equal("Easter Service", record.ServiceName);
        Assert.Equal(serviceDate, record.ServiceDate);
    }

    [Fact]
    public void FromSong_LeavesIdEmptyForServiceToAssign()
    {
        var song = new Song { Title = "Test", Artist = "A", Ccli = "1" };

        var record = SongUsageMapper.FromSong(song, "Svc", DateTime.Today);

        // Service assigns a GUID only when Id is empty.
        Assert.Equal(string.Empty, record.Id);
    }

    [Fact]
    public async Task FromSong_LoggedThroughService_AggregatesWithCcliNumber()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), $"CcliMapTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        try
        {
            var usageService = new SongUsageService(Path.Combine(tempFolder, "usage.json"));
            var reportService = new CcliReportService(usageService);

            var song = new Song
            {
                Title = "Build My Life",
                Artist = "Pat Barrett",
                Ccli = "7070345",
                FolderPath = @"C:\Songs\build-my-life"
            };

            await usageService.LogUsageAsync(SongUsageMapper.FromSong(song, "Sunday AM", DateTime.Today));

            var report = await reportService.GetReportAsync(
                DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

            Assert.Single(report);
            Assert.Equal("Build My Life", report[0].SongTitle);
            Assert.Equal("Pat Barrett", report[0].Artist);
            Assert.Equal("7070345", report[0].CcliNumber);
            Assert.NotEqual(default, report[0].LastPlayed);
        }
        finally
        {
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, true);
        }
    }

    [Fact]
    public void PresenterSettings_CcliLicenseNumber_RoundTripsThroughJson()
    {
        var settings = new PresenterSettings { CcliLicenseNumber = "CCLI-998877" };

        var json = JsonSerializer.Serialize(settings);
        Assert.Contains("ccliLicenseNumber", json);

        var restored = JsonSerializer.Deserialize<PresenterSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("CCLI-998877", restored!.CcliLicenseNumber);
    }
}
