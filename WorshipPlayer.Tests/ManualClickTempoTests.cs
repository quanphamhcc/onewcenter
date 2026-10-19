using WorshipPlayer.Audio.DSP;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the PURE manual-tempo pulse pattern in BeatDetectionService:
/// ComputeMetronomePulses / BuildManualClickPositions / the 5-arg BuildGridFromManualAnchor
/// overload. These exercise tempo/time-signature math on synthetic inputs; no WAV files are
/// written. The pulse pattern is the single source of truth shared by the live metronome preview
/// (FEAT-002) and the generated WAV, so these cover 3/4, 4/4 and 6/8-felt-in-2 accent rules.
/// </summary>
public class ManualClickTempoTests
{
    private const double Tol = 1e-6;

    // ── (a) 4/4 at 120 BPM: 4 pulses/bar, accent only on beat 1 ──────────────
    [Fact]
    public void FourFour_120Bpm_FourPulsesPerBar_AccentOnlyBeatOne()
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beatMs = 60000.0 / bpm; // 500ms
        // Exactly two full bars: 2 * 4 * 500 = 4000ms (half-open, so stop just before 4000).
        var pulses = service.ComputeMetronomePulses(0, bpm, 4, 4, 4000 - 1);

        Assert.Equal(8, pulses.Count); // 4 per bar * 2 bars
        // Accent on every 4th pulse starting at index 0.
        for (int i = 0; i < pulses.Count; i++)
        {
            Assert.Equal(i * beatMs, pulses[i].TimeMs, 3);
            Assert.Equal(i % 4 == 0, pulses[i].IsAccent);
        }
    }

    // ── (b) 3/4: 3 pulses/bar, accent only on beat 1 ─────────────────────────
    [Fact]
    public void ThreeFour_ThreePulsesPerBar_AccentOnlyBeatOne()
    {
        var service = new BeatDetectionService();
        double bpm = 90;
        double beatMs = 60000.0 / bpm;
        // Two full 3/4 bars = 6 beats.
        var pulses = service.ComputeMetronomePulses(0, bpm, 3, 4, 6 * beatMs - 1);

        Assert.Equal(6, pulses.Count);
        for (int i = 0; i < pulses.Count; i++)
        {
            Assert.Equal(i * beatMs, pulses[i].TimeMs, 3);
            Assert.Equal(i % 3 == 0, pulses[i].IsAccent);
        }
    }

    // ── (c) 6/8 felt in 2: EXACTLY 2 pulses/bar at barStart and barStart+3*eighthMs ──
    [Fact]
    public void SixEight_FeltInTwo_TwoPulsesPerBar_AccentOnlyFirst()
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beatMs = 60000.0 / bpm;   // quarter note = 500ms
        double eighthMs = beatMs / 2.0;  // 250ms
        double barMs = 6 * eighthMs;     // 1500ms

        // Exactly two bars.
        var pulses = service.ComputeMetronomePulses(0, bpm, 6, 8, 2 * barMs - 1);

        Assert.Equal(4, pulses.Count); // 2 pulses * 2 bars

        // Bar 0.
        Assert.Equal(0, pulses[0].TimeMs, 3);
        Assert.True(pulses[0].IsAccent);
        Assert.Equal(3 * eighthMs, pulses[1].TimeMs, 3);
        Assert.False(pulses[1].IsAccent);

        // Bar 1.
        Assert.Equal(barMs, pulses[2].TimeMs, 3);
        Assert.True(pulses[2].IsAccent);
        Assert.Equal(barMs + 3 * eighthMs, pulses[3].TimeMs, 3);
        Assert.False(pulses[3].IsAccent);
    }

    // ── (d) Pulses start at GridAnchorMs; last pulse within one bar of total duration ──
    [Fact]
    public void Pulses_StartAtAnchor_And_CoverFullDuration()
    {
        var service = new BeatDetectionService();
        double bpm = 100;
        double beatMs = 60000.0 / bpm; // 600ms
        double anchor = 1234.5;
        double duration = anchor + 20000;

        var pulses = service.ComputeMetronomePulses(anchor, bpm, 4, 4, duration);

        Assert.NotEmpty(pulses);
        Assert.Equal(anchor, pulses[0].TimeMs, 3);
        // Last pulse must be within one bar (4 beats) of the total duration (full coverage).
        double barMs = 4 * beatMs;
        Assert.True(pulses[^1].TimeMs >= duration - barMs - Tol,
            $"last pulse {pulses[^1].TimeMs} not within one bar of {duration}");
        Assert.True(pulses[^1].TimeMs < duration, "pulses must stay within the half-open range");
    }

    // ── (e) Accent pulses marked true, others false (via BuildManualClickPositions parity) ──
    [Fact]
    public void BuildManualClickPositions_MatchesPulseTimes_Exactly()
    {
        var service = new BeatDetectionService();
        var grid = service.BuildGridFromManualAnchor(500, 120, 6, 8, 10000);

        var pulses = service.ComputeMetronomePulses(grid.GridAnchorMs, grid.Bpm,
            grid.TimeSignatureNumerator, grid.TimeSignatureDenominator, grid.TotalDurationMs);
        var positions = service.BuildManualClickPositions(grid);

        // Same pattern → identical count and times (WAV must match the live preview exactly).
        Assert.Equal(pulses.Count, positions.Count);
        for (int i = 0; i < pulses.Count; i++)
            Assert.Equal(pulses[i].TimeMs, positions[i], 6);

        // Accent relationship: the first pulse of each 6/8 bar is the accent.
        Assert.True(pulses[0].IsAccent);
        Assert.False(pulses[1].IsAccent);
    }

    // ── (f) Slow (60 BPM) and fast (180 BPM) produce correct interval spacing ────────
    [Theory]
    [InlineData(60, 4, 4)]
    [InlineData(180, 4, 4)]
    [InlineData(60, 3, 4)]
    [InlineData(180, 3, 4)]
    public void SlowAndFast_SimpleMeters_HaveCorrectBeatSpacing(double bpm, int num, int den)
    {
        var service = new BeatDetectionService();
        double beatMs = 60000.0 / bpm;
        var pulses = service.ComputeMetronomePulses(0, bpm, num, den, 30000);

        Assert.True(pulses.Count > 1);
        for (int i = 1; i < pulses.Count; i++)
            Assert.Equal(beatMs, pulses[i].TimeMs - pulses[i - 1].TimeMs, 3);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(180)]
    public void SlowAndFast_SixEight_HaveCorrectDottedQuarterSpacing(double bpm)
    {
        var service = new BeatDetectionService();
        double eighthMs = (60000.0 / bpm) / 2.0;
        double barMs = 6 * eighthMs;
        var pulses = service.ComputeMetronomePulses(0, bpm, 6, 8, 30000);

        Assert.True(pulses.Count >= 4);
        // Within a bar: pulse2 - pulse1 = 3*eighthMs; across the bar boundary: pulse3 - pulse2 = 3*eighthMs too.
        for (int i = 1; i < pulses.Count; i++)
            Assert.Equal(3 * eighthMs, pulses[i].TimeMs - pulses[i - 1].TimeMs, 3);
        // Bar-to-bar accent spacing is a full bar.
        Assert.Equal(barMs, pulses[2].TimeMs - pulses[0].TimeMs, 3);
    }

    // ── (g) Degenerate inputs return empty and do not throw ──────────────────────────
    [Fact]
    public void Degenerate_Inputs_ReturnEmpty_NoThrow()
    {
        var service = new BeatDetectionService();

        Assert.Empty(service.ComputeMetronomePulses(0, 0, 4, 4, 10000));    // bpm <= 0
        Assert.Empty(service.ComputeMetronomePulses(0, -120, 4, 4, 10000)); // negative bpm
        Assert.Empty(service.ComputeMetronomePulses(0, 120, 0, 4, 10000));  // numerator <= 0
        Assert.Empty(service.ComputeMetronomePulses(0, 120, -4, 4, 10000)); // negative numerator
        Assert.Empty(service.ComputeMetronomePulses(0, 120, 4, 4, 0));      // duration <= 0
        Assert.Empty(service.ComputeMetronomePulses(0, 120, 4, 4, -5));     // negative duration

        // BuildManualClickPositions on a degenerate grid also returns empty without throwing.
        var badGrid = service.BuildGridFromManualAnchor(0, 0, 4, 4, 10000);
        Assert.Empty(service.BuildManualClickPositions(badGrid));
    }

    // ── (h) 5-arg overload sets denominator + consistent bar duration; 4-arg stays 4/4 ──
    [Fact]
    public void FiveArgOverload_SetsDenominator_And_ConsistentBarDuration()
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beatMs = 60000.0 / bpm; // 500ms

        var fourFour = service.BuildGridFromManualAnchor(0, bpm, 4, 4, 60000);
        Assert.Equal(4, fourFour.TimeSignatureNumerator);
        Assert.Equal(4, fourFour.TimeSignatureDenominator);
        Assert.Equal(4 * beatMs, fourFour.BarDurationMs, 3);

        var threeFour = service.BuildGridFromManualAnchor(0, bpm, 3, 4, 60000);
        Assert.Equal(4, threeFour.TimeSignatureDenominator);
        Assert.Equal(3 * beatMs, threeFour.BarDurationMs, 3);

        // 6/8 felt in 2: bar = 6 eighth notes = 3 * beatIntervalMs.
        var sixEight = service.BuildGridFromManualAnchor(0, bpm, 6, 8, 60000);
        Assert.Equal(6, sixEight.TimeSignatureNumerator);
        Assert.Equal(8, sixEight.TimeSignatureDenominator);
        Assert.Equal(3 * beatMs, sixEight.BarDurationMs, 3);
    }

    [Fact]
    public void FourArgOverload_DefaultsToFourFour()
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beatMs = 60000.0 / bpm;

        var grid = service.BuildGridFromManualAnchor(0, bpm, 4, 60000);
        Assert.Equal(4, grid.TimeSignatureNumerator);
        Assert.Equal(4, grid.TimeSignatureDenominator); // default 4/4 preserved
        Assert.Equal(4 * beatMs, grid.BarDurationMs, 3);
    }

    // ── Accent click synthesized louder than normal click (WAV generate path) ────────
    [Fact]
    public void GenerateManualClickTrack_DegenerateGrid_WritesSilentWav_NoThrow()
    {
        var service = new BeatDetectionService();
        var grid = service.BuildGridFromManualAnchor(0, 120, 4, 4, 0); // zero duration
        string path = Path.Combine(Path.GetTempPath(), $"owc_manual_click_{Guid.NewGuid():N}.wav");
        try
        {
            // Must not throw; writes a valid (empty/silent) WAV.
            service.GenerateManualClickTrack(grid, path);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void GenerateManualClickTrack_WritesWav_WithPulses_NoThrow()
    {
        var service = new BeatDetectionService();
        var grid = service.BuildGridFromManualAnchor(0, 120, 4, 4, 4000);
        string path = Path.Combine(Path.GetTempPath(), $"owc_manual_click_{Guid.NewGuid():N}.wav");
        try
        {
            service.GenerateManualClickTrack(grid, path);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 44, "WAV should contain more than just a header");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── Beat-1 ACCENT is actually LOUDER in the rendered WAV, not just flagged ───────
    // Reads the generated WAV back with NAudio and asserts the peak amplitude in the beat-1
    // accent region exceeds the peak amplitude in a normal (non-accent) click region. Guards
    // against someone reverting the accent amplitude (0.9) to the normal level (0.6): the pure
    // pulse tests would stay green, but this one would fail.
    [Fact]
    public void GenerateManualClickTrack_Beat1AccentIsLouderThanNormalClick_InWav()
    {
        var service = new BeatDetectionService();
        const int sampleRate = 44100;
        double bpm = 120;              // beat interval = 500ms
        double beatMs = 60000.0 / bpm;

        // 4/4 at anchor 0: pulse[0] @ 0ms is the beat-1 accent; pulse[1] @ 500ms is a normal click.
        var grid = service.BuildGridFromManualAnchor(0, bpm, 4, 4, 4000);
        string path = Path.Combine(Path.GetTempPath(), $"owc_manual_accent_{Guid.NewGuid():N}.wav");

        try
        {
            service.GenerateManualClickTrack(grid, path, sampleRate);
            Assert.True(File.Exists(path));

            // Read the whole file back as float samples (NAudio converts 16-bit PCM -> float).
            float[] samples;
            using (var reader = new NAudio.Wave.AudioFileReader(path))
            {
                Assert.Equal(1, reader.WaveFormat.Channels); // mono
                var buffer = new List<float>();
                var chunk = new float[4096];
                int read;
                while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
                    for (int i = 0; i < read; i++)
                        buffer.Add(chunk[i]);
                samples = buffer.ToArray();
            }

            // Each synthesized click is 10ms; scan a generous 15ms window around each pulse start
            // so we capture the whole burst regardless of exact sample alignment.
            int windowSamples = (int)(0.015 * sampleRate);

            int accentStart = (int)(0.0 / 1000.0 * sampleRate);     // pulse[0] @ 0ms (accent)
            int normalStart = (int)(beatMs / 1000.0 * sampleRate);  // pulse[1] @ 500ms (normal)

            float accentPeak = PeakAbs(samples, accentStart, windowSamples);
            float normalPeak = PeakAbs(samples, normalStart, windowSamples);

            // Both regions must actually contain audio...
            Assert.True(accentPeak > 0f, "accent region should contain a click");
            Assert.True(normalPeak > 0f, "normal region should contain a click");
            // ...and the accent must be meaningfully louder than the normal click.
            Assert.True(accentPeak > normalPeak,
                $"accent peak {accentPeak} must exceed normal peak {normalPeak}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static float PeakAbs(float[] samples, int start, int count)
    {
        float peak = 0f;
        int end = Math.Min(samples.Length, start + count);
        for (int i = Math.Max(0, start); i < end; i++)
        {
            float a = Math.Abs(samples[i]);
            if (a > peak) peak = a;
        }
        return peak;
    }
}
