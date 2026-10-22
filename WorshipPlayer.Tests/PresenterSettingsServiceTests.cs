using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Load/save round-trip tests for the consolidated PresenterSettingsService (SQL phase 4), now
/// SQLite-backed (app.db, PresenterSettings single-row table). These lock in: defaults on an
/// absent row (no auto-write), defaults on malformed data (no throw), additive E5/J4 field
/// survival across a save+reload, a single save path (service only) reloading to identical values
/// (proving no divergent writer), and the one-time, non-duplicating import of a legacy
/// presenter-settings.json. Each test injects a temp app.db and clears SQLite connection pools in
/// teardown so the temp .db file can be deleted on Windows (mirrors SongUsageServiceTests).
/// </summary>
public class PresenterSettingsServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;

    private static readonly JsonSerializerOptions LegacyOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public PresenterSettingsServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"PresenterSettingsTests_{Guid.NewGuid():N}");
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
    public async Task LoadAsync_NoRow_YieldsDefaults_AndDoesNotCreateRow()
    {
        var service = new PresenterSettingsService(_dbPath);
        await service.LoadAsync();

        Assert.True(service.Settings.StageServerEnabled);
        Assert.Equal(5055, service.Settings.StageServerPort);
        Assert.True(service.Settings.ScriptureShowReference);

        // Presenter settings never auto-created a record on a missing store: a fresh service on
        // the same db still sees defaults (no row was written by the first load).
        var reopened = new PresenterSettingsService(_dbPath);
        await reopened.LoadAsync();
        Assert.True(reopened.Settings.StageServerEnabled);
        Assert.Equal(5055, reopened.Settings.StageServerPort);
    }

    [Fact]
    public async Task LoadAsync_MalformedLegacyFile_YieldsDefaults_NoThrow()
    {
        var legacyPath = Path.Combine(_tempFolder, "presenter-settings.json");
        await File.WriteAllTextAsync(legacyPath, "{ this is not valid json ]");

        var service = new PresenterSettingsService(_dbPath);
        await service.LoadAsync();

        Assert.True(service.Settings.StageServerEnabled);
        Assert.Equal(5055, service.Settings.StageServerPort);
        Assert.True(service.Settings.ScriptureShowReference);
    }

    [Fact]
    public async Task SaveThenReload_PreservesAdditiveAndFeatureFields()
    {
        var writer = new PresenterSettingsService(_dbPath);
        await writer.LoadAsync();

        // E5 (scripture display) + J4/CCLI additive fields.
        writer.Settings.ScriptureShowReference = false;
        writer.Settings.ScriptureShowTranslationName = false;
        writer.Settings.CcliLicenseNumber = "1234567";

        // NDI fields.
        writer.Settings.NdiEnabled = true;
        writer.Settings.NdiSourceName = "Test NDI";
        writer.Settings.NdiFps = 60;

        // Stage-server fields.
        writer.Settings.StageServerEnabled = false;
        writer.Settings.StageServerPort = 8080;

        // Remote fields.
        writer.Settings.RemoteEnabled = true;
        writer.Settings.RemotePin = "9999";

        // Shortcut fields.
        writer.Settings.ShortcutNextSlide = "Space";

        await writer.SaveAsync();

        var reader = new PresenterSettingsService(_dbPath);
        await reader.LoadAsync();

        Assert.False(reader.Settings.ScriptureShowReference);
        Assert.False(reader.Settings.ScriptureShowTranslationName);
        Assert.Equal("1234567", reader.Settings.CcliLicenseNumber);

        Assert.True(reader.Settings.NdiEnabled);
        Assert.Equal("Test NDI", reader.Settings.NdiSourceName);
        Assert.Equal(60, reader.Settings.NdiFps);

        Assert.False(reader.Settings.StageServerEnabled);
        Assert.Equal(8080, reader.Settings.StageServerPort);

        Assert.True(reader.Settings.RemoteEnabled);
        Assert.Equal("9999", reader.Settings.RemotePin);

        Assert.Equal("Space", reader.Settings.ShortcutNextSlide);
    }

    [Fact]
    public async Task SingleSavePath_ServiceSaveAndServiceReload_AreIdentical()
    {
        var writer = new PresenterSettingsService(_dbPath);
        await writer.LoadAsync();
        writer.Settings.OutputDisplay = 1;
        writer.Settings.StageServerPort = 7000;
        writer.Settings.CcliLicenseNumber = "42";
        writer.Settings.CongregationShowChords = true;
        await writer.SaveAsync();

        var reader = new PresenterSettingsService(_dbPath);
        await reader.LoadAsync();

        Assert.Equal(writer.Settings.OutputDisplay, reader.Settings.OutputDisplay);
        Assert.Equal(writer.Settings.StageServerPort, reader.Settings.StageServerPort);
        Assert.Equal(writer.Settings.CcliLicenseNumber, reader.Settings.CcliLicenseNumber);
        Assert.Equal(writer.Settings.CongregationShowChords, reader.Settings.CongregationShowChords);
    }

    [Fact]
    public async Task Import_LegacyJson_ImportsOnce_AndSecondInstanceDoesNotDuplicate()
    {
        // Write a legacy presenter-settings.json beside the (not-yet-created) app.db.
        var legacyPath = Path.Combine(_tempFolder, "presenter-settings.json");
        var legacy = new PresenterSettings
        {
            StageServerPort = 6060,
            CcliLicenseNumber = "LEGACY-123",
            ScriptureShowReference = false,
            NdiEnabled = true
        };
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(legacy, LegacyOptions));

        // First service instance imports the legacy record.
        var first = new PresenterSettingsService(_dbPath);
        await first.LoadAsync();
        Assert.Equal(6060, first.Settings.StageServerPort);
        Assert.Equal("LEGACY-123", first.Settings.CcliLicenseNumber);
        Assert.False(first.Settings.ScriptureShowReference);
        Assert.True(first.Settings.NdiEnabled);

        // Mutate + save, then a SECOND instance on the same app.db must read the SAVED values,
        // not re-import the legacy file (single logical record, no duplication).
        first.Settings.CcliLicenseNumber = "SAVED-999";
        await first.SaveAsync();

        var second = new PresenterSettingsService(_dbPath);
        await second.LoadAsync();
        Assert.Equal("SAVED-999", second.Settings.CcliLicenseNumber);
        // Additive E5/J4 field survived import + round-trip.
        Assert.False(second.Settings.ScriptureShowReference);
        Assert.True(second.Settings.NdiEnabled);
    }

    [Fact]
    public void FreshPresenterSettings_DefaultsAutoSlideConfig_To4And500()
    {
        // The EditSections wiring (MainWindow.EditSections_Click) copies
        // _presenterSettingsService.Settings.LinesPerSlide/SlideLeadMs into the editor.
        // When the user never changed Settings, the fallback MUST stay 4/500 so the
        // slide-creation pipeline behaves exactly as before. This pins that contract.
        var fresh = new PresenterSettings();

        Assert.Equal(4, fresh.LinesPerSlide);
        Assert.Equal(500, fresh.SlideLeadMs);
    }

    [Fact]
    public async Task SaveThenReload_PreservesAutoSlideConfig_NonDefaultValues()
    {
        // Non-default LinesPerSlide/SlideLeadMs must survive the service save+reload so the
        // values the EditSections wiring reads from _presenterSettingsService.Settings are the
        // user's configured values (e.g. 2 / 1200), not the 4/500 defaults.
        var writer = new PresenterSettingsService(_dbPath);
        await writer.LoadAsync();

        writer.Settings.LinesPerSlide = 2;
        writer.Settings.SlideLeadMs = 1200;
        await writer.SaveAsync();

        var reader = new PresenterSettingsService(_dbPath);
        await reader.LoadAsync();

        Assert.Equal(2, reader.Settings.LinesPerSlide);
        Assert.Equal(1200, reader.Settings.SlideLeadMs);
    }

    [Fact]
    public async Task Import_MalformedLegacyJson_DoesNotThrow_AndYieldsDefaults()
    {
        var legacyPath = Path.Combine(_tempFolder, "presenter-settings.json");
        await File.WriteAllTextAsync(legacyPath, "{ this is not valid json ]");

        var service = new PresenterSettingsService(_dbPath);
        await service.LoadAsync();

        Assert.Equal(5055, service.Settings.StageServerPort);
        Assert.True(service.Settings.StageServerEnabled);
    }
}
