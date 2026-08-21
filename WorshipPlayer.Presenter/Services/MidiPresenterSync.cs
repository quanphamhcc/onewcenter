using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Bridges the Backtrack Player's MidiCueService with the Presenter Engine.
/// 
/// This is the KEY INTEGRATION POINT between the two modules:
/// - Backtrack plays audio and fires MIDI cues at specific timestamps
/// - This service receives those cues and translates them into slide actions
/// - The Presenter Engine advances slides accordingly
/// 
/// Also handles:
/// - Section change events from the backtrack (jump to section's first slide)
/// - Section loop events (re-show first slide of looped section)
/// - Song end events (advance to next service item)
/// - Position updates (for timestamp-based auto-advance)
/// - Pitch shift changes (real-time transposition updates)
/// - Tempo changes
/// </summary>
public class MidiPresenterSync : IDisposable
{
    private readonly PresenterEngine _presenterEngine;
    private readonly MidiCueService _midiCueService;
    private readonly PresenterSettings _settings;

    private bool _isActive = false;

    /// <summary>
    /// Whether this sync bridge is actively listening for cues.
    /// </summary>
    public bool IsActive => _isActive;

    public MidiPresenterSync(
        PresenterEngine presenterEngine,
        MidiCueService midiCueService,
        PresenterSettings settings)
    {
        _presenterEngine = presenterEngine;
        _midiCueService = midiCueService;
        _settings = settings;
    }

    /// <summary>
    /// Start listening for MIDI cues from the backtrack player.
    /// </summary>
    public void Activate()
    {
        if (_isActive) return;

        _midiCueService.CueTriggered += OnMidiCueTriggered;
        _isActive = true;
    }

    /// <summary>
    /// Stop listening for MIDI cues.
    /// </summary>
    public void Deactivate()
    {
        if (!_isActive) return;

        _midiCueService.CueTriggered -= OnMidiCueTriggered;
        _isActive = false;
    }

    /// <summary>
    /// Called when the MidiCueService fires a cue during backtrack playback.
    /// Translates the MIDI action into a Presenter slide action.
    /// </summary>
    private void OnMidiCueTriggered(object? sender, MidiCueTriggeredEventArgs e)
    {
        if (!_settings.AutoAdvanceFromMidi) return;

        _presenterEngine.OnMidiCueReceived(e.Action, (int)e.Velocity);
    }

    /// <summary>
    /// Called by the AudioEngine's position update callback.
    /// Forwards the position to the Presenter for timestamp-based advance.
    /// </summary>
    public void OnPlaybackPositionUpdate(double positionMs)
    {
        if (!_isActive) return;
        _presenterEngine.OnPlaybackPositionUpdate(positionMs);
    }

    /// <summary>
    /// Called when the backtrack player changes sections.
    /// Syncs the Presenter to show slides for the new section.
    /// </summary>
    public void OnBacktrackSectionChanged(string sectionName)
    {
        if (!_isActive) return;
        _presenterEngine.OnSectionChanged(sectionName);
    }

    /// <summary>
    /// Called when the backtrack player loops a section.
    /// Re-shows the first slide of the looped section.
    /// </summary>
    public void OnSectionLooped(string sectionName)
    {
        if (!_isActive) return;
        _presenterEngine.OnSectionLooped(sectionName);
    }

    /// <summary>
    /// Called when the backtrack player changes pitch shift (real-time transposition).
    /// Updates the Presenter so displays re-render with new chord transposition.
    /// </summary>
    public void OnPitchShiftChanged(int semitones)
    {
        if (!_isActive) return;
        _presenterEngine.OnPitchShiftChanged(semitones);
    }

    /// <summary>
    /// Called when the backtrack player changes tempo.
    /// </summary>
    public void OnTempoChanged(double tempoPercent)
    {
        if (!_isActive) return;
        _presenterEngine.OnTempoChanged(tempoPercent);
    }

    /// <summary>
    /// Called when a song is loaded into the backtrack player.
    /// Passes the Song object to the Presenter for section-aware display.
    /// </summary>
    public void OnSongLoaded(Song song)
    {
        if (!_isActive) return;
        _presenterEngine.OnSongLoaded(song);
    }

    /// <summary>
    /// Called when the backtrack player finishes a song.
    /// Advances the Presenter to the next service item if configured.
    /// </summary>
    public void OnSongEnded()
    {
        if (!_isActive || !_settings.AutoAdvanceOnSongEnd) return;
        _presenterEngine.OnSongEnded();
    }

    public void Dispose()
    {
        Deactivate();
    }
}
