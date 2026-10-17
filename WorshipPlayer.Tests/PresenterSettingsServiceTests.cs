using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Load/save round-trip tests for the consolidated PresenterSettingsService (SQL phase 4,
/// FEAT-001). These lock in: defaults on a missing file (no auto-write), defaults on a
/// malformed file (no throw), additive E5/J4 field survival across a save+reload, and that a
/// single save path (service only) reloads to identical values — proving no divergent writer.
/// </summary>
public class PresenterSettingsServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _settingsPath;

    public PresenterSettingsServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"PresenterSettingsTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _settingsPath = Path.Combine(_tempFolder, "presenter-settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public async Task LoadAsync_NoFile_YieldsDefaults_AndDoesNotCreateFile()
    {
        var service = new PresenterSettingsService(_settingsPath);
        await service.LoadAsync();

        Assert.True(service.Settings.StageServerEnabled);
        Assert.Equal(5055, service.Settings.StageServerPort);
        Assert.True(service.Settings.ScriptureShowReference);
        // Presenter settings never auto-created a file on a missing path.
        Assert.False(File.Exists(_settingsPath));
    }

    [Fact]
    public async Task LoadAsync_MalformedFile_YieldsDefaults_NoThrow()
    {
        await File.WriteAllTextAsync(_settingsPath, "{ this is not valid json ]");

        var service = new PresenterSettingsService(_settingsPath);
        await service.LoadAsync();

        Assert.True(service.Settings.StageServerEnabled);
        Assert.Equal(5055, service.Settings.StageServerPort);
        Assert.True(service.Settings.ScriptureShowReference);
    }

    [Fact]
    public async Task SaveThenReload_PreservesAdditiveAndFeatureFields()
    {
        var writer = new PresenterSettingsService(_settingsPath);
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

        var reader = new PresenterSettingsService(_settingsPath);
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
        var writer = new PresenterSettingsService(_settingsPath);
        await writer.LoadAsync();
        writer.Settings.OutputDisplay = 1;
        writer.Settings.StageServerPort = 7000;
        writer.Settings.CcliLicenseNumber = "42";
        writer.Settings.CongregationShowChords = true;
        await writer.SaveAsync();

        var reader = new PresenterSettingsService(_settingsPath);
        await reader.LoadAsync();

        Assert.Equal(writer.Settings.OutputDisplay, reader.Settings.OutputDisplay);
        Assert.Equal(writer.Settings.StageServerPort, reader.Settings.StageServerPort);
        Assert.Equal(writer.Settings.CcliLicenseNumber, reader.Settings.CcliLicenseNumber);
        Assert.Equal(writer.Settings.CongregationShowChords, reader.Settings.CongregationShowChords);
    }
}
