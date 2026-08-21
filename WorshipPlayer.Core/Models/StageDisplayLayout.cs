using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Layout configuration for the stage display output.
/// Defines the aspect ratio and positioned content blocks.
/// </summary>
public class StageDisplayLayout
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default Layout";

    [JsonPropertyName("aspectRatio")]
    public string AspectRatio { get; set; } = "16:9";

    [JsonPropertyName("blocks")]
    public List<StageDisplayBlock> Blocks { get; set; } = new();

    [JsonPropertyName("foregroundOption")]
    public StageDisplayForeground ForegroundOption { get; set; } = StageDisplayForeground.CurrentSlide;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// A positioned content block in the stage display layout.
/// </summary>
public class StageDisplayBlock
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("x")]
    public double X { get; set; } = 0;

    [JsonPropertyName("y")]
    public double Y { get; set; } = 0;

    [JsonPropertyName("width")]
    public double Width { get; set; } = 100;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 100;

    [JsonPropertyName("contentType")]
    public StageBlockContentType ContentType { get; set; } = StageBlockContentType.CurrentSlide;

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 48;

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#000000";
}

/// <summary>
/// Content type for a stage display block.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StageBlockContentType
{
    CurrentSlide,
    NextSlide,
    Clock,
    Countdown,
    Notes,
    SongTitle,
    ChordChart
}

/// <summary>
/// Foreground display option for stage display.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StageDisplayForeground
{
    CurrentSlide,
    NextSlide,
    Notes,
    ChordChart
}
