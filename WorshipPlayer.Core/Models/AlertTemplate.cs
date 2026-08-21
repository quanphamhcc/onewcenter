using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Represents an alert template for displaying overlay text on outputs.
/// </summary>
public class AlertTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled Alert";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 48;

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("hasShadow")]
    public bool HasShadow { get; set; } = true;

    [JsonPropertyName("shadowColor")]
    public string ShadowColor { get; set; } = "#000000";

    [JsonPropertyName("hasOutline")]
    public bool HasOutline { get; set; } = false;

    [JsonPropertyName("outlineColor")]
    public string OutlineColor { get; set; } = "#000000";

    [JsonPropertyName("outlineWidth")]
    public double OutlineWidth { get; set; } = 1.0;

    [JsonPropertyName("showOnStageDisplay")]
    public bool ShowOnStageDisplay { get; set; } = true;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}
