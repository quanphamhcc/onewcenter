using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Visual theme/style for slide presentation.
/// Defines backgrounds, fonts, colors, and positioning.
/// Can be applied at service level, item level, or slide level.
/// </summary>
public class SlideTheme
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    // ─── Background ────────────────────────────────────────────────
    [JsonPropertyName("backgroundType")]
    public BackgroundType BackgroundType { get; set; } = BackgroundType.SolidColor;

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#000000";

    /// <summary>
    /// Path to background image file.
    /// </summary>
    [JsonPropertyName("backgroundImage")]
    public string BackgroundImage { get; set; } = "";

    /// <summary>
    /// Background image opacity (0.0 - 1.0). Allows dimming for readability.
    /// </summary>
    [JsonPropertyName("backgroundImageOpacity")]
    public double BackgroundImageOpacity { get; set; } = 0.6;

    /// <summary>
    /// Path to a looping motion background video file (MP4, MOV, WebM).
    /// Used when BackgroundType is Video.
    /// </summary>
    [JsonPropertyName("motionBackgroundPath")]
    public string MotionBackgroundPath { get; set; } = "";

    /// <summary>
    /// Opacity of the motion background video (0.0 - 1.0).
    /// Lower values allow the video to appear more subtle behind lyrics.
    /// </summary>
    [JsonPropertyName("motionBackgroundOpacity")]
    public double MotionBackgroundOpacity { get; set; } = 0.5;

    /// <summary>
    /// Optional gradient overlay on background (dark at bottom for text readability).
    /// </summary>
    [JsonPropertyName("useGradientOverlay")]
    public bool UseGradientOverlay { get; set; } = true;

    [JsonPropertyName("gradientColor")]
    public string GradientColor { get; set; } = "#000000";

    [JsonPropertyName("gradientOpacity")]
    public double GradientOpacity { get; set; } = 0.5;

    // ─── Primary Text (Lyrics/Main Content) ────────────────────────
    [JsonPropertyName("primaryFontFamily")]
    public string PrimaryFontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("primaryFontSize")]
    public double PrimaryFontSize { get; set; } = 60;

    [JsonPropertyName("primaryFontColor")]
    public string PrimaryFontColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("primaryFontBold")]
    public bool PrimaryFontBold { get; set; } = true;

    [JsonPropertyName("primaryFontItalic")]
    public bool PrimaryFontItalic { get; set; } = false;

    [JsonPropertyName("primaryAlignment")]
    public TextAlignment PrimaryAlignment { get; set; } = TextAlignment.Center;

    [JsonPropertyName("primaryVerticalAlignment")]
    public VerticalAlignment PrimaryVerticalAlignment { get; set; } = VerticalAlignment.Center;

    /// <summary>
    /// Text shadow for readability over backgrounds.
    /// </summary>
    [JsonPropertyName("primaryTextShadow")]
    public bool PrimaryTextShadow { get; set; } = true;

    [JsonPropertyName("primaryTextShadowColor")]
    public string PrimaryTextShadowColor { get; set; } = "#000000";

    [JsonPropertyName("primaryTextShadowBlur")]
    public double PrimaryTextShadowBlur { get; set; } = 4.0;

    // ─── Secondary Text (Subtitle/Reference) ──────────────────────
    [JsonPropertyName("secondaryFontFamily")]
    public string SecondaryFontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("secondaryFontSize")]
    public double SecondaryFontSize { get; set; } = 32;

    [JsonPropertyName("secondaryFontColor")]
    public string SecondaryFontColor { get; set; } = "#CCCCCC";

    [JsonPropertyName("secondaryFontBold")]
    public bool SecondaryFontBold { get; set; } = false;

    // ─── Footer Text (Copyright/CCLI) ─────────────────────────────
    [JsonPropertyName("footerFontSize")]
    public double FooterFontSize { get; set; } = 18;

    [JsonPropertyName("footerFontColor")]
    public string FooterFontColor { get; set; } = "#888888";

    // ─── Layout/Margins ────────────────────────────────────────────
    /// <summary>
    /// Margin around text content (percentage of screen, 0.0-0.5).
    /// </summary>
    [JsonPropertyName("marginHorizontal")]
    public double MarginHorizontal { get; set; } = 0.1;

    [JsonPropertyName("marginVertical")]
    public double MarginVertical { get; set; } = 0.1;

    /// <summary>
    /// Line spacing multiplier (1.0 = normal, 1.5 = 150%).
    /// </summary>
    [JsonPropertyName("lineSpacing")]
    public double LineSpacing { get; set; } = 1.3;

    // ─── Display Options ───────────────────────────────────────────
    /// <summary>
    /// How many lines of lyrics to show per slide (for auto-splitting).
    /// </summary>
    [JsonPropertyName("lyricsLinesPerSlide")]
    public int LyricsLinesPerSlide { get; set; } = 2;

    /// <summary>
    /// Lyric display mode.
    /// </summary>
    [JsonPropertyName("lyricDisplayMode")]
    public LyricDisplayMode LyricDisplayMode { get; set; } = LyricDisplayMode.TwoLines;

    /// <summary>
    /// Whether to show the song title on the first slide of a song.
    /// </summary>
    [JsonPropertyName("showSongTitle")]
    public bool ShowSongTitle { get; set; } = true;

    /// <summary>
    /// Whether to show copyright/CCLI info on lyric slides.
    /// </summary>
    [JsonPropertyName("showCopyright")]
    public bool ShowCopyright { get; set; } = true;

    /// <summary>
    /// Logo/watermark to show in corner.
    /// </summary>
    [JsonPropertyName("logoPath")]
    public string LogoPath { get; set; } = "";

    [JsonPropertyName("logoPosition")]
    public LogoPosition LogoPosition { get; set; } = LogoPosition.BottomRight;

    [JsonPropertyName("logoOpacity")]
    public double LogoOpacity { get; set; } = 0.3;

    [JsonPropertyName("logoSize")]
    public double LogoSize { get; set; } = 0.08; // 8% of screen height
}

/// <summary>
/// Background type for a slide theme.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundType
{
    SolidColor,
    Image,
    Video,
    Gradient
}

/// <summary>
/// How lyrics are displayed on screen.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LyricDisplayMode
{
    /// <summary>Show 2 lines at a time (most common, like ProPresenter default).</summary>
    TwoLines,

    /// <summary>Show full verse/section (4-6+ lines).</summary>
    FullVerse,

    /// <summary>Show one line at a time (karaoke style, good for new songs).</summary>
    OneLine,

    /// <summary>Custom — uses LyricsLinesPerSlide setting.</summary>
    Custom
}

/// <summary>
/// Text horizontal alignment.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Text vertical alignment on slide.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// Position for the logo watermark.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogoPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center
}
