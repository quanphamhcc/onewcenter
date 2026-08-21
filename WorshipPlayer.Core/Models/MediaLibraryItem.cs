using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Represents a media item in the local media library (image, video, audio).
/// </summary>
public class MediaLibraryItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("mediaType")]
    public MediaLibraryType MediaType { get; set; } = MediaLibraryType.Image;

    [JsonPropertyName("thumbnailPath")]
    public string ThumbnailPath { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; } = 0;

    [JsonPropertyName("dateAdded")]
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// Types of media in the media library.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaLibraryType
{
    Image,
    Video,
    Audio
}
