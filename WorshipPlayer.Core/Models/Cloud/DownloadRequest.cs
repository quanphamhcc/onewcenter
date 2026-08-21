using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models.Cloud;

/// <summary>
/// Request model for downloading a song from the cloud library.
/// Allows selecting which components to include in the download.
/// </summary>
public class DownloadRequest
{
    [JsonPropertyName("songId")]
    public string SongId { get; set; } = "";

    [JsonPropertyName("includeBacktracks")]
    public bool IncludeBacktracks { get; set; } = true;

    [JsonPropertyName("includeLyrics")]
    public bool IncludeLyrics { get; set; } = true;

    [JsonPropertyName("includeChordChart")]
    public bool IncludeChordChart { get; set; } = true;

    [JsonPropertyName("targetFolder")]
    public string TargetFolder { get; set; } = "";
}
