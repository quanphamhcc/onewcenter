using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Represents a single slide to display on the Presenter output.
/// A slide is the atomic unit of display — one "screen" of content.
/// 
/// For songs: each slide typically contains 2-4 lines of lyrics.
/// For scripture: the full passage split across slides.
/// For announcements: one slide per announcement item.
/// </summary>
public class Slide
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public SlideType Type { get; set; } = SlideType.Lyrics;

    [JsonPropertyName("content")]
    public SlideContent Content { get; set; } = new();

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    /// <summary>
    /// Which song section this slide belongs to (for section-aware auto-advance).
    /// Maps to Song.Sections[index].Name (e.g., "VERSE 1", "CHORUS 1").
    /// </summary>
    [JsonPropertyName("sectionName")]
    public string SectionName { get; set; } = "";

    /// <summary>
    /// Optional: exact timestamp (ms) when this slide should appear.
    /// Used for precise auto-advance synced to backtrack playback.
    /// If null/0, slide advances on MIDI cue or manual trigger.
    /// </summary>
    [JsonPropertyName("timestamp_ms")]
    public double? TimestampMs { get; set; }

    /// <summary>
    /// Transition to use when moving TO this slide.
    /// </summary>
    [JsonPropertyName("transition")]
    public SlideTransition Transition { get; set; } = SlideTransition.Fade;

    /// <summary>
    /// Transition duration in milliseconds.
    /// </summary>
    [JsonPropertyName("transitionDuration_ms")]
    public int TransitionDurationMs { get; set; } = 500;

    /// <summary>
    /// Override theme for this specific slide. If null, uses item-level or service-level theme.
    /// </summary>
    [JsonPropertyName("themeOverride")]
    public SlideTheme? ThemeOverride { get; set; }
}

/// <summary>
/// The content displayed on a slide.
/// </summary>
public class SlideContent
{
    /// <summary>
    /// Primary text (lyrics, scripture text, announcement body).
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>
    /// Secondary/subtitle text (scripture reference, song section label, author).
    /// </summary>
    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = "";

    /// <summary>
    /// Header text (song title on first slide, "ANNOUNCEMENTS" label, etc.).
    /// </summary>
    [JsonPropertyName("header")]
    public string Header { get; set; } = "";

    /// <summary>
    /// Footer text (copyright notice, CCLI license number).
    /// </summary>
    [JsonPropertyName("footer")]
    public string Footer { get; set; } = "";

    /// <summary>
    /// Path to a background image for this slide.
    /// Overrides the theme background if set.
    /// </summary>
    [JsonPropertyName("backgroundImage")]
    public string BackgroundImage { get; set; } = "";

    /// <summary>
    /// Path to a foreground image (logo, QR code for giving).
    /// </summary>
    [JsonPropertyName("foregroundImage")]
    public string ForegroundImage { get; set; } = "";
}

/// <summary>
/// Types of slides.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SlideType
{
    Lyrics,         // Song lyrics (2-4 lines)
    Scripture,      // Bible verse
    Title,          // Song/item title card
    Announcement,   // Announcement text/image
    SermonPoint,    // Sermon outline point
    Media,          // Image or video
    Blank,          // Black/empty (or logo watermark)
    Offering,       // Giving information + QR code
    Custom          // User-defined content
}

/// <summary>
/// Slide transition effects.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SlideTransition
{
    Cut,        // Instant switch (no animation)
    Fade,       // Crossfade (opacity transition)
    Dissolve,   // Dissolve (similar to fade but with pixel-level blending)
    SlideLeft,  // New slide enters from right
    SlideUp     // New slide enters from bottom
}
