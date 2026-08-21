using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages MIDI cue playback for WorshipTools Presenter integration.
/// 
/// During track playback, monitors the current position and fires MIDI Note On
/// messages when the playback position crosses a cue's timestamp.
/// 
/// CRITICAL: Only sends Note ON messages (velocity > 0). NEVER sends Note OFF.
/// WorshipTools Presenter responds to BOTH Note On AND Note Off events, which
/// causes double-advances if Note Off is sent.
/// 
/// Connection: Uses a virtual MIDI port (loopMIDI on Windows, IAC Driver on Mac)
/// or network MIDI (rtpMIDI) to communicate with Presenter.
/// </summary>
public class MidiCueService : IDisposable
{
    private readonly AppSettings _settings;
    private IMidiCueOutput? _midiOutput;
    private Song? _currentSong;
    private List<MidiCue> _sortedCues = new();
    private int _nextCueIndex = 0;
    private double _lastPositionMs = 0;
    private bool _isPlaying = false;
    private readonly object _lock = new();

    /// <summary>
    /// Fired when a MIDI cue is triggered during playback.
    /// </summary>
    public event EventHandler<MidiCueTriggeredEventArgs>? CueTriggered;

    /// <summary>
    /// Whether the MIDI output is connected and ready to send.
    /// </summary>
    public bool IsConnected => _midiOutput?.IsConnected ?? false;

    /// <summary>
    /// Name of the currently connected MIDI device.
    /// </summary>
    public string ConnectedDeviceName => _midiOutput?.DeviceName ?? "";

    public MidiCueService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Set the MIDI output implementation (injected from the Audio/UI layer
    /// which has access to NAudio.Midi).
    /// </summary>
    public void SetMidiOutput(IMidiCueOutput output)
    {
        _midiOutput?.Dispose();
        _midiOutput = output;
    }

    /// <summary>
    /// Connect to the configured MIDI output port.
    /// </summary>
    public bool Connect()
    {
        if (_midiOutput == null) return false;
        if (!_settings.MidiPresenterEnabled) return false;

        string deviceName = !string.IsNullOrEmpty(_settings.MidiPresenterDeviceName)
            ? _settings.MidiPresenterDeviceName
            : _settings.MidiDeviceName;

        if (string.IsNullOrEmpty(deviceName)) return false;

        return _midiOutput.Connect(deviceName);
    }

    /// <summary>
    /// Disconnect from the MIDI output port.
    /// </summary>
    public void Disconnect()
    {
        _midiOutput?.Disconnect();
    }

    /// <summary>
    /// Load a song's MIDI cues for playback. Sorts cues by timestamp.
    /// </summary>
    public void LoadSong(Song? song)
    {
        lock (_lock)
        {
            _currentSong = song;
            _nextCueIndex = 0;
            _lastPositionMs = 0;

            if (song?.MidiCues != null && song.MidiCues.Count > 0)
            {
                // Sort cues by timestamp and filter to enabled only
                _sortedCues = song.MidiCues
                    .Where(c => c.Enabled)
                    .OrderBy(c => c.TimestampMs)
                    .ToList();
            }
            else
            {
                _sortedCues = new List<MidiCue>();
            }
        }
    }

    /// <summary>
    /// Notify the service that playback has started.
    /// </summary>
    public void OnPlaybackStarted()
    {
        _isPlaying = true;
    }

    /// <summary>
    /// Notify the service that playback has stopped.
    /// </summary>
    public void OnPlaybackStopped()
    {
        _isPlaying = false;
    }

    /// <summary>
    /// Notify the service that playback position has been seeked (e.g., section jump).
    /// Resets the cue index to the appropriate position.
    /// </summary>
    public void OnSeek(double positionMs)
    {
        lock (_lock)
        {
            _lastPositionMs = positionMs;

            // Find the first cue AFTER the new position
            _nextCueIndex = 0;
            for (int i = 0; i < _sortedCues.Count; i++)
            {
                if (_sortedCues[i].TimestampMs > positionMs)
                {
                    _nextCueIndex = i;
                    return;
                }
            }
            // All cues are before current position
            _nextCueIndex = _sortedCues.Count;
        }
    }

    /// <summary>
    /// Called periodically during playback with the current position.
    /// Checks if any cues should fire and sends MIDI messages.
    /// 
    /// This should be called from the position reporting callback (~every 46ms).
    /// </summary>
    public void UpdatePosition(double currentPositionMs)
    {
        if (!_isPlaying || !_settings.MidiPresenterEnabled) return;
        if (_midiOutput == null || !_midiOutput.IsConnected) return;

        lock (_lock)
        {
            // Fire all cues between last position and current position
            while (_nextCueIndex < _sortedCues.Count)
            {
                var cue = _sortedCues[_nextCueIndex];

                if (cue.TimestampMs <= currentPositionMs && cue.TimestampMs > _lastPositionMs)
                {
                    // Fire this cue!
                    FireCue(cue);
                    _nextCueIndex++;
                }
                else if (cue.TimestampMs > currentPositionMs)
                {
                    // Next cue is in the future — stop checking
                    break;
                }
                else
                {
                    // Cue is in the past (before lastPosition) — skip
                    _nextCueIndex++;
                }
            }

            _lastPositionMs = currentPositionMs;
        }
    }

    /// <summary>
    /// Called when a song finishes playing (reached the end).
    /// Sends "Next Item" if configured to auto-advance in Presenter.
    /// </summary>
    public void OnSongEnded()
    {
        if (!_settings.MidiPresenterEnabled || !_settings.MidiPresenterAutoNextItem) return;
        if (_midiOutput == null || !_midiOutput.IsConnected) return;

        int channel = _settings.MidiPresenterChannel;
        int note = _settings.MidiPresenterNoteNextItem;

        SendNoteOn(channel, note, 127);

        CueTriggered?.Invoke(this, new MidiCueTriggeredEventArgs
        {
            Action = MidiCueAction.NextItem,
            Channel = channel,
            Note = note,
            Velocity = 127,
            TimestampMs = -1, // End of song
            Label = "[Auto] Song ended → Next Item"
        });
    }

    /// <summary>
    /// Manually send a test MIDI message to verify the connection.
    /// </summary>
    public bool SendTestMessage()
    {
        if (_midiOutput == null || !_midiOutput.IsConnected) return false;

        int channel = _settings.MidiPresenterChannel;
        int note = _settings.MidiPresenterNoteNextSlide;

        SendNoteOn(channel, note, 127);
        return true;
    }

    /// <summary>
    /// Manually fire a specific action (e.g., from a button press or foot controller).
    /// </summary>
    public void ManualTrigger(MidiCueAction action, int slideNumber = 0)
    {
        if (_midiOutput == null || !_midiOutput.IsConnected) return;

        int channel = _settings.MidiPresenterChannel;
        int note = GetDefaultNoteForAction(action);
        int velocity = action == MidiCueAction.TriggerSlide && slideNumber > 0
            ? Math.Clamp(slideNumber, 1, 127)
            : 127;

        SendNoteOn(channel, note, velocity);

        CueTriggered?.Invoke(this, new MidiCueTriggeredEventArgs
        {
            Action = action,
            Channel = channel,
            Note = note,
            Velocity = velocity,
            TimestampMs = _lastPositionMs,
            Label = $"[Manual] {action}"
        });
    }

    /// <summary>
    /// Get available MIDI output devices.
    /// </summary>
    public List<string> GetAvailableDevices()
    {
        return _midiOutput?.GetAvailableDevices() ?? new List<string>();
    }

    // ─── Private Methods ──────────────────────────────────────────────────

    private void FireCue(MidiCue cue)
    {
        // Determine effective channel, note, and velocity
        int channel = cue.Channel > 0 ? cue.Channel : _settings.MidiPresenterChannel;
        int note = cue.Note > 0 ? cue.Note : GetDefaultNoteForAction(cue.Action);
        int velocity = cue.EffectiveVelocity;

        // Send MIDI Note On — NEVER send Note Off!
        SendNoteOn(channel, note, velocity);

        // Notify listeners
        CueTriggered?.Invoke(this, new MidiCueTriggeredEventArgs
        {
            Action = cue.Action,
            Channel = channel,
            Note = note,
            Velocity = velocity,
            TimestampMs = cue.TimestampMs,
            Label = cue.Label
        });
    }

    /// <summary>
    /// Send a MIDI Note On message. NEVER sends Note Off.
    /// </summary>
    private void SendNoteOn(int channel, int note, int velocity)
    {
        // Clamp values to valid MIDI range
        channel = Math.Clamp(channel, 1, 16);
        note = Math.Clamp(note, 0, 127);
        velocity = Math.Clamp(velocity, 1, 127); // Must be > 0 (velocity 0 = Note Off!)

        _midiOutput?.SendNoteOn(channel, note, velocity);
    }

    private int GetDefaultNoteForAction(MidiCueAction action)
    {
        return action switch
        {
            MidiCueAction.NextSlide => _settings.MidiPresenterNoteNextSlide,
            MidiCueAction.PrevSlide => _settings.MidiPresenterNotePrevSlide,
            MidiCueAction.NextItem => _settings.MidiPresenterNoteNextItem,
            MidiCueAction.TriggerSlide => _settings.MidiPresenterNoteTriggerSlide,
            _ => _settings.MidiPresenterNoteNextSlide
        };
    }

    public void Dispose()
    {
        _midiOutput?.Dispose();
    }
}

/// <summary>
/// Event args fired when a MIDI cue is triggered.
/// </summary>
public class MidiCueTriggeredEventArgs : EventArgs
{
    public MidiCueAction Action { get; set; }
    public int Channel { get; set; }
    public int Note { get; set; }
    public int Velocity { get; set; }
    public double TimestampMs { get; set; }
    public string Label { get; set; } = "";
}

/// <summary>
/// Interface for MIDI output - implemented in the Audio project using NAudio.Midi.
/// This abstraction keeps the Core project platform-independent.
/// 
/// IMPORTANT: Implementations must ONLY send Note On messages.
/// Do NOT send Note Off for any reason — WorshipTools Presenter
/// treats Note Off as another trigger event (causes double-advance).
/// </summary>
public interface IMidiCueOutput : IDisposable
{
    bool IsConnected { get; }
    string DeviceName { get; }
    bool Connect(string deviceName);
    void Disconnect();

    /// <summary>
    /// Send a MIDI Note On message. Velocity MUST be > 0.
    /// Do NOT send a corresponding Note Off.
    /// </summary>
    void SendNoteOn(int channel, int note, int velocity);

    List<string> GetAvailableDevices();
}
