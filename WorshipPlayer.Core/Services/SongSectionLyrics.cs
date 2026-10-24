using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure per-section lyric edits (field feature 2026-10-24: from a slide thumbnail in
/// the OperatorWindow, the operator edits ONE song section — e.g. CHORUS — and the
/// whole section's text is replaced, so every slide generated from that section
/// picks the new words up on the next refresh).
///
/// Pure by design: no I/O, no SongManager, no engine. The caller owns persistence
/// (SongManager.SaveSongAsync) and re-rendering (RefreshSlideSections / engine
/// slide replacement). Section lookup is by name, case-insensitive, first match
/// wins — the same matching the slide generator uses for section names.
/// </summary>
public static class SongSectionLyrics
{
    /// <summary>Find a section by name (case-insensitive). Null when absent.</summary>
    public static Section? FindSection(Song? song, string? sectionName)
    {
        if (song?.Sections == null || string.IsNullOrWhiteSpace(sectionName))
            return null;

        foreach (var section in song.Sections)
        {
            if (section != null &&
                string.Equals(section.Name, sectionName, StringComparison.OrdinalIgnoreCase))
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>
    /// Replace one section's lyric text. Returns false (and changes nothing) when
    /// the song has no section with that name. Line endings are normalized to
    /// '\n' so the slide generator's block splitter sees one consistent format
    /// no matter what editor produced the text.
    /// </summary>
    public static bool TrySetSectionText(Song? song, string? sectionName, string? newText)
    {
        var section = FindSection(song, sectionName);
        if (section == null)
            return false;

        section.Text = (newText ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        return true;
    }
}
