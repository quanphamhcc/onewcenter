using NAudio.Wave;

namespace WorshipPlayer.Audio.DSP;

/// <summary>
/// Service for detecting beats, BPM, and generating click tracks from audio.
/// Provides three detection methods:
/// 1. Click Track Analysis — reads existing click track peaks
/// 2. Drum/Audio Onset Detection — derives tempo from rhythmic content
/// 3. Tap-Along — user taps beats, app calculates grid
/// 
/// All methods produce a BeatGrid that can be used to generate section boundaries.
/// </summary>
public class BeatDetectionService
{
    /// <summary>
    /// Selects how <see cref="GenerateClickTrack(BeatGrid,string,ClickGenerationMode,int,string?)"/>
    /// computes the beat positions for the generated click. The three modes let the
    /// operator A/B/C three different strategies for the same song.
    /// </summary>
    public enum ClickGenerationMode
    {
        /// <summary>
        /// Mode A — robust adaptive sync. Detects the real click peaks with a local
        /// (moving-median) threshold, snaps them to the grid, and interpolates any missing
        /// beats so every beat across the full duration has a click (never silent).
        /// </summary>
        AdaptiveSync,

        /// <summary>
        /// Mode B — hybrid. Every beat is generated mathematically from the grid; a usable
        /// existing click is used ONLY to lock the anchor/phase (shift the grid anchor to the
        /// detected downbeat). No silent beats.
        /// </summary>
        HybridAnchor,

        /// <summary>
        /// Mode C — pure math grid. The existing click is ignored entirely; beats are
        /// GridAnchorMs + n * BeatIntervalMs across the full duration.
        /// </summary>
        PureGrid
    }

    /// <summary>
    /// Result of beat detection containing the grid parameters.
    /// </summary>
    public class BeatGrid
    {
        public double Bpm { get; set; }
        public double BeatIntervalMs { get; set; }
        public double BarDurationMs { get; set; }
        public double GridAnchorMs { get; set; }
        public int TimeSignatureNumerator { get; set; } = 4;
        public int TimeSignatureDenominator { get; set; } = 4;
        public int TotalBars { get; set; }
        public double TotalDurationMs { get; set; }
        public List<double> BeatPositionsMs { get; set; } = new();
        public double Confidence { get; set; } // 0.0 to 1.0
    }

    /// <summary>
    /// A single schedulable metronome click. <see cref="TimeMs"/> is the pulse position (ms) and
    /// <see cref="IsAccent"/> marks the LOUDER beat-1 accent. This pure value type is the SINGLE
    /// source of truth shared by the live metronome preview (FEAT-002) and the generated WAV, so
    /// the two always match exactly.
    /// </summary>
    public readonly record struct ClickPulse(double TimeMs, bool IsAccent);

    // ═══════════════════════════════════════════════════════════════════════
    // METHOD 1: Click Track Analysis
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyze a click track audio file to extract exact beat positions.
    /// This is the most accurate method — the click IS the grid.
    /// </summary>
    public BeatGrid? AnalyzeClickTrack(string clickTrackPath)
    {
        if (!File.Exists(clickTrackPath))
            return null;

        // Load entire click track into memory
        float[] audioData;
        int sampleRate;
        int channels;

        using (var reader = new AudioFileReader(clickTrackPath))
        {
            sampleRate = reader.WaveFormat.SampleRate;
            channels = reader.WaveFormat.Channels;
            long totalSamples = reader.Length / sizeof(float);
            audioData = new float[totalSamples];

            int offset = 0;
            int remaining = (int)totalSamples;
            while (remaining > 0)
            {
                int read = reader.Read(audioData, offset, Math.Min(remaining, 65536));
                if (read == 0) break;
                offset += read;
                remaining -= read;
            }
        }

        // Convert to mono if stereo
        float[] mono;
        if (channels == 2)
        {
            mono = new float[audioData.Length / 2];
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (audioData[i * 2] + audioData[i * 2 + 1]) * 0.5f;
        }
        else
        {
            mono = audioData;
        }

        // Detect peaks (click events)
        var peaks = DetectPeaks(mono, sampleRate, 
            thresholdFactor: 0.3, 
            minIntervalMs: 300); // Clicks are at least 300ms apart (max ~200 BPM)

        if (peaks.Count < 4)
            return null; // Not enough clicks detected

        // Calculate intervals between peaks
        var intervals = new List<double>();
        for (int i = 1; i < peaks.Count; i++)
        {
            intervals.Add(peaks[i] - peaks[i - 1]);
        }

        // Find median interval (robust to outliers like the count-in)
        intervals.Sort();
        double medianInterval = intervals[intervals.Count / 2];

        // Filter out the first interval if it's significantly different (count-in pickup)
        var cleanPeaks = new List<double>();
        cleanPeaks.Add(peaks[0]);
        for (int i = 1; i < peaks.Count; i++)
        {
            double interval = peaks[i] - peaks[i - 1];
            // Accept if within 15% of median
            if (Math.Abs(interval - medianInterval) / medianInterval < 0.15)
            {
                cleanPeaks.Add(peaks[i]);
            }
            else if (cleanPeaks.Count == 1)
            {
                // First interval was off (count-in) — restart from this peak
                cleanPeaks.Clear();
                cleanPeaks.Add(peaks[i]);
            }
        }

        if (cleanPeaks.Count < 4)
        {
            // Fall back to using all peaks
            cleanPeaks = peaks;
        }

        // Recalculate median from clean peaks
        var cleanIntervals = new List<double>();
        for (int i = 1; i < cleanPeaks.Count; i++)
            cleanIntervals.Add(cleanPeaks[i] - cleanPeaks[i - 1]);

        cleanIntervals.Sort();
        double beatIntervalMs = cleanIntervals[cleanIntervals.Count / 2];
        double bpm = 60000.0 / beatIntervalMs;

        // Grid anchor = first clean peak
        double gridAnchor = cleanPeaks[0];

        // Total duration
        double totalDurationMs = mono.Length * 1000.0 / sampleRate;
        int totalBars = (int)((totalDurationMs - gridAnchor) / (beatIntervalMs * 4));

        // Calculate confidence based on interval consistency
        double intervalStdDev = 0;
        foreach (var interval in cleanIntervals)
            intervalStdDev += Math.Pow(interval - beatIntervalMs, 2);
        intervalStdDev = Math.Sqrt(intervalStdDev / cleanIntervals.Count);
        double confidence = Math.Max(0, 1.0 - (intervalStdDev / beatIntervalMs) * 10);

        return new BeatGrid
        {
            Bpm = Math.Round(bpm, 4),
            BeatIntervalMs = Math.Round(beatIntervalMs, 3),
            BarDurationMs = Math.Round(beatIntervalMs * 4, 3),
            GridAnchorMs = Math.Round(gridAnchor, 3),
            TotalBars = totalBars,
            TotalDurationMs = totalDurationMs,
            BeatPositionsMs = cleanPeaks,
            Confidence = confidence
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // METHOD 2: Drum/Audio Onset Detection (Auto-generate BPM from any track)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Detect BPM from a rhythmic audio track (drums, bass, or full mix).
    /// Uses onset detection + autocorrelation to find the dominant tempo.
    /// </summary>
    public BeatGrid? DetectBpmFromAudio(string audioFilePath)
    {
        if (!File.Exists(audioFilePath))
            return null;

        float[] audioData;
        int sampleRate;
        int channels;

        using (var reader = new AudioFileReader(audioFilePath))
        {
            sampleRate = reader.WaveFormat.SampleRate;
            channels = reader.WaveFormat.Channels;
            long totalSamples = reader.Length / sizeof(float);
            audioData = new float[totalSamples];

            int offset = 0;
            int remaining = (int)totalSamples;
            while (remaining > 0)
            {
                int read = reader.Read(audioData, offset, Math.Min(remaining, 65536));
                if (read == 0) break;
                offset += read;
                remaining -= read;
            }
        }

        // Convert to mono
        float[] mono;
        if (channels == 2)
        {
            mono = new float[audioData.Length / 2];
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (audioData[i * 2] + audioData[i * 2 + 1]) * 0.5f;
        }
        else
        {
            mono = audioData;
        }

        // Compute onset strength envelope (energy difference over time)
        int hopSize = sampleRate / 100; // 10ms hops
        var onsetEnvelope = ComputeOnsetEnvelope(mono, sampleRate, hopSize);

        // Find dominant tempo via autocorrelation
        double bpm = FindDominantBpm(onsetEnvelope, sampleRate, hopSize);
        if (bpm <= 0) return null;

        double beatIntervalMs = 60000.0 / bpm;

        // Find the first strong onset as grid anchor
        var peaks = DetectPeaks(mono, sampleRate, thresholdFactor: 0.2, minIntervalMs: beatIntervalMs * 0.6);
        double gridAnchor = peaks.Count > 0 ? peaks[0] : 0;

        double totalDurationMs = mono.Length * 1000.0 / sampleRate;
        int totalBars = (int)((totalDurationMs - gridAnchor) / (beatIntervalMs * 4));

        return new BeatGrid
        {
            Bpm = Math.Round(bpm, 4),
            BeatIntervalMs = Math.Round(beatIntervalMs, 3),
            BarDurationMs = Math.Round(beatIntervalMs * 4, 3),
            GridAnchorMs = Math.Round(gridAnchor, 3),
            TotalBars = totalBars,
            TotalDurationMs = totalDurationMs,
            Confidence = 0.7 // Lower confidence than click track
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // METHOD 3: Tap-Along (User provides beat positions)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculate beat grid from user-tapped positions.
    /// User taps along to the beat while song plays — we collect timestamps.
    /// </summary>
    public BeatGrid? CalculateFromTaps(List<double> tapTimesMs, double totalDurationMs)
    {
        if (tapTimesMs.Count < 4)
            return null;

        // Calculate intervals
        var intervals = new List<double>();
        for (int i = 1; i < tapTimesMs.Count; i++)
            intervals.Add(tapTimesMs[i] - tapTimesMs[i - 1]);

        // Median interval = beat
        intervals.Sort();
        double medianInterval = intervals[intervals.Count / 2];

        // Filter outliers (keep within 30% of median)
        var goodIntervals = intervals.Where(iv => Math.Abs(iv - medianInterval) / medianInterval < 0.3).ToList();
        double beatIntervalMs = goodIntervals.Count > 0 ? goodIntervals.Average() : medianInterval;

        double bpm = 60000.0 / beatIntervalMs;
        double gridAnchor = tapTimesMs[0];
        int totalBars = (int)((totalDurationMs - gridAnchor) / (beatIntervalMs * 4));

        return new BeatGrid
        {
            Bpm = Math.Round(bpm, 4),
            BeatIntervalMs = Math.Round(beatIntervalMs, 3),
            BarDurationMs = Math.Round(beatIntervalMs * 4, 3),
            GridAnchorMs = Math.Round(gridAnchor, 3),
            TotalBars = totalBars,
            TotalDurationMs = totalDurationMs,
            BeatPositionsMs = tapTimesMs,
            Confidence = Math.Min(1.0, tapTimesMs.Count / 20.0) // More taps = more confidence
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // METHOD 4: Manual Downbeat Selection (User specifies grid anchor)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a beat grid from a manually-specified downbeat (grid anchor) position.
    /// Keeps the current BPM and time signature, recalculates bar/beat layout from the new anchor.
    /// </summary>
    public BeatGrid BuildGridFromManualAnchor(double anchorMs, double bpm, int timeSignatureNumerator, double totalDurationMs)
        => BuildGridFromManualAnchor(anchorMs, bpm, timeSignatureNumerator, 4, totalDurationMs);

    /// <summary>
    /// Build a beat grid from a manually-specified downbeat (grid anchor) and an explicit time
    /// signature (numerator AND denominator), so the manual-tempo selection can rebuild the shared
    /// grid for 3/4, 4/4 and 6/8. The bar length honors the denominator:
    ///   • 4/4 → 4 * beatIntervalMs   • 3/4 → 3 * beatIntervalMs
    ///   • 6/8 (felt in 2) → one full 6/8 bar = 6 eighth notes = 3 * beatIntervalMs
    /// The 4-arg overload delegates here with denominator 4 so existing callers remain 4/4.
    /// </summary>
    public BeatGrid BuildGridFromManualAnchor(double anchorMs, double bpm, int timeSignatureNumerator,
        int timeSignatureDenominator, double totalDurationMs)
    {
        // Guard degenerate inputs so callers never see NaN/Infinity or a crash.
        if (bpm <= 0 || timeSignatureNumerator <= 0)
        {
            return new BeatGrid
            {
                Bpm = bpm,
                BeatIntervalMs = 0,
                BarDurationMs = 0,
                GridAnchorMs = Math.Round(anchorMs, 3),
                TimeSignatureNumerator = timeSignatureNumerator,
                TimeSignatureDenominator = timeSignatureDenominator,
                TotalBars = 0,
                TotalDurationMs = totalDurationMs,
                BeatPositionsMs = new List<double>(),
                Confidence = 1.0
            };
        }

        double beatIntervalMs = 60000.0 / bpm;

        // Bar length honors the denominator. For 6/8 felt-in-2 the bar is 6 eighth notes; an
        // eighth note is half a quarter-note beat, so 6 * (beatIntervalMs / 2) = 3 * beatIntervalMs.
        double barDurationMs;
        if (timeSignatureDenominator == 8 && timeSignatureNumerator == 6)
        {
            double eighthMs = beatIntervalMs / 2.0;
            barDurationMs = 6 * eighthMs; // == 3 * beatIntervalMs
        }
        else
        {
            barDurationMs = beatIntervalMs * timeSignatureNumerator;
        }

        int totalBars = (totalDurationMs > 0 && barDurationMs > 0)
            ? (int)((totalDurationMs - anchorMs) / barDurationMs)
            : 0;

        // Generate beat positions from the anchor (quarter-note beats; the pulse pattern used for
        // the audible click/preview lives in ComputeMetronomePulses).
        var beatPositions = new List<double>();
        double t = anchorMs;
        while (t < totalDurationMs)
        {
            beatPositions.Add(t);
            t += beatIntervalMs;
        }

        return new BeatGrid
        {
            Bpm = Math.Round(bpm, 4),
            BeatIntervalMs = Math.Round(beatIntervalMs, 3),
            BarDurationMs = Math.Round(barDurationMs, 3),
            GridAnchorMs = Math.Round(anchorMs, 3),
            TimeSignatureNumerator = timeSignatureNumerator,
            TimeSignatureDenominator = timeSignatureDenominator,
            TotalBars = totalBars,
            TotalDurationMs = totalDurationMs,
            BeatPositionsMs = beatPositions,
            Confidence = 1.0 // Manual = highest confidence
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // METRONOME PULSE PATTERN (PURE — no I/O; single source of truth)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute the full schedule of metronome click pulses across [gridAnchorMs, totalDurationMs).
    /// This is a PURE function (no file/audio I/O) and is the single source of truth for BOTH the
    /// live metronome preview and the generated WAV, so the two always match exactly.
    ///
    /// The beat unit is the quarter note: beatIntervalMs = 60000 / bpm.
    ///   • 4/4 → 4 quarter-note clicks per bar, accent on beat 1.
    ///   • 3/4 → 3 quarter-note clicks per bar, accent on beat 1.
    ///   • 6/8 FELT IN 2 → the bar is 6 eighth notes; emit clicks ONLY on eighth positions 1 and 4
    ///     (the two dotted-quarter pulses). An eighth note = beatIntervalMs / 2, so the pulses fall
    ///     at barStart + 0*eighthMs (accent) and barStart + 3*eighthMs (normal click).
    ///
    /// Degenerate inputs (bpm &lt;= 0, numerator &lt;= 0, totalDurationMs &lt;= 0) return an empty list and
    /// never throw.
    /// </summary>
    public List<ClickPulse> ComputeMetronomePulses(double gridAnchorMs, double bpm,
        int timeSigNumerator, int timeSigDenominator, double totalDurationMs)
    {
        var pulses = new List<ClickPulse>();
        if (bpm <= 0 || timeSigNumerator <= 0 || totalDurationMs <= 0)
            return pulses;

        double beatIntervalMs = 60000.0 / bpm;
        if (beatIntervalMs <= 0)
            return pulses;

        bool sixEightFeltInTwo = (timeSigDenominator == 8 && timeSigNumerator == 6);

        if (sixEightFeltInTwo)
        {
            // Felt in 2: two dotted-quarter pulses per bar at eighth positions 1 and 4.
            double eighthMs = beatIntervalMs / 2.0;
            double barDurationMs = 6 * eighthMs; // 6 eighth notes
            for (double barStart = gridAnchorMs; barStart < totalDurationMs; barStart += barDurationMs)
            {
                double p1 = barStart;                 // accent
                double p2 = barStart + 3 * eighthMs;  // normal
                if (p1 >= 0 && p1 < totalDurationMs)
                    pulses.Add(new ClickPulse(p1, true));
                if (p2 >= 0 && p2 < totalDurationMs)
                    pulses.Add(new ClickPulse(p2, false));
            }
        }
        else
        {
            // 3/4, 4/4 and other simple meters: one click per quarter-note beat, accent on beat 1.
            int beatIndexInBar = 0;
            for (double t = gridAnchorMs; t < totalDurationMs; t += beatIntervalMs)
            {
                if (t >= 0)
                    pulses.Add(new ClickPulse(t, beatIndexInBar == 0));
                beatIndexInBar = (beatIndexInBar + 1) % timeSigNumerator;
            }
        }

        return pulses;
    }

    /// <summary>
    /// Compute the full manual-click pulse schedule honoring an explicit Beat-1 position and an
    /// optional count-in. This is the PURE source of truth for the Manual Click Track workflow
    /// (FEAT-002); the live metronome preview and the generated WAV both derive from it, so they
    /// cannot diverge.
    ///
    /// Semantics:
    ///   • The FIRST non-count-in pulse lands EXACTLY at <paramref name="beat1Ms"/> and is the
    ///     LOUDER beat-1 accent. Subsequent beats continue at the grid interval to
    ///     <paramref name="totalDurationMs"/> following the existing per-meter accent rules
    ///     (reusing <see cref="ComputeMetronomePulses"/> with gridAnchor = beat1Ms).
    ///   • <paramref name="countInBars"/> bars of clicks are prepended at the grid interval
    ///     IMMEDIATELY BEFORE beat1Ms, using the same per-meter pulse rule as the body:
    ///       4/4 → 4 clicks/bar, 3/4 → 3 clicks/bar, 6/8 felt-in-2 → 2 pulses/bar (at the bar
    ///       start and +3*eighthMs). Count-in pulses are NON-accent (IsAccent == false) so the
    ///       beat-1 accent is unambiguous.
    ///   • CLAMP to &gt;= 0 ms: if beat1Ms is too close to 0 to fit all requested count-in beats,
    ///     ONLY the whole count-in beats whose time is &gt;= 0 are emitted (beats that would land at a
    ///     negative time are dropped). This is the chosen clamp behavior.
    ///   • Degenerate inputs (bpm &lt;= 0, numerator &lt;= 0, totalDurationMs &lt;= 0) return an empty
    ///     list and never throw. A negative beat1Ms is treated as 0.
    /// </summary>
    public List<ClickPulse> ComputeManualClickPulses(double beat1Ms, double bpm,
        int timeSigNumerator, int timeSigDenominator, double totalDurationMs, int countInBars)
    {
        if (bpm <= 0 || timeSigNumerator <= 0 || totalDurationMs <= 0)
            return new List<ClickPulse>();

        if (beat1Ms < 0) beat1Ms = 0;

        // Body: first non-count-in pulse lands exactly at beat1Ms (gridAnchor = beat1Ms), then
        // continues at the grid interval with the standard per-meter accent pattern.
        var result = ComputeMetronomePulses(beat1Ms, bpm, timeSigNumerator, timeSigDenominator, totalDurationMs);

        // Count-in: prepend countInBars worth of pulses immediately BEFORE beat1Ms using the same
        // per-meter rule, clamped to >= 0. We synthesize them by walking the per-meter pulse offsets
        // within a bar, placed on the bars immediately preceding beat1Ms.
        if (countInBars > 0)
        {
            double beatIntervalMs = 60000.0 / bpm;
            bool sixEightFeltInTwo = (timeSigDenominator == 8 && timeSigNumerator == 6);

            // Per-bar pulse offsets (relative to bar start) and the bar length.
            double barDurationMs;
            double[] offsetsInBar;
            if (sixEightFeltInTwo)
            {
                double eighthMs = beatIntervalMs / 2.0;
                barDurationMs = 6 * eighthMs;               // felt-in-2 bar
                offsetsInBar = new[] { 0.0, 3 * eighthMs }; // two dotted-quarter pulses
            }
            else
            {
                barDurationMs = beatIntervalMs * timeSigNumerator;
                offsetsInBar = new double[timeSigNumerator];
                for (int b = 0; b < timeSigNumerator; b++)
                    offsetsInBar[b] = b * beatIntervalMs;
            }

            var countIn = new List<ClickPulse>();
            // Bars are placed immediately before beat1Ms: bar index -1 starts at beat1Ms - barDuration,
            // bar index -2 at beat1Ms - 2*barDuration, etc. All count-in pulses are non-accent.
            for (int bar = 1; bar <= countInBars; bar++)
            {
                double barStart = beat1Ms - bar * barDurationMs;
                foreach (double off in offsetsInBar)
                {
                    double t = barStart + off;
                    // Only keep whole count-in beats whose time is >= 0 (clamp) and strictly before beat1.
                    if (t >= 0 && t < beat1Ms)
                        countIn.Add(new ClickPulse(t, false));
                }
            }

            countIn.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            result.InsertRange(0, countIn);
        }

        return result;
    }

    /// <summary>
    /// Return the WAV click positions (ms) for the SAME pulse pattern used by the live preview, so
    /// the generated WAV and the metronome match exactly. Pure (no I/O). Honors the manual-click
    /// Beat 1 position (<paramref name="beat1Ms"/>; defaults to grid.GridAnchorMs when &lt; 0) and the
    /// count-in bars, routing through <see cref="ComputeManualClickPulses"/>.
    /// </summary>
    public List<double> BuildManualClickPositions(BeatGrid grid, double beat1Ms = -1, int countInBars = 0)
    {
        double b1 = beat1Ms >= 0 ? beat1Ms : grid.GridAnchorMs;
        var pulses = ComputeManualClickPulses(b1, grid.Bpm,
            grid.TimeSignatureNumerator, grid.TimeSignatureDenominator, grid.TotalDurationMs, countInBars);
        return pulses.Select(p => p.TimeMs).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // METHOD: Generate Click Track
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Backward-compatible entry point. Generates a click synchronized to an existing click
    /// when one is supplied (Mode A behavior), otherwise a pure math grid (Mode C).
    /// Prefer the mode-aware overload for A/B/C generation.
    /// </summary>
    public void GenerateClickTrack(BeatGrid grid, string outputPath, int sampleRate = 44100, string? existingClickTrackPath = null)
    {
        var mode = (existingClickTrackPath != null && File.Exists(existingClickTrackPath))
            ? ClickGenerationMode.AdaptiveSync
            : ClickGenerationMode.PureGrid;
        GenerateClickTrack(grid, outputPath, mode, sampleRate, existingClickTrackPath);
    }

    /// <summary>
    /// Generate a synthetic click track audio file from a beat grid using the selected mode.
    /// Mode A (AdaptiveSync) robustly syncs to the existing click and interpolates missing beats;
    /// Mode B (HybridAnchor) emits every grid beat but locks phase to the existing click;
    /// Mode C (PureGrid) ignores the existing click entirely.
    /// Modes A and B never leave a beat silent across the full duration.
    /// Saves as a 16-bit mono WAV file.
    /// </summary>
    public void GenerateClickTrack(BeatGrid grid, string outputPath, ClickGenerationMode mode,
        int sampleRate = 44100, string? existingClickTrackPath = null)
    {
        double totalDurationMs = grid.TotalDurationMs;
        if (totalDurationMs <= 0 || sampleRate <= 0)
        {
            // Nothing meaningful to write; emit an empty (silent) file so callers never crash.
            using var emptyWriter = new WaveFileWriter(outputPath, new WaveFormat(Math.Max(1, sampleRate), 16, 1));
            return;
        }

        int totalSamples = (int)(totalDurationMs / 1000.0 * sampleRate);
        float[] clickAudio = new float[Math.Max(0, totalSamples)];

        // Generate click sound (louder, longer sine burst — see GenerateManualClickTrack notes).
        int clickLengthSamples = (int)(ClickLengthMs / 1000.0 * sampleRate);
        float[] clickHigh = GenerateClickSound(sampleRate, 1500, clickLengthSamples, 1.0f);  // Beat 1: higher pitch, full-scale accent
        float[] clickLow = GenerateClickSound(sampleRate, 1000, clickLengthSamples, 0.95f);  // Beats 2-4: lower pitch, loud

        // Decode the existing click to mono (only when a mode that uses it is selected).
        float[]? existingMono = null;
        int existingSampleRate = sampleRate;
        if (mode != ClickGenerationMode.PureGrid)
        {
            existingMono = TryLoadMono(existingClickTrackPath, out existingSampleRate);
        }

        // Compute beat positions via the pure, unit-testable helper.
        List<double> beatPositions = ComputeBeatPositions(grid, mode, existingMono, existingSampleRate);

        // Place clicks at the determined positions
        for (int i = 0; i < beatPositions.Count; i++)
        {
            double posMs = beatPositions[i];
            int samplePos = (int)(posMs / 1000.0 * sampleRate);
            if (samplePos < 0 || samplePos >= totalSamples) continue;

            // Determine which beat in the bar this is
            // Use grid anchor to figure out the beat-in-bar index
            int beatInBar = 0;
            double elapsedFromAnchor = posMs - grid.GridAnchorMs;
            if (elapsedFromAnchor >= 0 && grid.BeatIntervalMs > 0 && grid.TimeSignatureNumerator > 0)
            {
                int totalBeats = (int)Math.Round(elapsedFromAnchor / grid.BeatIntervalMs);
                beatInBar = totalBeats % grid.TimeSignatureNumerator;
            }

            float[] clickSound = (beatInBar == 0) ? clickHigh : clickLow;

            for (int j = 0; j < clickSound.Length && samplePos + j < totalSamples; j++)
            {
                clickAudio[samplePos + j] = clickSound[j];
            }
        }

        // Save as WAV (16-bit PCM mono). NAudio converts the float sample to the configured
        // 16-bit format internally, so we write floats directly.
        using var writer = new WaveFileWriter(outputPath, new WaveFormat(sampleRate, 16, 1));
        foreach (float sample in clickAudio)
        {
            writer.WriteSample(Math.Clamp(sample, -1f, 1f));
        }
    }

    /// <summary>
    /// Generate a single click WAV for the manual-tempo workflow using the metronome pulse pattern
    /// (the SAME <see cref="ComputeMetronomePulses"/> schedule as the live preview, so the WAV and
    /// preview match exactly). Beat-1 accents are synthesized LOUDER (1500Hz amp 1.0) than normal
    /// clicks (1000Hz amp 0.8). Honors 3/4, 4/4 and 6/8-felt-in-2 and starts at grid.GridAnchorMs.
    /// Degenerate grids (duration/sampleRate &lt;= 0) write a valid empty/silent WAV and never throw,
    /// exactly like <see cref="GenerateClickTrack(BeatGrid,string,ClickGenerationMode,int,string?)"/>.
    /// </summary>
    /// <param name="beat1Ms">
    /// Manual-click Beat 1 position (ms); the first non-count-in pulse lands here. When &lt; 0 the
    /// method falls back to grid.GridAnchorMs so existing callers are unaffected.
    /// </param>
    /// <param name="countInBars">Count-in bars prepended immediately before Beat 1 (clamped to &gt;= 0 ms).</param>
    public void GenerateManualClickTrack(BeatGrid grid, string outputPath, int sampleRate = 44100,
        double beat1Ms = -1, int countInBars = 0)
    {
        double totalDurationMs = grid.TotalDurationMs;
        if (totalDurationMs <= 0 || sampleRate <= 0)
        {
            // Nothing meaningful to write; emit an empty (silent) file so callers never crash.
            using var emptyWriter = new WaveFileWriter(outputPath, new WaveFormat(Math.Max(1, sampleRate), 16, 1));
            return;
        }

        int totalSamples = (int)(totalDurationMs / 1000.0 * sampleRate);
        float[] clickAudio = new float[Math.Max(0, totalSamples)];

        // Click sounds: loud beat-1 accent vs. normal click (mirrors GenerateClickTrack).
        // LOUDNESS (user request): the manual click WAV must be as loud as possible so it is
        // clearly audible OVER the song track. We (a) raise the normal click to near full scale
        // (0.95) so every click sits close to the accent, and (b) lengthen the burst to ~40ms with
        // a slower decay (see ClickDecayRate) so each click carries far more energy/RMS than the
        // old 10ms fast-decay burst — that is the real perceived-loudness win over the music.
        int clickLengthSamples = (int)(ClickLengthMs / 1000.0 * sampleRate);
        float[] clickAccent = GenerateClickSound(sampleRate, 1500, clickLengthSamples, 1.0f);  // beat 1 accent (full-scale)
        float[] clickNormal = GenerateClickSound(sampleRate, 1000, clickLengthSamples, 0.95f); // other pulses (loud)

        // Single source of truth: the manual-click pulse schedule (Beat 1 offset + count-in).
        double b1 = beat1Ms >= 0 ? beat1Ms : grid.GridAnchorMs;
        var pulses = ComputeManualClickPulses(b1, grid.Bpm,
            grid.TimeSignatureNumerator, grid.TimeSignatureDenominator, totalDurationMs, countInBars);

        foreach (var pulse in pulses)
        {
            int samplePos = (int)(pulse.TimeMs / 1000.0 * sampleRate);
            if (samplePos < 0 || samplePos >= totalSamples) continue;

            float[] clickSound = pulse.IsAccent ? clickAccent : clickNormal;
            for (int j = 0; j < clickSound.Length && samplePos + j < totalSamples; j++)
            {
                clickAudio[samplePos + j] = clickSound[j];
            }
        }

        using var writer = new WaveFileWriter(outputPath, new WaveFormat(sampleRate, 16, 1));
        foreach (float sample in clickAudio)
        {
            writer.WriteSample(Math.Clamp(sample, -1f, 1f));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PURE BEAT-POSITION COMPUTATION (no file I/O — unit-testable)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute the beat positions (ms) for a given generation mode. This is a PURE function:
    /// it takes already-decoded mono samples (or null when no existing click is available) and
    /// returns the list of beat times. No file reads, no WAV writing. The WAV-writing wrapper
    /// delegates here so the algorithm can be unit-tested with synthetic buffers.
    /// </summary>
    public List<double> ComputeBeatPositions(BeatGrid grid, ClickGenerationMode mode,
        float[]? existingMono, int existingSampleRate)
    {
        switch (mode)
        {
            case ClickGenerationMode.PureGrid:
                return BuildGridPositions(grid.GridAnchorMs, grid);

            case ClickGenerationMode.HybridAnchor:
            {
                double anchor = grid.GridAnchorMs;
                var peaks = DetectAdaptivePeaks(existingMono, existingSampleRate, grid);
                if (peaks.Count >= 4)
                {
                    // Lock phase to the first confident peak, snapped to the grid phase.
                    anchor = PhaseAlignedAnchor(peaks[0], grid);
                }
                return BuildGridPositions(anchor, grid);
            }

            case ClickGenerationMode.AdaptiveSync:
            default:
            {
                var peaks = DetectAdaptivePeaks(existingMono, existingSampleRate, grid);
                if (peaks.Count < 4)
                {
                    // Too few usable peaks → fall back to the full math grid (never silent).
                    return BuildGridPositions(grid.GridAnchorMs, grid);
                }

                // Snap each detected peak to the nearest grid beat and de-duplicate.
                var snapped = new List<double>();
                foreach (var p in peaks)
                {
                    double s = SnapToBeat(p, grid);
                    if (snapped.Count == 0 || Math.Abs(s - snapped[^1]) > 1e-6)
                        snapped.Add(s);
                }
                snapped.Sort();

                // Interpolate so no beat is ever silent: fill start→first, internal gaps,
                // and last→end using the grid spacing.
                return InterpolateToFullGrid(snapped, grid);
            }
        }
    }

    /// <summary>
    /// Build the full mathematical grid of beats from a given anchor across the song duration.
    /// </summary>
    private static List<double> BuildGridPositions(double anchorMs, BeatGrid grid)
    {
        var positions = new List<double>();
        if (grid.BeatIntervalMs <= 0 || grid.TotalDurationMs <= 0)
            return positions;

        // Walk backwards so beats cover from (near) song start, then forwards to the end.
        double start = anchorMs;
        while (start - grid.BeatIntervalMs >= 0)
            start -= grid.BeatIntervalMs;

        double t = start;
        while (t < grid.TotalDurationMs)
        {
            if (t >= 0) positions.Add(t);
            t += grid.BeatIntervalMs;
        }
        return positions;
    }

    /// <summary>
    /// Return the grid anchor shifted so its phase matches the detected first-peak position
    /// (keeps BeatIntervalMs; only the phase within a beat changes).
    /// </summary>
    private static double PhaseAlignedAnchor(double firstPeakMs, BeatGrid grid)
    {
        if (grid.BeatIntervalMs <= 0) return grid.GridAnchorMs;
        // Phase offset of the detected peak relative to the beat interval.
        double phase = firstPeakMs % grid.BeatIntervalMs;
        if (phase < 0) phase += grid.BeatIntervalMs;
        return phase;
    }

    /// <summary>
    /// Given confident, grid-snapped beat times, fill any gap larger than ~1.5x the beat
    /// interval with grid-spaced beats, and extend coverage from song start to the first beat
    /// and from the last beat to the end. Guarantees no silent beat across the full duration.
    /// </summary>
    private static List<double> InterpolateToFullGrid(List<double> snapped, BeatGrid grid)
    {
        if (grid.BeatIntervalMs <= 0 || grid.TotalDurationMs <= 0)
            return snapped;
        if (snapped.Count == 0)
            return BuildGridPositions(grid.GridAnchorMs, grid);

        double interval = grid.BeatIntervalMs;
        double gapThreshold = interval * 1.5;
        var result = new List<double>();

        // Fill from song start up to the first detected beat using grid spacing.
        double firstBeat = snapped[0];
        for (double t = firstBeat - interval; t >= 0; t -= interval)
            result.Add(t);
        result.Reverse();

        // Walk the detected beats, filling internal gaps.
        result.Add(firstBeat);
        for (int i = 1; i < snapped.Count; i++)
        {
            double prev = snapped[i - 1];
            double cur = snapped[i];
            double gap = cur - prev;
            if (gap > gapThreshold)
            {
                for (double t = prev + interval; t < cur - 1e-6; t += interval)
                    result.Add(t);
            }
            result.Add(cur);
        }

        // Fill from the last detected beat to the end of the song.
        double last = snapped[^1];
        for (double t = last + interval; t < grid.TotalDurationMs; t += interval)
            result.Add(t);

        // Normalize: sort and remove near-duplicates.
        result.Sort();
        var deduped = new List<double>();
        foreach (var t in result)
        {
            if (deduped.Count == 0 || Math.Abs(t - deduped[^1]) > 1e-6)
                deduped.Add(t);
        }
        return deduped;
    }

    /// <summary>
    /// Adaptive peak detector used by Modes A and B. Operates on an in-memory mono buffer and
    /// uses a LOCAL (moving-median) threshold instead of a single global max*factor so quiet
    /// sections still register peaks. Returns peak positions in milliseconds. Pure (no I/O).
    /// </summary>
    public List<double> DetectAdaptivePeaks(float[]? mono, int sampleRate, BeatGrid grid)
    {
        var peaks = new List<double>();
        if (mono == null || mono.Length == 0 || sampleRate <= 0 || grid.BeatIntervalMs <= 0)
            return peaks;

        // RMS envelope: 20ms window / 5ms hop (matches the original detector).
        int windowSize = Math.Max(1, sampleRate / 50);
        int hopSize = Math.Max(1, sampleRate / 200);
        var envelope = new List<double>();
        for (int i = 0; i + windowSize <= mono.Length; i += hopSize)
        {
            double sum = 0;
            for (int j = 0; j < windowSize; j++)
                sum += mono[i + j] * mono[i + j];
            envelope.Add(Math.Sqrt(sum / windowSize));
        }
        if (envelope.Count == 0) return peaks;

        // Moving-median local threshold. Window spans roughly one beat so the threshold
        // tracks the local loudness and does not blind quiet sections.
        int beatFrames = Math.Max(1, (int)(grid.BeatIntervalMs / 1000.0 * sampleRate / hopSize));
        int medianHalf = Math.Max(1, beatFrames); // ~2 beats wide
        double globalMean = envelope.Average();
        double floor = globalMean * 0.15; // guard against pure-silence false peaks

        // Relaxed minimum-interval gate: 0.4x beat interval (was 0.6x) so true adjacent beats
        // are not swallowed.
        double minIntervalMs = grid.BeatIntervalMs * 0.4;
        double minIntervalFrames = minIntervalMs / 1000.0 * sampleRate / hopSize;
        int lastPeakIdx = -1_000_000;

        var window = new List<double>();
        for (int i = 1; i < envelope.Count - 1; i++)
        {
            int lo = Math.Max(0, i - medianHalf);
            int hi = Math.Min(envelope.Count - 1, i + medianHalf);
            window.Clear();
            for (int k = lo; k <= hi; k++) window.Add(envelope[k]);
            window.Sort();
            double localMedian = window[window.Count / 2];
            double threshold = Math.Max(localMedian * 1.5, floor);

            if (envelope[i] > threshold &&
                envelope[i] > envelope[i - 1] &&
                envelope[i] >= envelope[i + 1] &&
                (i - lastPeakIdx) >= minIntervalFrames)
            {
                double timeMs = (double)i * hopSize / sampleRate * 1000.0;
                peaks.Add(timeMs);
                lastPeakIdx = i;
            }
        }

        return peaks;
    }

    /// <summary>
    /// Load an audio file into a mono float buffer. Returns null on any problem (missing,
    /// corrupt, unreadable) so callers can gracefully fall back to the math grid.
    /// </summary>
    private static float[]? TryLoadMono(string? path, out int sampleRate)
    {
        sampleRate = 44100;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            float[] audioData;
            int channels;
            using (var reader = new AudioFileReader(path))
            {
                sampleRate = reader.WaveFormat.SampleRate;
                channels = reader.WaveFormat.Channels;
                long totalSamples = reader.Length / sizeof(float);
                if (totalSamples <= 0) return null;
                audioData = new float[totalSamples];

                int offset = 0;
                int remaining = (int)totalSamples;
                while (remaining > 0)
                {
                    int read = reader.Read(audioData, offset, Math.Min(remaining, 65536));
                    if (read == 0) break;
                    offset += read;
                    remaining -= read;
                }
            }

            if (channels == 2)
            {
                var monoBuf = new float[audioData.Length / 2];
                for (int i = 0; i < monoBuf.Length; i++)
                    monoBuf[i] = (audioData[i * 2] + audioData[i * 2 + 1]) * 0.5f;
                return monoBuf;
            }
            return audioData;
        }
        catch
        {
            return null; // corrupt / unreadable → graceful fallback
        }
    }

    /// <summary>
    /// Snap a millisecond position to the nearest bar boundary.
    /// </summary>
    public double SnapToBar(double ms, BeatGrid grid)
    {
        if (grid.BarDurationMs <= 0) return ms;
        double elapsed = ms - grid.GridAnchorMs;
        int barIndex = (int)Math.Round(elapsed / grid.BarDurationMs);
        return grid.GridAnchorMs + barIndex * grid.BarDurationMs;
    }

    /// <summary>
    /// Snap a millisecond position to the nearest beat boundary.
    /// </summary>
    public double SnapToBeat(double ms, BeatGrid grid)
    {
        if (grid.BeatIntervalMs <= 0) return ms;
        double elapsed = ms - grid.GridAnchorMs;
        int beatIndex = (int)Math.Round(elapsed / grid.BeatIntervalMs);
        return grid.GridAnchorMs + beatIndex * grid.BeatIntervalMs;
    }

    /// <summary>
    /// Get bar number for a given position.
    /// </summary>
    public int GetBarAtMs(double ms, BeatGrid grid)
    {
        double elapsed = ms - grid.GridAnchorMs;
        if (elapsed < 0) return 0;
        return (int)(elapsed / grid.BarDurationMs) + 1;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Detect peaks (onsets) in audio using threshold and minimum interval.
    /// Returns peak positions in milliseconds.
    /// </summary>
    private List<double> DetectPeaks(float[] mono, int sampleRate, double thresholdFactor, double minIntervalMs)
    {
        var peaks = new List<double>();

        // Compute RMS envelope with short window
        int windowSize = sampleRate / 50; // 20ms window
        int hopSize = sampleRate / 200; // 5ms hop
        var envelope = new List<double>();

        for (int i = 0; i < mono.Length - windowSize; i += hopSize)
        {
            double sum = 0;
            for (int j = 0; j < windowSize; j++)
                sum += mono[i + j] * mono[i + j];
            envelope.Add(Math.Sqrt(sum / windowSize));
        }

        if (envelope.Count == 0) return peaks;

        // Find threshold
        double maxEnv = envelope.Max();
        double threshold = maxEnv * thresholdFactor;

        // Find peaks above threshold with minimum interval
        double minIntervalSamples = minIntervalMs / 1000.0 * sampleRate / hopSize;
        int lastPeakIdx = -9999;

        for (int i = 1; i < envelope.Count - 1; i++)
        {
            if (envelope[i] > threshold &&
                envelope[i] > envelope[i - 1] &&
                envelope[i] >= envelope[i + 1] &&
                (i - lastPeakIdx) >= minIntervalSamples)
            {
                double timeMs = (double)i * hopSize / sampleRate * 1000.0;
                peaks.Add(timeMs);
                lastPeakIdx = i;
            }
        }

        return peaks;
    }

    /// <summary>
    /// Compute onset strength envelope using energy difference.
    /// </summary>
    private float[] ComputeOnsetEnvelope(float[] mono, int sampleRate, int hopSize)
    {
        int windowSize = hopSize * 2;
        int numFrames = (mono.Length - windowSize) / hopSize;
        if (numFrames <= 0) return Array.Empty<float>();

        float[] envelope = new float[numFrames];
        float prevEnergy = 0;

        for (int i = 0; i < numFrames; i++)
        {
            int start = i * hopSize;
            float energy = 0;
            for (int j = 0; j < windowSize && start + j < mono.Length; j++)
                energy += mono[start + j] * mono[start + j];
            energy /= windowSize;

            // Half-wave rectified difference (only increases)
            envelope[i] = Math.Max(0, energy - prevEnergy);
            prevEnergy = energy;
        }

        return envelope;
    }

    /// <summary>
    /// Find dominant BPM via autocorrelation of onset envelope.
    /// Searches BPM range 50-180 (typical worship song range).
    /// </summary>
    private double FindDominantBpm(float[] envelope, int sampleRate, int hopSize)
    {
        if (envelope.Length < 100) return 0;

        double bestBpm = 0;
        double bestCorrelation = 0;

        // Search BPM range 50-180
        for (double bpm = 50; bpm <= 180; bpm += 0.5)
        {
            double beatIntervalFrames = (60.0 / bpm) * sampleRate / hopSize;
            int lag = (int)beatIntervalFrames;

            if (lag >= envelope.Length / 2) continue;

            // Compute autocorrelation at this lag
            double correlation = 0;
            int count = 0;
            for (int i = 0; i < envelope.Length - lag; i++)
            {
                correlation += envelope[i] * envelope[i + lag];
                count++;
            }
            correlation /= count;

            if (correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestBpm = bpm;
            }
        }

        return bestBpm;
    }

    /// <summary>
    /// Click burst length (ms). Lengthened from 10ms so each click carries much more energy and is
    /// clearly audible over the song track (user-requested loudness increase).
    /// </summary>
    internal const double ClickLengthMs = 40.0;

    /// <summary>
    /// Exponential-decay rate for the click envelope. A slower decay (smaller value) keeps the
    /// burst near its peak for longer, raising perceived loudness without exceeding full scale.
    /// </summary>
    internal const double ClickDecayRate = 90.0;

    /// <summary>
    /// Generate a short sine burst for click sound. The envelope holds near full amplitude for the
    /// first few ms then decays, maximizing perceived loudness while the PEAK sample never exceeds
    /// <paramref name="amplitude"/> (so full-scale accents do not clip).
    /// </summary>
    private float[] GenerateClickSound(int sampleRate, double frequency, int lengthSamples, float amplitude)
    {
        float[] click = new float[Math.Max(0, lengthSamples)];
        for (int i = 0; i < click.Length; i++)
        {
            double t = (double)i / sampleRate;
            // Sine wave with a slower exponential decay envelope (louder, more energy than before).
            double envelope = Math.Exp(-t * ClickDecayRate);
            click[i] = (float)(Math.Sin(2 * Math.PI * frequency * t) * envelope * amplitude);
        }
        return click;
    }
}
