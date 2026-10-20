namespace WorshipPlayer.Audio.DSP;

/// <summary>
/// PURE (no audio/IO) scheduling math for the Section Editor "Play with Click" feature (FEAT-003).
///
/// Approach A plays the song through the shared <c>AudioEngine</c> (its own sample clock) and runs
/// the free-running <c>MetronomePlayer</c> on top. Because the metronome free-runs on its OWN clock
/// and is NOT inherently phase-locked to the song, the first click must be ALIGNED to the song's
/// downbeat. This type computes, from the song playback position, WHEN the metronome should be
/// started/armed and WHAT its first audible click is, so the count-in and Beat 1 land on the song
/// clock exactly where <see cref="BeatDetectionService.ComputeManualClickPulses"/> schedules them
/// (the same source of truth as <c>Generated_Click_Manual.wav</c>).
///
/// The song always starts from the BEGINNING (position 0), so "arm time" is measured from song
/// start. All math is deterministic and unit-tested; nothing here touches an audio device.
/// </summary>
public static class ClickSyncScheduler
{
    /// <summary>
    /// The result of aligning the metronome to the song clock for a start-from-zero playback.
    /// </summary>
    public readonly record struct ClickAlignment(
        double BarDurationMs,
        double CountInDurationMs,
        double ArmAtSongMs,
        double FirstClickSongMs,
        double Beat1SongMs);

    /// <summary>
    /// One full bar length (ms) for the given tempo/meter, honoring 6/8 felt-in-2 (= 3 quarter
    /// beats). Returns 0 for degenerate tempo/meter so callers can no-op without crashing.
    /// </summary>
    public static double BarDurationMs(double bpm, int numerator, int denominator)
    {
        if (bpm <= 0 || numerator <= 0 || double.IsNaN(bpm) || double.IsInfinity(bpm))
            return 0;

        double beatIntervalMs = 60000.0 / bpm;
        if (beatIntervalMs <= 0 || double.IsNaN(beatIntervalMs) || double.IsInfinity(beatIntervalMs))
            return 0;

        return (denominator == 8 && numerator == 6)
            ? 3.0 * beatIntervalMs           // 6/8 felt in 2 == 3 quarter-note beats
            : numerator * beatIntervalMs;
    }

    /// <summary>
    /// Compute how the metronome aligns to a song that starts at position 0.
    ///
    /// <para>The count-in occupies <c>countInBeats</c> individual beats ending exactly at Beat 1, but
    /// it is CLAMPED so it never starts before song position 0 (mirroring
    /// <see cref="BeatDetectionService.ComputeManualClickPulses"/>, which drops count-in pulses that
    /// would fall before 0). Therefore:</para>
    /// <list type="bullet">
    ///   <item><description><c>Beat1SongMs</c> = the user's Beat 1 (clamped to &gt;= 0).</description></item>
    ///   <item><description><c>CountInDurationMs</c> = min(countInBeats*beat, Beat1SongMs) — the audible
    ///   count-in cannot extend before the song start.</description></item>
    ///   <item><description><c>ArmAtSongMs</c> = Beat1SongMs - CountInDurationMs = the song position at
    ///   which the metronome's very first click must sound (count-in start, or Beat 1 itself when
    ///   there is no count-in / no room for it).</description></item>
    ///   <item><description><c>FirstClickSongMs</c> == <c>ArmAtSongMs</c> (the first audible click,
    ///   whether that is the count-in's first beat or Beat 1).</description></item>
    /// </list>
    ///
    /// Degenerate tempo/meter yields a zero-length alignment anchored at the clamped Beat 1 so
    /// callers can safely no-op. <paramref name="countInBeats"/> is clamped to &gt;= 0.
    /// </summary>
    public static ClickAlignment ComputeAlignment(double beat1Ms, double bpm,
        int numerator, int denominator, int countInBeats)
    {
        if (beat1Ms < 0) beat1Ms = 0;
        if (countInBeats < 0) countInBeats = 0;

        double bar = BarDurationMs(bpm, numerator, denominator);
        if (bar <= 0)
        {
            // No meaningful tempo: arm right at Beat 1 with no count-in.
            return new ClickAlignment(0, 0, beat1Ms, beat1Ms, beat1Ms);
        }

        // Count-in is measured in BEATS. In 6/8 felt-in-2 the "beat" is the dotted-quarter pulse.
        bool sixEightFeltInTwo = (denominator == 8 && numerator == 6);
        double beatIntervalMs = 60000.0 / bpm;
        double stepMs = sixEightFeltInTwo ? 3.0 * (beatIntervalMs / 2.0) : beatIntervalMs;

        double requestedCountIn = countInBeats * stepMs;
        // Clamp so the count-in never begins before song start (position 0).
        double countInDuration = Math.Min(requestedCountIn, beat1Ms);
        if (countInDuration < 0) countInDuration = 0;

        double armAt = beat1Ms - countInDuration;
        if (armAt < 0) armAt = 0;

        return new ClickAlignment(
            BarDurationMs: bar,
            CountInDurationMs: countInDuration,
            ArmAtSongMs: armAt,
            FirstClickSongMs: armAt,
            Beat1SongMs: beat1Ms);
    }

    /// <summary>
    /// Given the CURRENT song position (ms) and the alignment, how long (ms) until the metronome's
    /// first click should sound. Negative/zero means "arm now" (the song clock has already reached
    /// or passed the first-click time). Used to decide whether to start the metronome immediately
    /// (the common case where the song starts at 0 and Beat 1/count-in is at or after 0).
    /// </summary>
    public static double DelayUntilFirstClickMs(double songPositionMs, in ClickAlignment alignment)
        => alignment.FirstClickSongMs - songPositionMs;

    /// <summary>
    /// True when, at the given song position, the metronome's first click is due now or already
    /// past (so the caller should start/arm the metronome immediately). Since playback always
    /// starts at 0 and the first click is at <c>ArmAtSongMs</c> &gt;= 0, this is true as soon as the
    /// song clock reaches that point.
    /// </summary>
    public static bool ShouldArmNow(double songPositionMs, in ClickAlignment alignment)
        => songPositionMs >= alignment.FirstClickSongMs;
}
