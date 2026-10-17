using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Load/save tests for SettingsService (SQL phase 4), now SQLite-backed (app.db, AppSettings
/// single-row table). Covers: defaults when no row/file; save->reload round-trip through a fresh
/// instance on the same db (including SongsFolder/SetlistsFolder); the one-time non-duplicating
/// import of a legacy settings.json; malformed legacy json yielding defaults without throwing; and
/// enum fields (OutputMode / DefaultJumpTiming) round-tripping as string names inside DataJson.
/// Each test injects a temp app.db and clears SQLite connection pools in teardown so the temp .db
/// file can be deleted on Windows (mirrors SongUsageServiceTests).
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;

    private static readonly JsonSerializerOptions LegacyOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"SettingsTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
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
    public async Task LoadAsync_NoRowOrFile_YieldsDefaults()
    {
        var service = new SettingsService(_dbPath);
        await service.LoadAsync();

        var defaults = new AppSettings();
        Assert.Equal(defaults.SampleRate, service.Settings.SampleRate);
        Assert.Equal(defaults.BufferSize, service.Settings.BufferSize);
        Assert.Equal(defaults.OutputMode, service.Settings.OutputMode);
        Assert.Equal(defaults.DefaultJumpTiming, service.Settings.DefaultJumpTiming);
    }

    [Fact]
    public async Task SaveThenReload_RoundTripsThroughFreshInstance()
    {
        var writer = new SettingsService(_dbPath);
        await writer.LoadAsync();
        writer.Settings.AudioDevice = "Focusrite";
        writer.Settings.SampleRate = 48000;
        writer.Settings.BufferSize = 512;
        writer.Settings.SongsFolder = @"C:\Worship\Songs";
        writer.Settings.SetlistsFolder = @"C:\Worship\Setlists";
        await writer.SaveAsync();

        var reader = new SettingsService(_dbPath);
        await reader.LoadAsync();

        Assert.Equal("Focusrite", reader.Settings.AudioDevice);
        Assert.Equal(48000, reader.Settings.SampleRate);
        Assert.Equal(512, reader.Settings.BufferSize);
        Assert.Equal(@"C:\Worship\Songs", reader.Settings.SongsFolder);
        Assert.Equal(@"C:\Worship\Setlists", reader.Settings.SetlistsFolder);
    }

    [Fact]
    public async Task EnumFields_RoundTripAsStringNamesInDataJson()
    {
        var writer = new SettingsService(_dbPath);
        await writer.LoadAsync();
        writer.Settings.OutputMode = OutputMode.MultiOutput;
        writer.Settings.DefaultJumpTiming = JumpTiming.After2Bars;
        await writer.SaveAsync();

        // The DataJson column must store the enum as its string name, not its numeric value.
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT DataJson FROM AppSettings WHERE Id = 1";
            var json = (string)(await cmd.ExecuteScalarAsync())!;
            Assert.Contains("MultiOutput", json);
            Assert.Contains("After2Bars", json);
        }

        var reader = new SettingsService(_dbPath);
        await reader.LoadAsync();
        Assert.Equal(OutputMode.MultiOutput, reader.Settings.OutputMode);
        Assert.Equal(JumpTiming.After2Bars, reader.Settings.DefaultJumpTiming);
    }

    [Fact]
    public async Task Import_LegacyJson_ImportsOnce_AndSecondInstanceDoesNotDuplicate()
    {
        var legacyPath = Path.Combine(_tempFolder, "settings.json");
        var legacy = new AppSettings
        {
            AudioDevice = "Legacy Device",
            SampleRate = 96000,
            SongsFolder = @"C:\Legacy\Songs",
            OutputMode = OutputMode.Stereo
        };
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(legacy, LegacyOptions));

        // First instance imports the legacy settings.
        var first = new SettingsService(_dbPath);
        await first.LoadAsync();
        Assert.Equal("Legacy Device", first.Settings.AudioDevice);
        Assert.Equal(96000, first.Settings.SampleRate);
        Assert.Equal(@"C:\Legacy\Songs", first.Settings.SongsFolder);
        Assert.Equal(OutputMode.Stereo, first.Settings.OutputMode);

        // Save a change, then a SECOND instance on the same db must read the SAVED value, not
        // re-import the legacy file (single logical record, no duplication).
        first.Settings.AudioDevice = "Saved Device";
        await first.SaveAsync();

        var second = new SettingsService(_dbPath);
        await second.LoadAsync();
        Assert.Equal("Saved Device", second.Settings.AudioDevice);
        Assert.Equal(96000, second.Settings.SampleRate);
    }

    [Fact]
    public async Task Import_MalformedLegacyJson_YieldsDefaults_NoThrow()
    {
        var legacyPath = Path.Combine(_tempFolder, "settings.json");
        await File.WriteAllTextAsync(legacyPath, "{ this is not valid json ]");

        var service = new SettingsService(_dbPath);
        await service.LoadAsync();

        var defaults = new AppSettings();
        Assert.Equal(defaults.SampleRate, service.Settings.SampleRate);
        Assert.Equal(defaults.OutputMode, service.Settings.OutputMode);
    }
}
