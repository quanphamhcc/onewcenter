using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;
using WorshipPlayer.Audio.DSP;
using WorshipPlayer.Audio.Engine;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.UI.Views;

/// <summary>
/// Waveform editor for precise section loop point editing.
/// Features:
/// - Draggable loop markers (green=IN, red=OUT) with beat snapping
/// - Real-time playback position bar (syncs with main player or internal)
/// - Granular auto-detect: finds precise onset, splits sub-sections
/// - Merge sub-sections back together
/// - Tooltip showing bar/beat while dragging
/// </summary>
public partial class WaveformEditorWindow : Window
{
    private readonly Song _song;
    private readonly SectionViewModel _section;
    private readonly BeatDetectionService.BeatGrid? _grid;
    private readonly AudioEngine? _audioEngine;

    private float[] _clickWaveform = Array.Empty<float>();
    private float[] _mixWaveform = Array.Empty<float>();
    private float[] _vocalWaveform = Array.Empty<float>();
    private double _displayStartMs;
    private double _displayEndMs;
    private double _loopInMs;
    private double _loopOutMs;
    private string _lyricsFullText = ""; // Full text with [chord] brackets
    private bool _showChords = true;
    private bool _hasAppliedChanges = false; // Track if Apply was clicked at least once

    // Dragging state
    private enum DragTarget { None, LoopIn, LoopOut }
    private DragTarget _dragging = DragTarget.None;
    private const double MarkerHitZone = 8.0; // pixels

    // Playback position bar
    private readonly DispatcherTimer _playbackTimer;
    private Line? _playbackLineClick;
    private Line? _playbackLineMix;

    // Sub-sections from granular detect
    private List<SubSectionItem> _subSections = new();

    private readonly BeatDetectionService _beatService = new();

    /// <summary>
    /// Global lines-per-slide default (from PresenterSettings.LinesPerSlide). Used when a section
    /// has no <see cref="WorshipPlayer.Core.Models.Section.LinesPerSlideOverride"/>. Optional — the
    /// caller may set it after construction; defaults to 4 so behavior is sane if unset.
    /// </summary>
    public int GlobalLinesPerSlide { get; set; } = 4;

    /// <summary>
    /// Lead time (ms) subtracted from auto-computed slide times (PresenterSettings.SlideLeadMs).
    /// Default 500; the caller may override after construction.
    /// </summary>
    public int SlideLeadMs { get; set; } = 500;

    public WaveformEditorWindow(Song song, SectionViewModel section,
        BeatDetectionService.BeatGrid? grid, AudioEngine? audioEngine = null)
    {
        InitializeComponent();
        _song = song;
        _section = section;
        _grid = grid;
        _audioEngine = audioEngine;

        _loopInMs = section.LoopInMs;
        _loopOutMs = section.LoopOutMs;

        // Show wider context: 2 bars before and after
        double barMs = song.BarDurationMs;
        _displayStartMs = Math.Max(0, section.LoopInMs - barMs * 2);
        _displayEndMs = section.LoopOutMs + barMs * 2;

        SectionTitle.Text = $"Section: {section.Name}";
        SectionInfo.Text = $"Bars {section.StartBar}-{section.EndBar} | " +
                          $"BPM: {song.Bpm:F2} | Bar: {song.BarDurationMs:F0} ms";

        UpdateLoopPointDisplay();

        // Load lyrics text for this section
        LyricsTextBox.Text = section.Text ?? "";
        _lyricsFullText = section.Text ?? ""; // Store full text with chords

        // Reflect the section's per-section lines-per-slide override in the combo (Global = null).
        InitSectionLinesPerSlideCombo();

        // Playback position timer (30fps)
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        _playbackTimer.Start();

        Loaded += (s, e) => LoadWaveforms();
        Closed += (s, e) => _playbackTimer.Stop();
        PreviewKeyDown += WaveformEditor_KeyDown;
    }

    /// <summary>
    /// Global keyboard handler — captures SPACE for tap timing mode.
    /// Uses PreviewKeyDown to intercept BEFORE buttons handle it.
    /// </summary>
    private void WaveformEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _tapTimingMode)
        {
            HandleTapTimingKeyPress();
            e.Handled = true; // Prevent button activation
        }
    }

    // Overload for backward compatibility (without audioEngine)
    public WaveformEditorWindow(Song song, SectionViewModel section,
        BeatDetectionService.BeatGrid? grid)
        : this(song, section, grid, null) { }



    // ─── SLIDE TIMING ───────────────────────────────────────────────────

    // ─── PER-SECTION LINES-PER-SLIDE OVERRIDE ───────────────────────────
    private bool _suppressSectionLinesCombo = false;

    /// <summary>Select the combo item matching the section's current override (null = Global).</summary>
    private void InitSectionLinesPerSlideCombo()
    {
        if (SectionLinesPerSlideCombo == null) return;
        _suppressSectionLinesCombo = true;
        try
        {
            string want = _section.LinesPerSlideOverride?.ToString() ?? "";
            foreach (var obj in SectionLinesPerSlideCombo.Items)
            {
                if (obj is ComboBoxItem item && (item.Tag as string ?? "") == want)
                {
                    SectionLinesPerSlideCombo.SelectedItem = item;
                    return;
                }
            }
            SectionLinesPerSlideCombo.SelectedIndex = 0; // Global fallback
        }
        finally { _suppressSectionLinesCombo = false; }
    }

    private void SectionLinesPerSlide_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSectionLinesCombo || _section == null) return;

        int? overrideVal = null;
        if (SectionLinesPerSlideCombo?.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && !string.IsNullOrEmpty(tag) && int.TryParse(tag, out int n))
            overrideVal = Math.Clamp(n, 2, 4);

        _section.LinesPerSlideOverride = overrideVal;

        // Re-group this section's slides at the new line count and refresh the view. Manual slide
        // timings are preserved by RecalculateSlideTimings' manual-timing guard.
        _section.Text = _lyricsFullText;
        RecalculateSlideTimings();
        _hasAppliedChanges = true;
        DrawWaveforms();
    }

    /// <summary>
    /// Recalculate chord chart slide timing from the section text.
    /// Groups lyrics into slides via SlideGroupingService (lines-per-slide + per-section override)
    /// and divides section time evenly across slides. Timing is stored SEPARATELY in
    /// chordChart.slides[] — text stays clean. Also generates MIDI cues for Presenter auto-advance.
    /// </summary>
    private void RecalculateSlideTimings()
    {
        string sectionText = _section.Text ?? "";
        if (string.IsNullOrWhiteSpace(sectionText)) return;

        // Group lyrics into slide blocks using the configured lines-per-slide (per-section override
        // wins over the global default). This honors authored blank-line breaks AND sub-splits any
        // block that exceeds the line count — the single source of grouping truth.
        int effectiveLinesPerSlide = _section.LinesPerSlideOverride ?? GlobalLinesPerSlide;
        var groupedBlocks = WorshipPlayer.Core.Services.SlideGroupingService
            .GroupSection(sectionText, effectiveLinesPerSlide);
        var blocks = groupedBlocks.Select(b => b.Text).ToList();

        if (blocks.Count == 0) return;

        // Check if slides already have manual timing (from Tap Slide Timing)
        // If so, DON'T overwrite them
        if (_song.ChordChart != null)
        {
            var existingSlides = _song.ChordChart.Slides
                .Where(s => s.SectionName.Equals(_section.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (existingSlides.Any(s => s.ManualTiming))
            {
                // Manual timing exists — don't overwrite, just ensure slide count matches
                if (existingSlides.Count == blocks.Count)
                    return; // All good, keep manual timing
                // If slide count changed (user added/removed empty lines), 
                // we need to recalculate — clear manual timing
            }
        }

        double sectionStartMs = _section.LoopInMs;
        double sectionEndMs = _section.LoopOutMs;
        double sectionDuration = sectionEndMs - sectionStartMs;

        // Ensure chord chart data exists
        if (_song.ChordChart == null)
            _song.ChordChart = new WorshipPlayer.Core.Models.ChordChartData();

        // Remove existing slides for this section
        _song.ChordChart.Slides.RemoveAll(s =>
            s.SectionName.Equals(_section.Name, StringComparison.OrdinalIgnoreCase));

        // Divide section time evenly by slide count
        for (int i = 0; i < blocks.Count; i++)
        {
            double startMs = sectionStartMs + (sectionDuration * i / blocks.Count);
            double endMs = sectionStartMs + (sectionDuration * (i + 1) / blocks.Count);

            _song.ChordChart.Slides.Add(new WorshipPlayer.Core.Models.ChartSlide
            {
                SectionName = _section.Name,
                SlideIndex = i,
                StartMs = startMs,
                EndMs = endMs,
                ManualTiming = false
            });
        }

        // Sort all slides by startMs
        _song.ChordChart.Slides.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));

        // Generate MIDI "Next Slide" cues at each slide transition
        _song.MidiCues.RemoveAll(c =>
            c.TimestampMs >= sectionStartMs &&
            c.TimestampMs <= sectionEndMs &&
            c.Label.StartsWith("[Auto]"));

        for (int i = 1; i < blocks.Count; i++)
        {
            double cueMs = sectionStartMs + (sectionDuration * i / blocks.Count);
            _song.MidiCues.Add(new WorshipPlayer.Core.Models.MidiCue
            {
                TimestampMs = cueMs,
                Action = WorshipPlayer.Core.Models.MidiCueAction.NextSlide,
                Velocity = 127,
                Label = $"[Auto] Slide {i + 1} - {_section.Name}",
                Enabled = true
            });
        }
        _song.MidiCues.Sort((a, b) => a.TimestampMs.CompareTo(b.TimestampMs));
    }

    // ─── TAP SLIDE TIMING MODE ──────────────────────────────────────────

    private bool _tapTimingMode = false;
    private List<double> _tapTimestamps = new();
    private int _tapExpectedCount = 0; // Number of slide breaks (empty lines)

    private void TapSlideTimingBtn_Click(object sender, RoutedEventArgs e)
    {
        // Count slide breaks in the text
        string sectionText = _showChords ? (LyricsTextBox.Text ?? "") : _lyricsFullText;
        var blocks = sectionText.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.None)
            .Where(b => !string.IsNullOrWhiteSpace(b)).ToList();

        if (blocks.Count <= 1)
        {
            MessageBox.Show("No slide breaks found in the lyrics text.\n\n" +
                "Add empty lines between slide blocks first:\n" +
                "  Line 1 of slide 1\n" +
                "  Line 2 of slide 1\n" +
                "  (empty line = break)\n" +
                "  Line 1 of slide 2",
                "No Slide Breaks", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _tapExpectedCount = blocks.Count - 1; // Number of transitions (not slides)
        _tapTimestamps.Clear();

        TapTimingPanel.Visibility = Visibility.Visible;
        TapTimingInstruction.Text = $"TAP SLIDE TIMING: Press SPACE at each slide break ({_tapExpectedCount} taps needed)";
        TapTimingStatus.Text = $"Ready. Click START to begin playback, then tap SPACE at each slide transition.\n" +
            $"Slides: {blocks.Count} | Transitions to tap: {_tapExpectedCount}";
    }

    private void TapTimingStart_Click(object sender, RoutedEventArgs e)
    {
        if (_audioEngine == null) return;

        _tapTimingMode = true;
        _tapTimestamps.Clear();

        // Apply current text changes before starting
        _section.LoopInMs = _loopInMs;
        _section.LoopOutMs = _loopOutMs;
        if (_showChords)
            _section.Text = LyricsTextBox.Text ?? "";
        else
            _section.Text = _lyricsFullText;
        RecalculateSlideTimings();

        // Auto-open Preview Chord Chart window
        OpenPreviewChartForTapTiming();

        // Start playback from section start
        _audioEngine.SeekToMs(_loopInMs);
        _audioEngine.Play();
        PlayBtn.Content = "PAUSE";

        TapTimingStartBtn.Content = "RECORDING...";
        TapTimingStartBtn.Background = System.Windows.Media.Brushes.Red;
        TapTimingInstruction.Text = "RECORDING — Press SPACE at each slide transition!";
        TapTimingStatus.Text = $"Taps: 0 / {_tapExpectedCount} — Listening for SPACE key...";

        // Move focus to the window itself (not a button) so SPACE doesn't trigger buttons
        this.Focus();
        Keyboard.Focus(this);
    }

    /// <summary>
    /// Open the Preview Chord Chart for tap timing validation.
    /// Shows current section's slides and advances on each tap.
    /// </summary>
    private void OpenPreviewChartForTapTiming()
    {
        if (_song.ChordChart == null)
            _song.ChordChart = new WorshipPlayer.Core.Models.ChordChartData();

        // Get slides for this section
        var sectionSlides = _song.ChordChart.Slides
            .Where(s => s.SectionName.Equals(_section.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.StartMs)
            .ToList();

        // If no slides, create one for the whole section
        if (sectionSlides.Count == 0)
        {
            sectionSlides.Add(new WorshipPlayer.Core.Models.ChartSlide
            {
                SectionName = _section.Name,
                SlideIndex = 0,
                StartMs = _loopInMs,
                EndMs = _loopOutMs
            });
        }

        var previewChart = new WorshipPlayer.Core.Models.ChordChartData();
        previewChart.Slides.AddRange(sectionSlides);

        // Close existing preview window if open
        if (_previewChartWindow != null)
        {
            try { _previewChartWindow.Close(); } catch { }
        }

        _previewChartWindow = new ChordChartWindow();
        _previewChartWindow.SetData(_song, previewChart,
            () => _audioEngine?.GetPositionMs() ?? 0);
        _previewChartWindow.Title = $"Tap Timing Preview — {_section.Name} (tap SPACE to advance)";
        _previewChartWindow.Show();
    }

    private void TapTimingDone_Click(object sender, RoutedEventArgs e)
    {
        FinishTapTiming(save: true);
    }

    private void TapTimingCancel_Click(object sender, RoutedEventArgs e)
    {
        FinishTapTiming(save: false);
    }

    private void FinishTapTiming(bool save)
    {
        _tapTimingMode = false;

        // Stop playback
        if (_audioEngine != null && _audioEngine.IsPlaying)
        {
            _audioEngine.Stop();
            PlayBtn.Content = "PLAY";
        }

        // Reset UI
        TapTimingStartBtn.Content = "START";
        TapTimingStartBtn.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(142, 68, 173));
        TapTimingPanel.Visibility = Visibility.Collapsed;

        if (save && _tapTimestamps.Count > 0)
        {
            ApplyTapTimestamps();
        }
    }

    /// <summary>
    /// Handle SPACE key tap during Tap Slide Timing mode.
    /// Called from the Window's KeyDown handler.
    /// </summary>
    private void HandleTapTimingKeyPress()
    {
        if (!_tapTimingMode || _audioEngine == null) return;

        double currentMs = _audioEngine.GetPositionMs();

        // Only record if within the section range
        if (currentMs < _loopInMs || currentMs > _loopOutMs) return;

        _tapTimestamps.Add(currentMs);

        int tapsRemaining = _tapExpectedCount - _tapTimestamps.Count;
        TapTimingStatus.Text = $"Taps: {_tapTimestamps.Count} / {_tapExpectedCount} — " +
            $"Last tap at {MsToTimecode(currentMs)}" +
            (tapsRemaining > 0 ? $" — {tapsRemaining} more..." : " — DONE! Click DONE to save.");

        // Visual feedback
        TapTimingInstruction.Text = tapsRemaining > 0
            ? $"TAP! ({_tapTimestamps.Count}/{_tapExpectedCount}) — Next slide transition..."
            : $"All {_tapExpectedCount} taps recorded! Click DONE to save.";

        // Advance the Preview Chord Chart to the next slide
        if (_previewChartWindow != null && _previewChartWindow.IsVisible)
        {
            int nextSlideIndex = _tapTimestamps.Count; // Slide index = number of taps so far
            _previewChartWindow.JumpToSlide(nextSlideIndex);
        }

        // If all taps done, auto-finish
        if (_tapTimestamps.Count >= _tapExpectedCount)
        {
            TapTimingInstruction.Text = "All transitions tapped! Click DONE to save, or keep tapping to replace.";
        }
    }

    /// <summary>
    /// Apply the recorded tap timestamps to chord chart slides.
    /// </summary>
    private void ApplyTapTimestamps()
    {
        if (_tapTimestamps.Count == 0) return;

        // Sort timestamps (in case user tapped out of order somehow)
        _tapTimestamps.Sort();

        // Count slide blocks
        string sectionText = _showChords ? (LyricsTextBox.Text ?? "") : _lyricsFullText;
        var blocks = sectionText.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.None)
            .Where(b => !string.IsNullOrWhiteSpace(b)).ToList();

        // Build slide timing: first slide starts at section start, subsequent at tap points
        var slideTimings = new List<double> { _loopInMs };
        for (int i = 0; i < Math.Min(_tapTimestamps.Count, blocks.Count - 1); i++)
        {
            slideTimings.Add(_tapTimestamps[i]);
        }

        // Ensure chord chart data exists
        if (_song.ChordChart == null)
            _song.ChordChart = new WorshipPlayer.Core.Models.ChordChartData();

        // Remove existing slides for this section
        _song.ChordChart.Slides.RemoveAll(s =>
            s.SectionName.Equals(_section.Name, StringComparison.OrdinalIgnoreCase));

        // Add slides with tapped timing
        for (int i = 0; i < blocks.Count; i++)
        {
            double startMs = (i < slideTimings.Count) ? slideTimings[i] : slideTimings.Last();
            double endMs = (i + 1 < slideTimings.Count) ? slideTimings[i + 1] : _loopOutMs;

            _song.ChordChart.Slides.Add(new WorshipPlayer.Core.Models.ChartSlide
            {
                SectionName = _section.Name,
                SlideIndex = i,
                StartMs = startMs,
                EndMs = endMs,
                ManualTiming = true // Mark as manually tapped — won't be overwritten
            });
        }

        // Sort all slides
        _song.ChordChart.Slides.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));

        // Generate MIDI cues at each tapped transition
        _song.MidiCues.RemoveAll(c =>
            c.TimestampMs >= _loopInMs &&
            c.TimestampMs <= _loopOutMs &&
            c.Label.StartsWith("[Auto]"));

        for (int i = 0; i < _tapTimestamps.Count && i < blocks.Count - 1; i++)
        {
            _song.MidiCues.Add(new WorshipPlayer.Core.Models.MidiCue
            {
                TimestampMs = _tapTimestamps[i],
                Action = WorshipPlayer.Core.Models.MidiCueAction.NextSlide,
                Velocity = 127,
                Label = $"[Auto] Slide {i + 2} - {_section.Name}",
                Enabled = true
            });
        }
        _song.MidiCues.Sort((a, b) => a.TimestampMs.CompareTo(b.TimestampMs));

        // Also mark changes as applied
        _hasAppliedChanges = true;

        // Update the preview chart with new timing (so auto-advance works immediately)
        if (_previewChartWindow != null && _previewChartWindow.IsVisible)
        {
            var updatedSlides = _song.ChordChart.Slides
                .Where(s => s.SectionName.Equals(_section.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.StartMs)
                .ToList();

            var previewChart = new WorshipPlayer.Core.Models.ChordChartData();
            previewChart.Slides.AddRange(updatedSlides);

            _previewChartWindow.SetData(_song, previewChart,
                () => _audioEngine?.GetPositionMs() ?? 0);
        }

        MessageBox.Show(
            $"Slide timing saved!\n\n" +
            $"Transitions tapped: {Math.Min(_tapTimestamps.Count, blocks.Count - 1)}\n" +
            $"Slides: {blocks.Count}\n\n" +
            "Timing marked as manual (won't be overwritten by re-analysis).\n" +
            "Click 'Preview Chart' to verify auto-advance.",
            "Tap Timing Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ─── CHORD TOGGLE (original) ─────────────────────────────────────────

    private void ShowChordsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ShowChordsToggle.IsChecked == true)
        {
            // Show chords — restore full text with [chord] brackets
            _showChords = true;
            LyricsTextBox.Text = _lyricsFullText;
            ShowChordsToggle.Content = "Show Chords";
            ChordFormatHint.Text = "Inline format: [B]lyrics [E]here";
        }
        else
        {
            // Hide chords — show lyrics only (strip [chord] brackets)
            _showChords = false;
            // Save current full text before stripping
            _lyricsFullText = LyricsTextBox.Text ?? "";

            // Check if text actually has chords
            if (!_lyricsFullText.Contains('['))
            {
                ShowChordsToggle.Content = "No Chords";
                ChordFormatHint.Text = "No [chord] brackets found in text";
                // Toggle back since there's nothing to hide
                ShowChordsToggle.IsChecked = true;
                _showChords = true;
                return;
            }

            var parser = new WorshipPlayer.Core.Services.ChordProParserService();
            LyricsTextBox.Text = parser.StripChords(_lyricsFullText);
            ShowChordsToggle.Content = "Chords Hidden";
            ChordFormatHint.Text = "Editing lyrics only (chords preserved)";
        }
    }



    // ─── WAVEFORM LOADING ─────────────────────────────────────────────────

    // Cache full waveforms (loaded once, resampled on zoom/pan)
    private float[]? _clickWaveformFull;
    private float[]? _mixWaveformFull;
    private float[]? _vocalWaveformFull;
    private double _fullWaveformDurationMs;
    private int _fullWaveformSampleRate = 22050;
    private bool _waveformsLoaded = false;

    private void LoadWaveforms()
    {
        if (!_waveformsLoaded)
        {
            // Show loading indicator
            WaveformLoadingText.Visibility = Visibility.Visible;
            WaveformLoadingText.Text = "Loading waveforms...";

            // Load on background thread to prevent UI freeze
            Task.Run(() =>
            {
                LoadFullWaveformsFromDisk();
                _waveformsLoaded = true;

                // Update UI on dispatcher thread
                Dispatcher.Invoke(() =>
                {
                    WaveformLoadingText.Visibility = Visibility.Collapsed;
                    ResampleForDisplay();
                    DrawWaveforms();
                });
            });
        }
        else
        {
            // Already loaded — just resample for new display range (instant)
            ResampleForDisplay();
            DrawWaveforms();
        }
    }

    private void LoadFullWaveformsFromDisk()
    {
        var clickPath = _song.Tracks
            .Where(t => t.Label.Contains("Click", StringComparison.OrdinalIgnoreCase))
            .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
            .FirstOrDefault(System.IO.File.Exists);

        if (clickPath != null)
            _clickWaveformFull = LoadEntireAudioFile(clickPath, out _fullWaveformDurationMs);

        // For mix waveform: load only drums + bass + first harmonic stem (not ALL stems)
        // This is 90% visually identical but 3-5x faster than loading every track
        var priorityLabels = new[] { "Drum", "Bass", "Guitar", "Piano" };
        var mixStems = new List<string>();

        foreach (var label in priorityLabels)
        {
            var path = _song.Tracks
                .Where(t => t.Label.Contains(label, StringComparison.OrdinalIgnoreCase) &&
                           !t.Label.Contains("Click", StringComparison.OrdinalIgnoreCase))
                .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
                .FirstOrDefault(System.IO.File.Exists);
            if (path != null)
                mixStems.Add(path);
            if (mixStems.Count >= 3) break; // 3 stems is enough for waveform display
        }

        // Fallback: if no priority stems found, use first 3 non-click/cue tracks
        if (mixStems.Count == 0)
        {
            mixStems = _song.Tracks
                .Where(t => !t.Label.Contains("Click", StringComparison.OrdinalIgnoreCase) &&
                           !t.Label.Contains("Cue", StringComparison.OrdinalIgnoreCase))
                .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
                .Where(System.IO.File.Exists)
                .Take(3)
                .ToList();
        }

        if (mixStems.Count > 0)
        {
            float[]? combined = null;
            foreach (var stem in mixStems)
            {
                double dur;
                var data = LoadEntireAudioFile(stem, out dur);
                if (data == null) continue;
                if (combined == null)
                    combined = data;
                else
                    for (int i = 0; i < Math.Min(combined.Length, data.Length); i++)
                        combined[i] += data[i];
                if (dur > _fullWaveformDurationMs) _fullWaveformDurationMs = dur;
            }
            if (combined != null)
            {
                float max = combined.Max(Math.Abs);
                if (max > 0)
                    for (int i = 0; i < combined.Length; i++)
                        combined[i] /= max;
                _mixWaveformFull = combined;
            }
        }

        var vocalPath = _song.Tracks
            .Where(t => t.Label.Contains("Vocal", StringComparison.OrdinalIgnoreCase) &&
                        !t.Label.Contains("Backing", StringComparison.OrdinalIgnoreCase))
            .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
            .FirstOrDefault(System.IO.File.Exists);

        if (vocalPath != null)
        {
            double dur;
            _vocalWaveformFull = LoadEntireAudioFile(vocalPath, out dur);
        }
    }

    private float[]? LoadEntireAudioFile(string filePath, out double durationMs)
    {
        durationMs = 0;
        try
        {
            using var reader = new AudioFileReader(filePath);
            int sr = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;
            durationMs = reader.TotalTime.TotalMilliseconds;
            _fullWaveformSampleRate = sr;

            // Downsample to 200 samples per second for display (lightweight)
            int displayRate = 200;
            int totalDisplaySamples = (int)(durationMs / 1000.0 * displayRate);
            var display = new float[totalDisplaySamples];

            int totalSamples = (int)(reader.Length / sizeof(float));
            int samplesPerPixel = Math.Max(1, totalSamples / channels / totalDisplaySamples);
            var buffer = new float[samplesPerPixel * channels];

            for (int i = 0; i < totalDisplaySamples; i++)
            {
                int read = reader.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                float peak = 0;
                for (int j = 0; j < read; j += channels)
                {
                    float val = Math.Abs(buffer[j]);
                    if (val > peak) peak = val;
                }
                display[i] = peak;
            }
            return display;
        }
        catch { return null; }
    }

    private void ResampleForDisplay()
    {
        _clickWaveform = ResampleRange(_clickWaveformFull, _displayStartMs, _displayEndMs);
        _mixWaveform = ResampleRange(_mixWaveformFull, _displayStartMs, _displayEndMs);
        _vocalWaveform = ResampleRange(_vocalWaveformFull, _displayStartMs, _displayEndMs);
    }

    private float[] ResampleRange(float[]? fullData, double startMs, double endMs)
    {
        if (fullData == null || fullData.Length == 0 || _fullWaveformDurationMs <= 0)
            return Array.Empty<float>();

        // Map display range to indices in the full array
        double startRatio = Math.Clamp(startMs / _fullWaveformDurationMs, 0, 1);
        double endRatio = Math.Clamp(endMs / _fullWaveformDurationMs, 0, 1);
        int startIdx = (int)(startRatio * fullData.Length);
        int endIdx = (int)(endRatio * fullData.Length);

        if (endIdx <= startIdx) return Array.Empty<float>();

        int rangeLength = endIdx - startIdx;
        int displayWidth = 1000; // Target display samples
        var result = new float[displayWidth];

        double step = (double)rangeLength / displayWidth;
        for (int i = 0; i < displayWidth; i++)
        {
            int idx = startIdx + (int)(i * step);
            if (idx < fullData.Length)
                result[i] = fullData[idx];
        }
        return result;
    }

    // ─── DRAWING ──────────────────────────────────────────────────────────

    private void DrawWaveforms()
    {
        // Reset playback lines (they'll be re-added by the timer)
        _playbackLineClick = null;
        _playbackLineMix = null;

        DrawWaveform(ClickWaveformCanvas, _clickWaveform, new SolidColorBrush(Color.FromRgb(30, 144, 255))); // Blue
        DrawWaveform(MixWaveformCanvas, _mixWaveform, Brushes.DodgerBlue);

        // Draw vocal overlay on top of mix waveform (orange, semi-transparent)
        if (_vocalWaveform.Length > 0)
        {
            DrawVocalOverlay(MixWaveformCanvas, _vocalWaveform);
        }
    }

    /// <summary>
    /// Lightweight redraw: only updates the Loop IN/OUT marker positions
    /// without redrawing the entire waveform. Used during dragging for smooth performance.
    /// </summary>
    private void RedrawMarkersOnly()
    {
        double totalMs = _displayEndMs - _displayStartMs;

        // Update markers on both canvases
        foreach (var canvas in new[] { ClickWaveformCanvas, MixWaveformCanvas })
        {
            double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 1000;
            double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 120;

            // Remove old marker lines (tagged)
            var toRemove = canvas.Children.OfType<Line>()
                .Where(l => l.Tag is string s && (s == "LoopIn" || s == "LoopOut")).ToList();
            foreach (var line in toRemove) canvas.Children.Remove(line);

            // Remove old highlight rectangle
            var rects = canvas.Children.OfType<Rectangle>()
                .Where(r => r.Tag is string s && s == "LoopRegion").ToList();
            foreach (var r in rects) canvas.Children.Remove(r);

            // Draw new markers
            double inX = (_loopInMs - _displayStartMs) / totalMs * width;
            double outX = (_loopOutMs - _displayStartMs) / totalMs * width;

            // Highlighted region
            var region = new Rectangle
            {
                Width = Math.Abs(outX - inX), Height = height,
                Fill = new SolidColorBrush(Color.FromArgb(25, 74, 144, 217)),
                RenderTransform = new TranslateTransform(Math.Min(inX, outX), 0),
                Tag = "LoopRegion"
            };
            canvas.Children.Add(region);

            canvas.Children.Add(new Line
            {
                X1 = inX, X2 = inX, Y1 = 0, Y2 = height,
                Stroke = Brushes.LimeGreen, StrokeThickness = 2.5, Tag = "LoopIn"
            });
            canvas.Children.Add(new Line
            {
                X1 = outX, X2 = outX, Y1 = 0, Y2 = height,
                Stroke = Brushes.Red, StrokeThickness = 2.5, Tag = "LoopOut"
            });
        }
    }

    private void DrawVocalOverlay(Canvas canvas, float[] waveform)
    {
        double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 1000;
        double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 120;
        double centerY = height / 2;

        var polyUp = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 140, 50)), // Orange
            StrokeThickness = 1.2,
            Opacity = 0.7
        };
        var polyDown = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 140, 50)),
            StrokeThickness = 1.0,
            Opacity = 0.5
        };

        double samplesPerPx = (double)waveform.Length / width;
        for (int px = 0; px < (int)width; px++)
        {
            int idx = (int)(px * samplesPerPx);
            if (idx >= waveform.Length) break;
            float val = waveform[idx];
            polyUp.Points.Add(new Point(px, centerY - val * centerY * 0.85));
            polyDown.Points.Add(new Point(px, centerY + val * centerY * 0.85));
        }

        canvas.Children.Add(polyUp);
        canvas.Children.Add(polyDown);
    }

    private void DrawWaveform(Canvas canvas, float[] waveform, Brush color)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 1000;
        double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 120;
        double centerY = height / 2;
        double totalMs = _displayEndMs - _displayStartMs;

        // ── Beat grid lines ──────────────────────────────────────────────────────────────
        // Draw an EVENLY-SPACED grid, one bright (light-blue) line per bar downbeat, anchored to
        // the click-track downbeat.
        //
        // IMPORTANT: the beat spacing is taken from the DETECTED beat positions (their median
        // interval), NOT from the stored BPM field. The stored BPM can be rounded/slightly off
        // (e.g. 98 stored vs ~97.0 actual = 6ms per beat), and that error ACCUMULATES bar over bar,
        // so by later sections (VERSE 1) a BPM-derived grid drifts seconds away from the clicks.
        // The detected beat positions are evenly spaced at the TRUE tempo, so using their median
        // interval keeps the even grid locked to the real clicks across the whole song.
        int beatsPerBar = _grid?.TimeSignatureNumerator ?? _song.TimeSignatureNumerator;
        if (beatsPerBar <= 0) beatsPerBar = 4;

        // Single source of truth shared with the Snap buttons.
        var (beatMs, anchorMs) = GetEvenGrid();

        if (beatMs > 0)
        {
            double barMs = beatMs * beatsPerBar;

            // Step back from the anchor to the first beat at/after the visible range start, keeping
            // exact bar/beat phase relative to the anchor so Beat 1 lines stay on the downbeats.
            double firstBeat = anchorMs + Math.Floor((_displayStartMs - anchorMs) / beatMs) * beatMs;

            int beatIdx = (int)Math.Round((firstBeat - anchorMs) / beatMs);
            for (double beatTime = firstBeat; beatTime <= _displayEndMs; beatTime += beatMs, beatIdx++)
            {
                if (beatTime < _displayStartMs) continue;
                double x = (beatTime - _displayStartMs) / totalMs * width;

                // Beat 1 of the bar = an exact whole number of bars from the anchor (non-negative
                // modulo so beats before the anchor are classified correctly too).
                int phase = ((beatIdx % beatsPerBar) + beatsPerBar) % beatsPerBar;
                bool isDownbeat = phase == 0;

                canvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = 0, Y2 = height,
                    Stroke = isDownbeat ? new SolidColorBrush(Color.FromRgb(135, 206, 250))  // Sky blue = Beat 1
                                        : new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                    StrokeThickness = isDownbeat ? 2.0 : 0.8
                });

                // Ruler subdivisions between this beat and the next (1/8, 1/16, 1/32 ticks).
                double subdivLen = beatMs / 8.0;
                for (int s = 1; s <= 7; s++)
                {
                    double subdivMs = beatTime + s * subdivLen;
                    if (subdivMs < _displayStartMs || subdivMs > _displayEndMs) continue;
                    double sx = (subdivMs - _displayStartMs) / totalMs * width;

                    double tickTop, tickBottom;
                    byte alpha;
                    if (s == 4) { tickTop = height * 0.25; tickBottom = height * 0.75; alpha = 35; }
                    else if (s == 2 || s == 6) { tickTop = height * 0.35; tickBottom = height * 0.65; alpha = 25; }
                    else { tickTop = height * 0.42; tickBottom = height * 0.58; alpha = 15; }

                    canvas.Children.Add(new Line
                    {
                        X1 = sx, X2 = sx, Y1 = tickTop, Y2 = tickBottom,
                        Stroke = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
                        StrokeThickness = 0.5
                    });
                }
            }
        }

        // Waveform
        if (waveform.Length > 0)
        {
            var polyUp = new Polyline { Stroke = color, StrokeThickness = 1, Opacity = 0.8 };
            var polyDown = new Polyline { Stroke = color, StrokeThickness = 1, Opacity = 0.5 };
            double samplesPerPx = (double)waveform.Length / width;

            for (int px = 0; px < (int)width; px++)
            {
                int idx = (int)(px * samplesPerPx);
                if (idx >= waveform.Length) break;
                float val = waveform[idx];
                polyUp.Points.Add(new Point(px, centerY - val * centerY * 0.9));
                polyDown.Points.Add(new Point(px, centerY + val * centerY * 0.9));
            }
            canvas.Children.Add(polyUp);
            canvas.Children.Add(polyDown);
        }

        // Highlighted region
        double inX = (_loopInMs - _displayStartMs) / totalMs * width;
        double outX = (_loopOutMs - _displayStartMs) / totalMs * width;
        canvas.Children.Add(new Rectangle
        {
            Width = Math.Abs(outX - inX), Height = height,
            Fill = new SolidColorBrush(Color.FromArgb(25, 74, 144, 217)),
            RenderTransform = new TranslateTransform(Math.Min(inX, outX), 0)
        });

        // Loop IN marker (green)
        canvas.Children.Add(new Line
        {
            X1 = inX, X2 = inX, Y1 = 0, Y2 = height,
            Stroke = Brushes.LimeGreen, StrokeThickness = 2.5,
            Tag = "LoopIn"
        });

        // Loop OUT marker (red)
        canvas.Children.Add(new Line
        {
            X1 = outX, X2 = outX, Y1 = 0, Y2 = height,
            Stroke = Brushes.Red, StrokeThickness = 2.5,
            Tag = "LoopOut"
        });

        // Sub-section markers (orange dashed)
        foreach (var sub in _subSections)
        {
            double subX = (sub.StartMs - _displayStartMs) / totalMs * width;
            canvas.Children.Add(new Line
            {
                X1 = subX, X2 = subX, Y1 = 0, Y2 = height,
                Stroke = Brushes.Orange, StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            });
        }
    }



    // ─── DRAGGABLE MARKERS ────────────────────────────────────────────────

    // Pan/scroll state
    private bool _isPanning = false;
    private double _panStartX;
    private double _panStartDisplayStart;
    private double _panStartDisplayEnd;

    private void WaveformCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var canvas = sender as Canvas;
        if (canvas == null) return;
        double x = e.GetPosition(canvas).X;
        double width = canvas.ActualWidth;
        double totalMs = _displayEndMs - _displayStartMs;

        double inX = (_loopInMs - _displayStartMs) / totalMs * width;
        double outX = (_loopOutMs - _displayStartMs) / totalMs * width;

        if (_isPanning)
        {
            // Panning the view — shift display range
            double deltaX = x - _panStartX;
            double deltaMs = -deltaX / width * totalMs;
            _displayStartMs = _panStartDisplayStart + deltaMs;
            _displayEndMs = _panStartDisplayEnd + deltaMs;
            // Clamp to prevent scrolling past start
            if (_displayStartMs < 0)
            {
                _displayEndMs -= _displayStartMs;
                _displayStartMs = 0;
            }
            LoadWaveforms();
            e.Handled = true;
            return;
        }

        if (_dragging != DragTarget.None)
        {
            // Currently dragging — move the marker
            double ms = _displayStartMs + (x / width) * totalMs;

            // Snap to beat subdivision while dragging — use actual positions if available
            // Each beat is divided into 8 segments (32nd notes) for fine control
            if (_song.BeatPositionsMs != null && _song.BeatPositionsMs.Count > 1)
            {
                // Build subdivision grid from actual beat positions
                var subdivisions = new List<double>();
                for (int i = 0; i < _song.BeatPositionsMs.Count - 1; i++)
                {
                    double beatStart = _song.BeatPositionsMs[i];
                    double beatEnd = _song.BeatPositionsMs[i + 1];
                    double subdivLen = (beatEnd - beatStart) / 8.0;
                    for (int s = 0; s < 8; s++)
                        subdivisions.Add(beatStart + s * subdivLen);
                }
                // Add last beat
                if (_song.BeatPositionsMs.Count > 0)
                    subdivisions.Add(_song.BeatPositionsMs[^1]);

                // Snap to nearest subdivision
                double nearest = subdivisions.OrderBy(b => Math.Abs(b - ms)).First();
                ms = nearest;
            }
            else if (_grid != null && _grid.BeatIntervalMs > 0)
            {
                // Mathematical grid with 32nd-note subdivisions
                double subdivMs = _grid.BeatIntervalMs / 8.0;
                double elapsed = ms - _grid.GridAnchorMs;
                int subdivIdx = (int)Math.Round(elapsed / subdivMs);
                ms = _grid.GridAnchorMs + subdivIdx * subdivMs;
            }

            ms = Math.Clamp(ms, _displayStartMs, _displayEndMs);

            if (_dragging == DragTarget.LoopIn)
                _loopInMs = ms;
            else
                _loopOutMs = ms;

            UpdateLoopPointDisplay();
            // Only redraw markers, not the full waveform (prevents lag while dragging)
            RedrawMarkersOnly();
            ShowDragTooltip(canvas, x, e.GetPosition(canvas).Y, ms);
            e.Handled = true;
        }
        else
        {
            // Not dragging — check if near a marker for cursor change
            if (Math.Abs(x - inX) < MarkerHitZone || Math.Abs(x - outX) < MarkerHitZone)
                canvas.Cursor = Cursors.SizeWE;
            else
                canvas.Cursor = Cursors.Arrow;

            DragTooltip.Visibility = Visibility.Collapsed;
        }
    }

    private void WaveformCanvas_MarkerMouseDown(object sender, MouseButtonEventArgs e)
    {
        var canvas = sender as Canvas;
        if (canvas == null) return;
        double x = e.GetPosition(canvas).X;
        double width = canvas.ActualWidth;
        double totalMs = _displayEndMs - _displayStartMs;

        // DOUBLE-CLICK: Seek playback to this position (snap to nearest beat)
        if (e.ClickCount == 2)
        {
            double clickedMs = _displayStartMs + (x / width) * totalMs;

            // Snap to nearest beat
            clickedMs = SnapToNearestBeatPosition(clickedMs);

            // Clamp within section bounds
            clickedMs = Math.Clamp(clickedMs, _loopInMs, _loopOutMs);

            // Seek and start playing
            if (_audioEngine != null)
            {
                _audioEngine.SeekToMs(clickedMs);
                if (!_audioEngine.IsPlaying)
                {
                    _audioEngine.Play();
                    PlayBtn.Content = "PAUSE";
                }
            }

            e.Handled = true;
            return;
        }

        double inX = (_loopInMs - _displayStartMs) / totalMs * width;
        double outX = (_loopOutMs - _displayStartMs) / totalMs * width;

        // SINGLE-CLICK: Check which marker was clicked
        if (Math.Abs(x - inX) < MarkerHitZone)
        {
            _dragging = DragTarget.LoopIn;
            canvas.CaptureMouse();
            e.Handled = true;
        }
        else if (Math.Abs(x - outX) < MarkerHitZone)
        {
            _dragging = DragTarget.LoopOut;
            canvas.CaptureMouse();
            e.Handled = true;
        }
        else
        {
            // Not near a marker — start panning
            _isPanning = true;
            _panStartX = x;
            _panStartDisplayStart = _displayStartMs;
            _panStartDisplayEnd = _displayEndMs;
            canvas.CaptureMouse();
            canvas.Cursor = Cursors.Hand;
            e.Handled = true;
        }
    }

    private void WaveformCanvas_MarkerMouseUp(object sender, MouseButtonEventArgs e)
    {
        var canvas = sender as Canvas;
        if (_dragging != DragTarget.None)
        {
            _dragging = DragTarget.None;
            canvas?.ReleaseMouseCapture();
            DragTooltip.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        else if (_isPanning)
        {
            _isPanning = false;
            canvas?.ReleaseMouseCapture();
            if (canvas != null) canvas.Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    private void ShowDragTooltip(Canvas canvas, double x, double y, double ms)
    {
        int bar = 0;
        int beat = 0;
        int subdiv = 0; // 0-7 (32nd note position within beat)
        string[] subdivNames = { "", "e", "", "&", "", "a", "", "+" };

        if (_song.BeatPositionsMs != null && _song.BeatPositionsMs.Count > 1)
        {
            // Find which beat and subdivision we're at
            int beatsPerBar = _song.TimeSignatureNumerator > 0 ? _song.TimeSignatureNumerator : 4;
            int beatIdx = 0;
            for (int i = 0; i < _song.BeatPositionsMs.Count - 1; i++)
            {
                if (_song.BeatPositionsMs[i] <= ms && ms < _song.BeatPositionsMs[i + 1])
                {
                    beatIdx = i;
                    // Calculate subdivision within this beat
                    double beatStart = _song.BeatPositionsMs[i];
                    double beatEnd = _song.BeatPositionsMs[i + 1];
                    double progress = (ms - beatStart) / (beatEnd - beatStart);
                    subdiv = (int)(progress * 8);
                    subdiv = Math.Clamp(subdiv, 0, 7);
                    break;
                }
                if (ms >= _song.BeatPositionsMs[i])
                    beatIdx = i;
            }
            bar = (beatIdx / beatsPerBar) + 1;
            beat = (beatIdx % beatsPerBar) + 1;
        }
        else if (_grid != null && _grid.BeatIntervalMs > 0)
        {
            double elapsed = ms - _grid.GridAnchorMs;
            if (elapsed < 0) elapsed = 0;
            double subdivMs = _grid.BeatIntervalMs / 8.0;
            int totalSubdivs = (int)(elapsed / subdivMs);
            int totalBeats = totalSubdivs / 8;
            subdiv = totalSubdivs % 8;
            bar = (totalBeats / _grid.TimeSignatureNumerator) + 1;
            beat = (totalBeats % _grid.TimeSignatureNumerator) + 1;
        }

        string subdivStr = subdiv > 0 ? (subdivNames[subdiv] != "" ? subdivNames[subdiv] : $".{subdiv}") : "";
        DragTooltipText.Text = $"Bar {bar}, Beat {beat}{subdivStr}";
        DragTooltip.Visibility = Visibility.Visible;
        Canvas.SetLeft(DragTooltip, Math.Clamp(x - 40, 0, canvas.ActualWidth - 100));
        Canvas.SetTop(DragTooltip, Math.Max(0, y - 25));
    }



    // ─── PLAYBACK ─────────────────────────────────────────────────────────

    private bool _isLoopPreview = false;

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioEngine == null || !_audioEngine.IsPlaying) return;

        double posMs = _audioEngine.GetPositionMs();

        // If in loop preview mode, check if we've passed the OUT marker
        if (_isLoopPreview && posMs >= _loopOutMs)
        {
            _audioEngine.SeekToMs(_loopInMs);
            posMs = _loopInMs;
        }

        // Only update playback line if within our display range
        if (posMs >= _displayStartMs && posMs <= _displayEndMs)
        {
            double width = MixWaveformCanvas.ActualWidth;
            double totalMs = _displayEndMs - _displayStartMs;
            double x = (posMs - _displayStartMs) / totalMs * width;

            double height1 = ClickWaveformCanvas.ActualHeight;
            double height2 = MixWaveformCanvas.ActualHeight;

            // Move existing playback lines (or create them once)
            if (_playbackLineClick == null)
            {
                _playbackLineClick = new Line
                {
                    Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0.9,
                    Y1 = 0, Y2 = height1
                };
                ClickWaveformCanvas.Children.Add(_playbackLineClick);
            }
            if (_playbackLineMix == null)
            {
                _playbackLineMix = new Line
                {
                    Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0.9,
                    Y1 = 0, Y2 = height2
                };
                MixWaveformCanvas.Children.Add(_playbackLineMix);
            }

            // Just update X position (no full redraw!)
            _playbackLineClick.X1 = x;
            _playbackLineClick.X2 = x;
            _playbackLineClick.Y2 = height1;
            _playbackLineClick.Visibility = Visibility.Visible;

            _playbackLineMix.X1 = x;
            _playbackLineMix.X2 = x;
            _playbackLineMix.Y2 = height2;
            _playbackLineMix.Visibility = Visibility.Visible;
        }
        else
        {
            // Hide playback lines if outside display range
            if (_playbackLineClick != null) _playbackLineClick.Visibility = Visibility.Collapsed;
            if (_playbackLineMix != null) _playbackLineMix.Visibility = Visibility.Collapsed;
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_audioEngine == null) return;

        if (_audioEngine.IsPlaying)
        {
            _audioEngine.Pause();
            PlayBtn.Content = "PLAY";
            _isLoopPreview = false;
            PlayLoopBtn.Background = new SolidColorBrush(Color.FromRgb(0, 139, 139)); // Reset color
        }
        else
        {
            // Seek to loop in point and play (one-shot, no loop)
            _isLoopPreview = false;
            PlayLoopBtn.Background = new SolidColorBrush(Color.FromRgb(0, 139, 139));
            _audioEngine.SeekToMs(_loopInMs);
            _audioEngine.Play();
            PlayBtn.Content = "PAUSE";
        }
    }

    private void PlayLoop_Click(object sender, RoutedEventArgs e)
    {
        if (_audioEngine == null) return;

        if (_isLoopPreview && _audioEngine.IsPlaying)
        {
            // Already loop-previewing — stop it
            _isLoopPreview = false;
            _audioEngine.Pause();
            PlayBtn.Content = "PLAY";
            PlayLoopBtn.Background = new SolidColorBrush(Color.FromRgb(0, 139, 139));
        }
        else
        {
            // Start loop preview
            _isLoopPreview = true;
            PlayLoopBtn.Background = new SolidColorBrush(Color.FromRgb(0, 200, 200)); // Bright cyan = active
            _audioEngine.SeekToMs(_loopInMs);
            _audioEngine.Play();
            PlayBtn.Content = "PAUSE";
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_audioEngine == null) return;
        _audioEngine.Stop();
        _isLoopPreview = false;
        PlayBtn.Content = "PLAY";
        PlayLoopBtn.Background = new SolidColorBrush(Color.FromRgb(0, 139, 139));
    }

    private ChordChartWindow? _previewChartWindow;

    private void PreviewChordChart_Click(object sender, RoutedEventArgs e)
    {
        if (_audioEngine == null) return;

        // First, apply current changes (save text + recalculate slide timing)
        _section.LoopInMs = _loopInMs;
        _section.LoopOutMs = _loopOutMs;
        if (_showChords)
            _section.Text = LyricsTextBox.Text ?? "";
        else
            _section.Text = _lyricsFullText;
        RecalculateSlideTimings();
        _hasAppliedChanges = true;

        // Build chord chart data for just this section
        if (_song.ChordChart == null)
            _song.ChordChart = new WorshipPlayer.Core.Models.ChordChartData();

        // Get slides for this section only
        var sectionSlides = _song.ChordChart.Slides
            .Where(s => s.SectionName.Equals(_section.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.StartMs)
            .ToList();

        // If no slides exist, create one for the whole section
        if (sectionSlides.Count == 0)
        {
            sectionSlides.Add(new WorshipPlayer.Core.Models.ChartSlide
            {
                SectionName = _section.Name,
                SlideIndex = 0,
                StartMs = _loopInMs,
                EndMs = _loopOutMs
            });
        }

        // Create a temporary chord chart with only this section's slides
        var previewChart = new WorshipPlayer.Core.Models.ChordChartData();
        previewChart.Slides.AddRange(sectionSlides);

        // Close existing preview window if open
        if (_previewChartWindow != null)
        {
            try { _previewChartWindow.Close(); } catch { }
        }

        // Open chord chart preview window
        _previewChartWindow = new ChordChartWindow();
        _previewChartWindow.SetData(_song, previewChart,
            () => _audioEngine?.GetPositionMs() ?? 0);
        _previewChartWindow.Title = $"Chord Chart Preview — {_section.Name}";
        _previewChartWindow.Show();

        // Start playback from section start (loop mode)
        _isLoopPreview = true;
        _audioEngine.SeekToMs(_loopInMs);
        _audioEngine.Play();
        PlayBtn.Content = "PAUSE";
        PlayLoopBtn.Background = new SolidColorBrush(Color.FromRgb(0, 200, 200));
    }



    // ─── GRANULAR AUTO-DETECT ─────────────────────────────────────────────

    private void GranularDetect_Click(object sender, RoutedEventArgs e)
    {
        if (_grid == null || _grid.BeatIntervalMs <= 0)
        {
            MessageBox.Show("No BPM/grid available. Run Full Song Auto-Detect first.",
                "No Grid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 1. Find precise first beat onset (adjust Loop IN)
        double preciseIn = FindPreciseOnset(_loopInMs, searchForward: true);
        if (preciseIn > 0) _loopInMs = _beatService.SnapToBeat(preciseIn, _grid);

        // 2. Find precise last beat before silence (adjust Loop OUT)
        double preciseOut = FindPreciseOffset(_loopOutMs, searchBackward: true);
        if (preciseOut > 0) _loopOutMs = _beatService.SnapToBeat(preciseOut, _grid);

        // 3. Try to split into sub-sections based on energy changes within this section
        _subSections.Clear();
        var splits = DetectSubSections(_loopInMs, _loopOutMs);
        if (splits.Count > 1)
        {
            foreach (var sub in splits)
                _subSections.Add(sub);

            SubSectionsList.ItemsSource = _subSections;
            SubSectionsList.Visibility = Visibility.Visible;
        }

        UpdateLoopPointDisplay();
        DrawWaveforms();

        string msg = $"Loop IN adjusted to: {MsToTimecode(_loopInMs)}\n" +
                     $"Loop OUT adjusted to: {MsToTimecode(_loopOutMs)}";
        if (splits.Count > 1)
        {
            msg += $"\n\nDetected {splits.Count} sub-sections. Starting review...";
            MessageBox.Show(msg, "Granular Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            StartReviewMode();
        }
        else
        {
            msg += "\n\nNo sub-sections detected.";
            MessageBox.Show(msg, "Granular Detection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private double FindPreciseOnset(double searchFromMs, bool searchForward)
    {
        // Load a small segment around the search point and find the first strong transient
        var mixPath = _song.Tracks
            .Where(t => !t.Label.Contains("Click", StringComparison.OrdinalIgnoreCase))
            .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
            .FirstOrDefault(System.IO.File.Exists);

        if (mixPath == null) return -1;

        try
        {
            using var reader = new AudioFileReader(mixPath);
            int sr = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            // Search within 1 bar before/after
            double barMs = _song.BarDurationMs;
            double startMs = searchForward ? searchFromMs - barMs * 0.5 : searchFromMs - barMs;
            double endMs = searchForward ? searchFromMs + barMs : searchFromMs + barMs * 0.5;

            int startSample = Math.Max(0, (int)(startMs / 1000.0 * sr) * channels);
            int length = (int)((endMs - startMs) / 1000.0 * sr) * channels;

            reader.Position = startSample * sizeof(float);
            var buffer = new float[length];
            int read = reader.Read(buffer, 0, length);

            // Find first sample above threshold (onset)
            float maxVal = buffer.Take(read).Select(Math.Abs).Max();
            float threshold = maxVal * 0.1f;

            for (int i = 0; i < read; i += channels)
            {
                if (Math.Abs(buffer[i]) > threshold)
                {
                    double onsetMs = startMs + (double)i / channels / sr * 1000.0;
                    return onsetMs;
                }
            }
        }
        catch { }
        return -1;
    }

    private double FindPreciseOffset(double searchFromMs, bool searchBackward)
    {
        var mixPath = _song.Tracks
            .Where(t => !t.Label.Contains("Click", StringComparison.OrdinalIgnoreCase))
            .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
            .FirstOrDefault(System.IO.File.Exists);

        if (mixPath == null) return -1;

        try
        {
            using var reader = new AudioFileReader(mixPath);
            int sr = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            double barMs = _song.BarDurationMs;
            double startMs = searchFromMs - barMs;
            double endMs = searchFromMs + barMs * 0.5;

            int startSample = Math.Max(0, (int)(startMs / 1000.0 * sr) * channels);
            int length = (int)((endMs - startMs) / 1000.0 * sr) * channels;

            reader.Position = startSample * sizeof(float);
            var buffer = new float[length];
            int read = reader.Read(buffer, 0, length);

            // Find last sample above threshold (before silence/decay)
            float maxVal = buffer.Take(read).Select(Math.Abs).Max();
            float threshold = maxVal * 0.05f;

            double lastActiveMs = searchFromMs;
            for (int i = read - channels; i >= 0; i -= channels)
            {
                if (Math.Abs(buffer[i]) > threshold)
                {
                    lastActiveMs = startMs + (double)i / channels / sr * 1000.0;
                    break;
                }
            }
            // Add a small tail (1 beat) to avoid cutting sustain
            return lastActiveMs + _grid!.BeatIntervalMs;
        }
        catch { }
        return -1;
    }

    private List<SubSectionItem> DetectSubSections(double startMs, double endMs)
    {
        var results = new List<SubSectionItem>();
        if (_grid == null) return results;

        double barMs = _grid.BarDurationMs;
        if ((endMs - startMs) / barMs <= 4) return results; // Min 4 bars to split

        // Multi-signal voting: collect split candidates from multiple signals
        var votes = new Dictionary<double, int>();

        // Signal 1: Energy changes (2-bar segments)
        var mixPath = _song.Tracks
            .Where(t => !t.Label.Contains("Click", StringComparison.OrdinalIgnoreCase))
            .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
            .FirstOrDefault(System.IO.File.Exists);

        if (mixPath != null)
        {
            try
            {
                using var reader = new AudioFileReader(mixPath);
                int sr = reader.WaveFormat.SampleRate; int ch = reader.WaveFormat.Channels;
                double seg = barMs * 2; var rms = new List<double>();
                for (double t = startMs; t < endMs; t += seg)
                {
                    long s = (long)(t / 1000.0 * sr) * ch; int len = (int)(seg / 1000.0 * sr) * ch;
                    reader.Position = Math.Min(s * sizeof(float), reader.Length - 4);
                    var buf = new float[len]; int rd = reader.Read(buf, 0, len);
                    rms.Add(rd > 0 ? Math.Sqrt(buf.Take(rd).Select(v => (double)(v * v)).Average()) : 0);
                }
                double mx = rms.Count > 0 ? rms.Max() : 1.0;
                for (int i = 1; i < rms.Count; i++)
                    if (mx > 0 && Math.Abs(rms[i] - rms[i - 1]) / mx > 0.2)
                        votes[startMs + i * seg] = votes.GetValueOrDefault(startMs + i * seg, 0) + 1;
            }
            catch { }
        }

        // Signal 2: Drum pattern changes
        var drumPath = _song.Tracks
            .Where(t => t.Label.Contains("Drum", StringComparison.OrdinalIgnoreCase))
            .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
            .FirstOrDefault(System.IO.File.Exists);

        if (drumPath != null)
        {
            try
            {
                using var reader = new AudioFileReader(drumPath);
                int sr = reader.WaveFormat.SampleRate; int ch = reader.WaveFormat.Channels;
                double seg = barMs * 2; var rms = new List<double>();
                for (double t = startMs; t < endMs; t += seg)
                {
                    long s = (long)(t / 1000.0 * sr) * ch; int len = (int)(seg / 1000.0 * sr) * ch;
                    reader.Position = Math.Min(s * sizeof(float), reader.Length - 4);
                    var buf = new float[len]; int rd = reader.Read(buf, 0, len);
                    rms.Add(rd > 0 ? Math.Sqrt(buf.Take(rd).Select(v => (double)(v * v)).Average()) : 0);
                }
                double mx = rms.Count > 0 ? rms.Max() : 1.0;
                for (int i = 1; i < rms.Count; i++)
                    if (mx > 0 && Math.Abs(rms[i] - rms[i - 1]) / mx > 0.25)
                    {
                        double sp = startMs + i * seg;
                        var near = votes.Keys.FirstOrDefault(k => Math.Abs(k - sp) < barMs);
                        if (near > 0) votes[near] += 2; else votes[sp] = 2;
                    }
            }
            catch { }
        }

        // Signal 3: Vocal silence gaps (>0.5s)
        var vocalPath = _song.Tracks
            .Where(t => t.Label.Contains("Vocal", StringComparison.OrdinalIgnoreCase) &&
                        !t.Label.Contains("Backing", StringComparison.OrdinalIgnoreCase))
            .Select(t => System.IO.Path.Combine(_song.FolderPath, t.FileName))
            .FirstOrDefault(System.IO.File.Exists);

        if (vocalPath != null)
        {
            try
            {
                using var reader = new AudioFileReader(vocalPath);
                int sr = reader.WaveFormat.SampleRate; int ch = reader.WaveFormat.Channels;
                long ss = (long)(startMs / 1000.0 * sr) * ch;
                int len = (int)((endMs - startMs) / 1000.0 * sr) * ch;
                reader.Position = Math.Min(ss * sizeof(float), reader.Length - 4);
                var buf = new float[len]; int rd = reader.Read(buf, 0, len);
                int win = (int)(0.05 * sr) * ch; bool silent = false; double silStart = 0;
                for (int i = 0; i < rd - win; i += win)
                {
                    float rms = 0;
                    for (int j = 0; j < win && i + j < rd; j += ch) rms += buf[i + j] * buf[i + j];
                    rms = (float)Math.Sqrt(rms / (win / ch));
                    double ms = startMs + (double)i / ch / sr * 1000.0;
                    if (rms < 0.01) { if (!silent) { silent = true; silStart = ms; } }
                    else
                    {
                        if (silent && ms - silStart > 500)
                        {
                            double sp = silStart + (ms - silStart) / 2;
                            var near = votes.Keys.FirstOrDefault(k => Math.Abs(k - sp) < barMs);
                            if (near > 0) votes[near] += 1; else votes[sp] = 1;
                        }
                        silent = false;
                    }
                }
            }
            catch { }
        }

        // Keep splits with 2+ votes, snap to bar, filter min 2 bars apart
        var confirmed = votes.Where(kv => kv.Value >= 2)
            .Select(kv => kv.Key).OrderBy(s => s).ToList();

        // Snap to nearest bar position
        var snapped = confirmed.Select(s =>
        {
            if (_song.BeatPositionsMs != null && _song.BeatPositionsMs.Count > 0)
            {
                int bpb = _song.TimeSignatureNumerator > 0 ? _song.TimeSignatureNumerator : 4;
                var downbeats = _song.BeatPositionsMs.Where((b, i) => i % bpb == 0).ToList();
                return downbeats.Count > 0 ? downbeats.OrderBy(b => Math.Abs(b - s)).First() : s;
            }
            return _beatService.SnapToBar(s, _grid);
        }).Distinct().OrderBy(s => s).ToList();

        var filtered = new List<double> { startMs };
        foreach (var sp in snapped)
            if (sp > filtered.Last() + barMs * 1.5 && sp < endMs - barMs * 1.5)
                filtered.Add(sp);
        filtered.Add(endMs);

        if (filtered.Count <= 2) return results;

        string baseName = _section.Name;
        for (int i = 0; i < filtered.Count - 1; i++)
            results.Add(new SubSectionItem
            {
                Name = $"{baseName} ({(char)('a' + i)})",
                StartMs = filtered[i], EndMs = filtered[i + 1]
            });
        return results;
    }



    // ─── MERGE SUB-SECTIONS ───────────────────────────────────────────────

    private void MergeSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = SubSectionsList.SelectedItems.Cast<SubSectionItem>().ToList();
        if (selected.Count < 2)
        {
            MessageBox.Show("Select 2 or more sub-sections (Ctrl+click) to merge.",
                "Select More", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Merge: take earliest start and latest end
        double mergedStart = selected.Min(s => s.StartMs);
        double mergedEnd = selected.Max(s => s.EndMs);
        string mergedName = selected.First().Name.Split(" (")[0]; // Remove suffix

        // Remove selected, add merged
        foreach (var item in selected)
            _subSections.Remove(item);

        _subSections.Add(new SubSectionItem
        {
            Name = mergedName,
            StartMs = mergedStart,
            EndMs = mergedEnd
        });

        // Sort by start time
        _subSections = _subSections.OrderBy(s => s.StartMs).ToList();
        SubSectionsList.ItemsSource = _subSections;
        SubSectionsList.Items.Refresh();

        if (_subSections.Count <= 1)
            SubSectionsList.Visibility = Visibility.Collapsed;

        DrawWaveforms();
    }

    // ─── SUB-SECTION LOOP PLAYBACK ────────────────────────────────────────

    private SubSectionItem? _activeSubSection = null;

    private void SubSectionPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_audioEngine == null) return;
        var button = sender as System.Windows.Controls.Button;
        var sub = button?.Tag as SubSectionItem;
        if (sub == null) return;

        StartSubSectionLoop(sub);
    }

    private void SubSectionsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_audioEngine == null) return;
        if (SubSectionsList.SelectedItem is SubSectionItem sub)
        {
            StartSubSectionLoop(sub);
        }
    }

    private void StartSubSectionLoop(SubSectionItem sub)
    {
        if (_audioEngine == null) return;

        // Set the loop range to this sub-section
        _activeSubSection = sub;
        _isLoopPreview = true;

        // Update the main loop markers to show which sub-section is playing
        _loopInMs = sub.StartMs;
        _loopOutMs = sub.EndMs;
        UpdateLoopPointDisplay();
        DrawWaveforms();

        // Seek and play
        _audioEngine.SeekToMs(sub.StartMs);
        _audioEngine.Play();
        PlayBtn.Content = "PAUSE";
        PlayLoopBtn.Background = new SolidColorBrush(Color.FromRgb(0, 200, 200));
    }

    // ─── INTERACTIVE REVIEW MODE ──────────────────────────────────────────

    private int _reviewIndex = -1;
    private bool _inReviewMode = false;

    private void StartReviewMode()
    {
        if (_subSections.Count == 0) return;
        _inReviewMode = true;
        _reviewIndex = 0;
        SubSectionsList.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Visible;
        ReviewCurrentSubSection();
    }

    private void ReviewCurrentSubSection()
    {
        if (_reviewIndex < 0 || _reviewIndex >= _subSections.Count) return;
        var sub = _subSections[_reviewIndex];
        ReviewTitle.Text = $"Reviewing: {sub.Name} [{_reviewIndex + 1} of {_subSections.Count}]";
        ReviewInText.Text = MsToTimecode(sub.StartMs);
        ReviewOutText.Text = MsToTimecode(sub.EndMs);
        ReviewScore.Text = "Looping... listen and adjust if needed";
        _loopInMs = sub.StartMs;
        _loopOutMs = sub.EndMs;
        UpdateLoopPointDisplay();
        RedrawMarkersOnly();
        StartSubSectionLoop(sub);
    }

    private double GetNudgeAmountMs()
    {
        int idx = NudgeAmountCombo.SelectedIndex;
        double beatMs = _song.BeatIntervalMs > 0 ? _song.BeatIntervalMs : 500;
        return idx switch
        {
            0 => beatMs * (_song.TimeSignatureNumerator > 0 ? _song.TimeSignatureNumerator : 4),
            1 => beatMs,
            2 => beatMs / 4.0,
            3 => beatMs / 8.0,
            _ => beatMs
        };
    }

    private void NudgeInBack2_Click(object s, RoutedEventArgs e) => NudgeReview(-2, true);
    private void NudgeInBack_Click(object s, RoutedEventArgs e) => NudgeReview(-1, true);
    private void NudgeInFwd_Click(object s, RoutedEventArgs e) => NudgeReview(1, true);
    private void NudgeInFwd2_Click(object s, RoutedEventArgs e) => NudgeReview(2, true);
    private void NudgeOutBack2_Click(object s, RoutedEventArgs e) => NudgeReview(-2, false);
    private void NudgeOutBack_Click(object s, RoutedEventArgs e) => NudgeReview(-1, false);
    private void NudgeOutFwd_Click(object s, RoutedEventArgs e) => NudgeReview(1, false);
    private void NudgeOutFwd2_Click(object s, RoutedEventArgs e) => NudgeReview(2, false);

    private void NudgeReview(int direction, bool isLoopIn)
    {
        if (_reviewIndex < 0 || _reviewIndex >= _subSections.Count) return;
        var sub = _subSections[_reviewIndex];
        double amount = GetNudgeAmountMs() * direction;
        if (isLoopIn)
            sub.StartMs = SnapToNearestBeatPosition(sub.StartMs + amount);
        else
            sub.EndMs = SnapToNearestBeatPosition(sub.EndMs + amount);
        ReviewInText.Text = MsToTimecode(sub.StartMs);
        ReviewOutText.Text = MsToTimecode(sub.EndMs);
        _loopInMs = sub.StartMs;
        _loopOutMs = sub.EndMs;
        UpdateLoopPointDisplay();
        RedrawMarkersOnly();
        if (_audioEngine != null && _audioEngine.IsPlaying)
            _audioEngine.SeekToMs(sub.StartMs);
    }

    private void ReviewApprove_Click(object sender, RoutedEventArgs e)
    {
        _reviewIndex++;
        if (_reviewIndex < _subSections.Count)
            ReviewCurrentSubSection();
        else
            EndReviewMode();
    }

    private void ReviewSkip_Click(object sender, RoutedEventArgs e)
    {
        if (_reviewIndex >= 0 && _reviewIndex < _subSections.Count)
            _subSections[_reviewIndex].CanLoop = false;
        _reviewIndex++;
        if (_reviewIndex < _subSections.Count)
            ReviewCurrentSubSection();
        else
            EndReviewMode();
    }

    private void ReviewSaveAll_Click(object sender, RoutedEventArgs e)
    {
        EndReviewMode();
        Apply_Click(sender, e);
    }

    private void EndReviewMode()
    {
        _inReviewMode = false;
        _reviewIndex = -1;
        ReviewPanel.Visibility = Visibility.Collapsed;
        if (_audioEngine != null) _audioEngine.Stop();
        _isLoopPreview = false;
        PlayBtn.Content = "PLAY";
        PlayLoopBtn.Background = new SolidColorBrush(Color.FromRgb(0, 139, 139));
    }

    // ─── NAVIGATION ─────────────────────────────────────────────────────

    private void WaveformCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var canvas = sender as Canvas;
        if (canvas == null) return;

        double mouseX = e.GetPosition(canvas).X;
        double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 1000;
        double totalMs = _displayEndMs - _displayStartMs;

        // Position under the cursor in ms
        double cursorMs = _displayStartMs + (mouseX / width) * totalMs;

        // Zoom factor: scroll up = zoom in, scroll down = zoom out
        double zoomFactor = e.Delta > 0 ? 0.7 : 1.4;
        double newTotalMs = totalMs * zoomFactor;

        // Clamp to reasonable limits
        double minRange = _song.BeatIntervalMs * 2; // Min: 2 beats visible
        double maxRange = (_song.BeatPositionsMs?.LastOrDefault() ?? 300000) * 1.2;
        newTotalMs = Math.Clamp(newTotalMs, minRange, maxRange);

        // Keep cursor position fixed: calculate new start/end so cursorMs stays at same pixel
        double ratio = mouseX / width;
        _displayStartMs = cursorMs - ratio * newTotalMs;
        _displayEndMs = cursorMs + (1 - ratio) * newTotalMs;

        // Clamp start to 0
        if (_displayStartMs < 0)
        {
            _displayEndMs -= _displayStartMs;
            _displayStartMs = 0;
        }

        LoadWaveforms();
        e.Handled = true;
    }

    private void GoToLoopIn_Click(object sender, RoutedEventArgs e)
    {
        double range = _displayEndMs - _displayStartMs;
        _displayStartMs = _loopInMs - range / 2;
        _displayEndMs = _loopInMs + range / 2;
        if (_displayStartMs < 0) { _displayEndMs -= _displayStartMs; _displayStartMs = 0; }
        LoadWaveforms();
    }

    private void GoToLoopOut_Click(object sender, RoutedEventArgs e)
    {
        double range = _displayEndMs - _displayStartMs;
        _displayStartMs = _loopOutMs - range / 2;
        _displayEndMs = _loopOutMs + range / 2;
        if (_displayStartMs < 0) { _displayEndMs -= _displayStartMs; _displayStartMs = 0; }
        LoadWaveforms();
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        double range = _displayEndMs - _displayStartMs;
        double shift = range * 0.25;
        _displayStartMs = Math.Max(0, _displayStartMs - shift);
        _displayEndMs = _displayStartMs + range;
        LoadWaveforms();
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        double range = _displayEndMs - _displayStartMs;
        double shift = range * 0.25;
        _displayStartMs += shift;
        _displayEndMs = _displayStartMs + range;
        LoadWaveforms();
    }

    private void ScrollFarLeft_Click(object sender, RoutedEventArgs e)
    {
        double range = _displayEndMs - _displayStartMs;
        _displayStartMs = 0;
        _displayEndMs = range;
        LoadWaveforms();
    }

    private void ScrollFarRight_Click(object sender, RoutedEventArgs e)
    {
        double range = _displayEndMs - _displayStartMs;
        double totalDuration = _song.Tracks.Count > 0 ? _loopOutMs + _song.BarDurationMs * 4 : _displayEndMs + range;
        _displayEndMs = totalDuration;
        _displayStartMs = Math.Max(0, _displayEndMs - range);
        LoadWaveforms();
    }

    // ─── BUTTONS ──────────────────────────────────────────────────────────

    private double SnapToNearestBeatPosition(double ms)
    {
        if (_song.BeatPositionsMs != null && _song.BeatPositionsMs.Count > 0)
            return _song.BeatPositionsMs.OrderBy(b => Math.Abs(b - ms)).First();
        else if (_grid != null)
            return _beatService.SnapToBeat(ms, _grid);
        return ms;
    }

    private void SnapToBeat_Click(object sender, RoutedEventArgs e)
    {
        // Snap to the nearest BEAT on the SAME even grid the waveform draws (GetEvenGrid uses the
        // detected beat spacing anchored to the click-track downbeat), so a snap lands exactly on a
        // drawn grid line.
        var (beatMs, anchorMs) = GetEvenGrid();
        if (beatMs <= 0) { UpdateLoopPointDisplay(); DrawWaveforms(); return; }

        _loopInMs = SnapToGrid(_loopInMs, anchorMs, beatMs);
        _loopOutMs = SnapToGrid(_loopOutMs, anchorMs, beatMs);

        UpdateLoopPointDisplay();
        DrawWaveforms();
    }

    private void SnapToBar_Click(object sender, RoutedEventArgs e)
    {
        // Snap to the nearest BAR DOWNBEAT (Beat 1) on the SAME even grid the waveform draws:
        // downbeats are anchor + k * barMs (barMs = beatMs * beatsPerBar), with beatMs/anchor from
        // GetEvenGrid (detected beat spacing anchored to the click-track high-pitch downbeat). This
        // matches the bright light-blue lines exactly.
        var (beatMs, anchorMs) = GetEvenGrid();
        int beatsPerBar = (_grid?.TimeSignatureNumerator ?? _song.TimeSignatureNumerator);
        if (beatsPerBar <= 0) beatsPerBar = 4;
        if (beatMs <= 0) { UpdateLoopPointDisplay(); DrawWaveforms(); return; }

        double barMs = beatMs * beatsPerBar;
        _loopInMs = SnapToGrid(_loopInMs, anchorMs, barMs);
        _loopOutMs = SnapToGrid(_loopOutMs, anchorMs, barMs);

        UpdateLoopPointDisplay();
        DrawWaveforms();
    }

    /// <summary>
    /// Snap a time (ms) to the nearest point on an evenly-spaced grid defined by an anchor and a
    /// step: anchor + round((ms - anchor) / step) * step. Clamped to &gt;= 0. A non-positive step
    /// returns the input unchanged.
    /// </summary>
    private static double SnapToGrid(double ms, double anchorMs, double stepMs)
    {
        if (stepMs <= 0) return ms;
        double snapped = anchorMs + Math.Round((ms - anchorMs) / stepMs) * stepMs;
        return snapped < 0 ? 0 : snapped;
    }

    /// <summary>
    /// The authoritative beat spacing (ms) and Beat-1 anchor (ms) for the even grid. Prefers the
    /// DETECTED beat positions (median interval + the detected beat nearest GridAnchorMs), because
    /// the stored BPM can be slightly off and that error accumulates across a song. Falls back to
    /// the stored BPM/anchor only when no detected beats exist. Returns beatMs = 0 when nothing is
    /// available. This is the SINGLE source the grid drawing and both Snap buttons share, so they
    /// always agree.
    /// </summary>
    private (double beatMs, double anchorMs) GetEvenGrid()
    {
        var beats = _song.BeatPositionsMs;
        if (beats != null && beats.Count >= 2)
        {
            var intervals = new List<double>(beats.Count - 1);
            for (int i = 1; i < beats.Count; i++)
                intervals.Add(beats[i] - beats[i - 1]);
            intervals.Sort();
            double beatMs = intervals[intervals.Count / 2];

            double storedAnchor = _grid?.GridAnchorMs ?? _song.GridAnchorMs;
            double anchorMs = beats[0];
            if (storedAnchor > 0)
            {
                double best = double.MaxValue;
                foreach (var b in beats)
                {
                    double d = Math.Abs(b - storedAnchor);
                    if (d < best) { best = d; anchorMs = b; }
                }
            }
            return (beatMs, anchorMs);
        }

        double bpm = _grid?.Bpm ?? _song.Bpm;
        double anchor = _grid?.GridAnchorMs ?? _song.GridAnchorMs;
        if (anchor < 0) anchor = 0;
        return (bpm > 0 ? 60000.0 / bpm : 0, anchor);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        double center = (_displayStartMs + _displayEndMs) / 2;
        double range = (_displayEndMs - _displayStartMs) / 4;
        _displayStartMs = center - range;
        _displayEndMs = center + range;
        LoadWaveforms();
    }

    // ─── DOWNBEAT NUDGE (shift grid to align with click peaks) ────────────

    private void NudgeDownbeatLeft_Click(object sender, RoutedEventArgs e)
    {
        // Nudge by 1/32 note (1 beat / 8)
        double nudgeMs = _song.BeatIntervalMs / 8.0;
        NudgeDownbeat(-nudgeMs);
    }

    private void NudgeDownbeatRight_Click(object sender, RoutedEventArgs e)
    {
        // Nudge by 1/32 note (1 beat / 8)
        double nudgeMs = _song.BeatIntervalMs / 8.0;
        NudgeDownbeat(nudgeMs);
    }

    private void NudgeDownbeatLeftFine_Click(object sender, RoutedEventArgs e)
    {
        NudgeDownbeat(-1.0); // 1ms fine adjustment
    }

    private void NudgeDownbeatRightFine_Click(object sender, RoutedEventArgs e)
    {
        NudgeDownbeat(1.0); // 1ms fine adjustment
    }

    private void NudgeDownbeat(double offsetMs)
    {
        // Shift all beat positions
        if (_song.BeatPositionsMs != null && _song.BeatPositionsMs.Count > 0)
        {
            for (int i = 0; i < _song.BeatPositionsMs.Count; i++)
            {
                _song.BeatPositionsMs[i] += offsetMs;
            }
        }

        // Shift grid anchor
        _song.GridAnchorMs += offsetMs;
        if (_grid != null)
            _grid.GridAnchorMs += offsetMs;

        // Update display
        DownbeatInfoText.Text = $"Anchor: {MsToTimecode(_song.GridAnchorMs)}";

        // Redraw waveforms with shifted grid
        DrawWaveforms();

        _hasAppliedChanges = true;
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        double center = (_displayStartMs + _displayEndMs) / 2;
        double range = (_displayEndMs - _displayStartMs);
        _displayStartMs = Math.Max(0, center - range);
        _displayEndMs = center + range;
        LoadWaveforms();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        // Stop any playback before applying (prevent crash from invalid position)
        if (_audioEngine != null && _audioEngine.IsPlaying)
        {
            _audioEngine.Stop();
        }
        _isLoopPreview = false;

        try
        {
            _section.LoopInMs = _loopInMs;
            _section.LoopOutMs = _loopOutMs;

            // Save the full text (with chords) regardless of toggle state
            if (_showChords)
            {
                _section.Text = LyricsTextBox.Text ?? "";
                _lyricsFullText = _section.Text;
            }
            else
            {
                // If chords were hidden, user edited lyrics-only — preserve chord data
                _section.Text = _lyricsFullText;
            }

            if (_grid != null)
            {
                _section.StartBar = _beatService.GetBarAtMs(_loopInMs, _grid);
                _section.EndBar = _beatService.GetBarAtMs(_loopOutMs, _grid) - 1;
                if (_section.EndBar < _section.StartBar)
                    _section.EndBar = _section.StartBar;
            }

            // Recalculate slide timing from text (detect slide breaks by empty lines)
            // Timing stored SEPARATELY in chordChart.slides[] — text stays clean
            RecalculateSlideTimings();

            // Mark that changes were applied (parent will check on close)
            _hasAppliedChanges = true;

            // Show brief confirmation
            TapTimingInstruction.Visibility = Visibility.Visible;
            TapTimingInstruction.Text = "✓ Changes saved!";
            TapTimingInstruction.Foreground = System.Windows.Media.Brushes.LimeGreen;

            // Hide confirmation after 2 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s2, e2) =>
            {
                TapTimingInstruction.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error applying changes:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Stop any playback before closing
        if (_audioEngine != null && _audioEngine.IsPlaying)
        {
            _audioEngine.Stop();
        }
        _isLoopPreview = false;
        DialogResult = _hasAppliedChanges; // true if Apply was clicked at least once
        Close();
    }

    private void UpdateLoopPointDisplay()
    {
        LoopInText.Text = MsToTimecode(_loopInMs);
        LoopOutText.Text = MsToTimecode(_loopOutMs);
        double duration = _loopOutMs - _loopInMs;
        int bars = _grid != null && _grid.BarDurationMs > 0
            ? (int)Math.Round(duration / _grid.BarDurationMs) : 0;
        DurationText.Text = $"{MsToTimecode(duration)} ({bars} bars)";
    }

    private static string MsToTimecode(double ms)
    {
        int totalMs = (int)Math.Round(ms);
        int minutes = totalMs / 60000;
        int seconds = (totalMs % 60000) / 1000;
        int millis = totalMs % 1000;
        return $"{minutes}:{seconds:00}:{millis:000}";
    }
}

/// <summary>
/// Represents a sub-section found by granular detection.
/// </summary>
public class SubSectionItem
{
    public string Name { get; set; } = "";
    public double StartMs { get; set; }
    public double EndMs { get; set; }
    public bool CanLoop { get; set; } = true;
    public string Display => $"{Name}: {MsToTimecode(StartMs)} - {MsToTimecode(EndMs)}{(CanLoop ? "" : " [SKIP]")}";

    private static string MsToTimecode(double ms)
    {
        int totalMs = (int)Math.Round(ms);
        int minutes = totalMs / 60000;
        int seconds = (totalMs % 60000) / 1000;
        int millis = totalMs % 1000;
        return $"{minutes}:{seconds:00}:{millis:000}";
    }
}
