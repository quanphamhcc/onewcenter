using WorshipPlayer.Audio.DSP;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the PURE manual-click Beat 1 offset + count-in schedule (FEAT-002):
/// BeatDetectionService.ComputeManualClickPulses / BuildManualClickPositions overloads and the
/// GenerateManualClickTrack WAV schedule. All synthetic — no reliance on WAV files except the
/// existing read-back pattern. This schedule is the single source of truth shared by the live
/// metronome preview and the generated WAV, so these cover 4/4, 3/4 and 6/8-felt-in-2 with
/// Beat 1 at 0 / mid-bar / near song end and count-in 0/1/2.
/// </summary>
public class ManualClickBeat1CountInTests
{
    private static int PulsesPerBar(int num, int den) => (den == 8 && num == 6) ? 2 : num;

    // ── (a) First non-count-in pulse lands EXACTLY at Beat 1 for 4/4, 3/4, 6/8 ───────
    [Theory]
    [InlineData(4, 4)]
    [InlineData(3, 4)]
    [InlineData(6, 8)]
    public void FirstBodyPulse_LandsExactlyAtBeat1_NoCountIn(int num, int den)
    {
        var service = new BeatDetectionService();
        double beat1 = 2340; // mid-bar pickup
        var pulses = service.ComputeManualClickPulses(beat1, 120, num, den, 20000, 0);

        Assert.NotEmpty(pulses);
        Assert.Equal(beat1, pulses[0].TimeMs, 6);
        Assert.True(pulses[0].IsAccent, "the first body pulse at Beat 1 must be the accent");
    }

    // ── (a2) Mid-bar pickup 2340ms: following beats continue at the grid interval ────
    [Fact]
    public void MidBarPickup_FollowingBeats_ContinueAtGridInterval_4_4()
    {
        var service = new BeatDetectionService();
        double beat1 = 2340;
        double bpm = 120;
        double beatMs = 60000.0 / bpm; // 500ms
        var pulses = service.ComputeManualClickPulses(beat1, bpm, 4, 4, 10000, 0);

        Assert.Equal(beat1, pulses[0].TimeMs, 6);
        Assert.Equal(beat1 + beatMs, pulses[1].TimeMs, 6);
        Assert.Equal(beat1 + 2 * beatMs, pulses[2].TimeMs, 6);
        Assert.Equal(beat1 + 3 * beatMs, pulses[3].TimeMs, 6);
        // accent every 4th starting at beat 1
        Assert.True(pulses[0].IsAccent);
        Assert.False(pulses[1].IsAccent);
        Assert.False(pulses[2].IsAccent);
        Assert.False(pulses[3].IsAccent);
        Assert.True(pulses[4].IsAccent);
    }

    // ── (b) Count-in produces exactly N*pulsesPerBar pulses immediately before Beat 1 ─
    [Theory]
    [InlineData(4, 4, 1)]
    [InlineData(4, 4, 2)]
    [InlineData(3, 4, 1)]
    [InlineData(3, 4, 2)]
    [InlineData(6, 8, 1)]
    [InlineData(6, 8, 2)]
    public void CountIn_ProducesExactly_N_PulsesPerBar_BeforeBeat1(int num, int den, int bars)
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beat1 = 100000; // far from 0 so nothing clamps
        var pulses = service.ComputeManualClickPulses(beat1, bpm, num, den, beat1 + 10000, bars);

        int expectedCountIn = bars * PulsesPerBar(num, den);

        // Count-in pulses are the ones strictly before Beat 1 and non-accent.
        var countIn = pulses.Where(p => p.TimeMs < beat1).ToList();
        Assert.Equal(expectedCountIn, countIn.Count);
        Assert.All(countIn, p => Assert.False(p.IsAccent, "count-in pulses must be non-accent"));

        // The first pulse at/after Beat 1 must be exactly Beat 1 and the accent.
        var body = pulses.First(p => p.TimeMs >= beat1);
        Assert.Equal(beat1, body.TimeMs, 6);
        Assert.True(body.IsAccent);
    }

    // ── (b2) Count-in spacing is correct, including the 6/8-in-2 case (2 pulses/bar) ──
    [Fact]
    public void CountIn_6_8_UsesTwoPulsesPerBar_AtCorrectSpacing()
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beatMs = 60000.0 / bpm; // quarter = 500ms
        double eighthMs = beatMs / 2.0; // 250ms
        double barMs = 6 * eighthMs;    // 1500ms
        double beat1 = 10000;

        var pulses = service.ComputeManualClickPulses(beat1, bpm, 6, 8, beat1 + 5000, 2);
        var countIn = pulses.Where(p => p.TimeMs < beat1).OrderBy(p => p.TimeMs).ToList();

        Assert.Equal(4, countIn.Count); // 2 bars * 2 pulses
        // Two bars before beat1: bar -2 start, +3*eighth, bar -1 start, +3*eighth.
        Assert.Equal(beat1 - 2 * barMs, countIn[0].TimeMs, 6);
        Assert.Equal(beat1 - 2 * barMs + 3 * eighthMs, countIn[1].TimeMs, 6);
        Assert.Equal(beat1 - 1 * barMs, countIn[2].TimeMs, 6);
        Assert.Equal(beat1 - 1 * barMs + 3 * eighthMs, countIn[3].TimeMs, 6);
    }

    // ── (b3) 4/4 count-in spacing: evenly spaced quarter-note clicks ending one beat before Beat 1 ─
    [Fact]
    public void CountIn_4_4_OneBar_FourEvenlySpacedClicks()
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beatMs = 60000.0 / bpm; // 500ms
        double beat1 = 4000;

        var pulses = service.ComputeManualClickPulses(beat1, bpm, 4, 4, beat1 + 4000, 1);
        var countIn = pulses.Where(p => p.TimeMs < beat1).OrderBy(p => p.TimeMs).ToList();

        Assert.Equal(4, countIn.Count);
        // One bar before beat1 => clicks at beat1 - 4*beatMs .. beat1 - 1*beatMs.
        for (int i = 0; i < 4; i++)
            Assert.Equal(beat1 - (4 - i) * beatMs, countIn[i].TimeMs, 6);
    }

    // ── (c) Beat 1 is the louder accent AND is the first post-count-in pulse ─────────
    [Fact]
    public void Beat1_IsAccent_AndFirstAfterCountIn()
    {
        var service = new BeatDetectionService();
        double beat1 = 5000;
        var pulses = service.ComputeManualClickPulses(beat1, 100, 4, 4, beat1 + 8000, 1);

        int firstBodyIndex = pulses.FindIndex(p => p.TimeMs >= beat1);
        Assert.True(firstBodyIndex >= 0);
        Assert.Equal(beat1, pulses[firstBodyIndex].TimeMs, 6);
        Assert.True(pulses[firstBodyIndex].IsAccent);
        // Everything before it is count-in and must precede Beat 1.
        for (int i = 0; i < firstBodyIndex; i++)
            Assert.True(pulses[i].TimeMs < beat1);
    }

    // ── (d) Count-in clamps to >= 0: Beat 1 near 0 emits only whole beats that fit ───
    [Fact]
    public void CountIn_ClampsToNonNegative_WhenBeat1NearZero_4_4()
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beatMs = 60000.0 / bpm; // 500ms
        // Beat 1 at 1200ms: one bar of 4/4 count-in would want clicks at -800, -300, 200, 700.
        // Only 200 and 700 are >= 0 so exactly 2 count-in pulses survive.
        double beat1 = 1200;
        var pulses = service.ComputeManualClickPulses(beat1, bpm, 4, 4, 10000, 1);

        var countIn = pulses.Where(p => p.TimeMs < beat1).OrderBy(p => p.TimeMs).ToList();
        Assert.Equal(2, countIn.Count);
        Assert.Equal(200, countIn[0].TimeMs, 6);
        Assert.Equal(700, countIn[1].TimeMs, 6);
        Assert.All(pulses, p => Assert.True(p.TimeMs >= 0, "no pulse may be negative"));
    }

    [Fact]
    public void CountIn_NeverNegative_AllMeters()
    {
        var service = new BeatDetectionService();
        foreach (var (num, den) in new[] { (4, 4), (3, 4), (6, 8) })
        {
            var pulses = service.ComputeManualClickPulses(300, 120, num, den, 10000, 2);
            Assert.All(pulses, p => Assert.True(p.TimeMs >= 0));
        }
    }

    // ── (e) Beat 1 at 0 ms: no count-in pulses can fit (all would be negative) ───────
    [Fact]
    public void Beat1AtZero_NoCountInFits_FirstPulseIsAccentAtZero()
    {
        var service = new BeatDetectionService();
        var pulses = service.ComputeManualClickPulses(0, 120, 4, 4, 10000, 2);

        Assert.All(pulses, p => Assert.True(p.TimeMs >= 0));
        // No pulse strictly before 0 => no count-in.
        Assert.DoesNotContain(pulses, p => p.TimeMs < 0);
        Assert.Equal(0, pulses[0].TimeMs, 6);
        Assert.True(pulses[0].IsAccent);
    }

    // ── (e2) Beat 1 near song end still places Beat 1 exactly and stays in range ─────
    [Fact]
    public void Beat1NearSongEnd_PlacesBeat1Exactly_WithinRange()
    {
        var service = new BeatDetectionService();
        double duration = 10000;
        double beat1 = 9900;
        var pulses = service.ComputeManualClickPulses(beat1, 120, 4, 4, duration, 1);

        var body = pulses.First(p => p.TimeMs >= beat1);
        Assert.Equal(beat1, body.TimeMs, 6);
        Assert.All(pulses, p => Assert.True(p.TimeMs < duration, "pulses stay within the half-open range"));
    }

    // ── (f) count-in 0/1/2 across 3/4, 4/4, 6/8: count equals N*pulsesPerBar ─────────
    [Theory]
    [InlineData(4, 4, 0)]
    [InlineData(4, 4, 1)]
    [InlineData(4, 4, 2)]
    [InlineData(3, 4, 0)]
    [InlineData(3, 4, 1)]
    [InlineData(3, 4, 2)]
    [InlineData(6, 8, 0)]
    [InlineData(6, 8, 1)]
    [InlineData(6, 8, 2)]
    public void CountIn_Count_MatchesBarsTimesPulsesPerBar_WhenRoomToFit(int num, int den, int bars)
    {
        var service = new BeatDetectionService();
        double beat1 = 100000; // plenty of room before Beat 1
        var pulses = service.ComputeManualClickPulses(beat1, 120, num, den, beat1 + 5000, bars);
        int countInCount = pulses.Count(p => p.TimeMs < beat1);
        Assert.Equal(bars * PulsesPerBar(num, den), countInCount);
    }

    // ── (g) BuildManualClickPositions matches the pure pulse times exactly (WAV == preview) ──
    [Theory]
    [InlineData(4, 4, 0)]
    [InlineData(4, 4, 2)]
    [InlineData(3, 4, 1)]
    [InlineData(6, 8, 2)]
    public void BuildManualClickPositions_WithBeat1AndCountIn_MatchesPulseTimes(int num, int den, int bars)
    {
        var service = new BeatDetectionService();
        double beat1 = 2340;
        var grid = service.BuildGridFromManualAnchor(0, 120, num, den, 20000);

        var pulses = service.ComputeManualClickPulses(beat1, grid.Bpm,
            grid.TimeSignatureNumerator, grid.TimeSignatureDenominator, grid.TotalDurationMs, bars);
        var positions = service.BuildManualClickPositions(grid, beat1, bars);

        Assert.Equal(pulses.Count, positions.Count);
        for (int i = 0; i < pulses.Count; i++)
            Assert.Equal(pulses[i].TimeMs, positions[i], 6);
    }

    // ── (g2) Default BuildManualClickPositions (beat1 < 0) falls back to GridAnchorMs ─
    [Fact]
    public void BuildManualClickPositions_DefaultBeat1_FallsBackToGridAnchor()
    {
        var service = new BeatDetectionService();
        var grid = service.BuildGridFromManualAnchor(500, 120, 4, 4, 10000);

        var defaultPositions = service.BuildManualClickPositions(grid);           // beat1 = -1 => anchor
        var explicitPositions = service.BuildManualClickPositions(grid, 500, 0);  // beat1 = anchor

        Assert.Equal(explicitPositions, defaultPositions);
        Assert.Equal(500, defaultPositions[0], 6);
    }

    // ── (g3) The generated WAV schedule == the pure pulse schedule (same method) ─────
    // Proves the WAV and preview cannot diverge: the WAV routes through ComputeManualClickPulses.
    // We reconstruct the expected click sample positions from the pure schedule and verify each
    // has audio in the rendered WAV, and that the beat-1 accent is louder than a count-in click.
    [Fact]
    public void GeneratedWav_Schedule_MatchesPurePulses_AndAccentLouderThanCountIn()
    {
        var service = new BeatDetectionService();
        const int sampleRate = 44100;
        double bpm = 120;
        int countInBars = 1;
        double beat1 = 4000; // room for a full 4/4 count-in bar before it
        var grid = service.BuildGridFromManualAnchor(0, bpm, 4, 4, 9000);

        var pulses = service.ComputeManualClickPulses(beat1, bpm, 4, 4, grid.TotalDurationMs, countInBars);
        Assert.Contains(pulses, p => p.TimeMs < beat1); // count-in present

        string path = Path.Combine(Path.GetTempPath(), $"owc_beat1_countin_{Guid.NewGuid():N}.wav");
        try
        {
            service.GenerateManualClickTrack(grid, path, sampleRate, beat1, countInBars);
            Assert.True(File.Exists(path));

            float[] samples;
            using (var reader = new NAudio.Wave.AudioFileReader(path))
            {
                Assert.Equal(1, reader.WaveFormat.Channels);
                var buffer = new List<float>();
                var chunk = new float[4096];
                int read;
                while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
                    for (int i = 0; i < read; i++)
                        buffer.Add(chunk[i]);
                samples = buffer.ToArray();
            }

            int windowSamples = (int)(0.015 * sampleRate);

            // Every scheduled pulse must have audio at its sample position in the WAV.
            foreach (var pulse in pulses)
            {
                int start = (int)(pulse.TimeMs / 1000.0 * sampleRate);
                float peak = PeakAbs(samples, start, windowSamples);
                Assert.True(peak > 0f, $"expected a click at {pulse.TimeMs}ms");
            }

            // Beat-1 accent louder than a count-in click.
            var countInPulse = pulses.First(p => p.TimeMs < beat1);
            float accentPeak = PeakAbs(samples, (int)(beat1 / 1000.0 * sampleRate), windowSamples);
            float countInPeak = PeakAbs(samples, (int)(countInPulse.TimeMs / 1000.0 * sampleRate), windowSamples);
            Assert.True(accentPeak > countInPeak,
                $"beat-1 accent {accentPeak} must exceed count-in click {countInPeak}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── (g4) Generated WAV loudness (FEAT-002): accent peak is full-scale (~1.0) and ─
    // normal clicks are ~0.8, with the beat-1 accent strictly louder than normal clicks.
    // Reads back the rendered 16-bit WAV (file I/O only, no audio device) and inspects peaks.
    [Fact]
    public void GeneratedWav_AccentPeakIsFullScale_NormalIsLouder_NoClipping()
    {
        var service = new BeatDetectionService();
        const int sampleRate = 44100;
        double bpm = 120;
        double beat1 = 0; // Beat 1 at the very start = accent at sample 0
        var grid = service.BuildGridFromManualAnchor(0, bpm, 4, 4, 5000);

        var pulses = service.ComputeManualClickPulses(beat1, bpm, 4, 4, grid.TotalDurationMs, 0);
        // First pulse is the beat-1 accent; the next is a normal (non-accent) click.
        Assert.True(pulses[0].IsAccent);
        var normalPulse = pulses.First(p => !p.IsAccent);

        string path = Path.Combine(Path.GetTempPath(), $"owc_loudness_{Guid.NewGuid():N}.wav");
        try
        {
            service.GenerateManualClickTrack(grid, path, sampleRate, beat1, 0);
            Assert.True(File.Exists(path));

            float[] samples;
            using (var reader = new NAudio.Wave.AudioFileReader(path))
            {
                var buffer = new List<float>();
                var chunk = new float[4096];
                int read;
                while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
                    for (int i = 0; i < read; i++)
                        buffer.Add(chunk[i]);
                samples = buffer.ToArray();
            }

            int windowSamples = (int)(0.045 * sampleRate); // whole ~40ms click

            float accentPeak = PeakAbs(samples, (int)(beat1 / 1000.0 * sampleRate), windowSamples);
            float normalPeak = PeakAbs(samples, (int)(normalPulse.TimeMs / 1000.0 * sampleRate), windowSamples);

            // LOUDNESS (user request): the manual click WAV is made as loud as possible to be heard
            // over the song. The accent is synthesized at full-scale amplitude 1.0; the longer,
            // slower-decay burst lets a sample reach very near full scale. It must NOT exceed 1.0.
            Assert.True(accentPeak >= 0.95f && accentPeak <= 1.0f,
                $"accent peak {accentPeak} should be at/near full scale (1.0 amplitude, no clipping)");
            // The normal click is now synthesized at amplitude 0.95 (loud) and must still be at or
            // below the accent (never louder than beat 1).
            Assert.True(normalPeak >= 0.85f && normalPeak <= accentPeak,
                $"normal peak {normalPeak} should be ~0.95-scale (loud) and not exceed accent {accentPeak}");
            // No sample anywhere exceeds full scale (no clipping introduced).
            Assert.True(PeakAbs(samples, 0, samples.Length) <= 1.0f, "no sample may exceed 1.0");
            // Beat-1 accent is at least as loud as normal clicks (1.0 >= 0.95).
            Assert.True(accentPeak >= normalPeak, $"accent {accentPeak} must be >= normal {normalPeak}");
            // The accent/normal peak ratio reflects the 1.0/0.95 amplitude design (close to 1.0).
            float ratio = accentPeak / normalPeak;
            Assert.True(ratio >= 1.0f && ratio < 1.15f,
                $"accent/normal peak ratio {ratio} should reflect the 1.0/0.95 loud design");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── (h) Degenerate inputs return empty and write a valid silent WAV, no throw ────
    [Fact]
    public void Degenerate_Inputs_ReturnEmpty_NoThrow()
    {
        var service = new BeatDetectionService();
        Assert.Empty(service.ComputeManualClickPulses(1000, 0, 4, 4, 10000, 1));   // bpm <= 0
        Assert.Empty(service.ComputeManualClickPulses(1000, 120, 0, 4, 10000, 1)); // numerator <= 0
        Assert.Empty(service.ComputeManualClickPulses(1000, 120, 4, 4, 0, 1));     // duration <= 0
        Assert.Empty(service.ComputeManualClickPulses(1000, -1, 4, 4, 10000, 1));  // negative bpm
    }

    [Fact]
    public void GenerateManualClickTrack_WithBeat1AndCountIn_DegenerateGrid_WritesSilentWav_NoThrow()
    {
        var service = new BeatDetectionService();
        var grid = service.BuildGridFromManualAnchor(0, 120, 4, 4, 0); // zero duration
        string path = Path.Combine(Path.GetTempPath(), $"owc_beat1_degenerate_{Guid.NewGuid():N}.wav");
        try
        {
            service.GenerateManualClickTrack(grid, path, 44100, 2340, 2);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── Body pulses identical to the no-offset pattern shifted to Beat 1 (3/4) ───────
    [Fact]
    public void BodyPulses_3_4_AccentEveryThree_StartingAtBeat1()
    {
        var service = new BeatDetectionService();
        double bpm = 90;
        double beatMs = 60000.0 / bpm;
        double beat1 = 1500;
        var pulses = service.ComputeManualClickPulses(beat1, bpm, 3, 4, beat1 + 6 * beatMs - 1, 0);

        Assert.Equal(6, pulses.Count);
        for (int i = 0; i < pulses.Count; i++)
        {
            Assert.Equal(beat1 + i * beatMs, pulses[i].TimeMs, 3);
            Assert.Equal(i % 3 == 0, pulses[i].IsAccent);
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
