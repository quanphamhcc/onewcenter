using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models.Cloud;

/// <summary>
/// Represents the synchronization state of a song between local and cloud.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SyncState
{
    UpToDate,
    LocalChanges,
    RemoteChanges,
    Conflict,
    Downloading,
    Uploading
}

/// <summary>
/// Tracks sync information for a single song, including version details
/// and conflict information when applicable.
/// </summary>
public class SyncInfo
{
    [JsonPropertyName("songId")]
    public string SongId { get; set; } = "";

    [JsonPropertyName("lastSyncTime")]
    public DateTime? LastSyncTime { get; set; }

    [JsonPropertyName("state")]
    public SyncState State { get; set; } = SyncState.UpToDate;

    [JsonPropertyName("conflictDetails")]
    public string? ConflictDetails { get; set; }

    [JsonPropertyName("localVersion")]
    public int LocalVersion { get; set; } = 1;

    [JsonPropertyName("remoteVersion")]
    public int RemoteVersion { get; set; } = 1;
}
