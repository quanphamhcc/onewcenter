using System.Text.Json;
using WorshipPlayer.Core.Models;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the stage server settings in PresenterSettings.
/// </summary>
public class PresenterSettingsStageServerTests
{
    [Fact]
    public void StageServerEnabled_DefaultsToTrue()
    {
        var settings = new PresenterSettings();
        Assert.True(settings.StageServerEnabled);
    }

    [Fact]
    public void StageServerPort_DefaultsTo5055()
    {
        var settings = new PresenterSettings();
        Assert.Equal(5055, settings.StageServerPort);
    }

    [Fact]
    public void StageServerSettings_RoundTripSerialization()
    {
        var settings = new PresenterSettings
        {
            StageServerEnabled = false,
            StageServerPort = 8080
        };

        var json = JsonSerializer.Serialize(settings);
        var deserialized = JsonSerializer.Deserialize<PresenterSettings>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.StageServerEnabled);
        Assert.Equal(8080, deserialized.StageServerPort);
    }

    [Fact]
    public void StageServerSettings_JsonPropertyNames_Correct()
    {
        var settings = new PresenterSettings
        {
            StageServerEnabled = true,
            StageServerPort = 5055
        };

        var json = JsonSerializer.Serialize(settings);

        Assert.Contains("\"stageServerEnabled\":", json);
        Assert.Contains("\"stageServerPort\":", json);
    }
}
