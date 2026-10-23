using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// One entry of the shared slide-label catalog (đợt 2 T7 / R5): a named, colored tag with an
/// optional hotkey that a song <see cref="Section"/> can reference via
/// <see cref="Section.LabelId"/>. The catalog lives in
/// <see cref="PresenterSettings.SlideLabels"/> so every consumer (Settings UI, song editor,
/// Operator) works from the same definitions. Labels are an ADDITIVE layer over sections —
/// a label never changes a section's loop/backtrack meaning (R5.5).
/// </summary>
public class SlideLabelDefinition
{
    /// <summary>
    /// Stable identifier referenced by <see cref="Section.LabelId"/>. The seeded defaults use
    /// fixed ids ("verse-1", "chorus", ...) so older data keeps resolving after a re-seed.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name, e.g. "Verse 1", "Chorus", "Bridge". Also the fallback key when resolving
    /// a legacy section that carries no <see cref="Section.LabelId"/> (name match, see
    /// <see cref="Services.SlideLabelService.ResolveForSection"/>).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Hex color (e.g. "#4A90D9") shown next to the section/slide in the UI.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#4A90D9";

    /// <summary>
    /// Optional hotkey text (e.g. "C"). Empty = no hotkey. Two labels must not share a
    /// non-empty hotkey (case-insensitive) — enforced by
    /// <see cref="Services.SlideLabelService.FindDuplicateHotkey"/> before a save (R5.2).
    /// </summary>
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "";
}
