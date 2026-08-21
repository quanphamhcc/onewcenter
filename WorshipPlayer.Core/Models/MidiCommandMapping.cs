using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Maps a MIDI note/channel to a presenter command for remote control.
/// </summary>
public class MidiCommandMapping
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("command")]
    public MidiPresenterCommand Command { get; set; } = MidiPresenterCommand.NextSlide;

    [JsonPropertyName("midiNote")]
    public int MidiNote { get; set; } = 0;

    [JsonPropertyName("midiChannel")]
    public int MidiChannel { get; set; } = 1;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// Commands that can be triggered via MIDI for presenter control.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MidiPresenterCommand
{
    NextSlide,
    PrevSlide,
    NextItem,
    PrevItem,
    GoBlack,
    GoLive
}
