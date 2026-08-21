using System.Text.Json.Serialization;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Data model representing the current state of the stage display.
/// Serialized to JSON and sent over WebSocket to connected stage display clients.
/// </summary>
public class StageDisplayData
{
    /// <summary>
    /// Current slide text with chords (transposed as needed).
    /// </summary>
    [JsonPropertyName("currentText")]
    public string CurrentText { get; set; } = "";

    /// <summary>
    /// Next slide text preview.
    /// </summary>
    [JsonPropertyName("nextText")]
    public string NextText { get; set; } = "";

    /// <summary>
    /// Current section name (e.g., "Verse 1", "Chorus").
    /// </summary>
    [JsonPropertyName("sectionName")]
    public string SectionName { get; set; } = "";

    /// <summary>
    /// Current song title.
    /// </summary>
    [JsonPropertyName("songTitle")]
    public string SongTitle { get; set; } = "";

    /// <summary>
    /// Current key of the song.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>
    /// Original key of the song (before pitch shift).
    /// </summary>
    [JsonPropertyName("originalKey")]
    public string OriginalKey { get; set; } = "";

    /// <summary>
    /// Effective key after pitch shift (concert pitch key).
    /// </summary>
    [JsonPropertyName("effectiveKey")]
    public string EffectiveKey { get; set; } = "";

    /// <summary>
    /// Capo position (0 = no capo).
    /// </summary>
    [JsonPropertyName("capo")]
    public int Capo { get; set; } = 0;

    /// <summary>
    /// Tempo as percentage of original (100 = normal).
    /// </summary>
    [JsonPropertyName("tempoPercent")]
    public double TempoPercent { get; set; } = 100.0;

    /// <summary>
    /// Pitch shift in semitones from original key.
    /// </summary>
    [JsonPropertyName("pitchShift")]
    public int PitchShift { get; set; } = 0;

    /// <summary>
    /// Whether the presenter is live (outputting to congregation display).
    /// </summary>
    [JsonPropertyName("isLive")]
    public bool IsLive { get; set; } = false;

    /// <summary>
    /// Whether the display is blacked out.
    /// </summary>
    [JsonPropertyName("isBlacked")]
    public bool IsBlacked { get; set; } = false;

    /// <summary>
    /// Zero-based index of the current slide.
    /// </summary>
    [JsonPropertyName("currentSlideIndex")]
    public int CurrentSlideIndex { get; set; } = 0;

    /// <summary>
    /// Total number of slides in the current item.
    /// </summary>
    [JsonPropertyName("totalSlides")]
    public int TotalSlides { get; set; } = 0;
}
