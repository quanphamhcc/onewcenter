using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Configuration for an output display screen (audience, stage, operator).
/// </summary>
public class ScreenConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("displayIndex")]
    public int DisplayIndex { get; set; } = 0;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Display";

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("useGpuAcceleration")]
    public bool UseGpuAcceleration { get; set; } = true;

    [JsonPropertyName("purpose")]
    public ScreenPurpose Purpose { get; set; } = ScreenPurpose.Audience;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// The purpose/role of an output screen.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScreenPurpose
{
    Audience,
    Stage,
    Operator
}
