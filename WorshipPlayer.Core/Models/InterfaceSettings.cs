using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Settings for the editor/interface appearance and behavior.
/// </summary>
public class InterfaceSettings
{
    [JsonPropertyName("editorBackgroundColor")]
    public string EditorBackgroundColor { get; set; } = "#0F1117";

    [JsonPropertyName("mediaLoadBehavior")]
    public MediaLoadBehavior MediaLoadBehavior { get; set; } = MediaLoadBehavior.CopyToLibrary;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// Behavior when loading media into the application.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaLoadBehavior
{
    CopyToLibrary,
    LinkToOriginal
}
