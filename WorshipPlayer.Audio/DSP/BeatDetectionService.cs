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
    /// Result of beat detection containing the grid parameters.
    /// </summary>
    public class BeatGrid
    {
        public double Bpm { get; set; }
        public double BeatIntervalMs { get; set; }
        public double BarDurationMs { get; set; }
        public double GridAnchorMs { get; set; }
        public int TimeSignatureNumerator { get; set; } = 4;
        public int TotalBars { get; set; }
        public double TotalDurationMs { get; set; }
        public List<double> BeatPositionsMs { get; set; } = new();
        public double Confidence { get; set; } // 0.0 to 1.0
    }

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
    {
        double beatIntervalMs = 60000.0 / bpm;
        double barDurationMs = beatIntervalMs * timeSignatureNumerator;
        int totalBars = totalDurationMs > 0 ? (int)((totalDurationMs - anchorMs) / barDurationMs) : 0;

        // Generate beat positions from the anchor
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
            TotalBars = totalBars,
            TotalDurationMs = totalDurationMs,
            BeatPositionsMs = beatPositions,
            Confidence = 1.0 // Manual = highest confidence
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // METHOD: Generate Click Track
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a synthetic click track audio file from a beat grid.
    /// If an existing click track is provided, the generated click will be synchronized
    /// to the actual peak positions of the existing click (eliminating drift).
    /// Saves as WAV file in the song folder.
    /// </summary>
    public void GenerateClickTrack(BeatGrid grid, string outputPath, int sampleRate = 44100, string? existingClickTrackPath = null)
    {
        double totalDurationMs = grid.TotalDurationMs;
        int totalSamples = (int)(totalDurationMs / 1000.0 * sampleRate);
        float[] clickAudio = new float[totalSamples];

        // Generate click sound (short sine burst)
        int clickLengthSamples = (int)(0.01 * sampleRate); // 10ms click
        float[] clickHigh = GenerateClickSound(sampleRate, 1500, clickLengthSamples, 0.9f); // Beat 1: higher pitch
        float[] clickLow = GenerateClickSound(sampleRate, 1000, clickLengthSamples, 0.6f);  // Beats 2-4: lower pitch

        // Determine beat positions to use
        List<double> beatPositions;

        if (existingClickTrackPath != null && File.Exists(existingClickTrackPath))
        {
            // Sync to existing click track: use its actual peak positions
            beatPositions = ExtractClickPositions(existingClickTrackPath, grid);
        }
        else
        {
            // No existing click: generate from grid mathematically
            beatPositions = new List<double>();
            double t = grid.GridAnchorMs;
            while (t < totalDurationMs)
            {
                beatPositions.Add(t);
                t += grid.BeatIntervalMs;
            }
        }

        // Place clicks at the determined positions
        int beatInBar = 0;
        for (int i = 0; i < beatPositions.Count; i++)
        {
            double posMs = beatPositions[i];
            int samplePos = (int)(posMs / 1000.0 * sampleRate);
            if (samplePos < 0 || samplePos >= totalSamples) continue;

            // Determine which beat in the bar this is
            // Use grid anchor to figure out the beat-in-bar index
            double elapsedFromAnchor = posMs - grid.GridAnchorMs;
            if (elapsedFromAnchor >= 0)
            {
                int totalBeats = (int)Math.Round(elapsedFromAnchor / grid.BeatIntervalMs);
                beatInBar = totalBeats % grid.TimeSignatureNumerator;
            }
            else
            {
                beatInBar = 0;
            }

            float[] clickSound = (beatInBar == 0) ? clickHigh : clickLow;

            for (int j = 0; j < clickSound.Length && samplePos + j < totalSamples; j++)
            {
                clickAudio[samplePos + j] = clickSound[j];
            }
        }

        // Save as WAV
        using var writer = new WaveFileWriter(outputPath, new WaveFormat(sampleRate, 16, 1));
        // Convert float to 16-bit PCM
        foreach (float sample in clickAudio)
        {
            short pcmSample = (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
            writer.WriteSample(sample);
        }
    }

    /// <summary>
    /// Extract actual click positions from an existing click track audio file.
    /// Used to synchronize the generated click with the original click track,
    /// so both are perfectly aligned to the actual recorded beats.
    /// </summary>
    private List<double> ExtractClickPositions(string clickTrackPath, BeatGrid grid)
    {
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

        // Use a minimum interval slightly less than the expected beat interval
        // to catch all clicks without false positives
        double minInterval = grid.BeatIntervalMs * 0.6;
        var peaks = DetectPeaks(mono, sampleRate, thresholdFactor: 0.25, minIntervalMs: minInterval);

        if (peaks.Count < 4)
        {
            // Fallback: if we can't get good peaks, use mathematical grid
            var fallback = new List<double>();
            double t = grid.GridAnchorMs;
            while (t < grid.TotalDurationMs)
            {
                fallback.Add(t);
                t += grid.BeatIntervalMs;
            }
            return fallback;
        }

        return peaks;
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
    /// Generate a short sine burst for click sound.
    /// </summary>
    private float[] GenerateClickSound(int sampleRate, double frequency, int lengthSamples, float amplitude)
    {
        float[] click = new float[lengthSamples];
        for (int i = 0; i < lengthSamples; i++)
        {
            double t = (double)i / sampleRate;
            // Sine wave with exponential decay envelope
            double envelope = Math.Exp(-t * 500); // Fast decay
            click[i] = (float)(Math.Sin(2 * Math.PI * frequency * t) * envelope * amplitude);
        }
        return click;
    }
}
