using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models.Bible;

/// <summary>
/// A parsed scripture reference (e.g., "John 3:16-18" parsed into components).
/// </summary>
public class ScriptureReference
{
    [JsonPropertyName("bookName")]
    public string BookName { get; set; } = "";

    [JsonPropertyName("bookId")]
    public int BookId { get; set; } = 0;

    [JsonPropertyName("chapter")]
    public int Chapter { get; set; } = 0;

    [JsonPropertyName("startVerse")]
    public int StartVerse { get; set; } = 0;

    [JsonPropertyName("endVerse")]
    public int? EndVerse { get; set; }

    [JsonPropertyName("endChapter")]
    public int? EndChapter { get; set; }

    [JsonPropertyName("translationId")]
    public string TranslationId { get; set; } = "";

    [JsonPropertyName("displayText")]
    public string DisplayText { get; set; } = "";

    /// <summary>
    /// Returns the formatted reference string (e.g., "John 3:16-18").
    /// </summary>
    public string ToReferenceString()
    {
        if (EndVerse == null && EndChapter == null)
            return $"{BookName} {Chapter}:{StartVerse}";

        if (EndChapter != null && EndChapter != Chapter)
            return $"{BookName} {Chapter}:{StartVerse}-{EndChapter}:{EndVerse}";

        return $"{BookName} {Chapter}:{StartVerse}-{EndVerse}";
    }
}
