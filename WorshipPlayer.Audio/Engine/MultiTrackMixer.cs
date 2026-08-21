using NAudio.Wave;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Audio.DSP;

namespace WorshipPlayer.Audio.Engine;

/// <summary>
/// Mixes multiple audio tracks together with per-track volume, solo, mute,
/// and applies tempo/pitch changes via SoundTouch.
/// Routes audio based on output mode (pan-split, multi-out, stereo).
/// </summary>
public class MultiTrackMixer : IWaveProvider, IDisposable
{
    private readonly Song _song;
    private readonly AppSettings _settings;
    private readonly List<TrackPlayer> _tracks = new();
    private readonly WaveFormat _waveFormat;

    private long _position; // in samples
    private int _currentSectionIndex = -1;
    private double _tempoRatio = 1.0;
    private int _pitchSemitones = 0;

    private readonly object _lock = new();

    public bool LoopEnabled { get; set; } = false;
    public bool LoopInfinite { get; set; } = false;
    public int LoopCount { get; set; } = 0;
    private int _loopIterations = 0;
    private int _queuedSectionIndex = -1;  // Section queued to play after current finishes

    // Pre-allocated buffers to avoid GC pressure in the audio callback
    private float[]? _readBuffer;
    private float[]? _trackReadBuffer;
    private TrackPlayer? _cachedRefTrack; // Cached reference track (non-vocal) for position
    private bool _refTrackCacheDirty = true;

    public double CurrentPositionMs => SamplesToMs(_position / _waveFormat.Channels);
    public int CurrentSectionIndex => _currentSectionIndex;
    public int QueuedSectionIndex => _queuedSectionIndex;
    public int TrackCount => _tracks.Count;

    /// <summary>
    /// Get the actual playback position from an instrument track's file reader.
    /// This is the most accurate position, directly synced with what's being played.
    /// Uses an instrument track (not vocal) to avoid offset from vocal pickup pre-roll.
    /// </summary>
    public double GetActualPositionMs()
    {
        if (_tracks.Count > 0)
        {
            var refTrack = GetReferenceTrack();
            return refTrack.GetPositionMs();
        }
        return CurrentPositionMs;
    }

    /// <summary>
    /// Get the cached non-vocal reference track for position reporting.
    /// Avoids LINQ allocation on every call.
    /// </summary>
    private TrackPlayer GetReferenceTrack()
    {
        if (_refTrackCacheDirty || _cachedRefTrack == null)
        {
            _cachedRefTrack = null;
            for (int i = 0; i < _tracks.Count; i++)
            {
                if (!IsVocalTrack(_tracks[i]))
                {
                    _cachedRefTrack = _tracks[i];
                    break;
                }
            }
            _cachedRefTrack ??= _tracks[0];
            _refTrackCacheDirty = false;
        }
        return _cachedRefTrack;
    }

    // Events
    public event EventHandler<PlaybackPositionEventArgs>? PositionChanged;
    public event EventHandler<SectionChangedEventArgs>? SectionChanged;

    // Position reporting
    private int _samplesUntilNextReport = 0;
    private const int ReportIntervalSamples = 2048; // ~46ms at 44100Hz

    public WaveFormat WaveFormat => _waveFormat;

    public MultiTrackMixer(Song song, AppSettings settings)
    {
        _song = song;
        _settings = settings;
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(settings.SampleRate, 2); // Stereo output

        // Load all tracks
        foreach (var trackInfo in song.Tracks)
        {
            var filePath = Path.Combine(song.FolderPath, trackInfo.FileName);
            if (File.Exists(filePath))
            {
                var track = new TrackPlayer(filePath, _waveFormat, trackInfo);
                _tracks.Add(track);
            }
        }

        // Start in section 0 if sections are defined
        if (song.Sections.Count > 0)
        {
            _currentSectionIndex = 0;
            var firstSection = song.Sections[0];
            SeekToMs(firstSection.LoopInMs);
        }
    }

    // Micro-crossfade length for seamless loop splicing (3ms = ~132 samples at 44100Hz)
    private const double CrossfadeMs = 3.0;
    private int CrossfadeSamples => (int)(_waveFormat.SampleRate * CrossfadeMs / 1000.0);

    /// <summary>
    /// Read and mix audio from all tracks with SAMPLE-ACCURATE section boundaries.
    /// 
    /// Instead of reading the full buffer then checking if we passed a boundary (which
    /// causes overshoot/drift), this method:
    /// 1. Calculates exactly how many samples remain until the section boundary
    /// 2. If the boundary is within this buffer, splits the read at that exact sample
    /// 3. Seeks to the next position (loop start or next section)
    /// 4. Fills the remainder of the buffer from the new position
    /// 5. Applies a micro-crossfade at the splice point to prevent clicks
    /// 
    /// This guarantees zero drift regardless of buffer size or callback timing.
    /// </summary>
    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            int totalFloats = count / 4;

            // Reuse buffer to avoid GC pressure on audio thread
            if (_readBuffer == null || _readBuffer.Length < totalFloats)
                _readBuffer = new float[totalFloats];
            else
                Array.Clear(_readBuffer, 0, totalFloats);

            int floatsWritten = 0;
            int maxIterations = 4; // Safety: prevent infinite loop (max 4 splits per buffer)

            while (floatsWritten < totalFloats && maxIterations-- > 0)
            {
                int floatsRemaining = totalFloats - floatsWritten;

                // Calculate how many STEREO FRAMES we can read before hitting a boundary
                int framesToBoundary = GetFramesToBoundary();
                int floatsToBoundary = framesToBoundary * _waveFormat.Channels;

                // Determine how much to read this iteration
                int floatsToRead;
                bool willHitBoundary;

                if (floatsToBoundary > 0 && floatsToBoundary < floatsRemaining)
                {
                    // Boundary is within this buffer — read only up to it
                    floatsToRead = floatsToBoundary;
                    willHitBoundary = true;
                }
                else
                {
                    // No boundary in this buffer — read the full remainder
                    floatsToRead = floatsRemaining;
                    willHitBoundary = false;
                }

                // Read and mix from all tracks
                ReadAndMix(_readBuffer, floatsWritten, floatsToRead);
                floatsWritten += floatsToRead;

                // Update internal position
                int framesRead = floatsToRead / _waveFormat.Channels;
                _position += framesRead;

                if (willHitBoundary)
                {
                    // We've reached the exact boundary sample — handle the transition
                    HandleSectionBoundary(_readBuffer, floatsWritten, totalFloats - floatsWritten);
                }
            }

            // Clip protection
            for (int i = 0; i < totalFloats; i++)
            {
                _readBuffer[i] = Math.Clamp(_readBuffer[i], -1.0f, 1.0f);
            }

            // Convert to bytes
            Buffer.BlockCopy(_readBuffer, 0, buffer, offset, count);

            // Report position periodically
            int totalFrames = totalFloats / _waveFormat.Channels;
            _samplesUntilNextReport -= totalFrames;
            if (_samplesUntilNextReport <= 0)
            {
                _samplesUntilNextReport = ReportIntervalSamples;
                ReportPosition();
            }

            return count;
        }
    }

    /// <summary>
    /// Calculate how many stereo frames remain from current position to section boundary.
    /// Returns int.MaxValue if no boundary is relevant (no sections, or boundary is very far away).
    /// </summary>
    private int GetFramesToBoundary()
    {
        if (_song.Sections.Count == 0 || _currentSectionIndex < 0)
            return int.MaxValue;

        var currentSection = _song.Sections[_currentSectionIndex];

        // Use an INSTRUMENT track for position reference (not a vocal track which may be
        // seeked earlier due to pickup offset). Falls back to first track if all are vocal.
        double currentMs;
        if (_tracks.Count > 0)
        {
            currentMs = GetReferenceTrack().GetPositionMs();
        }
        else
        {
            currentMs = CurrentPositionMs;
        }

        double boundaryMs = currentSection.LoopOutMs;

        double remainingMs = boundaryMs - currentMs;
        if (remainingMs <= 0)
            return 0; // Already at or past boundary

        long remainingFrames = (long)(remainingMs * _waveFormat.SampleRate / 1000.0);
        return (int)Math.Min(remainingFrames, int.MaxValue);
    }

    /// <summary>
    /// Handle the section boundary: determine where to go next (loop, queue, or advance),
    /// seek there, and apply a micro-crossfade at the splice point.
    /// </summary>
    private void HandleSectionBoundary(float[] buffer, int spliceOffset, int remainingFloats)
    {
        if (_song.Sections.Count == 0 || _currentSectionIndex < 0)
            return;

        // Capture pre-splice audio for crossfade (last few ms before the boundary)
        int crossfadeFloats = CrossfadeSamples * _waveFormat.Channels;
        float[]? preSpliceAudio = null;

        if (crossfadeFloats > 0 && spliceOffset >= crossfadeFloats)
        {
            preSpliceAudio = new float[crossfadeFloats];
            Array.Copy(buffer, spliceOffset - crossfadeFloats, preSpliceAudio, 0, crossfadeFloats);
        }

        // Determine next action
        if (LoopEnabled && LoopInfinite)
        {
            // Infinite loop — seek back to section start with vocal pickup
            var currentSection = _song.Sections[_currentSectionIndex];
            SeekToMsWithVocalPickup(currentSection);
            _loopIterations++;
        }
        else if (_queuedSectionIndex >= 0)
        {
            // Queued section — jump to it
            int targetIndex = _queuedSectionIndex;
            _queuedSectionIndex = -1;
            LoopEnabled = false;
            LoopInfinite = false;
            JumpToSection(targetIndex);
        }
        else
        {
            // Advance to next section sequentially
            int nextIndex = _currentSectionIndex + 1;
            if (nextIndex < _song.Sections.Count)
            {
                JumpToSection(nextIndex);
            }
            else
            {
                // Song ended — don't read more
                _loopIterations = 0;
                return;
            }
        }

        // Apply micro-crossfade at the splice point
        // Read a few samples from the NEW position and blend with the pre-splice audio
        if (preSpliceAudio != null && crossfadeFloats > 0 && spliceOffset >= crossfadeFloats)
        {
            // Read crossfade-length of audio from the new position
            var postSpliceAudio = new float[crossfadeFloats];
            ReadAndMix(postSpliceAudio, 0, crossfadeFloats);

            // Apply equal-power crossfade: old fades out, new fades in
            for (int i = 0; i < crossfadeFloats; i++)
            {
                float t = (float)i / crossfadeFloats; // 0.0 → 1.0
                float fadeOut = (float)Math.Cos(t * Math.PI * 0.5); // 1.0 → 0.0
                float fadeIn = (float)Math.Sin(t * Math.PI * 0.5);  // 0.0 → 1.0

                int bufIdx = spliceOffset - crossfadeFloats + i;
                if (bufIdx >= 0 && bufIdx < buffer.Length)
                {
                    buffer[bufIdx] = preSpliceAudio[i] * fadeOut + postSpliceAudio[i] * fadeIn;
                }
            }

            // Update position for the crossfade samples we consumed
            int crossfadeFrames = crossfadeFloats / _waveFormat.Channels;
            _position += crossfadeFrames;
        }
    }

    /// <summary>
    /// Read from all tracks and mix into the output buffer at the specified offset.
    /// This is the core mixing routine extracted for reuse by the buffer-split logic.
    /// </summary>
    private void ReadAndMix(float[] outputBuffer, int outputOffset, int floatCount)
    {
        if (floatCount <= 0) return;

        // Determine solo state (avoid LINQ .Any() on hot audio path)
        bool anySolo = false;
        for (int t = 0; t < _tracks.Count; t++)
        {
            if (_tracks[t].IsSolo) { anySolo = true; break; }
        }

        // Reuse track buffer to avoid allocation per track per callback
        if (_trackReadBuffer == null || _trackReadBuffer.Length < floatCount)
            _trackReadBuffer = new float[floatCount];

        // Read from ALL tracks to keep them in sync
        foreach (var track in _tracks)
        {
            Array.Clear(_trackReadBuffer, 0, floatCount);

            // ALWAYS read from track to keep position in sync
            track.Read(_trackReadBuffer, 0, floatCount);

            // Only mix into output if track should be heard
            bool shouldPlay = anySolo ? track.IsSolo : !track.IsMuted;
            if (!shouldPlay) continue;

            // Apply volume and routing
            float volume = track.Volume;
            var outputMode = _settings.OutputMode;
            var trackOutput = track.TrackInfo.Output;

            for (int i = 0; i < floatCount; i += 2)
            {
                float sampleL = _trackReadBuffer[i] * volume;
                float sampleR = (i + 1 < floatCount) ? _trackReadBuffer[i + 1] * volume : sampleL;
                int outIdx = outputOffset + i;

                switch (outputMode)
                {
                    case OutputMode.PanSplit:
                        if (trackOutput == TrackOutput.FOH || trackOutput == TrackOutput.Both)
                            outputBuffer[outIdx] += sampleL;
                        if (trackOutput == TrackOutput.IEM || trackOutput == TrackOutput.Both)
                            outputBuffer[outIdx + 1] += sampleR;
                        break;

                    case OutputMode.Stereo:
                    default:
                        outputBuffer[outIdx] += sampleL;
                        if (outIdx + 1 < outputBuffer.Length)
                            outputBuffer[outIdx + 1] += sampleR;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Check if playback has reached a section boundary and handle looping.
    /// NOTE: This is now only used as a safety fallback — the primary boundary
    /// handling happens inside Read() via GetFramesToBoundary + HandleSectionBoundary.
    /// This catches edge cases where position drifts past boundary without triggering
    /// the in-Read split (e.g., after a seek or DSP latency).
    /// </summary>
    private void CheckSectionBoundary()
    {
        if (_song.Sections.Count == 0 || _currentSectionIndex < 0)
            return;

        var currentSection = _song.Sections[_currentSectionIndex];
        double currentMs;
        if (_tracks.Count > 0)
        {
            currentMs = GetReferenceTrack().GetPositionMs();
        }
        else
        {
            currentMs = CurrentPositionMs;
        }

        // Only trigger if we're significantly past the boundary (safety net)
        if (currentMs >= currentSection.LoopOutMs + 50) // 50ms grace period
        {
            if (LoopEnabled && LoopInfinite)
            {
                SeekToMsWithVocalPickup(currentSection);
                _loopIterations++;
            }
            else if (_queuedSectionIndex >= 0)
            {
                int targetIndex = _queuedSectionIndex;
                _queuedSectionIndex = -1;
                LoopEnabled = false;
                LoopInfinite = false;
                JumpToSection(targetIndex);
            }
            else
            {
                int nextIndex = _currentSectionIndex + 1;
                if (nextIndex < _song.Sections.Count)
                    JumpToSection(nextIndex);
            }
        }
    }

    /// <summary>
    /// Queue a section to jump to after the current section finishes playing.
    /// The queued section will play, then continue sequentially from there.
    /// </summary>
    public void QueueSection(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _song.Sections.Count)
            return;
        _queuedSectionIndex = sectionIndex;
    }

    /// <summary>
    /// Clear any queued section.
    /// </summary>
    public void ClearQueue()
    {
        _queuedSectionIndex = -1;
    }

    /// <summary>
    /// Jump to a specific section.
    /// </summary>
    public void JumpToSection(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _song.Sections.Count)
            return;

        int previousIndex = _currentSectionIndex;
        _currentSectionIndex = sectionIndex;
        _loopIterations = 0;

        var section = _song.Sections[sectionIndex];
        SeekToMsWithVocalPickup(section);

        SectionChanged?.Invoke(this, new SectionChangedEventArgs
        {
            PreviousSectionIndex = previousIndex,
            NewSectionIndex = sectionIndex,
            NewSection = section
        });
    }

    /// <summary>
    /// Seek all tracks to a specific millisecond position.
    /// ALL tracks are seeked atomically to maintain perfect sync.
    /// </summary>
    public void SeekToMs(double ms)
    {
        // Seek ALL tracks to the same position — this is critical for sync
        foreach (var track in _tracks)
        {
            track.SeekToMs(ms);
        }

        // Update internal position counter
        _position = MsToSamples(ms) * _waveFormat.Channels;
    }

    /// <summary>
    /// Seek tracks with vocal-aware pickup support.
    /// Vocal tracks (Lead Vocal, Backing Vocals) are seeked to an earlier position
    /// to include pickup notes before the section boundary.
    /// Instrument tracks are seeked to the section's exact LoopInMs.
    /// 
    /// This enables seamless loops where the vocal's pickup syllable is preserved
    /// (e.g., "Ama-" before "-zing Grace" on beat 1 of the section).
    /// </summary>
    private void SeekToMsWithVocalPickup(Section section)
    {
        double instrumentLoopIn = section.LoopInMs;
        double vocalLoopIn = section.VocalLoopInMs; // LoopInMs - VocalPickupMs

        if (section.VocalPickupMs <= 0 || vocalLoopIn >= instrumentLoopIn)
        {
            // No vocal pickup — seek all tracks to the same position
            SeekToMs(instrumentLoopIn);
            return;
        }

        // Per-track seeking: vocal tracks start earlier, instruments on the downbeat
        foreach (var track in _tracks)
        {
            if (IsVocalTrack(track))
            {
                track.SeekToMs(vocalLoopIn);
            }
            else
            {
                track.SeekToMs(instrumentLoopIn);
            }
        }

        // Internal position tracks the instrument position (the "true" section start)
        // The vocal pre-roll is playing ahead of the instruments
        _position = MsToSamples(instrumentLoopIn) * _waveFormat.Channels;
    }

    /// <summary>
    /// Determines if a track is a vocal track (Lead Vocal, Backing Vocals, BGV).
    /// These tracks get the pickup offset when looping.
    /// </summary>
    private static bool IsVocalTrack(TrackPlayer track)
    {
        var label = track.TrackInfo.Label;
        if (string.IsNullOrEmpty(label)) return false;

        return label.Contains("Vocal", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("BGV", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("Sing", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reset position to the beginning.
    /// </summary>
    public void ResetPosition()
    {
        _loopIterations = 0;
        if (_song.Sections.Count > 0)
        {
            _currentSectionIndex = 0;
            SeekToMs(_song.Sections[0].LoopInMs);
        }
        else
        {
            SeekToMs(0);
        }
    }

    /// <summary>
    /// Set tempo ratio (1.0 = original).
    /// </summary>
    public void SetTempo(double ratio)
    {
        _tempoRatio = Math.Clamp(ratio, 0.5, 2.0);
        foreach (var track in _tracks)
        {
            track.SetTempo(_tempoRatio);
        }
    }

    /// <summary>
    /// Set pitch shift in semitones.
    /// </summary>
    public void SetPitchShift(int semitones)
    {
        _pitchSemitones = Math.Clamp(semitones, -6, 6);
        foreach (var track in _tracks)
        {
            track.SetPitchShift(_pitchSemitones);
        }
    }

    public void SetTrackVolume(int index, float volume)
    {
        if (index >= 0 && index < _tracks.Count)
            _tracks[index].Volume = Math.Clamp(volume, 0f, 2f);
    }

    public void SetTrackMute(int index, bool mute)
    {
        if (index >= 0 && index < _tracks.Count)
            _tracks[index].IsMuted = mute;
    }

    public void SetTrackSolo(int index, bool solo)
    {
        if (index >= 0 && index < _tracks.Count)
            _tracks[index].IsSolo = solo;
    }

    private void ReportPosition()
    {
        double ms = CurrentPositionMs;
        int bar = _song.GetBarAtMs(ms);
        int beat = ((int)((ms - _song.GridAnchorMs) / _song.BeatIntervalMs) % _song.TimeSignatureNumerator) + 1;
        double sectionProgress = 0;

        if (_currentSectionIndex >= 0 && _currentSectionIndex < _song.Sections.Count)
        {
            var section = _song.Sections[_currentSectionIndex];
            if (section.DurationMs > 0)
                sectionProgress = (ms - section.LoopInMs) / section.DurationMs * 100;
        }

        PositionChanged?.Invoke(this, new PlaybackPositionEventArgs
        {
            PositionMs = ms,
            CurrentBar = bar,
            CurrentBeat = beat,
            CurrentSectionIndex = _currentSectionIndex,
            SectionProgressPercent = sectionProgress
        });
    }

    private long MsToSamples(double ms) => (long)(ms * _waveFormat.SampleRate / 1000.0);
    private double SamplesToMs(long samples) => samples * 1000.0 / _waveFormat.SampleRate;

    public void Dispose()
    {
        foreach (var track in _tracks)
            track.Dispose();
        _tracks.Clear();
    }
}
