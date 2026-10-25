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
    /// Output list (đợt 2 T15 / R8): replaces the two fixed display indices above with a
    /// list of <see cref="OutputConfig"/> (each: name, display index, purpose, optional
    /// per-output foreground template override).
    /// Nullable: null = the settings predate the list (missing "outputs" key) → migrated
    /// from OutputDisplay/StageDisplay at LoadAsync
    /// (see <see cref="Services.OutputMigration.EnsureOutputs"/>). A present list — even
    /// an EMPTY one — is the user's own state and is kept as-is.
    /// The legacy OutputDisplay/StageDisplay fields are KEPT (not removed) for backward
    /// compatibility: DisplayManager still reads them until T16 rewires the engine.
    /// </summary>
    [JsonPropertyName("outputs")]
    public List<OutputConfig>? Outputs { get; set; }

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

    // ─── Auto-Timed Slides (lyric grouping + vocal timing) ─────────
    /// <summary>
    /// Global default number of lyric lines per slide when grouping a section's lyrics
    /// (clamped 2–4). A section may override this via <c>Section.LinesPerSlideOverride</c>.
    /// Field request 2026-10-24: the old default of 4 projected a whole 4-line chorus
    /// on one screen in tiny text, so the default is now 2 (two lines on screen, then
    /// the next two). Users who want denser slides can still pick 3–4 in Settings.
    /// </summary>
    [JsonPropertyName("linesPerSlide")]
    public int LinesPerSlide { get; set; } = 2;

    /// <summary>
    /// Lead time (ms) subtracted from each auto-computed slide advance time so the slide appears
    /// slightly BEFORE the first word of the slide is sung (congregation reads ahead). Clamped so a
    /// slide time never goes below 0 or before its section start. Default 500 ms.
    /// </summary>
    [JsonPropertyName("slideLeadMs")]
    public int SlideLeadMs { get; set; } = 500;

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

    // ─── Scripture Slide Display (E5) ──────────────────────────────
    /// <summary>
    /// Default for the Scripture panel's "Show reference" toggle — whether generated
    /// scripture slides include the reference (e.g. "John 3:16") in the subtitle.
    /// Additive/backward-compatible: a missing JSON key deserializes to this default.
    /// </summary>
    [JsonPropertyName("scriptureShowReference")]
    public bool ScriptureShowReference { get; set; } = true;

    /// <summary>
    /// Default for the Scripture panel's "Show translation" toggle — whether generated
    /// scripture slides include the translation name (e.g. "KJV") in the subtitle.
    /// Additive/backward-compatible: a missing JSON key deserializes to this default.
    /// </summary>
    [JsonPropertyName("scriptureShowTranslationName")]
    public bool ScriptureShowTranslationName { get; set; } = true;

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

    // ─── Slide Labels (đợt 2 T7 / R5) ──────────────────────────────
    /// <summary>
    /// The shared slide-label catalog (name + color + hotkey per label) that song sections
    /// reference via <see cref="Section.LabelId"/>. Null means the settings predate slide
    /// labels (the JSON key is absent): the catalog is seeded with the six defaults at load
    /// time (<see cref="Services.SlideLabelService.EnsureLabels"/>, called by
    /// PresenterSettingsService.LoadAsync). A present-but-empty list is a deliberate user
    /// state (all labels deleted) and is never re-seeded.
    /// </summary>
    [JsonPropertyName("slideLabels")]
    public List<SlideLabelDefinition>? SlideLabels { get; set; }

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
