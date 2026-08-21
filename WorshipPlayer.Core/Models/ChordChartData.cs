using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Holds timing data for chord chart slide display.
/// 
/// UNIFIED LYRICS SYSTEM:
/// - Lyrics + chords are stored in sections[].text using inline [chord] format
///   e.g., "[B]    The Lord bless you  [E]and keep you"
/// - ChordChartData only stores TIMING (when to advance slides)
/// - The chord chart display reads text from sections[].text
/// - Slides are defined by empty lines in sections[].text
/// 
/// Legacy support: old song.json files with text in slides still work
/// (the display falls back to slide text if section text is empty).
/// </summary>
public class ChordChartData
{
    /// <summary>
    /// Slide timing entries. Each entry defines WHEN a slide starts/ends.
    /// The actual text content comes from sections[].text (split by empty lines).
    /// </summary>
    [JsonPropertyName("slides")]
    public List<ChartSlide> Slides { get; set; } = new();
}

/// <summary>
/// A single slide with timing information.
/// Text is read from the corresponding section's text field at display time.
/// </summary>
public class ChartSlide
{
    /// <summary>Section name this slide belongs to (e.g., "VERSE 1", "CHORUS").</summary>
    [JsonPropertyName("sectionName")]
    public string SectionName { get; set; } = "";

    /// <summary>Which slide within the section (0-based). Used to match to text blocks
    /// split by empty lines in sections[].text.</summary>
    [JsonPropertyName("slideIndex")]
    public int SlideIndex { get; set; } = 0;

    /// <summary>Start time of this slide in milliseconds (from forced alignment or manual).</summary>
    [JsonPropertyName("startMs")]
    public double StartMs { get; set; }

    /// <summary>End time of this slide in milliseconds.</summary>
    [JsonPropertyName("endMs")]
    public double EndMs { get; set; }

    /// <summary>Name of the next section (shown as preview at bottom of slide).</summary>
    [JsonPropertyName("nextSectionName")]
    public string NextSectionName { get; set; } = "";

    /// <summary>First chord of the next section (preview).</summary>
    [JsonPropertyName("nextSectionChord")]
    public string NextSectionChord { get; set; } = "";

    /// <summary>
    /// Whether timing was manually set by user (vs auto-detected).
    /// Manual timings are not overwritten by re-analysis.
    /// </summary>
    [JsonPropertyName("manualTiming")]
    public bool ManualTiming { get; set; } = false;

    // ─── LEGACY FIELDS (backward compatibility with old song.json) ───

    /// <summary>
    /// [LEGACY] Lines of lyrics with chord annotations.
    /// In the new system, text comes from sections[].text.
    /// Kept for backward compatibility with old song.json files.
    /// </summary>
    [JsonPropertyName("lines")]
    public List<ChartLine>? Lines { get; set; }
}

/// <summary>
/// A single line of lyrics with timing data.
/// In the new system, this is primarily used for word-level timestamps
/// from forced alignment (for karaoke-style highlighting).
/// </summary>
public class ChartLine
{
    /// <summary>
    /// [LEGACY] The lyric text for this line. In the new system, text comes from sections[].text.
    /// Kept for backward compatibility.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Start time of this line in milliseconds.</summary>
    [JsonPropertyName("startMs")]
    public double StartMs { get; set; }

    /// <summary>End time of this line in milliseconds.</summary>
    [JsonPropertyName("endMs")]
    public double EndMs { get; set; }

    /// <summary>
    /// [LEGACY] Chords positioned above this line by character offset.
    /// In the new system, chords are inline in sections[].text as [chord] notation.
    /// Kept for backward compatibility.
    /// </summary>
    [JsonPropertyName("chords")]
    public List<ChordPosition>? Chords { get; set; }

    /// <summary>
    /// Individual words with timestamps from forced alignment.
    /// Used for karaoke-style word highlighting and slide advance timing.
    /// </summary>
    [JsonPropertyName("words")]
    public List<TimedWord>? Words { get; set; }
}

/// <summary>
/// A chord positioned above a specific character offset in a lyric line.
/// [LEGACY] In the new system, chords are inline as [B], [E], etc.
/// </summary>
public class ChordPosition
{
    /// <summary>Character offset in the lyric line text where this chord appears.</summary>
    [JsonPropertyName("charOffset")]
    public int CharOffset { get; set; }

    /// <summary>The chord symbol (e.g., "G", "D/F#", "Em").</summary>
    [JsonPropertyName("chord")]
    public string Chord { get; set; } = "";

    /// <summary>Time in ms when this chord starts.</summary>
    [JsonPropertyName("timeMs")]
    public double TimeMs { get; set; }
}

/// <summary>
/// A single word with its timestamp for karaoke-style highlighting
/// and forced alignment timing.
/// </summary>
public class TimedWord
{
    [JsonPropertyName("word")]
    public string Word { get; set; } = "";

    [JsonPropertyName("startMs")]
    public double StartMs { get; set; }

    [JsonPropertyName("endMs")]
    public double EndMs { get; set; }
}
