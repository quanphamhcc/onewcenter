using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models.Cloud;

/// <summary>
/// Represents a song in the cloud library.
/// Contains metadata for browsing, searching, and syncing songs.
/// </summary>
public class SongLibraryItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = "";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "C";

    [JsonPropertyName("bpm")]
    public double Bpm { get; set; } = 120.0;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("ccliNumber")]
    public string CcliNumber { get; set; } = "";

    [JsonPropertyName("hasBacktracks")]
    public bool HasBacktracks { get; set; }

    [JsonPropertyName("hasLyrics")]
    public bool HasLyrics { get; set; }

    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;
}
