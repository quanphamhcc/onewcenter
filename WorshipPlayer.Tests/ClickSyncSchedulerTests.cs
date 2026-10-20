using WorshipPlayer.Audio.DSP;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the PURE Play-with-Click alignment math (FEAT-003): ClickSyncScheduler.
///
/// Approach A plays the song from 0 through the shared AudioEngine and runs the free-running
/// MetronomePlayer on top. ClickSyncScheduler computes WHEN the metronome's first click (count-in
/// start, or Beat 1 when there is no count-in) must land on the song clock so the count-in and
/// Beat 1 align to the song exactly where BeatDetectionService.ComputeManualClickPulses schedules
/// them. These tests assert that alignment and the count-in clamp, and cross-check against the
/// pure pulse schedule so the two sources cannot diverge. All synthetic — no audio device.
/// </summary>
public class ClickSyncSchedulerTests
{
    private const double Tol = 1e-6;

    // ── Bar length honors meter (incl. 6/8 felt-in-2 == 3 quarter beats) ─────────────
    [Theory]
    [InlineData(120, 4, 4, 2000)]   // 4 * 500
    [InlineData(120, 3, 4, 1500)]   // 3 * 500
    [InlineData(120, 6, 8, 1500)]   // felt in 2 => 3 * 500
    [InlineData(90, 4, 4, 2666.6667)]
    public void BarDurationMs_HonorsMeter(double bpm, int num, int den, double expected)
    {
        double bar = ClickSyncScheduler.BarDurationMs(bpm, num, den);
        Assert.Equal(expected, bar, 3);
    }

    [Theory]
    [InlineData(0, 4, 4)]
    [InlineData(-5, 4, 4)]
    [InlineData(120, 0, 4)]
    public void BarDurationMs_Degenerate_ReturnsZero(double bpm, int num, int den)
    {
        Assert.Equal(0, ClickSyncScheduler.BarDurationMs(bpm, num, den), 6);
    }

    // ── No count-in: first click IS Beat 1, arm at Beat 1, nothing before it ─────────
    [Fact]
    public void NoCountIn_FirstClickIsBeat1()
    {
        double beat1 = 2340;
        var a = ClickSyncScheduler.ComputeAlignment(beat1, 120, 4, 4, 0);

        Assert.Equal(0, a.CountInDurationMs, Tol);
        Assert.Equal(beat1, a.ArmAtSongMs, Tol);
        Assert.Equal(beat1, a.FirstClickSongMs, Tol);
        Assert.Equal(beat1, a.Beat1SongMs, Tol);
    }

    // ── Count-in precedes Beat 1 by exactly countInBars whole bars when room exists ──
    [Theory]
    [InlineData(4, 4, 1)]
    [InlineData(4, 4, 2)]
    [InlineData(3, 4, 2)]
    [InlineData(6, 8, 2)]
    public void CountIn_PrecedesBeat1_ByWholeBars_WhenRoom(int num, int den, int bars)
    {
        double bpm = 120;
        double bar = ClickSyncScheduler.BarDurationMs(bpm, num, den);
        double beat1 = 100000; // far from 0 so nothing clamps
        var a = ClickSyncScheduler.ComputeAlignment(beat1, bpm, num, den, bars);

        Assert.Equal(bars * bar, a.CountInDurationMs, 6);
        Assert.Equal(beat1 - bars * bar, a.ArmAtSongMs, 6);
        Assert.Equal(a.ArmAtSongMs, a.FirstClickSongMs, Tol);
        Assert.Equal(beat1, a.Beat1SongMs, Tol);
        // The first click precedes Beat 1 exactly when there is a count-in.
        Assert.True(a.FirstClickSongMs < a.Beat1SongMs);
    }

    // ── Count-in clamps so it never starts before song position 0 ───────────────────
    [Fact]
    public void CountIn_Clamps_NeverStartsBeforeZero()
    {
        double bpm = 120; // bar = 2000ms for 4/4
        double beat1 = 1200; // one 4/4 bar (2000ms) would start at -800ms
        var a = ClickSyncScheduler.ComputeAlignment(beat1, bpm, 4, 4, 1);

        // Clamp => count-in duration is only beat1 (1200), arm exactly at 0.
        Assert.Equal(1200, a.CountInDurationMs, 6);
        Assert.Equal(0, a.ArmAtSongMs, 6);
        Assert.Equal(0, a.FirstClickSongMs, 6);
        Assert.True(a.ArmAtSongMs >= 0, "arm time must never be negative");
    }

    [Fact]
    public void Beat1AtZero_ArmsAtZero_NoCountInRoom()
    {
        var a = ClickSyncScheduler.ComputeAlignment(0, 120, 4, 4, 2);
        Assert.Equal(0, a.CountInDurationMs, Tol);
        Assert.Equal(0, a.ArmAtSongMs, Tol);
        Assert.Equal(0, a.Beat1SongMs, Tol);
    }

    [Fact]
    public void NegativeBeat1_TreatedAsZero()
    {
        var a = ClickSyncScheduler.ComputeAlignment(-500, 120, 4, 4, 1);
        Assert.Equal(0, a.Beat1SongMs, Tol);
        Assert.Equal(0, a.ArmAtSongMs, Tol);
    }

    [Fact]
    public void NegativeCountInBars_ClampedToZero()
    {
        var a = ClickSyncScheduler.ComputeAlignment(3000, 120, 4, 4, -3);
        Assert.Equal(0, a.CountInDurationMs, Tol);
        Assert.Equal(3000, a.ArmAtSongMs, Tol);
    }

    // ── Degenerate tempo: arm at Beat 1, zero bar, never throws ──────────────────────
    [Fact]
    public void DegenerateTempo_ArmsAtBeat1_ZeroBar()
    {
        var a = ClickSyncScheduler.ComputeAlignment(1500, 0, 4, 4, 2);
        Assert.Equal(0, a.BarDurationMs, Tol);
        Assert.Equal(0, a.CountInDurationMs, Tol);
        Assert.Equal(1500, a.ArmAtSongMs, Tol);
        Assert.Equal(1500, a.Beat1SongMs, Tol);
    }

    // ── Delay/arm helpers: song starts at 0 => arm immediately for Beat1/count-in >= 0 ─
    [Fact]
    public void DelayUntilFirstClick_IsArmTimeMinusPosition()
    {
        var a = ClickSyncScheduler.ComputeAlignment(2000, 120, 4, 4, 0);
        // At song start (0), the first click (Beat 1 @ 2000) is 2000ms away.
        Assert.Equal(2000, ClickSyncScheduler.DelayUntilFirstClickMs(0, a), Tol);
        // Once the song clock reaches the arm time, the delay is zero/negative => arm now.
        Assert.Equal(0, ClickSyncScheduler.DelayUntilFirstClickMs(2000, a), Tol);
        Assert.True(ClickSyncScheduler.DelayUntilFirstClickMs(2500, a) < 0);
    }

    [Fact]
    public void ShouldArmNow_TrueWhenSongReachesFirstClick()
    {
        var a = ClickSyncScheduler.ComputeAlignment(2000, 120, 4, 4, 1); // count-in start at 0
        Assert.True(ClickSyncScheduler.ShouldArmNow(0, a), "count-in starts at 0, arm immediately");

        var b = ClickSyncScheduler.ComputeAlignment(2000, 120, 4, 4, 0); // first click at Beat 1 = 2000
        Assert.False(ClickSyncScheduler.ShouldArmNow(1999, b));
        Assert.True(ClickSyncScheduler.ShouldArmNow(2000, b));
    }

    // ── CROSS-CHECK: alignment agrees with the pure pulse schedule (same source of truth) ─
    // The count-in duration and Beat 1 position must match where ComputeManualClickPulses places
    // the first count-in pulse and the Beat-1 accent, so Play-with-Click == Generated_Click_Manual.wav.
    [Theory]
    [InlineData(4, 4, 1)]
    [InlineData(4, 4, 2)]
    [InlineData(3, 4, 2)]
    [InlineData(6, 8, 2)]
    public void Alignment_AgreesWith_ComputeManualClickPulses(int num, int den, int bars)
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beat1 = 20000; // plenty of room
        var a = ClickSyncScheduler.ComputeAlignment(beat1, bpm, num, den, bars);

        var pulses = service.ComputeManualClickPulses(beat1, bpm, num, den, beat1 + 5000, bars);

        // The Beat-1 accent is the first pulse at/after beat1 and must equal Beat1SongMs.
        var body = pulses.First(p => p.TimeMs >= beat1);
        Assert.Equal(a.Beat1SongMs, body.TimeMs, 6);

        // The earliest count-in pulse must equal the arm time (first audible click).
        var countIn = pulses.Where(p => p.TimeMs < beat1).OrderBy(p => p.TimeMs).ToList();
        Assert.NotEmpty(countIn);
        Assert.Equal(a.ArmAtSongMs, countIn[0].TimeMs, 6);
        // And the count-in window length matches the schedule's span before Beat 1.
        Assert.Equal(a.CountInDurationMs, beat1 - countIn[0].TimeMs, 6);
    }

    // Clamped case must also agree with the pulse schedule's dropped-negative-pulses behavior.
    [Fact]
    public void Alignment_Clamped_AgreesWith_PulseSchedule_4_4()
    {
        var service = new BeatDetectionService();
        double bpm = 120;
        double beat1 = 1200; // forces the 4/4 count-in to clamp
        var a = ClickSyncScheduler.ComputeAlignment(beat1, bpm, 4, 4, 1);

        var pulses = service.ComputeManualClickPulses(beat1, bpm, 4, 4, 10000, 1);
        var countIn = pulses.Where(p => p.TimeMs < beat1).OrderBy(p => p.TimeMs).ToList();

        // When the count-in clamps against song start, the scheduler's arm time is the clamped
        // window start (0 here), and every surviving count-in pulse falls within the count-in
        // window [ArmAtSongMs, Beat1SongMs). The arm time is never negative.
        Assert.Equal(0, a.ArmAtSongMs, 6);
        Assert.True(a.ArmAtSongMs >= 0);
        Assert.All(countIn, p =>
        {
            Assert.True(p.TimeMs >= a.ArmAtSongMs, "count-in pulse not before the arm time");
            Assert.True(p.TimeMs < a.Beat1SongMs, "count-in pulse precedes Beat 1");
        });
        // The clamped count-in window spans exactly Beat 1 (the whole-bar count-in was truncated).
        Assert.Equal(beat1, a.CountInDurationMs, 6);
    }
}
