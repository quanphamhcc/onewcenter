using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// A setlist containing ordered songs for a worship service.
/// </summary>
public class Setlist
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled Setlist";

    [JsonPropertyName("date")]
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");

    [JsonPropertyName("entries")]
    public List<SetlistEntry> Entries { get; set; } = new();
}

/// <summary>
/// A single entry in a setlist.
/// </summary>
public class SetlistEntry
{
    [JsonPropertyName("songFolder")]
    public string SongFolder { get; set; } = "";

    [JsonPropertyName("keyShift")]
    public int KeyShift { get; set; } = 0;

    [JsonPropertyName("tempoPercent")]
    public double TempoPercent { get; set; } = 100.0;

    [JsonPropertyName("transition")]
    public TransitionType Transition { get; set; } = TransitionType.Stop;

    [JsonPropertyName("transitionDuration_ms")]
    public double TransitionDurationMs { get; set; } = 3000.0;
}

/// <summary>
/// Types of transitions between songs in a setlist.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransitionType
{
    Stop,           // Stop at end, wait for manual trigger
    AutoAdvance,    // Automatically start next song
    Crossfade,      // Crossfade into next song
    Overlap,        // Overlap (both play simultaneously briefly)
    FadeOut         // Fade out current, gap, then next
}
