using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// One entry in a song's custom slide order (đợt 2 T9 / R6.1): either a reference to a
/// section BY NAME (the section may appear several times in the order — e.g. the chorus
/// after every verse) or a single Blank slide inserted at that position.
///
/// Entries reference sections by name (not by object identity) so the order survives
/// song save/load as plain JSON. An entry whose section no longer exists in the song is
/// skipped at resolve time (see <see cref="Services.SongSlideOrderResolver"/>), never an
/// error. Additive and backward-compatible: songs saved before T9 simply have no
/// <see cref="Song.CustomSlideOrder"/> list at all.
/// </summary>
public class SongSlideOrderEntry
{
    /// <summary>
    /// Name of the section this entry plays (matched case-insensitively against
    /// <see cref="Section.Name"/>). Null/empty for a Blank entry.
    /// </summary>
    [JsonPropertyName("section")]
    public string? SectionName { get; set; }

    /// <summary>True when this entry is one blank slide instead of a section.</summary>
    [JsonPropertyName("blank")]
    public bool IsBlank { get; set; }

    /// <summary>Create an order entry that plays the named section.</summary>
    public static SongSlideOrderEntry ForSection(string sectionName) =>
        new() { SectionName = sectionName };

    /// <summary>Create an order entry that inserts one blank slide.</summary>
    public static SongSlideOrderEntry BlankSlide() =>
        new() { IsBlank = true };
}
