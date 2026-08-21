using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Records when a song is played during a service for usage tracking and CCLI reporting.
/// </summary>
public class SongUsageRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("songTitle")]
    public string SongTitle { get; set; } = "";

    [JsonPropertyName("songId")]
    public string SongId { get; set; } = "";

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = "";

    [JsonPropertyName("serviceDate")]
    public DateTime ServiceDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("datePlayed")]
    public DateTime DatePlayed { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("ccliNumber")]
    public string CcliNumber { get; set; } = "";

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = "";

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}
