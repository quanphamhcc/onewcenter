namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure remaining-time math for the per-item auto-advance countdown (đợt 2 T5 / R3.4).
/// The engine (<c>PresenterEngine</c>) owns WHEN a countdown starts and stops — it
/// arms one alongside the Timer-condition auto-advance and cancels it the instant
/// the current item changes — while this class only answers "how much time is
/// left" and formats it for the operator UI. Keeping the math here (instead of in
/// the engine or the window) makes it unit-testable without timers or WPF.
///
/// This is deliberately separate from <c>CountdownService</c> in the Presenter
/// project: that service counts down to the service START time, a different job.
/// </summary>
public static class AutoAdvanceCountdown
{
    /// <summary>
    /// Fallback delay used when an item's configured delay is not positive — the
    /// same default the engine has always applied when arming the auto-advance
    /// timer, so the displayed countdown and the real timer never disagree.
    /// </summary>
    public const int DefaultDelayMs = 5000;

    /// <summary>The delay that actually applies: the configured value when positive, else <see cref="DefaultDelayMs"/>.</summary>
    public static int EffectiveDelayMs(int configuredDelayMs)
        => configuredDelayMs > 0 ? configuredDelayMs : DefaultDelayMs;

    /// <summary>
    /// Remaining milliseconds for a countdown of <paramref name="delayMs"/> after
    /// <paramref name="elapsedMs"/> has passed. Clamped at zero — never negative —
    /// and a non-positive delay is already finished (0).
    /// </summary>
    public static long RemainingMs(long delayMs, long elapsedMs)
    {
        if (delayMs <= 0)
            return 0;

        long remaining = delayMs - elapsedMs;
        return remaining > 0 ? remaining : 0;
    }

    /// <summary>
    /// Remaining milliseconds for a countdown that started at
    /// <paramref name="startedAtUtc"/>, observed at <paramref name="nowUtc"/>,
    /// using the item's configured delay (with the
    /// <see cref="EffectiveDelayMs"/> fallback). A clock that moved backwards is
    /// treated as "just started" (full delay remaining) rather than extending the
    /// countdown past its configured delay.
    /// </summary>
    public static long RemainingMs(int configuredDelayMs, DateTimeOffset startedAtUtc, DateTimeOffset nowUtc)
    {
        long elapsed = (long)(nowUtc - startedAtUtc).TotalMilliseconds;
        if (elapsed < 0)
            elapsed = 0;

        return RemainingMs(EffectiveDelayMs(configuredDelayMs), elapsed);
    }

    /// <summary>True when the countdown has run out (remaining time is zero).</summary>
    public static bool IsFinished(int configuredDelayMs, DateTimeOffset startedAtUtc, DateTimeOffset nowUtc)
        => RemainingMs(configuredDelayMs, startedAtUtc, nowUtc) <= 0;

    /// <summary>
    /// Format a remaining time as <c>m:ss</c> for the operator display. Seconds are
    /// rounded UP so a freshly started countdown shows its full delay (10s shows
    /// "0:10", not "0:09") and the display reaches "0:00" exactly when the
    /// countdown finishes. Zero or negative input is "0:00".
    /// </summary>
    public static string Format(long remainingMs)
    {
        if (remainingMs <= 0)
            return "0:00";

        long totalSeconds = (remainingMs + 999) / 1000;
        return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
    }
}
