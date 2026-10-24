using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure slide-label navigation/display logic for đợt 2 T8 / R5.3–R5.4 — the ONE place that
/// maps a slide to its effective label and a label hotkey to a target slide, so the Operator
/// window's thumbnails and its keyboard handling behave identically (and are unit-testable
/// without WPF).
///
/// A slide knows only its <see cref="Slide.SectionName"/>; the bridge back to a
/// <see cref="Section"/> (and therefore to an explicit <see cref="Section.LabelId"/>) is a
/// name match against the song's sections (trimmed, case-insensitive — the same convention
/// the slide generators use when they stamp SectionName). When no section matches, a
/// synthetic name-only section is resolved instead, which keeps the legacy name fallback of
/// <see cref="SlideLabelService.ResolveForSection"/> working for slides whose song is not
/// loaded.
/// </summary>
public static class SlideLabelNavigation
{
    /// <summary>
    /// The effective label of one slide, or null when the slide's section carries no label.
    /// Never throws on null/empty inputs.
    /// </summary>
    public static SlideLabelDefinition? ResolveLabelForSlide(
        Slide? slide,
        IReadOnlyList<Section>? sections,
        IEnumerable<SlideLabelDefinition>? labels)
    {
        if (slide == null || labels == null) return null;

        var sectionName = (slide.SectionName ?? "").Trim();
        if (sectionName.Length == 0) return null;

        var section = FindSection(sections, sectionName)
            ?? new Section { Name = sectionName };
        return SlideLabelService.ResolveForSection(section, labels);
    }

    /// <summary>
    /// The catalog entry whose hotkey equals <paramref name="hotkey"/> (trimmed,
    /// case-insensitive), or null. Empty hotkeys mean "no hotkey" and never match.
    /// </summary>
    public static SlideLabelDefinition? FindLabelByHotkey(
        IEnumerable<SlideLabelDefinition>? labels, string? hotkey)
    {
        var wanted = (hotkey ?? "").Trim();
        if (wanted.Length == 0 || labels == null) return null;

        return labels.FirstOrDefault(l =>
            !string.IsNullOrWhiteSpace(l.Hotkey) &&
            string.Equals(l.Hotkey.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Index of the FIRST slide whose effective label is the one bound to
    /// <paramref name="hotkey"/> — i.e. the first slide of the first section carrying that
    /// label, in slide order (the T8 hotkey behaviour: when several sections share a label,
    /// the jump goes to the first matching section). Returns -1 when the hotkey has no
    /// label, or no slide carries that label.
    /// </summary>
    public static int FindFirstSlideIndexByHotkey(
        IReadOnlyList<Slide>? slides,
        IReadOnlyList<Section>? sections,
        IEnumerable<SlideLabelDefinition>? labels,
        string? hotkey)
    {
        if (slides == null || slides.Count == 0) return -1;

        var label = FindLabelByHotkey(labels, hotkey);
        if (label == null) return -1;

        for (int i = 0; i < slides.Count; i++)
        {
            var resolved = ResolveLabelForSlide(slides[i], sections, labels);
            if (resolved != null && resolved.Id == label.Id)
                return i;
        }
        return -1;
    }

    private static Section? FindSection(IReadOnlyList<Section>? sections, string sectionName)
    {
        if (sections == null) return null;
        return sections.FirstOrDefault(s =>
            string.Equals((s.Name ?? "").Trim(), sectionName, StringComparison.OrdinalIgnoreCase));
    }
}
