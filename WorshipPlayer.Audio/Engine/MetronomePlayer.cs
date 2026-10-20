using NAudio.Wave;
using WorshipPlayer.Audio.DSP;

namespace WorshipPlayer.Audio.Engine;

/// <summary>
/// Self-contained looping metronome for the manual click-track live preview.
///
/// This player does NOT reuse the song mixer. It opens its own NAudio output device
/// (WaveOutEvent) and loops a single bar of click audio rendered from the SAME pulse
/// pattern as the generated WAV (<see cref="BeatDetectionService.ComputeMetronomePulses"/>),
/// so beat-1 is a LOUDER accent and 6/8 is felt-in-2 (two pulses per bar).
///
/// Safety contract (never throws to the caller):
///   • Start is idempotent — it stops any existing output first so starting twice does not stack players.
///   • Stop is safe when not started.
///   • If no audio output device is available, Start swallows the error and reports it via
///     <see cref="LastError"/> / <see cref="IsPlaying"/> == false instead of crashing.
///   • Update retunes the live loop (bpm/time-sig) in place (phase-preserving, no stacked players,
///     and without replaying an already-consumed count-in).
/// </summary>
public sealed class MetronomePlayer : IDisposable
{
    private readonly BeatDetectionService _beatService = new();
    private readonly object _lock = new();

    private WaveOutEvent? _output;
    private LoopingClickProvider? _provider;
    private bool _disposed;

    /// <summary>True while a looping output is actively playing.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Last error message (e.g. no audio device). Null when the last operation succeeded.</summary>
    public string? LastError { get; private set; }

    private const int SampleRate = 44100;

    /// <summary>
    /// Start (or restart) the looping metronome at the given tempo and time signature.
    /// Idempotent: any existing output is stopped first. Never throws; on failure
    /// <see cref="IsPlaying"/> stays false and <see cref="LastError"/> is set.
    /// </summary>
    public void Start(double bpm, int numerator, int denominator)
        => Start(bpm, numerator, denominator, 0);

    /// <summary>
    /// Start (or restart) the looping metronome, playing <paramref name="countInBars"/> bars of
    /// count-in clicks ONCE before the steady bar pattern begins looping. A pure looping bar cannot
    /// itself encode a one-time count-in, so the count-in is sequenced as a one-shot prefix buffer
    /// in front of the looping steady bar. Idempotent and never throws.
    /// </summary>
    public void Start(double bpm, int numerator, int denominator, int countInBars)
    {
        lock (_lock)
        {
            if (_disposed) return;

            // Stop any existing output so starting twice does not stack players.
            StopInternal();

            LastError = null;

            float[] loop = BuildLoopSamples(bpm, numerator, denominator);
            if (loop.Length == 0)
            {
                // Degenerate tempo — nothing to play, but this is not a crash.
                LastError = "Invalid tempo for metronome.";
                IsPlaying = false;
                return;
            }

            float[] countIn = BuildCountInSamples(bpm, numerator, denominator, countInBars);

            try
            {
                _provider = new LoopingClickProvider(loop, SampleRate, countIn);
                _output = new WaveOutEvent();
                _output.Init(_provider);
                _output.Play();
                IsPlaying = true;
            }
            catch (Exception ex)
            {
                // No audio device / device open failed — surface a friendly status, do not crash.
                LastError = ex.Message;
                StopInternal();
                IsPlaying = false;
            }
        }
    }

    /// <summary>
    /// Retune the live loop to a new tempo/time-signature. If the metronome is not playing,
    /// this is equivalent to <see cref="Start"/>. Never stacks players.
    /// </summary>
    public void Update(double bpm, int numerator, int denominator)
        => Update(bpm, numerator, denominator, 0);

    /// <summary>
    /// Retune the live loop (bpm/time-sig) and count-in IN PLACE without stacking players. This is a
    /// phase-preserving swap: the running bar keeps its read position (only clamped into the new
    /// buffer), so fine ±1ms Beat-1 nudges / BPM / meter changes during playback do not restart the
    /// bar. The one-shot count-in is re-armed only when it actually changed AND has not already been
    /// consumed — a count-in that already finished playing is never replayed by a live retune.
    /// If not playing, this is equivalent to <see cref="Start(double,int,int,int)"/>, which plays the
    /// count-in once from the top.
    /// </summary>
    public void Update(double bpm, int numerator, int denominator, int countInBars)
    {
        lock (_lock)
        {
            if (_disposed) return;

            if (!IsPlaying || _provider == null)
            {
                Start(bpm, numerator, denominator, countInBars);
                return;
            }

            float[] loop = BuildLoopSamples(bpm, numerator, denominator);
            if (loop.Length == 0)
            {
                LastError = "Invalid tempo for metronome.";
                return;
            }

            float[] countIn = BuildCountInSamples(bpm, numerator, denominator, countInBars);

            // Swap the loop buffer in place so the running WaveOutEvent keeps playing
            // the new pattern without opening a second device; re-arm the one-shot count-in.
            _provider.Retune(loop, countIn);
        }
    }

    /// <summary>
    /// Render the one-shot count-in prefix: <paramref name="countInBars"/> bars of NON-accent clicks
    /// at the grid interval, using the same per-meter pulse rule as the steady bar (6/8 felt-in-2 =>
    /// 2 pulses/bar). Returns an empty array when there is no count-in or the tempo is degenerate.
    /// </summary>
    private float[] BuildCountInSamples(double bpm, int numerator, int denominator, int countInBars)
    {
        if (countInBars <= 0 || bpm <= 0 || numerator <= 0)
            return Array.Empty<float>();

        double beatIntervalMs = 60000.0 / bpm;
        if (beatIntervalMs <= 0 || double.IsNaN(beatIntervalMs) || double.IsInfinity(beatIntervalMs))
            return Array.Empty<float>();

        double barDurationMs = (denominator == 8 && numerator == 6)
            ? 3.0 * beatIntervalMs
            : numerator * beatIntervalMs;
        if (barDurationMs <= 0)
            return Array.Empty<float>();

        double totalMs = barDurationMs * countInBars;

        // Derive count-in pulses from the SHARED pure method so preview == WAV. Placing Beat 1 at
        // totalMs makes the count-in occupy exactly [0, totalMs); we drop the body pulse at totalMs
        // because the steady loop plays it.
        var pulses = _beatService.ComputeManualClickPulses(totalMs, bpm, numerator, denominator, totalMs + 1, countInBars);

        int totalSamples = (int)Math.Round(totalMs / 1000.0 * SampleRate);
        if (totalSamples <= 0)
            return Array.Empty<float>();

        float[] buffer = new float[totalSamples];
        int clickLengthSamples = Math.Min(totalSamples, (int)(0.01 * SampleRate));
        float[] normal = SynthClick(1000, clickLengthSamples, 0.6f); // count-in clicks are non-accent

        foreach (var pulse in pulses)
        {
            if (pulse.TimeMs >= totalMs) continue; // skip the Beat-1 body pulse (played by the loop)
            int start = (int)Math.Round(pulse.TimeMs / 1000.0 * SampleRate);
            if (start < 0 || start >= totalSamples) continue;
            for (int i = 0; i < normal.Length && start + i < totalSamples; i++)
                buffer[start + i] = normal[i];
        }

        return buffer;
    }

    /// <summary>Stop the looping metronome. Safe to call when not started.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
            IsPlaying = false;
        }
    }

    private void StopInternal()
    {
        try
        {
            _output?.Stop();
        }
        catch { /* ignore device teardown errors */ }

        try
        {
            _output?.Dispose();
        }
        catch { /* ignore */ }

        _output = null;
        _provider = null;
    }

    /// <summary>
    /// Render exactly ONE bar of click audio from the shared pulse pattern so it loops seamlessly.
    /// Returns an empty array for degenerate tempos.
    /// </summary>
    private float[] BuildLoopSamples(double bpm, int numerator, int denominator)
    {
        if (bpm <= 0 || numerator <= 0)
            return Array.Empty<float>();

        double beatIntervalMs = 60000.0 / bpm;
        if (beatIntervalMs <= 0 || double.IsNaN(beatIntervalMs) || double.IsInfinity(beatIntervalMs))
            return Array.Empty<float>();

        // One-bar length (ms): 6/8 felt-in-2 bar = 3 * beat interval; others = numerator beats.
        double barDurationMs = (denominator == 8 && numerator == 6)
            ? 3.0 * beatIntervalMs
            : numerator * beatIntervalMs;

        if (barDurationMs <= 0)
            return Array.Empty<float>();

        // Pulse pattern for a single bar (anchor at 0). This is the SAME source of truth
        // used by the generated WAV so preview and file match.
        var pulses = _beatService.ComputeMetronomePulses(0, bpm, numerator, denominator, barDurationMs);
        if (pulses.Count == 0)
            return Array.Empty<float>();

        int totalSamples = (int)Math.Round(barDurationMs / 1000.0 * SampleRate);
        if (totalSamples <= 0)
            return Array.Empty<float>();

        float[] buffer = new float[totalSamples];

        int clickLengthSamples = Math.Min(totalSamples, (int)(0.01 * SampleRate)); // 10ms click
        float[] accent = SynthClick(1500, clickLengthSamples, 0.9f);  // LOUDER beat-1 accent
        float[] normal = SynthClick(1000, clickLengthSamples, 0.6f);  // other beats

        foreach (var pulse in pulses)
        {
            int start = (int)Math.Round(pulse.TimeMs / 1000.0 * SampleRate);
            if (start < 0 || start >= totalSamples) continue;
            float[] click = pulse.IsAccent ? accent : normal;
            for (int i = 0; i < click.Length && start + i < totalSamples; i++)
                buffer[start + i] = click[i];
        }

        return buffer;
    }

    /// <summary>Short sine burst with a quick fade so clicks don't pop. Matches the WAV click shape.</summary>
    private static float[] SynthClick(double frequency, int lengthSamples, float amplitude)
    {
        float[] click = new float[Math.Max(0, lengthSamples)];
        for (int i = 0; i < click.Length; i++)
        {
            double t = (double)i / SampleRate;
            double envelope = Math.Exp(-t * 300.0); // fast decay
            click[i] = (float)(amplitude * envelope * Math.Sin(2 * Math.PI * frequency * t));
        }
        return click;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            StopInternal();
            IsPlaying = false;
            _disposed = true;
        }
    }

    /// <summary>
    /// A seamless looping mono float sample provider. The loop buffer can be swapped at runtime
    /// (via <see cref="SetLoop"/>) so the metronome can be retuned without reopening the device.
    /// </summary>
    private sealed class LoopingClickProvider : ISampleProvider
    {
        private readonly object _bufLock = new();
        private float[] _loop;
        private float[] _countIn;   // one-shot prefix played ONCE before the loop
        private int _countInPos;    // read position within the one-shot prefix
        private int _position;      // read position within the looping bar

        public WaveFormat WaveFormat { get; }

        public LoopingClickProvider(float[] loop, int sampleRate, float[]? countIn = null)
        {
            _loop = loop.Length > 0 ? loop : new float[1];
            _countIn = countIn ?? Array.Empty<float>();
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        /// <summary>Swap the looping bar only (count-in already consumed). Keeps playing seamlessly.</summary>
        public void SetLoop(float[] loop)
        {
            lock (_bufLock)
            {
                _loop = loop.Length > 0 ? loop : new float[1];
                if (_position >= _loop.Length) _position = 0;
            }
        }

        /// <summary>
        /// Retune the live loop IN PLACE while the metronome keeps playing. This is a smooth,
        /// phase-preserving swap: the steady loop buffer is replaced and the read position is
        /// only clamped into the new buffer length (never forced back to 0), exactly like the
        /// pre-existing <see cref="SetLoop"/>. A Beat-1 nudge / BPM / meter change mid-playback
        /// therefore does NOT restart the bar from phase zero.
        ///
        /// The one-shot count-in is re-armed from the top ONLY when it ACTUALLY changed AND has
        /// not yet been consumed. Once the count-in has already finished playing (its read
        /// position reached the end), we never resurrect it — a retune during steady looping
        /// stays in the steady loop. This matches a real count-in: it happens once, before the
        /// groove, and is not replayed every time the operator fine-tunes Beat 1.
        /// </summary>
        public void Retune(float[] loop, float[] countIn)
        {
            lock (_bufLock)
            {
                // Phase-preserving loop swap (clamp, do not reset).
                _loop = loop.Length > 0 ? loop : new float[1];
                if (_position >= _loop.Length) _position = 0;

                float[] newCountIn = countIn ?? Array.Empty<float>();

                // Only touch the count-in if it is still in-flight (not yet fully consumed).
                // If it already finished (or there never was one), leave it consumed so a live
                // retune does not replay it.
                bool countInStillPlaying = _countInPos < _countIn.Length;
                if (countInStillPlaying && !SameSamples(_countIn, newCountIn))
                {
                    // The count-in genuinely changed while it is still playing — re-arm from the
                    // top so the audible count-in reflects the new tempo/meter/bars.
                    _countIn = newCountIn;
                    _countInPos = 0;
                }
                // Otherwise: keep the current count-in buffer and position untouched. If it was
                // already consumed, it stays consumed (no replay); if it is unchanged, it keeps
                // playing from where it is.
            }
        }

        private static bool SameSamples(float[] a, float[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_bufLock)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_countInPos < _countIn.Length)
                    {
                        // Play the one-shot count-in prefix exactly once.
                        buffer[offset + i] = _countIn[_countInPos];
                        _countInPos++;
                    }
                    else
                    {
                        buffer[offset + i] = _loop[_position];
                        _position++;
                        if (_position >= _loop.Length)
                            _position = 0;
                    }
                }
            }
            return count; // endless loop
        }
    }
}
