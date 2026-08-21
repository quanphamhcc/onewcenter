using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Unified lighting service that coordinates OSC (MaestroDMX) and MIDI output.
/// Automatically sends lighting commands on section changes.
/// </summary>
public class LightingService : IDisposable
{
    private readonly OscLightingController _oscController;
    private readonly IMidiLightingController _midiController;
    private readonly AppSettings _settings;

    private Song? _currentSong;
    private int _currentSectionIndex = -1;

    public OscLightingController OscController => _oscController;
    public IMidiLightingController MidiController => _midiController;

    public LightingService(AppSettings settings, IMidiLightingController? midiController = null)
    {
        _settings = settings;
        _oscController = new OscLightingController();
        _midiController = midiController ?? new MidiLightingControllerStub();
    }

    /// <summary>
    /// Initialize connections based on settings.
    /// </summary>
    public void Initialize()
    {
        if (_settings.OscEnabled)
        {
            _oscController.Connect(_settings.OscHost, _settings.OscPort);
        }

        if (_settings.MidiEnabled)
        {
            _midiController.Connect(_settings.MidiDeviceName, _settings.MidiChannel);
        }
    }

    /// <summary>
    /// Set the current song for lighting reference.
    /// </summary>
    public void SetSong(Song song)
    {
        _currentSong = song;
        _currentSectionIndex = -1;
    }

    /// <summary>
    /// Called when a section change occurs - sends lighting commands.
    /// </summary>
    public void OnSectionChanged(int sectionIndex)
    {
        if (_currentSong == null || sectionIndex < 0 || sectionIndex >= _currentSong.Sections.Count)
            return;

        _currentSectionIndex = sectionIndex;
        var section = _currentSong.Sections[sectionIndex];

        // Find the lighting preset for this section
        LightingPreset? preset = null;
        if (section.LightingPresetIndex >= 0 && section.LightingPresetIndex < _currentSong.LightingPresets.Count)
        {
            preset = _currentSong.LightingPresets[section.LightingPresetIndex];
        }

        if (preset == null)
        {
            // Auto-generate a default preset based on section type
            preset = GenerateDefaultPreset(section);
        }

        // Send via OSC
        if (_settings.OscEnabled && _oscController.IsConnected)
        {
            _oscController.ApplyPreset(preset);

            // Also sync BPM
            if (_currentSong.Bpm > 0)
                _oscController.SetBpm((float)_currentSong.Bpm);
        }

        // Send via MIDI
        if (_settings.MidiEnabled && _midiController.IsConnected)
        {
            _midiController.ApplyPreset(preset);
        }
    }

    /// <summary>
    /// Manual override - force a blackout.
    /// </summary>
    public void ForceBlackout()
    {
        if (_settings.OscEnabled && _oscController.IsConnected)
            _oscController.Blackout();

        if (_settings.MidiEnabled && _midiController.IsConnected)
            _midiController.AllNotesOff();
    }

    /// <summary>
    /// Manual override - flash/bump effect.
    /// </summary>
    public void ForceFlash()
    {
        if (_settings.OscEnabled && _oscController.IsConnected)
            _oscController.Flash();
    }

    /// <summary>
    /// Generate a reasonable default lighting preset from section characteristics.
    /// </summary>
    private LightingPreset GenerateDefaultPreset(Section section)
    {
        string nameLower = section.Name.ToLower();

        // Heuristic-based defaults
        if (nameLower.Contains("intro"))
            return new LightingPreset { Name = "Intro", PaletteIndex = 0, Intensity = 0.3f, Excitement = 0.2f, MidiNote = 60 };
        if (nameLower.Contains("verse"))
            return new LightingPreset { Name = "Verse", PaletteIndex = 1, Intensity = 0.4f, Excitement = 0.3f, MidiNote = 62 };
        if (nameLower.Contains("pre") || nameLower.Contains("build"))
            return new LightingPreset { Name = "Build", PaletteIndex = 2, Intensity = 0.6f, Excitement = 0.5f, MidiNote = 64 };
        if (nameLower.Contains("chorus"))
            return new LightingPreset { Name = "Chorus", PaletteIndex = 3, Intensity = 0.9f, Excitement = 0.8f, MidiNote = 65 };
        if (nameLower.Contains("bridge"))
            return new LightingPreset { Name = "Bridge", PaletteIndex = 4, Intensity = 0.5f, Excitement = 0.3f, MidiNote = 67 };
        if (nameLower.Contains("outro") || nameLower.Contains("ending"))
            return new LightingPreset { Name = "Outro", PaletteIndex = 0, Intensity = 0.2f, Excitement = 0.1f, MidiNote = 69 };

        // Default
        return new LightingPreset { Name = "Default", PaletteIndex = 1, Intensity = 0.5f, Excitement = 0.5f, MidiNote = 60 };
    }

    public void Dispose()
    {
        _oscController.Dispose();
        _midiController.Dispose();
    }
}
