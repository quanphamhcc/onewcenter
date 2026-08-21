using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Configuration for a countdown/count-up timer displayed on outputs.
/// </summary>
public class CountdownConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled Countdown";

    [JsonPropertyName("type")]
    public CountdownType Type { get; set; } = CountdownType.CountdownToZero;

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; } = 300000; // 5 minutes default

    [JsonPropertyName("targetTime")]
    public DateTime? TargetTime { get; set; }

    [JsonPropertyName("showOnAudience")]
    public bool ShowOnAudience { get; set; } = true;

    [JsonPropertyName("showOnStage")]
    public bool ShowOnStage { get; set; } = true;

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 72;

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// Type of countdown timer.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CountdownType
{
    CountdownToZero,
    CountdownToTime,
    CountUp
}
