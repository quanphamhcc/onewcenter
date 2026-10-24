using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure resolver for a song's Custom Slide Order (đợt 2 T9 / R6.1): turns a
/// <see cref="Song"/> into the ordered sequence of "slide generation units" the slide
/// pipeline must walk — each unit is either one of the song's sections (the same section
/// may appear several times when the custom order repeats it) or a single Blank slide.
///
/// Resolution rules:
///   • <see cref="Song.CustomSlideOrder"/> is null (not set — every pre-T9 song) → the
///     sections in their original order, exactly the pre-T9 behavior.
///   • Custom order set → walk its entries in order: a Blank entry yields one blank
///     unit; a section entry yields the FIRST section whose name matches
///     case-insensitively; an entry naming a section that no longer exists (renamed or
///     deleted after the order was authored) is SKIPPED — never an exception.
///   • An explicitly empty custom-order list yields no units (the user cleared the
///     order), which is deliberately different from null (= original order).
///
/// This is the single source of truth for song-level slide order: both live slide
/// generation paths (<c>PresenterEngine.GenerateSlidesForSong</c> and
/// <c>LyricSlideGenerator.GenerateFromSong</c>) walk these units instead of iterating
/// <see cref="Song.Sections"/> directly, so every song item created from the song
/// inherits the custom order (R6.1). Per-service-item order (T10) layers on top of
/// this later; lines-per-slide grouping is unaffected and still applied per section
/// by the generators themselves (R6.4).
/// </summary>
public static class SongSlideOrderResolver
{
    /// <summary>
    /// One slide-generation unit: a song section to generate slides from, or — when
    /// <see cref="Section"/> is null — exactly one blank slide.
    /// </summary>
    public readonly record struct SlideUnit(Section? Section)
    {
        /// <summary>True when this unit is a single blank slide (no section).</summary>
        public bool IsBlank => Section == null;

        /// <summary>The shared blank unit.</summary>
        public static SlideUnit Blank => new(null);
    }

    /// <summary>True when the song carries an explicit custom slide order.</summary>
    public static bool HasCustomOrder(Song? song) => song?.CustomSlideOrder != null;

    /// <summary>
    /// Resolve the song's slide-generation units in play order. Never throws; a null
    /// song or a song without sections yields only the units the custom order can
    /// still produce (blank entries), or an empty list.
    /// </summary>
    public static IReadOnlyList<SlideUnit> ResolveUnits(Song? song)
    {
        var units = new List<SlideUnit>();
        if (song == null)
            return units;

        var sections = song.Sections ?? new List<Section>();

        // No custom order → original section order (pre-T9 behavior, byte-for-byte).
        if (song.CustomSlideOrder == null)
        {
            foreach (var section in sections)
                units.Add(new SlideUnit(section));
            return units;
        }

        foreach (var entry in song.CustomSlideOrder)
        {
            if (entry == null)
                continue;

            if (entry.IsBlank)
            {
                units.Add(SlideUnit.Blank);
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.SectionName))
                continue;

            var match = sections.FirstOrDefault(s =>
                string.Equals(s.Name, entry.SectionName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                units.Add(new SlideUnit(match));
            // No match: the section was renamed/deleted after the order was set —
            // skip the entry silently (R6 edge case), do not fail slide generation.
        }

        return units;
    }
}
