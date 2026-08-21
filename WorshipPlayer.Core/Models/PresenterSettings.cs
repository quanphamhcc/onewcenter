using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Settings specific to the Presenter module.
/// Stored alongside AppSettings in the settings file.
/// </summary>
public class PresenterSettings
{
    // ─── Display Configuration ─────────────────────────────────────
    /// <summary>
    /// Which monitor to use for the congregation output (0-based index).
    /// -1 = no output window (preview only). User must configure via Display Settings.
    /// </summary>
    [JsonPropertyName("outputDisplay")]
    public int OutputDisplay { get; set; } = -1;

    /// <summary>
    /// Which monitor to use for the stage display / confidence monitor.
    /// -1 = disabled.
    /// </summary>
    [JsonPropertyName("stageDisplay")]
    public int StageDisplay { get; set; } = -1;

    /// <summary>
    /// Output resolution (0 = match display native resolution).
    /// </summary>
    [JsonPropertyName("outputWidth")]
    public int OutputWidth { get; set; } = 0;

    [JsonPropertyName("outputHeight")]
    public int OutputHeight { get; set; } = 0;

    // ─── Auto-Advance ──────────────────────────────────────────────
    /// <summary>
    /// Enable auto-advance from backtrack MIDI cues.
    /// When true, MidiCueService events automatically advance slides.
    /// </summary>
    [JsonPropertyName("autoAdvanceFromMidi")]
    public bool AutoAdvanceFromMidi { get; set; } = true;

    /// <summary>
    /// Enable auto-advance from timestamps embedded in slides.
    /// When true, slides with TimestampMs values advance automatically during playback.
    /// </summary>
    [JsonPropertyName("autoAdvanceFromTimestamp")]
    public bool AutoAdvanceFromTimestamp { get; set; } = true;

    /// <summary>
    /// Automatically advance to the next service item when a song ends.
    /// </summary>
    [JsonPropertyName("autoAdvanceOnSongEnd")]
    public bool AutoAdvanceOnSongEnd { get; set; } = true;

    /// <summary>
    /// Show blank/logo slide between service items during transitions.
    /// </summary>
    [JsonPropertyName("blankBetweenItems")]
    public bool BlankBetweenItems { get; set; } = false;

    // ─── Default Theme ─────────────────────────────────────────────
    /// <summary>
    /// Default theme ID for new slides/items that don't specify one.
    /// </summary>
    [JsonPropertyName("defaultThemeId")]
    public string DefaultThemeId { get; set; } = "";

    // ─── Stage Server ──────────────────────────────────────────────
    /// <summary>
    /// Whether to start the embedded stage display web server (Kestrel).
    /// When enabled, iPads and other devices can connect via browser.
    /// </summary>
    [JsonPropertyName("stageServerEnabled")]
    public bool StageServerEnabled { get; set; } = true;

    /// <summary>
    /// Port for the embedded stage display web server.
    /// </summary>
    [JsonPropertyName("stageServerPort")]
    public int StageServerPort { get; set; } = 5055;

    // ─── Stage Display Options ─────────────────────────────────────
    /// <summary>
    /// Show next slide preview on stage display.
    /// </summary>
    [JsonPropertyName("stageShowNextSlide")]
    public bool StageShowNextSlide { get; set; } = true;

    /// <summary>
    /// Show current chord/key on stage display (for musicians).
    /// </summary>
    [JsonPropertyName("stageShowChords")]
    public bool StageShowChords { get; set; } = true;

    /// <summary>
    /// Show chords on the congregation output (live screen/projector).
    /// When false (default), congregation sees clean lyrics only.
    /// When true, chords are displayed above lyrics (same as stage display).
    /// Toggle this during service via operator window or remote.
    /// </summary>
    [JsonPropertyName("congregationShowChords")]
    public bool CongregationShowChords { get; set; } = false;

    /// <summary>
    /// Show countdown timer on stage display.
    /// </summary>
    [JsonPropertyName("stageShowTimer")]
    public bool StageShowTimer { get; set; } = false;

    /// <summary>
    /// Stage display font size multiplier relative to output.
    /// </summary>
    [JsonPropertyName("stageFontSizeMultiplier")]
    public double StageFontSizeMultiplier { get; set; } = 0.8;

    // ─── CCLI / Copyright ──────────────────────────────────────────
    /// <summary>
    /// Church's CCLI license number (displayed on lyric slides).
    /// </summary>
    [JsonPropertyName("ccliLicenseNumber")]
    public string CcliLicenseNumber { get; set; } = "";

    /// <summary>
    /// Default copyright notice format.
    /// </summary>
    [JsonPropertyName("copyrightFormat")]
    public string CopyrightFormat { get; set; } = "CCLI License #{0}";

    // ─── Keyboard Shortcuts ────────────────────────────────────────
    [JsonPropertyName("shortcutNextSlide")]
    public string ShortcutNextSlide { get; set; } = "Right";

    [JsonPropertyName("shortcutPrevSlide")]
    public string ShortcutPrevSlide { get; set; } = "Left";

    [JsonPropertyName("shortcutNextItem")]
    public string ShortcutNextItem { get; set; } = "Down";

    [JsonPropertyName("shortcutPrevItem")]
    public string ShortcutPrevItem { get; set; } = "Up";

    [JsonPropertyName("shortcutBlank")]
    public string ShortcutBlank { get; set; } = "B";

    [JsonPropertyName("shortcutLogo")]
    public string ShortcutLogo { get; set; } = "F1";

    // ─── Remote Control ────────────────────────────────────────────
    /// <summary>
    /// Whether the remote control endpoint is enabled.
    /// When enabled, operators can control the presenter from a phone/tablet browser.
    /// Defaults to false for security - must be explicitly enabled by the user.
    /// </summary>
    [JsonPropertyName("remoteEnabled")]
    public bool RemoteEnabled { get; set; } = false;

    /// <summary>
    /// PIN code required to authenticate remote control connections.
    /// </summary>
    [JsonPropertyName("remotePin")]
    public string RemotePin { get; set; } = "1234";

    /// <summary>
    /// Whether remote clients are allowed to advance/retreat slides.
    /// </summary>
    [JsonPropertyName("remoteAllowSlideAdvance")]
    public bool RemoteAllowSlideAdvance { get; set; } = true;

    /// <summary>
    /// Whether remote clients are allowed to navigate between service items.
    /// </summary>
    [JsonPropertyName("remoteAllowItemNavigation")]
    public bool RemoteAllowItemNavigation { get; set; } = true;

    /// <summary>
    /// Whether remote clients are allowed to control display state (go live, black, logo).
    /// </summary>
    [JsonPropertyName("remoteAllowDisplayControl")]
    public bool RemoteAllowDisplayControl { get; set; } = true;

    // ─── NDI Output ───────────────────────────────────────────────
    /// <summary>
    /// Whether to enable NDI output for streaming the congregation display
    /// to professional AV equipment, OBS, vMix, or other NDI receivers.
    /// </summary>
    [JsonPropertyName("ndiEnabled")]
    public bool NdiEnabled { get; set; } = false;

    /// <summary>
    /// The NDI source name visible to receivers on the network.
    /// </summary>
    [JsonPropertyName("ndiSourceName")]
    public string NdiSourceName { get; set; } = "OWC Presenter Output";

    /// <summary>
    /// Frame rate for NDI output capture (frames per second).
    /// </summary>
    [JsonPropertyName("ndiFps")]
    public int NdiFps { get; set; } = 30;

    /// <summary>
    /// NDI output resolution width in pixels.
    /// </summary>
    [JsonPropertyName("ndiResolutionWidth")]
    public int NdiResolutionWidth { get; set; } = 1920;

    /// <summary>
    /// NDI output resolution height in pixels.
    /// </summary>
    [JsonPropertyName("ndiResolutionHeight")]
    public int NdiResolutionHeight { get; set; } = 1080;

    // ─── Video Library ─────────────────────────────────────────────
    /// <summary>
    /// Path to the folder containing video background files organized by category.
    /// </summary>
    [JsonPropertyName("videoLibraryPath")]
    public string VideoLibraryPath { get; set; } = "";

    /// <summary>
    /// Default motion background video path used when a theme specifies Video background
    /// but no specific video is set.
    /// </summary>
    [JsonPropertyName("defaultMotionBackgroundPath")]
    public string DefaultMotionBackgroundPath { get; set; } = "";
}
