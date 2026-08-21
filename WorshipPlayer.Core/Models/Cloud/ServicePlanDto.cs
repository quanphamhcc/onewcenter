using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models.Cloud;

/// <summary>
/// Data transfer object for cloud service plans.
/// Maps to the local ServiceOrder model for desktop execution.
/// </summary>
public class ServicePlanDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("serviceType")]
    public string ServiceType { get; set; } = "Sunday Morning";

    [JsonPropertyName("items")]
    public List<ServicePlanItemDto> Items { get; set; } = new();
}

/// <summary>
/// A single item within a cloud service plan.
/// Contains enough information to construct a local ServiceItem.
/// </summary>
public class ServicePlanItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("songId")]
    public string? SongId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Song";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("keyShift")]
    public int KeyShift { get; set; } = 0;

    [JsonPropertyName("tempoPercent")]
    public double TempoPercent { get; set; } = 100.0;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;
}
