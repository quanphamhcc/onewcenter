using System.Text.Json.Serialization;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Global application settings.
/// </summary>
public class AppSettings
{
    [JsonPropertyName("audioDevice")]
    public string AudioDevice { get; set; } = "";

    [JsonPropertyName("sampleRate")]
    public int SampleRate { get; set; } = 44100;

    [JsonPropertyName("bufferSize")]
    public int BufferSize { get; set; } = 256;

    /// <summary>
    /// Milliseconds subtracted from the mixer's read position when capturing a "Tap Beat 1"
    /// timestamp, so the captured value matches the sound the user actually HEARD rather than the
    /// read-ahead position. The true output latency is usually close to (but not exactly) the
    /// requested <see cref="BufferSize"/>, so this is a dedicated, tunable value. Lower it slightly
    /// if tapped beats land a touch early; raise it if they land late. Default matches BufferSize.
    /// </summary>
    [JsonPropertyName("tapLatencyCompensationMs")]
    public int TapLatencyCompensationMs { get; set; } = 256;

    [JsonPropertyName("outputMode")]
    public OutputMode OutputMode { get; set; } = OutputMode.PanSplit;

    [JsonPropertyName("wasapiExclusive")]
    public bool WasapiExclusive { get; set; } = false;

    [JsonPropertyName("songsFolder")]
    public string SongsFolder { get; set; } = "";

    [JsonPropertyName("setlistsFolder")]
    public string SetlistsFolder { get; set; } = "";

    // Optional explicit Python interpreter path for AI analysis. When set and valid it takes
    // priority over auto-discovery; empty means "auto-discover". Persisted via SettingsService.
    [JsonPropertyName("pythonExecutablePath")]
    public string PythonExecutablePath { get; set; } = "";

    // AI Training Data Settings
    [JsonPropertyName("trainingDataFolder")]
    public string TrainingDataFolder { get; set; } = TrainingDataPaths.DefaultFolder();

    // OSC Settings for MaestroDMX
    [JsonPropertyName("oscEnabled")]
    public bool OscEnabled { get; set; } = false;

    [JsonPropertyName("oscHost")]
    public string OscHost { get; set; } = "192.168.1.100";

    [JsonPropertyName("oscPort")]
    public int OscPort { get; set; } = 8000;

    // MIDI Settings (Lighting)
    [JsonPropertyName("midiEnabled")]
    public bool MidiEnabled { get; set; } = false;

    [JsonPropertyName("midiDeviceName")]
    public string MidiDeviceName { get; set; } = "";

    [JsonPropertyName("midiChannel")]
    public int MidiChannel { get; set; } = 1;

    // MIDI Input (foot controller)
    [JsonPropertyName("midiInputEnabled")]
    public bool MidiInputEnabled { get; set; } = false;

    [JsonPropertyName("midiInputDeviceName")]
    public string MidiInputDeviceName { get; set; } = "";

    // ─── MIDI Presenter Integration (WorshipTools) ───────────────────────
    // Sends MIDI cues to WorshipTools Presenter for lyric slide advancement.
    // Uses a separate MIDI output port from the lighting controller.

    [JsonPropertyName("midiPresenterEnabled")]
    public bool MidiPresenterEnabled { get; set; } = false;

    /// <summary>
    /// MIDI output device name for Presenter (e.g., "loopMIDI Port" or "IAC Driver Bus 1").
    /// If empty, uses the same device as midiDeviceName.
    /// </summary>
    [JsonPropertyName("midiPresenterDeviceName")]
    public string MidiPresenterDeviceName { get; set; } = "";

    /// <summary>
    /// Default MIDI channel for Presenter cues (1-16).
    /// </summary>
    [JsonPropertyName("midiPresenterChannel")]
    public int MidiPresenterChannel { get; set; } = 1;

    /// <summary>
    /// Default MIDI note for "Next Slide" action (0-127).
    /// Maps to whatever note Presenter is configured to listen for.
    /// </summary>
    [JsonPropertyName("midiPresenterNoteNextSlide")]
    public int MidiPresenterNoteNextSlide { get; set; } = 60;

    /// <summary>
    /// Default MIDI note for "Previous Slide" action (0-127).
    /// </summary>
    [JsonPropertyName("midiPresenterNotePrevSlide")]
    public int MidiPresenterNotePrevSlide { get; set; } = 61;

    /// <summary>
    /// Default MIDI note for "Next Item" (next song) action (0-127).
    /// </summary>
    [JsonPropertyName("midiPresenterNoteNextItem")]
    public int MidiPresenterNoteNextItem { get; set; } = 62;

    /// <summary>
    /// Default MIDI note for "Trigger Specific Slide" action (0-127).
    /// The velocity byte carries the slide number.
    /// </summary>
    [JsonPropertyName("midiPresenterNoteTriggerSlide")]
    public int MidiPresenterNoteTriggerSlide { get; set; } = 63;

    /// <summary>
    /// Automatically send "Next Item" when a song finishes playing.
    /// This advances Presenter to the next song in the service.
    /// </summary>
    [JsonPropertyName("midiPresenterAutoNextItem")]
    public bool MidiPresenterAutoNextItem { get; set; } = true;

    // Jump timing
    [JsonPropertyName("defaultJumpTiming")]
    public JumpTiming DefaultJumpTiming { get; set; } = JumpTiming.EndOfSection;

    // UI
    [JsonPropertyName("darkTheme")]
    public bool DarkTheme { get; set; } = true;

    [JsonPropertyName("touchMode")]
    public bool TouchMode { get; set; } = false;

    [JsonPropertyName("clickSound")]
    public int ClickSound { get; set; } = 0;

    // GPU/CUDA
    [JsonPropertyName("cudaSetupComplete")]
    public bool CudaSetupComplete { get; set; } = false;

    [JsonPropertyName("cudaAvailable")]
    public bool CudaAvailable { get; set; } = false;
}

/// <summary>
/// Output routing mode.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutputMode
{
    PanSplit,       // Left=FOH, Right=IEM
    MultiOutput,    // Separate audio device outputs
    Stereo          // Everything in stereo (no routing)
}

/// <summary>
/// When to execute a section jump.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JumpTiming
{
    Immediate,      // Jump right now
    EndOfBar,       // Jump at the end of current bar
    EndOfSection,   // Jump at the end of current section
    After1Bar,      // Jump after 1 more bar
    After2Bars      // Jump after 2 more bars
}
