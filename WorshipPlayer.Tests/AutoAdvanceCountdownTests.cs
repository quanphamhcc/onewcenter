using Xunit;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the pure auto-advance countdown math (đợt 2 T5 / R3.4).
/// AutoAdvanceCountdown is exercised directly with explicit delays, elapsed times
/// and timestamps — no timers, engine, or UI — mirroring the resolver tests.
/// The engine's default-delay rule (non-positive → 5000 ms) must match the
/// countdown's, so both are pinned here.
/// </summary>
public class AutoAdvanceCountdownTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    // ─── EffectiveDelayMs ──────────────────────────────────────────────

    [Fact]
    public void EffectiveDelayMs_PositiveDelay_UsedAsConfigured()
    {
        Assert.Equal(10000, AutoAdvanceCountdown.EffectiveDelayMs(10000));
        Assert.Equal(1, AutoAdvanceCountdown.EffectiveDelayMs(1));
    }

    [Fact]
    public void EffectiveDelayMs_ZeroDelay_FallsBackToEngineDefault5000()
    {
        Assert.Equal(5000, AutoAdvanceCountdown.EffectiveDelayMs(0));
        Assert.Equal(AutoAdvanceCountdown.DefaultDelayMs, AutoAdvanceCountdown.EffectiveDelayMs(0));
    }

    [Fact]
    public void EffectiveDelayMs_NegativeDelay_FallsBackToEngineDefault5000()
    {
        Assert.Equal(5000, AutoAdvanceCountdown.EffectiveDelayMs(-250));
    }

    // ─── RemainingMs (delay + elapsed) ─────────────────────────────────

    [Fact]
    public void RemainingMs_NoTimeElapsed_FullDelayRemains()
    {
        Assert.Equal(10000, AutoAdvanceCountdown.RemainingMs(10000, 0));
    }

    [Fact]
    public void RemainingMs_DecreasesExactlyWithElapsedTime()
    {
        Assert.Equal(7000, AutoAdvanceCountdown.RemainingMs(10000, 3000));
        Assert.Equal(1, AutoAdvanceCountdown.RemainingMs(10000, 9999));
    }

    [Fact]
    public void RemainingMs_ExactlyAtDelay_IsZero()
    {
        Assert.Equal(0, AutoAdvanceCountdown.RemainingMs(10000, 10000));
    }

    [Fact]
    public void RemainingMs_PastDelay_ClampedToZeroNeverNegative()
    {
        Assert.Equal(0, AutoAdvanceCountdown.RemainingMs(10000, 10001));
        Assert.Equal(0, AutoAdvanceCountdown.RemainingMs(10000, 999999));
    }

    [Fact]
    public void RemainingMs_NonPositiveDelay_IsZero()
    {
        Assert.Equal(0, AutoAdvanceCountdown.RemainingMs(0, 0));
        Assert.Equal(0, AutoAdvanceCountdown.RemainingMs(-500, 0));
    }

    // ─── RemainingMs (timestamps) ──────────────────────────────────────

    [Fact]
    public void RemainingMs_Timestamps_CountsDownFromStart()
    {
        Assert.Equal(10000, AutoAdvanceCountdown.RemainingMs(10000, T0, T0));
        Assert.Equal(7000, AutoAdvanceCountdown.RemainingMs(10000, T0, T0.AddSeconds(3)));
        Assert.Equal(0, AutoAdvanceCountdown.RemainingMs(10000, T0, T0.AddSeconds(10)));
        Assert.Equal(0, AutoAdvanceCountdown.RemainingMs(10000, T0, T0.AddSeconds(25)));
    }

    [Fact]
    public void RemainingMs_Timestamps_NonPositiveConfiguredDelay_UsesDefault5000()
    {
        Assert.Equal(5000, AutoAdvanceCountdown.RemainingMs(0, T0, T0));
        Assert.Equal(4000, AutoAdvanceCountdown.RemainingMs(0, T0, T0.AddSeconds(1)));
        Assert.Equal(5000, AutoAdvanceCountdown.RemainingMs(-1, T0, T0));
    }

    [Fact]
    public void RemainingMs_Timestamps_ClockMovedBackwards_TreatedAsJustStarted()
    {
        // A backwards clock must not extend the countdown beyond the full delay.
        Assert.Equal(10000, AutoAdvanceCountdown.RemainingMs(10000, T0, T0.AddSeconds(-30)));
    }

    // ─── IsFinished ────────────────────────────────────────────────────

    [Fact]
    public void IsFinished_BeforeDelayExpires_False_AtAndAfter_True()
    {
        Assert.False(AutoAdvanceCountdown.IsFinished(10000, T0, T0.AddSeconds(9)));
        Assert.True(AutoAdvanceCountdown.IsFinished(10000, T0, T0.AddSeconds(10)));
        Assert.True(AutoAdvanceCountdown.IsFinished(10000, T0, T0.AddSeconds(11)));
    }

    // ─── Format ────────────────────────────────────────────────────────

    [Fact]
    public void Format_ZeroAndNegative_AreZeroZero()
    {
        Assert.Equal("0:00", AutoAdvanceCountdown.Format(0));
        Assert.Equal("0:00", AutoAdvanceCountdown.Format(-1500));
    }

    [Fact]
    public void Format_WholeSeconds_MinutesAndSeconds()
    {
        Assert.Equal("0:10", AutoAdvanceCountdown.Format(10000));
        Assert.Equal("0:09", AutoAdvanceCountdown.Format(9000));
        Assert.Equal("1:05", AutoAdvanceCountdown.Format(65000));
        Assert.Equal("10:00", AutoAdvanceCountdown.Format(600000));
    }

    [Fact]
    public void Format_PartialSecond_RoundsUpSoFullDelayShowsAtStart()
    {
        // 9.5 s remaining displays as 0:10 — a live countdown shows its full
        // configured delay at the start and only reaches 0:00 at the end.
        Assert.Equal("0:10", AutoAdvanceCountdown.Format(9500));
        Assert.Equal("0:01", AutoAdvanceCountdown.Format(1));
    }
}
