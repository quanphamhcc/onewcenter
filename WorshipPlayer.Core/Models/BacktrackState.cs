using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Represents the current state of the backtrack player.
/// The Presenter reads this to sync its display with audio playback.
/// Updated by the audio engine during playback.
/// </summary>
public class BacktrackState
{
    /// <summary>
    /// Whether the backtrack is currently playing.
    /// </summary>
    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; set; }

    /// <summary>
    /// Title of the currently loaded song.
    /// </summary>
    [JsonPropertyName("currentSongTitle")]
    public string CurrentSongTitle { get; set; } = "";

    /// <summary>
    /// Index of the current section in Song.Sections.
    /// </summary>
    [JsonPropertyName("currentSectionIndex")]
    public int CurrentSectionIndex { get; set; } = -1;

    /// <summary>
    /// Name of the current section (e.g., "VERSE 1", "CHORUS").
    /// </summary>
    [JsonPropertyName("currentSectionName")]
    public string CurrentSectionName { get; set; } = "";

    /// <summary>
    /// Current playback position in milliseconds.
    /// </summary>
    [JsonPropertyName("currentPositionMs")]
    public double CurrentPositionMs { get; set; }

    /// <summary>
    /// Current pitch shift in semitones (from real-time transposition).
    /// Positive = up, negative = down.
    /// </summary>
    [JsonPropertyName("pitchShiftSemitones")]
    public int PitchShiftSemitones { get; set; }

    /// <summary>
    /// Current tempo as a percentage of original (100 = normal).
    /// </summary>
    [JsonPropertyName("tempoPercent")]
    public double TempoPercent { get; set; } = 100.0;

    /// <summary>
    /// Whether the current section is looping.
    /// </summary>
    [JsonPropertyName("isLooping")]
    public bool IsLooping { get; set; }

    /// <summary>
    /// Name of the section queued to play next (if a section jump has been queued).
    /// Empty if no section is queued.
    /// </summary>
    [JsonPropertyName("queuedSectionName")]
    public string QueuedSectionName { get; set; } = "";
}
