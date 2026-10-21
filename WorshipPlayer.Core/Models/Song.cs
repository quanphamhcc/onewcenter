using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Represents a complete song with all its tracks, sections, and lighting configuration.
/// </summary>
public class Song
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "Untitled";

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = "";

    [JsonPropertyName("composer")]
    public string Composer { get; set; } = "";

    [JsonPropertyName("lyricist")]
    public string Lyricist { get; set; } = "";

    [JsonPropertyName("arranger")]
    public string Arranger { get; set; } = "";

    [JsonPropertyName("themes")]
    public string Themes { get; set; } = "";

    [JsonPropertyName("ccli")]
    public string Ccli { get; set; } = "";

    [JsonPropertyName("bpm")]
    public double Bpm { get; set; } = 120.0;

    [JsonPropertyName("timeSignatureNumerator")]
    public int TimeSignatureNumerator { get; set; } = 4;

    [JsonPropertyName("timeSignatureDenominator")]
    public int TimeSignatureDenominator { get; set; } = 4;

    [JsonPropertyName("beatInterval_ms")]
    public double BeatIntervalMs { get; set; } = 500.0;

    [JsonPropertyName("barDuration_ms")]
    public double BarDurationMs { get; set; } = 2000.0;

    [JsonPropertyName("gridAnchor_ms")]
    public double GridAnchorMs { get; set; } = 0.0;

    /// <summary>
    /// DEDICATED manual-click Beat 1 position (ms) used ONLY by the Manual Click Track workflow
    /// (FEAT-002). This is independent of <see cref="GridAnchorMs"/> so that setting/tapping/nudging
    /// the manual-click Beat 1 never moves section loop points, <see cref="BeatPositionsMs"/>, or the
    /// separate "Downbeat (Beat 1)" panel. A sentinel of -1 means "unset -> fall back to GridAnchorMs".
    /// Additive and backward-compatible: older song.json without this field load with the default.
    /// </summary>
    [JsonPropertyName("manualClickBeat1_ms")]
    public double ManualClickBeat1Ms { get; set; } = -1.0;

    /// <summary>
    /// Number of count-in BEATS (0-4) of clicks emitted immediately BEFORE the manual-click Beat 1
    /// — the classic "1-2-3-4" lead-in — in both the live metronome preview and the generated
    /// Generated_Click_Manual.wav. Each count-in click is spaced one beat apart. Additive and
    /// backward-compatible; defaults to 4 (a full one-bar-of-4 count-in).
    /// </summary>
    [JsonPropertyName("manualClickCountInBeats")]
    public int ManualClickCountInBeats { get; set; } = 4;

    [JsonPropertyName("originalKey")]
    public string OriginalKey { get; set; } = "C";

    [JsonPropertyName("tracks")]
    public List<TrackInfo> Tracks { get; set; } = new();

    [JsonPropertyName("sections")]
    public List<Section> Sections { get; set; } = new();

    [JsonPropertyName("lightingPresets")]
    public List<LightingPreset> LightingPresets { get; set; } = new();

    [JsonPropertyName("chordChart")]
    public ChordChartData? ChordChart { get; set; }

    /// <summary>
    /// MIDI cues for WorshipTools Presenter integration.
    /// Each cue fires a MIDI Note On message at a specific playback position
    /// to advance lyrics, change slides, or trigger other Presenter actions.
    /// </summary>
    [JsonPropertyName("midiCues")]
    public List<MidiCue> MidiCues { get; set; } = new();

    /// <summary>
    /// Actual beat positions in milliseconds (from click track peaks or detected onsets).
    /// Used instead of mathematical grid for sample-accurate beat alignment.
    /// </summary>
    [JsonPropertyName("beatPositions_ms")]
    public List<double>? BeatPositionsMs { get; set; }

    /// <summary>
    /// The folder path where this song's files are stored.
    /// Not serialized - set at runtime.
    /// </summary>
    [JsonIgnore]
    public string FolderPath { get; set; } = "";

    /// <summary>
    /// Calculate the bar duration from BPM and time signature.
    /// </summary>
    public void RecalculateGrid()
    {
        BeatIntervalMs = 60000.0 / Bpm;
        BarDurationMs = BeatIntervalMs * TimeSignatureNumerator;
    }

    /// <summary>
    /// Get the millisecond position for a given bar number (1-based).
    /// Uses actual beat positions when available for sample-accuracy.
    /// </summary>
    public double GetBarStartMs(int barNumber)
    {
        if (BeatPositionsMs != null && BeatPositionsMs.Count > 0)
        {
            int beatsPerBar = TimeSignatureNumerator > 0 ? TimeSignatureNumerator : 4;
            int beatIndex = (barNumber - 1) * beatsPerBar;
            if (beatIndex >= 0 && beatIndex < BeatPositionsMs.Count)
                return BeatPositionsMs[beatIndex];
        }
        return GridAnchorMs + (barNumber - 1) * BarDurationMs;
    }

    /// <summary>
    /// Get the bar number for a given millisecond position.
    /// Uses actual beat positions when available for sample-accuracy.
    /// </summary>
    public int GetBarAtMs(double ms)
    {
        if (BeatPositionsMs != null && BeatPositionsMs.Count > 0)
        {
            int beatsPerBar = TimeSignatureNumerator > 0 ? TimeSignatureNumerator : 4;
            // Find which beat index this ms falls at or after
            int beatIdx = 0;
            for (int i = 0; i < BeatPositionsMs.Count; i++)
            {
                if (BeatPositionsMs[i] <= ms)
                    beatIdx = i;
                else
                    break;
            }
            return (beatIdx / beatsPerBar) + 1;
        }
        if (ms < GridAnchorMs) return 0;
        return (int)((ms - GridAnchorMs) / BarDurationMs) + 1;
    }
}

/// <summary>
/// Information about a single audio track/stem in a song.
/// </summary>
public class TrackInfo
{
    [JsonPropertyName("file")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("output")]
    public TrackOutput Output { get; set; } = TrackOutput.FOH;

    [JsonPropertyName("defaultVolume")]
    public float DefaultVolume { get; set; } = 1.0f;

    [JsonPropertyName("defaultMute")]
    public bool DefaultMute { get; set; } = false;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFF";
}

/// <summary>
/// Defines where a track's audio is routed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrackOutput
{
    FOH,    // Front of House (main speakers)
    IEM,    // In-Ear Monitor (click/cues only)
    Both    // Both outputs
}

/// <summary>
/// A section of a song with precise loop points.
/// </summary>
public class Section
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("loopIn_ms")]
    public double LoopInMs { get; set; }

    [JsonPropertyName("loopOut_ms")]
    public double LoopOutMs { get; set; }

    [JsonPropertyName("startBar")]
    public int StartBar { get; set; }

    [JsonPropertyName("endBar")]
    public int EndBar { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#4A90D9";

    [JsonPropertyName("lightingPresetIndex")]
    public int LightingPresetIndex { get; set; } = -1;

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>
    /// Whether this section can be looped. False = play once and advance (skipped during review).
    /// </summary>
    [JsonPropertyName("canLoop")]
    public bool CanLoop { get; set; } = true;

    /// <summary>
    /// Vocal pickup duration in milliseconds. When looping, the vocal track will
    /// start this many ms BEFORE the section's LoopInMs to capture pickup notes
    /// (e.g., "Ama-" before "-zing Grace" on beat 1).
    /// 0 = no pickup, vocal starts on or after beat 1.
    /// Typically quantized to half-beat or full-beat values.
    /// </summary>
    [JsonPropertyName("vocalPickup_ms")]
    public double VocalPickupMs { get; set; } = 0;

    /// <summary>
    /// Optional per-section background image path. Null when unset — older songs
    /// without this field still load (DefaultIgnoreCondition.WhenWritingNull).
    /// </summary>
    [JsonPropertyName("backgroundImage")]
    public string? BackgroundImage { get; set; }

    /// <summary>
    /// Optional per-section slide template id (matches SlideTemplate.Id).
    /// Null when unset. Additive and backward-compatible.
    /// </summary>
    [JsonPropertyName("templateId")]
    public string? TemplateId { get; set; }

    /// <summary>
    /// Optional per-section override of the global lines-per-slide setting
    /// (<c>PresenterSettings.LinesPerSlide</c>). Null = use the global value. Clamped 2–4 when set.
    /// Lets a dense verse group differently from a short chorus. Additive and backward-compatible.
    /// </summary>
    [JsonPropertyName("linesPerSlideOverride")]
    public int? LinesPerSlideOverride { get; set; }

    /// <summary>
    /// The effective loop-in point for the vocal track (LoopInMs - VocalPickupMs).
    /// </summary>
    [JsonIgnore]
    public double VocalLoopInMs => LoopInMs - VocalPickupMs;

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    [JsonIgnore]
    public double DurationMs => LoopOutMs - LoopInMs;

    /// <summary>
    /// Number of bars in this section.
    /// </summary>
    [JsonIgnore]
    public int BarCount => EndBar - StartBar + 1;
}

/// <summary>
/// Lighting preset configuration for a section.
/// </summary>
public class LightingPreset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("paletteIndex")]
    public int PaletteIndex { get; set; } = 0;

    [JsonPropertyName("intensity")]
    public float Intensity { get; set; } = 0.5f;

    [JsonPropertyName("excitement")]
    public float Excitement { get; set; } = 0.5f;

    [JsonPropertyName("snapshotName")]
    public string SnapshotName { get; set; } = "";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFF";

    // MIDI fallback settings
    [JsonPropertyName("midiNote")]
    public int MidiNote { get; set; } = 60;

    [JsonPropertyName("midiChannel")]
    public int MidiChannel { get; set; } = 1;

    [JsonPropertyName("midiVelocity")]
    public int MidiVelocity { get; set; } = 127;
}

/// <summary>
/// A MIDI cue that fires during playback to control WorshipTools Presenter.
/// Each cue sends a single MIDI Note On message (NO Note Off — Presenter
/// responds to both, causing double-advances if Note Off is sent).
/// </summary>
public class MidiCue
{
    /// <summary>
    /// Playback position in milliseconds when this cue should fire.
    /// </summary>
    [JsonPropertyName("timestamp_ms")]
    public double TimestampMs { get; set; }

    /// <summary>
    /// The action to perform in WorshipTools Presenter.
    /// </summary>
    [JsonPropertyName("action")]
    public MidiCueAction Action { get; set; } = MidiCueAction.NextSlide;

    /// <summary>
    /// MIDI channel (1-16). Default: use global setting.
    /// 0 = use the global default from AppSettings.
    /// </summary>
    [JsonPropertyName("channel")]
    public int Channel { get; set; } = 0;

    /// <summary>
    /// MIDI note number (0-127). If 0, uses the default note for this action type
    /// from AppSettings.MidiPresenter* settings.
    /// </summary>
    [JsonPropertyName("note")]
    public int Note { get; set; } = 0;

    /// <summary>
    /// MIDI velocity (1-127). For NextSlide/PrevSlide/NextItem: 127 (any non-zero).
    /// For TriggerSlide: the slide number to jump to.
    /// </summary>
    [JsonPropertyName("velocity")]
    public int Velocity { get; set; } = 127;

    /// <summary>
    /// Target slide number (1-based). Only used when Action = TriggerSlide.
    /// This value is sent as the velocity byte.
    /// </summary>
    [JsonPropertyName("slideNumber")]
    public int SlideNumber { get; set; } = 0;

    /// <summary>
    /// Optional label for display in the cue editor (e.g., "Verse 1 line 2").
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>
    /// Whether this cue is enabled. Disabled cues are skipped during playback.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Get the effective velocity to send. For TriggerSlide, uses SlideNumber.
    /// </summary>
    [JsonIgnore]
    public int EffectiveVelocity => Action == MidiCueAction.TriggerSlide && SlideNumber > 0
        ? Math.Clamp(SlideNumber, 1, 127)
        : Math.Clamp(Velocity, 1, 127);
}

/// <summary>
/// Actions that can be triggered in WorshipTools Presenter via MIDI.
/// Each action maps to a specific MIDI note (configurable in Presenter's settings).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MidiCueAction
{
    /// <summary>Advance to the next lyric slide within the current song.</summary>
    NextSlide,

    /// <summary>Go back one slide.</summary>
    PrevSlide,

    /// <summary>Move to the next service item (next song in setlist).</summary>
    NextItem,

    /// <summary>Jump directly to a specific slide (velocity = slide number).</summary>
    TriggerSlide
}
