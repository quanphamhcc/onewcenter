using NAudio.Wave;
using SoundTouch;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Audio.DSP;

/// <summary>
/// Handles reading and DSP processing for a single audio track.
/// 
/// PRELOAD STRATEGY:
/// - Entire audio file is decoded and loaded into memory on construction
/// - Playback reads directly from the in-memory float array (zero disk I/O)
/// - Seeking is instant (just change the read position index)
/// - Supports MP3, WAV, FLAC, AIFF, OGG, and any format NAudio can decode
///
/// SYNC STRATEGY:
/// - When tempo=1.0 and pitch=0: bypass SoundTouch (zero latency, perfect sync)
/// - When DSP is active: SoundTouch processes the in-memory samples
/// - On seek: Clear SoundTouch buffer, reset position index
/// </summary>
public class TrackPlayer : IDisposable
{
    // In-memory audio data (entire track preloaded)
    private readonly float[] _audioData;
    private readonly int _sampleRate;
    private readonly int _fileChannels;
    private readonly int _totalSamples; // Total float samples (frames * channels)
    private int _readPosition; // Current read position in _audioData (in floats)

    private readonly SoundTouchProcessor _soundTouch;
    private readonly WaveFormat _targetFormat;
    private readonly float[] _processedBuffer;
    private readonly int _processBufferSize;

    public TrackInfo TrackInfo { get; }
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; } = false;
    public bool IsSolo { get; set; } = false;

    private double _tempoRatio = 1.0;
    private int _pitchSemitones = 0;
    private bool _dspActive = false;

    /// <summary>
    /// Total duration of the track in milliseconds.
    /// </summary>
    public double DurationMs { get; }

    public TrackPlayer(string filePath, WaveFormat targetFormat, TrackInfo trackInfo)
    {
        _targetFormat = targetFormat;
        TrackInfo = trackInfo;
        Volume = trackInfo.DefaultVolume;
        IsMuted = trackInfo.DefaultMute;

        // === PRELOAD: Decode entire file into memory ===
        using (var reader = new AudioFileReader(filePath))
        {
            _sampleRate = reader.WaveFormat.SampleRate;
            _fileChannels = reader.WaveFormat.Channels;
            DurationMs = reader.TotalTime.TotalMilliseconds;

            // Calculate total samples and allocate buffer
            long totalFloats = reader.Length / sizeof(float);
            _audioData = new float[totalFloats];

            // Read entire file into memory
            int offset = 0;
            int remaining = (int)totalFloats;
            while (remaining > 0)
            {
                int read = reader.Read(_audioData, offset, Math.Min(remaining, 65536));
                if (read == 0) break;
                offset += read;
                remaining -= read;
            }
            _totalSamples = offset;
        }
        // File handle is now closed — all data is in memory

        _readPosition = 0;

        // Initialize SoundTouch
        _soundTouch = new SoundTouchProcessor();
        _soundTouch.SampleRate = _sampleRate;
        _soundTouch.Channels = _fileChannels;
        _soundTouch.Tempo = 1.0;
        _soundTouch.Pitch = 1.0;

        // Quality settings for live performance
        _soundTouch.SetSetting(SettingId.UseQuickSeek, 0);
        _soundTouch.SetSetting(SettingId.UseAntiAliasFilter, 1);
        _soundTouch.SetSetting(SettingId.SequenceDurationMs, 40);
        _soundTouch.SetSetting(SettingId.SeekWindowDurationMs, 15);
        _soundTouch.SetSetting(SettingId.OverlapDurationMs, 8);

        _processBufferSize = 8192;
        _processedBuffer = new float[_processBufferSize * 4];
    }

    /// <summary>
    /// Read audio data from this track.
    /// Bypasses SoundTouch when no DSP is needed for perfect sync.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        if (!_dspActive)
        {
            return ReadDirect(buffer, offset, count);
        }
        else
        {
            return ReadWithDSP(buffer, offset, count);
        }
    }

    /// <summary>
    /// Direct read from memory — no DSP, guaranteed sample-accurate sync.
    /// </summary>
    private int ReadDirect(float[] buffer, int offset, int count)
    {
        int destChannels = _targetFormat.Channels;
        int written = 0;

        if (_fileChannels == destChannels)
        {
            // Same channel count — direct copy from memory
            int available = _totalSamples - _readPosition;
            int toCopy = Math.Min(count, available);

            if (toCopy > 0)
            {
                Array.Copy(_audioData, _readPosition, buffer, offset, toCopy);
                _readPosition += toCopy;
                written = toCopy;
            }

            // Fill remainder with silence
            if (written < count)
                Array.Clear(buffer, offset + written, count - written);
        }
        else if (_fileChannels == 1 && destChannels == 2)
        {
            // Mono to stereo
            int framesToRead = count / 2;
            int availableFrames = (_totalSamples - _readPosition);
            int frames = Math.Min(framesToRead, availableFrames);

            for (int i = 0; i < frames; i++)
            {
                float sample = _audioData[_readPosition + i];
                buffer[offset + i * 2] = sample;
                buffer[offset + i * 2 + 1] = sample;
            }
            _readPosition += frames;
            written = frames * 2;

            if (written < count)
                Array.Clear(buffer, offset + written, count - written);
        }
        else if (_fileChannels == 2 && destChannels == 1)
        {
            // Stereo to mono
            int framesToRead = count;
            int availableFrames = (_totalSamples - _readPosition) / 2;
            int frames = Math.Min(framesToRead, availableFrames);

            for (int i = 0; i < frames; i++)
            {
                buffer[offset + i] = (_audioData[_readPosition + i * 2] + _audioData[_readPosition + i * 2 + 1]) * 0.5f;
            }
            _readPosition += frames * 2;
            written = frames;

            if (written < count)
                Array.Clear(buffer, offset + written, count - written);
        }

        return count;
    }

    /// <summary>
    /// Read through SoundTouch DSP pipeline (for tempo/pitch changes).
    /// </summary>
    private int ReadWithDSP(float[] buffer, int offset, int count)
    {
        int totalWritten = 0;
        int remaining = count;
        int maxIterations = 200;

        while (remaining > 0 && maxIterations-- > 0)
        {
            // Try to get processed samples from SoundTouch
            int availableFrames = _soundTouch.AvailableSamples;

            if (availableFrames > 0)
            {
                int framesToGet = Math.Min(remaining / _targetFormat.Channels, availableFrames);
                if (framesToGet <= 0) break;

                int spanSize = framesToGet * _fileChannels;
                Span<float> outputSpan = _processedBuffer.AsSpan(0, spanSize);
                int received = _soundTouch.ReceiveSamples(outputSpan, framesToGet);

                if (received > 0)
                {
                    int outputSamples = CopyChannels(buffer, offset + totalWritten, _processedBuffer, received * _fileChannels);
                    totalWritten += outputSamples;
                    remaining -= outputSamples;
                }
            }
            else
            {
                // Feed more samples from memory to SoundTouch
                int available = _totalSamples - _readPosition;
                if (available <= 0)
                {
                    Array.Clear(buffer, offset + totalWritten, remaining);
                    totalWritten += remaining;
                    break;
                }

                int feedSize = Math.Min(available, _processBufferSize);
                // Align to frame boundary
                feedSize = (feedSize / _fileChannels) * _fileChannels;
                int feedFrames = feedSize / _fileChannels;

                ReadOnlySpan<float> inputSpan = _audioData.AsSpan(_readPosition, feedSize);
                _soundTouch.PutSamples(inputSpan, feedFrames);
                _readPosition += feedSize;
            }
        }

        // Fill any remaining with silence
        if (remaining > 0)
        {
            Array.Clear(buffer, offset + totalWritten, remaining);
            totalWritten += remaining;
        }

        return totalWritten;
    }

    /// <summary>
    /// Copy samples with channel conversion. Returns number of output samples written.
    /// </summary>
    private int CopyChannels(float[] dest, int destOffset, float[] src, int srcCount)
    {
        int destChannels = _targetFormat.Channels;

        if (_fileChannels == destChannels)
        {
            int toCopy = Math.Min(srcCount, dest.Length - destOffset);
            Array.Copy(src, 0, dest, destOffset, toCopy);
            return toCopy;
        }
        else if (_fileChannels == 1 && destChannels == 2)
        {
            int frames = srcCount;
            int outputCount = 0;
            for (int i = 0; i < frames && destOffset + outputCount + 1 < dest.Length; i++)
            {
                dest[destOffset + outputCount] = src[i];
                dest[destOffset + outputCount + 1] = src[i];
                outputCount += 2;
            }
            return outputCount;
        }
        else if (_fileChannels == 2 && destChannels == 1)
        {
            int frames = srcCount / 2;
            int outputCount = 0;
            for (int i = 0; i < frames && destOffset + outputCount < dest.Length; i++)
            {
                dest[destOffset + outputCount] = (src[i * 2] + src[i * 2 + 1]) * 0.5f;
                outputCount++;
            }
            return outputCount;
        }

        return 0;
    }

    /// <summary>
    /// Seek to a specific position in milliseconds. Instant — just changes the array index.
    /// </summary>
    public void SeekToMs(double ms)
    {
        // Clear SoundTouch pipeline
        _soundTouch.Clear();

        // Calculate sample position
        long frame = (long)(ms / 1000.0 * _sampleRate);
        long samplePos = frame * _fileChannels;

        // Clamp and align to frame boundary
        samplePos = Math.Clamp(samplePos, 0, _totalSamples);
        samplePos = (samplePos / _fileChannels) * _fileChannels;

        _readPosition = (int)samplePos;
    }

    /// <summary>
    /// Set the tempo ratio (1.0 = normal).
    /// </summary>
    public void SetTempo(double ratio)
    {
        _tempoRatio = Math.Clamp(ratio, 0.5, 2.0);
        _soundTouch.Tempo = _tempoRatio;
        UpdateDspState();
    }

    /// <summary>
    /// Set pitch shift in semitones.
    /// </summary>
    public void SetPitchShift(int semitones)
    {
        _pitchSemitones = Math.Clamp(semitones, -12, 12);
        _soundTouch.PitchSemiTones = _pitchSemitones;
        UpdateDspState();
    }

    /// <summary>
    /// Determine if DSP is needed or if we can bypass SoundTouch.
    /// </summary>
    private void UpdateDspState()
    {
        bool needsDsp = Math.Abs(_tempoRatio - 1.0) > 0.001 || _pitchSemitones != 0;

        if (needsDsp != _dspActive)
        {
            _dspActive = needsDsp;
            if (_dspActive)
            {
                _soundTouch.Clear();
            }
        }
    }

    /// <summary>
    /// Get the current position in milliseconds.
    /// </summary>
    public double GetPositionMs()
    {
        long frame = _readPosition / _fileChannels;
        return frame * 1000.0 / _sampleRate;
    }

    /// <summary>
    /// Get the total duration in milliseconds.
    /// </summary>
    public double GetDurationMs()
    {
        return DurationMs;
    }

    public void Dispose()
    {
        _soundTouch.Clear();
        // _audioData is managed memory — GC will handle it
    }
}
