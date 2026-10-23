using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure slide-label logic for đợt 2 T7 / R5 — the ONE place that seeds the shared label
/// catalog, validates hotkeys, and resolves the effective label of a song section, so the
/// Settings UI (T8), the song editor, and the Operator all behave identically.
///
/// Resolution contract of <see cref="ResolveForSection"/>:
///   1. A section WITH a <see cref="Section.LabelId"/> resolves to the catalog entry with
///      that id. If the id is no longer in the catalog (the label was deleted), the section
///      resolves to NO label — never an exception, and no silent fallback to the name rule
///      (the reference was explicit, so its disappearance is explicit too).
///   2. A legacy section WITHOUT a LabelId resolves by name: the first catalog entry whose
///      name equals the section name (trimmed, case-insensitive). This is what keeps
///      pre-label song files meaningful without rewriting them (no bulk migration).
///   3. Anything else (unknown name, empty catalog, null inputs) resolves to null.
/// </summary>
public static class SlideLabelService
{
    /// <summary>
    /// The seeded catalog for settings that predate slide labels (R5.1): the six reference
    /// labels with distinct colors, stable ids, and no hotkeys.
    /// A fresh list with fresh instances is returned on every call.
    /// </summary>
    public static List<SlideLabelDefinition> CreateDefaults() => new()
    {
        new() { Id = "verse-1", Name = "Verse 1", Color = "#4A90D9", Hotkey = "" },
        new() { Id = "verse-2", Name = "Verse 2", Color = "#7B61FF", Hotkey = "" },
        new() { Id = "verse-3", Name = "Verse 3", Color = "#00A896", Hotkey = "" },
        new() { Id = "verse-4", Name = "Verse 4", Color = "#F2A104", Hotkey = "" },
        new() { Id = "chorus",  Name = "Chorus",  Color = "#E4572E", Hotkey = "" },
        new() { Id = "bridge",  Name = "Bridge",  Color = "#D6336C", Hotkey = "" },
    };

    /// <summary>
    /// Seed <see cref="PresenterSettings.SlideLabels"/> with the defaults when the settings
    /// carry no catalog at all (null = the JSON key was absent, i.e. an older file — R5
    /// edge case). A present list is the user's own state and is returned untouched — even
    /// an EMPTY list (the user may have deleted every label on purpose). Idempotent.
    /// Returns the effective catalog.
    /// </summary>
    public static List<SlideLabelDefinition> EnsureLabels(PresenterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SlideLabels ??= CreateDefaults();
        return settings.SlideLabels;
    }

    /// <summary>
    /// Find a duplicated non-empty hotkey in the catalog (R5.2: duplicates must block a
    /// save). Comparison ignores case and surrounding whitespace; empty/whitespace hotkeys
    /// mean "no hotkey" and never count as duplicates. Returns the hotkey text as stored on
    /// the first label that has a duplicate partner, or null when the catalog is valid.
    /// </summary>
    public static string? FindDuplicateHotkey(IEnumerable<SlideLabelDefinition>? labels)
    {
        if (labels == null) return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            var normalized = NormalizeHotkey(label.Hotkey);
            if (normalized.Length == 0) continue;
            if (!seen.Add(normalized))
                return label.Hotkey;
        }
        return null;
    }

    /// <summary>
    /// Resolve the effective label for a section against a catalog, per the contract in the
    /// class remarks. Never throws on dangling references or null inputs.
    /// </summary>
    public static SlideLabelDefinition? ResolveForSection(
        Section? section, IEnumerable<SlideLabelDefinition>? labels)
    {
        if (section == null || labels == null) return null;

        if (!string.IsNullOrWhiteSpace(section.LabelId))
        {
            // Explicit reference: exact id match or nothing (deleted label → no label).
            return labels.FirstOrDefault(l => l.Id == section.LabelId);
        }

        var name = section.Name?.Trim() ?? "";
        if (name.Length == 0) return null;
        return labels.FirstOrDefault(
            l => string.Equals(l.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeHotkey(string? hotkey)
        => (hotkey ?? "").Trim().ToUpperInvariant();
}
