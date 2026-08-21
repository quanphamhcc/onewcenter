using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models.Bible;

/// <summary>
/// Represents an installed Bible translation (e.g., KJV, WEB).
/// </summary>
public class BibleTranslation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("abbreviation")]
    public string Abbreviation { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; } = false;

    [JsonPropertyName("databasePath")]
    public string DatabasePath { get; set; } = "";

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}
