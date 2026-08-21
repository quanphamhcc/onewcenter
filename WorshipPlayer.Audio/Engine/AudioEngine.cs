using NAudio.Wave;
using NAudio.CoreAudioApi;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Audio.Engine;

/// <summary>
/// The main audio engine that manages WASAPI output and multitrack mixing.
/// </summary>
public class AudioEngine : IDisposable
{
    private WasapiOut? _wasapiOut;
    private MultiTrackMixer? _mixer;
    private readonly AppSettings _settings;

    public bool IsPlaying => _wasapiOut?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _wasapiOut?.PlaybackState == PlaybackState.Paused;

    // Events
    public event EventHandler<PlaybackPositionEventArgs>? PositionChanged;
    public event EventHandler<SectionChangedEventArgs>? SectionChanged;
    public event EventHandler? PlaybackStopped;

    public MultiTrackMixer? Mixer => _mixer;

    public AudioEngine(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Initialize the audio output device.
    /// </summary>
    public void Initialize()
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice device;

        if (!string.IsNullOrEmpty(_settings.AudioDevice))
        {
            // Try to find specific device
            device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName == _settings.AudioDevice)
                ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        else
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        var shareMode = _settings.WasapiExclusive
            ? AudioClientShareMode.Exclusive
            : AudioClientShareMode.Shared;

        _wasapiOut = new WasapiOut(device, shareMode, true, _settings.BufferSize);
        _wasapiOut.PlaybackStopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Load a song into the mixer. Two-phase approach:
    /// Phase 1 (heavy): Create MultiTrackMixer which decodes all audio files
    /// Phase 2 (light): Initialize WASAPI output (must be on UI thread)
    /// </summary>
    public void LoadSong(Song song)
    {
        // Stop and dispose the current output device completely
        if (_wasapiOut != null)
        {
            _wasapiOut.Stop();
            _wasapiOut.Dispose();
            _wasapiOut = null;
        }

        _mixer?.Dispose();
        _mixer = new MultiTrackMixer(song, _settings);
        _mixer.PositionChanged += (s, e) => PositionChanged?.Invoke(this, e);
        _mixer.SectionChanged += (s, e) => SectionChanged?.Invoke(this, e);

        // Create a fresh WASAPI output device
        Initialize();
        _wasapiOut!.Init(_mixer);
    }

    /// <summary>
    /// Pre-decode all audio files into memory (heavy, can run on background thread).
    /// Call ActivateSong() on UI thread after this completes.
    /// </summary>
    public MultiTrackMixer PreloadSong(Song song)
    {
        var mixer = new MultiTrackMixer(song, _settings);
        return mixer;
    }

    /// <summary>
    /// Activate a pre-loaded mixer (light, must run on UI thread for WASAPI).
    /// </summary>
    public void ActivateSong(MultiTrackMixer mixer)
    {
        if (_wasapiOut != null)
        {
            _wasapiOut.Stop();
            _wasapiOut.Dispose();
            _wasapiOut = null;
        }

        _mixer?.Dispose();
        _mixer = mixer;
        _mixer.PositionChanged += (s, e) => PositionChanged?.Invoke(this, e);
        _mixer.SectionChanged += (s, e) => SectionChanged?.Invoke(this, e);

        Initialize();
        _wasapiOut!.Init(_mixer);
    }

    /// <summary>
    /// Start or resume playback.
    /// </summary>
    public void Play()
    {
        if (_wasapiOut?.PlaybackState == PlaybackState.Stopped ||
            _wasapiOut?.PlaybackState == PlaybackState.Paused)
        {
            _wasapiOut.Play();
        }
    }

    /// <summary>
    /// Pause playback.
    /// </summary>
    public void Pause()
    {
        _wasapiOut?.Pause();
    }

    /// <summary>
    /// Stop playback and reset position.
    /// </summary>
    public void Stop()
    {
        _wasapiOut?.Stop();
        _mixer?.ResetPosition();
    }

    /// <summary>
    /// Jump to a specific section.
    /// </summary>
    public void JumpToSection(int sectionIndex)
    {
        _mixer?.JumpToSection(sectionIndex);
    }

    /// <summary>
    /// Queue a section to play after the current section finishes.
    /// </summary>
    public void QueueSection(int sectionIndex)
    {
        _mixer?.QueueSection(sectionIndex);
    }

    /// <summary>
    /// Clear any queued section.
    /// </summary>
    public void ClearQueue()
    {
        _mixer?.ClearQueue();
    }

    /// <summary>
    /// Get the index of the currently queued section (-1 if none).
    /// </summary>
    public int QueuedSectionIndex => _mixer?.QueuedSectionIndex ?? -1;

    /// <summary>
    /// Get the index of the currently playing section.
    /// </summary>
    public int CurrentSectionIndex => _mixer?.CurrentSectionIndex ?? -1;

    /// <summary>
    /// Toggle loop mode.
    /// </summary>
    public void SetLoopEnabled(bool enabled)
    {
        if (_mixer != null)
            _mixer.LoopEnabled = enabled;
    }

    /// <summary>
    /// Set loop mode: infinite or 1x.
    /// </summary>
    public void SetLoopInfinite(bool infinite)
    {
        if (_mixer != null)
            _mixer.LoopInfinite = infinite;
    }

    /// <summary>
    /// Set the tempo ratio (1.0 = original, 0.5 = half speed, 2.0 = double).
    /// </summary>
    public void SetTempo(double tempoRatio)
    {
        _mixer?.SetTempo(tempoRatio);
    }

    /// <summary>
    /// Set pitch shift in semitones (-6 to +6).
    /// </summary>
    public void SetPitchShift(int semitones)
    {
        _mixer?.SetPitchShift(semitones);
    }

    /// <summary>
    /// Set volume for a specific track (0.0 to 1.0).
    /// </summary>
    public void SetTrackVolume(int trackIndex, float volume)
    {
        _mixer?.SetTrackVolume(trackIndex, volume);
    }

    /// <summary>
    /// Set mute state for a specific track.
    /// </summary>
    public void SetTrackMute(int trackIndex, bool mute)
    {
        _mixer?.SetTrackMute(trackIndex, mute);
    }

    /// <summary>
    /// Set solo state for a specific track.
    /// </summary>
    public void SetTrackSolo(int trackIndex, bool solo)
    {
        _mixer?.SetTrackSolo(trackIndex, solo);
    }

    /// <summary>
    /// Seek to a specific millisecond position.
    /// </summary>
    public void SeekToMs(double ms)
    {
        _mixer?.SeekToMs(ms);
    }

    /// <summary>
    /// Get the current playback position in milliseconds.
    /// Uses the first track's actual file position for accuracy.
    /// </summary>
    public double GetPositionMs()
    {
        if (_mixer != null && _mixer.TrackCount > 0)
        {
            return _mixer.GetActualPositionMs();
        }
        return _mixer?.CurrentPositionMs ?? 0;
    }

    /// <summary>
    /// Get list of available audio output devices.
    /// </summary>
    public static List<string> GetAvailableDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => d.FriendlyName)
            .ToList();
    }

    public void Dispose()
    {
        try
        {
            _wasapiOut?.Stop();
        }
        catch { }

        try
        {
            _wasapiOut?.Dispose();
            _wasapiOut = null;
        }
        catch { }

        try
        {
            _mixer?.Dispose();
            _mixer = null;
        }
        catch { }
    }
}

public class PlaybackPositionEventArgs : EventArgs
{
    public double PositionMs { get; set; }
    public int CurrentBar { get; set; }
    public int CurrentBeat { get; set; }
    public int CurrentSectionIndex { get; set; }
    public double SectionProgressPercent { get; set; }
}

public class SectionChangedEventArgs : EventArgs
{
    public int PreviousSectionIndex { get; set; }
    public int NewSectionIndex { get; set; }
    public Section? NewSection { get; set; }
}
