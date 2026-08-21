using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Per-item settings for auto-advance, transitions, and navigation behavior.
/// </summary>
public class ServiceItemSettings
{
    [JsonPropertyName("autoAdvanceGoTo")]
    public AutoAdvanceGoTo AutoAdvanceGoTo { get; set; } = AutoAdvanceGoTo.NextSlide;

    [JsonPropertyName("autoAdvanceAfterCondition")]
    public AutoAdvanceAfterCondition AutoAdvanceAfterCondition { get; set; } = AutoAdvanceAfterCondition.Manual;

    [JsonPropertyName("autoAdvanceDelayMs")]
    public int AutoAdvanceDelayMs { get; set; } = 5000;

    [JsonPropertyName("transitionFadeTimeMs")]
    public int TransitionFadeTimeMs { get; set; } = 500;

    [JsonPropertyName("goToItemId")]
    public string? GoToItemId { get; set; }

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// Defines where auto-advance navigates to.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoAdvanceGoTo
{
    NextSlide,
    NextItem,
    SpecificItem,
    Loop
}

/// <summary>
/// Defines the condition that triggers auto-advance.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoAdvanceAfterCondition
{
    Manual,
    Timer,
    AudioEnd,
    VideoEnd
}
