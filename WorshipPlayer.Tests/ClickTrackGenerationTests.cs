using WorshipPlayer.Audio.DSP;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the PURE click-track beat-position logic (A/B/C modes) in BeatDetectionService.
/// These exercise ComputeBeatPositions / DetectAdaptivePeaks on synthetic in-memory buffers;
/// no WAV files are written. The silent-gap regression test (ModeA_QuietSection...) would FAIL
/// against the old global-threshold logic that blinded quiet sections.
/// </summary>
public class ClickTrackGenerationTests
{
    private const int SampleRate = 44100;

    private static BeatDetectionService.BeatGrid MakeGrid(double bpm = 120, double anchorMs = 0, double durationMs = 20000)
    {
        double interval = 60000.0 / bpm;
        return new BeatDetectionService.BeatGrid
        {
            Bpm = bpm,
            BeatIntervalMs = interval,
            BarDurationMs = interval * 4,
            GridAnchorMs = anchorMs,
            TimeSignatureNumerator = 4,
            TotalDurationMs = durationMs
        };
    }

    private static double MaxGap(List<double> positions)
    {
        double max = 0;
        for (int i = 1; i < positions.Count; i++)
            max = Math.Max(max, positions[i] - positions[i - 1]);
        return max;
    }

    /// <summary>
    /// Build a synthetic mono buffer with a short loud burst (a "click") at every grid beat
    /// inside [loudStartMs, loudEndMs); everything outside that window is silent.
    /// </summary>
    private static float[] BuildClickBuffer(BeatDetectionService.BeatGrid grid, double loudStartMs, double loudEndMs)
    {
        int totalSamples = (int)(grid.TotalDurationMs / 1000.0 * SampleRate);
        var buffer = new float[totalSamples];
        int burstLen = (int)(0.008 * SampleRate); // 8ms burst

        double t = grid.GridAnchorMs;
        while (t < grid.TotalDurationMs)
        {
            if (t >= loudStartMs && t < loudEndMs)
            {
                int start = (int)(t / 1000.0 * SampleRate);
                for (int j = 0; j < burstLen && start + j < totalSamples; j++)
                {
                    double tt = (double)j / SampleRate;
                    buffer[start + j] = (float)(Math.Sin(2 * Math.PI * 1200 * tt) * 0.9);
                }
            }
            t += grid.BeatIntervalMs;
        }
        return buffer;
    }

    // (1) Mode C produces beats at exact grid spacing covering the full duration.
    [Fact]
    public void ModeC_PureGrid_CoversFullDuration_NoGaps()
    {
        var grid = MakeGrid(bpm: 120, anchorMs: 0, durationMs: 20000);
        var service = new BeatDetectionService();

        var positions = service.ComputeBeatPositions(grid, BeatDetectionService.ClickGenerationMode.PureGrid, null, SampleRate);

        Assert.NotEmpty(positions);
        // First beat at/near start, last beat near the end.
        Assert.True(positions[0] <= grid.BeatIntervalMs + 1e-6);
        Assert.True(positions[^1] >= grid.TotalDurationMs - grid.BeatIntervalMs - 1e-6);
        // No gap larger than the beat interval.
        Assert.True(MaxGap(positions) <= grid.BeatIntervalMs + 1e-6);
    }

    // (2) Silent-gap regression: Mode A with strong peaks only in a loud region still returns a
    // beat for every grid position across the full duration.
    [Fact]
    public void ModeA_QuietSection_StillEmitsEveryBeat_NoSilentGap()
    {
        var grid = MakeGrid(bpm: 120, anchorMs: 0, durationMs: 24000);
        var service = new BeatDetectionService();

        // Clicks present only in the middle third; first and last thirds are silent.
        var buffer = BuildClickBuffer(grid, loudStartMs: 8000, loudEndMs: 16000);

        var positions = service.ComputeBeatPositions(grid, BeatDetectionService.ClickGenerationMode.AdaptiveSync, buffer, SampleRate);

        Assert.NotEmpty(positions);
        // Coverage spans from start to the end of the song.
        Assert.True(positions[0] <= grid.BeatIntervalMs + 1e-6,
            $"first beat {positions[0]} should be near start");
        Assert.True(positions[^1] >= grid.TotalDurationMs - 2 * grid.BeatIntervalMs,
            $"last beat {positions[^1]} should be near end {grid.TotalDurationMs}");
        // No silent gap: max gap must be <= ~1.5x the beat interval across the whole duration,
        // including the quiet first and last thirds where the old global-threshold path was silent.
        Assert.True(MaxGap(positions) <= grid.BeatIntervalMs * 1.5 + 1e-6,
            $"max gap {MaxGap(positions)} exceeds 1.5x beat interval {grid.BeatIntervalMs}");
    }

    // (3) Mode A with fewer than the minimum usable peaks falls back to a full grid.
    [Fact]
    public void ModeA_TooFewPeaks_FallsBackToFullGrid()
    {
        var grid = MakeGrid(bpm: 120, anchorMs: 0, durationMs: 20000);
        var service = new BeatDetectionService();

        // A nearly-silent buffer: no detectable peaks.
        var buffer = new float[(int)(grid.TotalDurationMs / 1000.0 * SampleRate)];

        var positions = service.ComputeBeatPositions(grid, BeatDetectionService.ClickGenerationMode.AdaptiveSync, buffer, SampleRate);

        Assert.NotEmpty(positions);
        // Full grid fallback => no gaps larger than a single beat interval, full coverage.
        Assert.True(MaxGap(positions) <= grid.BeatIntervalMs + 1e-6);
        Assert.True(positions[^1] >= grid.TotalDurationMs - grid.BeatIntervalMs - 1e-6);
    }

    // (4) Mode B shifts anchor/phase from a detected first peak but still emits every beat.
    [Fact]
    public void ModeB_LocksPhaseToFirstPeak_StillEmitsEveryBeat()
    {
        // Grid anchored at 0, but the real click is offset by ~37ms (phase shift).
        var grid = MakeGrid(bpm: 120, anchorMs: 0, durationMs: 20000);
        var service = new BeatDetectionService();

        double offsetMs = 37;
        // Build a buffer with strong peaks at offsetMs + n*interval across the whole song.
        int totalSamples = (int)(grid.TotalDurationMs / 1000.0 * SampleRate);
        var buffer = new float[totalSamples];
        int burstLen = (int)(0.008 * SampleRate);
        for (double t = offsetMs; t < grid.TotalDurationMs; t += grid.BeatIntervalMs)
        {
            int start = (int)(t / 1000.0 * SampleRate);
            for (int j = 0; j < burstLen && start + j < totalSamples; j++)
            {
                double tt = (double)j / SampleRate;
                buffer[start + j] = (float)(Math.Sin(2 * Math.PI * 1200 * tt) * 0.9);
            }
        }

        var positions = service.ComputeBeatPositions(grid, BeatDetectionService.ClickGenerationMode.HybridAnchor, buffer, SampleRate);

        Assert.NotEmpty(positions);
        // Every beat present (grid spacing, no gaps).
        Assert.True(MaxGap(positions) <= grid.BeatIntervalMs + 1e-6);
        // Phase locked to the detected peak: the first beat's phase (mod interval) should be
        // close to the offset, not 0.
        double phase = positions[0] % grid.BeatIntervalMs;
        Assert.True(Math.Abs(phase - offsetMs) < 10 || Math.Abs(phase - offsetMs) > grid.BeatIntervalMs - 10,
            $"expected phase ~{offsetMs}ms, got {phase}ms");
    }

    // (5) Degenerate inputs do not throw.
    [Fact]
    public void Degenerate_Inputs_DoNotThrow()
    {
        var service = new BeatDetectionService();

        // Zero beat interval.
        var badGrid = new BeatDetectionService.BeatGrid
        {
            Bpm = 0,
            BeatIntervalMs = 0,
            TotalDurationMs = 10000
        };
        var r1 = service.ComputeBeatPositions(badGrid, BeatDetectionService.ClickGenerationMode.PureGrid, null, SampleRate);
        Assert.NotNull(r1);

        // Empty samples with a valid grid (Mode A must fall back, not throw).
        var grid = MakeGrid();
        var r2 = service.ComputeBeatPositions(grid, BeatDetectionService.ClickGenerationMode.AdaptiveSync, Array.Empty<float>(), SampleRate);
        Assert.NotNull(r2);
        Assert.NotEmpty(r2); // fell back to full grid

        // Null existing mono for Mode B.
        var r3 = service.ComputeBeatPositions(grid, BeatDetectionService.ClickGenerationMode.HybridAnchor, null, SampleRate);
        Assert.NotNull(r3);
        Assert.NotEmpty(r3);

        // Zero duration.
        var zeroDur = MakeGrid(durationMs: 0);
        var r4 = service.ComputeBeatPositions(zeroDur, BeatDetectionService.ClickGenerationMode.PureGrid, null, SampleRate);
        Assert.NotNull(r4);
    }
}
