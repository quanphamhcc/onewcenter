using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Configuration for the CCLI/copyright footer displayed on slides.
/// </summary>
public class FooterSettings
{
    [JsonPropertyName("ccliLicenseNumber")]
    public string CcliLicenseNumber { get; set; } = "";

    [JsonPropertyName("footerLocation")]
    public FooterLocation FooterLocation { get; set; } = FooterLocation.BottomCenter;

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 18;

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#888888";

    [JsonPropertyName("showOnLyrics")]
    public bool ShowOnLyrics { get; set; } = true;

    [JsonPropertyName("showOnScripture")]
    public bool ShowOnScripture { get; set; } = false;

    [JsonPropertyName("showOnAll")]
    public bool ShowOnAll { get; set; } = false;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// Location of the footer on slides.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FooterLocation
{
    BottomLeft,
    BottomCenter,
    BottomRight
}
