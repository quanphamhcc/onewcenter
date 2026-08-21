using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Represents a collapsible section group in the service builder sidebar.
/// Groups organize service items into named sections like "Opening Worship", "Message", "Response".
/// </summary>
public class ServiceGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled Group";

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = new();

    [JsonPropertyName("isCollapsed")]
    public bool IsCollapsed { get; set; } = false;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}
