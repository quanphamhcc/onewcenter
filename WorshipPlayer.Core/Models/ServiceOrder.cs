using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Represents a complete Sunday service order — the master timeline that
/// drives both the Backtrack Player (audio) and Presenter (display).
/// 
/// This is the SINGLE SOURCE OF TRUTH for the service flow.
/// In the future, this is created via the cloud Service Planning module
/// and synced down to the desktop app for Sunday execution.
/// </summary>
public class ServiceOrder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled Service";

    [JsonPropertyName("date")]
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>
    /// Optional start date+time of the service (e.g. 10/18/2026 08:00 AM).
    /// Nullable so existing date-only service files load with no time and nothing
    /// time-related is written back (the manager ignores null when serializing).
    /// The UI formats this with <see cref="Services.ServiceDateFormatter"/>.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime? StartTime { get; set; }

    [JsonPropertyName("serviceType")]
    public string ServiceType { get; set; } = "Sunday Morning";

    [JsonPropertyName("items")]
    public List<ServiceItem> Items { get; set; } = new();

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("groups")]
    public List<ServiceGroup> Groups { get; set; } = new();

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// A single item in the service order (song, scripture, announcement, etc.).
/// Each item can have associated slides for the Presenter to display.
/// </summary>
public class ServiceItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public ServiceItemType Type { get; set; } = ServiceItemType.Song;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("duration_minutes")]
    public double DurationMinutes { get; set; } = 0;

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    // ─── Song-specific properties ──────────────────────────────────
    /// <summary>
    /// Path to the song folder (for backtrack audio stems).
    /// Only used when Type = Song.
    /// </summary>
    [JsonPropertyName("songFolder")]
    public string SongFolder { get; set; } = "";

    /// <summary>
    /// Key override for this service (e.g., original is G, but this week we play in A).
    /// 0 = original key. +2 = two semitones up, etc.
    /// </summary>
    [JsonPropertyName("keyShift")]
    public int KeyShift { get; set; } = 0;

    /// <summary>
    /// Tempo override as percentage (100 = original).
    /// </summary>
    [JsonPropertyName("tempoPercent")]
    public double TempoPercent { get; set; } = 100.0;

    /// <summary>
    /// Whether to use backtrack audio for this song, or it's played live (no audio).
    /// If false, Presenter still shows lyrics but Backtrack doesn't play.
    /// </summary>
    [JsonPropertyName("useBacktrack")]
    public bool UseBacktrack { get; set; } = true;

    // ─── Scripture-specific properties ─────────────────────────────
    [JsonPropertyName("scriptureReference")]
    public string ScriptureReference { get; set; } = "";

    [JsonPropertyName("scriptureTranslation")]
    public string ScriptureTranslation { get; set; } = "NIV";

    [JsonPropertyName("scriptureText")]
    public string ScriptureText { get; set; } = "";

    // ─── Media-specific properties ─────────────────────────────────
    [JsonPropertyName("mediaPath")]
    public string MediaPath { get; set; } = "";

    [JsonPropertyName("mediaType")]
    public MediaItemType MediaType { get; set; } = MediaItemType.Image;

    // ─── Slides (Presenter content for this item) ──────────────────
    /// <summary>
    /// The slides to display for this item. For songs, these are auto-generated
    /// from lyrics. For scripture/announcements, manually defined.
    /// </summary>
    [JsonPropertyName("slides")]
    public List<Slide> Slides { get; set; } = new();

    /// <summary>
    /// Id of the authored Slides-library presentation this item is a LIVE LINK to.
    /// When set (Type = Slides), the item's slides are resolved on demand from the current
    /// presentation at render time, so later edits to the presentation show live (mirrors how
    /// Song items resolve their linked library song). When null, the item uses its snapshot
    /// <see cref="Slides"/> (backward compatible: older service orders have no such link).
    /// Plain nullable with only [JsonPropertyName] — the manager's global WhenWritingNull
    /// serialization condition omits it from JSON when null (same convention as
    /// <see cref="ThemeId"/>/<see cref="BackgroundType"/>).
    /// </summary>
    [JsonPropertyName("slidePresentationId")]
    public string? SlidePresentationId { get; set; }

    // ─── Theme/Styling ─────────────────────────────────────────────
    /// <summary>
    /// Override theme for this item. If null, uses the service-level default theme.
    /// </summary>
    [JsonPropertyName("themeId")]
    public string? ThemeId { get; set; }

    // ─── Per-item Settings ─────────────────────────────────────────
    /// <summary>
    /// Auto-advance and transition settings for this item.
    /// </summary>
    [JsonPropertyName("autoAdvanceSettings")]
    public ServiceItemSettings? AutoAdvanceSettings { get; set; }

    /// <summary>
    /// Background type for this item (overrides theme).
    /// LEGACY (pre đợt 2): kept for backward compatibility. When the explicit
    /// <see cref="BackgroundKind"/> is not set, this value is mapped to the
    /// equivalent kind by <see cref="Services.ItemBackgroundResolver"/>
    /// (SolidColor→Color, Image→Image, Video→Video, Gradient→Theme); loading an
    /// old file never overwrites it.
    /// </summary>
    [JsonPropertyName("backgroundType")]
    public BackgroundType? BackgroundType { get; set; }

    /// <summary>
    /// Explicit per-item background kind (đợt 2 T1 / R1): the item carries its OWN
    /// background configuration, layered under its foreground. Null = not set on the
    /// item — the effective kind is then derived from the legacy
    /// <see cref="BackgroundType"/> when present, otherwise the item follows the
    /// theme (pre-T1 behaviour). Plain nullable with only [JsonPropertyName] —
    /// omitted from JSON when null (the manager's global WhenWritingNull convention,
    /// same as <see cref="BackgroundType"/>), so old service files are unaffected.
    /// </summary>
    [JsonPropertyName("backgroundKind")]
    public ItemBackgroundKind? BackgroundKind { get; set; }

    /// <summary>
    /// Per-item background color (hex, e.g. "#1A2B3C"), used when the effective
    /// background kind is Color. Null/empty = fall back to the effective theme's
    /// background color at resolve time. Omitted from JSON when null.
    /// </summary>
    [JsonPropertyName("backgroundColor")]
    public string? BackgroundColor { get; set; }

    /// <summary>
    /// Per-item background media file (image or video path), used when the effective
    /// background kind is Image or Video. Null/empty = fall back to the legacy
    /// <see cref="MediaPath"/> (pre-T1 items stored their background file there —
    /// see SlideServiceItemEditorWindow.ApplyBackgroundSource). Omitted from JSON when null.
    /// </summary>
    [JsonPropertyName("backgroundFilePath")]
    public string? BackgroundFilePath { get; set; }

    /// <summary>
    /// Foreground type for this item.
    /// </summary>
    [JsonPropertyName("foregroundType")]
    public ForegroundType ForegroundType { get; set; } = ForegroundType.Lyrics;

    /// <summary>
    /// Transition fade time in milliseconds for this item.
    /// </summary>
    [JsonPropertyName("transitionTimeMs")]
    public int TransitionTimeMs { get; set; } = 500;

    /// <summary>
    /// Path to an attached audio file for this item (e.g., background music for announcements).
    /// </summary>
    [JsonPropertyName("audioAttachmentPath")]
    public string AudioAttachmentPath { get; set; } = "";

    /// <summary>
    /// Attached audio files for this item. Additive multi-audio replacement for the legacy single
    /// <see cref="AudioAttachmentPath"/>. Nullable so it is omitted from JSON when unset (older files
    /// have no such key). On load, a non-empty legacy AudioAttachmentPath is migrated into this list
    /// as the first entry (see <see cref="Services.ServiceItemAudio.Resolve"/>).
    /// Plain nullable with only [JsonPropertyName] — the manager's global WhenWritingNull
    /// serialization condition omits it from JSON when null (same convention as
    /// <see cref="SlidePresentationId"/>/<see cref="ThemeId"/>).
    /// </summary>
    [JsonPropertyName("audioAttachments")]
    public List<string>? AudioAttachments { get; set; }

    /// <summary>
    /// Id of the stage-display template this item uses, or null to inherit the service default.
    /// Plain nullable with only [JsonPropertyName] — the manager's global WhenWritingNull
    /// serialization condition omits it from JSON when null (older files have no such key).
    /// </summary>
    [JsonPropertyName("stageTemplateId")]
    public string? StageTemplateId { get; set; }
}

/// <summary>
/// Types of service items. The Presenter displays all types.
/// The Backtrack Player only acts on Song items.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceItemType
{
    Song,           // Worship song (backtrack + lyrics)
    Scripture,      // Bible reading
    Announcement,   // Text/image announcement
    Sermon,         // Sermon points/outline
    Video,          // Video playback
    Image,          // Static image (logo, artwork)
    Prayer,         // Prayer text/prompt
    Offering,       // Giving moment (QR code, instructions)
    AltarCall,      // Response/altar call
    Blank,          // Black screen or logo
    Custom,         // User-defined (flexible)
    Slides          // Authored Slides-library presentation (live link via SlidePresentationId, or legacy snapshotted slides)
}

/// <summary>
/// Types of media that can be attached to a service item.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaItemType
{
    Image,
    Video,
    Audio,
    Document
}

/// <summary>
/// Types of foreground content for a service item.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ForegroundType
{
    Lyrics,
    Scripture,
    Text,
    Media,
    Blank
}

/// <summary>
/// Explicit per-item background kind (đợt 2 T1 / R1). Theme = follow the effective
/// <see cref="SlideTheme"/> (the pre-T1 behaviour and the default); None = no
/// background (black); Color / Image / Video use the item's own
/// <see cref="ServiceItem.BackgroundColor"/> / <see cref="ServiceItem.BackgroundFilePath"/>.
/// (Inherit is added by đợt 2 T2.)
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemBackgroundKind
{
    Theme,
    None,
    Color,
    Image,
    Video,
    /// <summary>
    /// Đợt 2 T2 (R2): use the background of the item immediately above in the
    /// service list (walk up past other Inherit items to the first item with a
    /// real background; the list's first item or an empty chain falls back to the
    /// theme). Resolved at call time by
    /// <see cref="Services.InheritBackgroundResolver"/> — never persisted into
    /// child items. (Appended last: pre-T2 files never carry this value.)
    /// </summary>
    Inherit
}
