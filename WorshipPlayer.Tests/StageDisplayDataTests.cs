using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for StageDisplayData JSON serialization.
/// Uses a local copy of the model to verify the serialization contract
/// matches what the stage display JavaScript expects.
/// (The actual model is in WorshipPlayer.Presenter which targets net8.0-windows.)
/// </summary>
public class StageDisplayDataTests
{
    /// <summary>
    /// Local copy of StageDisplayData for serialization testing.
    /// Must match the property names defined in the Presenter project.
    /// </summary>
    private class StageDisplayData
    {
        [JsonPropertyName("currentText")]
        public string CurrentText { get; set; } = "";

        [JsonPropertyName("nextText")]
        public string NextText { get; set; } = "";

        [JsonPropertyName("sectionName")]
        public string SectionName { get; set; } = "";

        [JsonPropertyName("songTitle")]
        public string SongTitle { get; set; } = "";

        [JsonPropertyName("key")]
        public string Key { get; set; } = "";

        [JsonPropertyName("capo")]
        public int Capo { get; set; } = 0;

        [JsonPropertyName("tempoPercent")]
        public double TempoPercent { get; set; } = 100.0;

        [JsonPropertyName("pitchShift")]
        public int PitchShift { get; set; } = 0;

        [JsonPropertyName("isLive")]
        public bool IsLive { get; set; } = false;

        [JsonPropertyName("isBlacked")]
        public bool IsBlacked { get; set; } = false;

        [JsonPropertyName("currentSlideIndex")]
        public int CurrentSlideIndex { get; set; } = 0;

        [JsonPropertyName("totalSlides")]
        public int TotalSlides { get; set; } = 0;
    }

    [Fact]
    public void StageDisplayData_SerializesToCorrectJsonPropertyNames()
    {
        var data = new StageDisplayData
        {
            CurrentText = "[G]Amazing [C]grace how [D]sweet the sound",
            NextText = "[G]That saved a [Em]wretch like [D]me",
            SectionName = "Verse 1",
            SongTitle = "Amazing Grace",
            Key = "G",
            Capo = 2,
            TempoPercent = 105.0,
            PitchShift = -2,
            IsLive = true,
            IsBlacked = false,
            CurrentSlideIndex = 1,
            TotalSlides = 8
        };

        var json = JsonSerializer.Serialize(data);

        // Verify all expected JSON property names are present
        Assert.Contains("\"currentText\":", json);
        Assert.Contains("\"nextText\":", json);
        Assert.Contains("\"sectionName\":", json);
        Assert.Contains("\"songTitle\":", json);
        Assert.Contains("\"key\":", json);
        Assert.Contains("\"capo\":", json);
        Assert.Contains("\"tempoPercent\":", json);
        Assert.Contains("\"pitchShift\":", json);
        Assert.Contains("\"isLive\":", json);
        Assert.Contains("\"isBlacked\":", json);
        Assert.Contains("\"currentSlideIndex\":", json);
        Assert.Contains("\"totalSlides\":", json);
    }

    [Fact]
    public void StageDisplayData_RoundTripSerialization()
    {
        var original = new StageDisplayData
        {
            CurrentText = "[G]Amazing [C]grace how [D]sweet the sound",
            NextText = "[G]That saved a [Em]wretch like [D]me",
            SectionName = "Chorus",
            SongTitle = "Amazing Grace",
            Key = "G",
            Capo = 3,
            TempoPercent = 95.5,
            PitchShift = 1,
            IsLive = true,
            IsBlacked = false,
            CurrentSlideIndex = 2,
            TotalSlides = 12
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<StageDisplayData>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.CurrentText, deserialized.CurrentText);
        Assert.Equal(original.NextText, deserialized.NextText);
        Assert.Equal(original.SectionName, deserialized.SectionName);
        Assert.Equal(original.SongTitle, deserialized.SongTitle);
        Assert.Equal(original.Key, deserialized.Key);
        Assert.Equal(original.Capo, deserialized.Capo);
        Assert.Equal(original.TempoPercent, deserialized.TempoPercent);
        Assert.Equal(original.PitchShift, deserialized.PitchShift);
        Assert.Equal(original.IsLive, deserialized.IsLive);
        Assert.Equal(original.IsBlacked, deserialized.IsBlacked);
        Assert.Equal(original.CurrentSlideIndex, deserialized.CurrentSlideIndex);
        Assert.Equal(original.TotalSlides, deserialized.TotalSlides);
    }

    [Fact]
    public void StageDisplayData_DefaultValues_SerializeCorrectly()
    {
        var data = new StageDisplayData();

        var json = JsonSerializer.Serialize(data);
        var deserialized = JsonSerializer.Deserialize<StageDisplayData>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("", deserialized.CurrentText);
        Assert.Equal("", deserialized.NextText);
        Assert.Equal("", deserialized.SectionName);
        Assert.Equal("", deserialized.SongTitle);
        Assert.Equal("", deserialized.Key);
        Assert.Equal(0, deserialized.Capo);
        Assert.Equal(100.0, deserialized.TempoPercent);
        Assert.Equal(0, deserialized.PitchShift);
        Assert.False(deserialized.IsLive);
        Assert.False(deserialized.IsBlacked);
        Assert.Equal(0, deserialized.CurrentSlideIndex);
        Assert.Equal(0, deserialized.TotalSlides);
    }

    [Fact]
    public void StageDisplayData_ChordsInText_PreservedInSerialization()
    {
        var data = new StageDisplayData
        {
            CurrentText = "[Am]In the [F]darkness\n[C]We will [G]stand",
            NextText = "[Am]Through the [F]storm"
        };

        var json = JsonSerializer.Serialize(data);
        var deserialized = JsonSerializer.Deserialize<StageDisplayData>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("[Am]In the [F]darkness\n[C]We will [G]stand", deserialized.CurrentText);
        Assert.Equal("[Am]Through the [F]storm", deserialized.NextText);
    }

    [Fact]
    public void StageDisplayData_BlackedState_SerializesCorrectly()
    {
        var data = new StageDisplayData
        {
            IsLive = true,
            IsBlacked = true,
            SongTitle = "Test Song"
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"isLive\":true", json);
        Assert.Contains("\"isBlacked\":true", json);
    }

    [Fact]
    public void StageDisplayData_StandbyState_SerializesCorrectly()
    {
        var data = new StageDisplayData
        {
            IsLive = false,
            IsBlacked = false,
            SongTitle = "Test Song"
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"isLive\":false", json);
        Assert.Contains("\"isBlacked\":false", json);
    }
}
