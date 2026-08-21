using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models.Bible;

/// <summary>
/// Represents a single Bible verse with its text and location.
/// </summary>
public class BibleVerse
{
    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("bookId")]
    public int BookId { get; set; } = 0;

    [JsonPropertyName("bookName")]
    public string BookName { get; set; } = "";

    [JsonPropertyName("chapter")]
    public int Chapter { get; set; } = 0;

    [JsonPropertyName("verse")]
    public int Verse { get; set; } = 0;

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("translationId")]
    public string TranslationId { get; set; } = "";
}
