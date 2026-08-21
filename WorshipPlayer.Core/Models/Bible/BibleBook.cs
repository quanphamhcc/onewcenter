using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models.Bible;

/// <summary>
/// Represents a book of the Bible (e.g., Genesis, Matthew).
/// </summary>
public class BibleBook
{
    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("abbreviation")]
    public string Abbreviation { get; set; } = "";

    [JsonPropertyName("testament")]
    public Testament Testament { get; set; } = Testament.OldTestament;

    [JsonPropertyName("chapterCount")]
    public int ChapterCount { get; set; } = 0;

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;
}

/// <summary>
/// Bible testament.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Testament
{
    OldTestament,
    NewTestament
}
