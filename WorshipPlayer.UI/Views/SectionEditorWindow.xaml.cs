using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorshipPlayer.Audio.DSP;
using WorshipPlayer.Audio.Engine;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.UI.Views;

public partial class SectionEditorWindow : Window
{
    private readonly Song _song;
    private readonly ObservableCollection<SectionViewModel> _sections = new();
    private BeatDetectionService _beatService = new();
    private BeatDetectionService.BeatGrid? _currentGrid;
    private readonly AudioEngine? _audioEngine;

    // Tap-along state
    private bool _tapping = false;
    private List<double> _tapTimes = new();
    private DateTime _tapStartTime;

    // Manual click-track / metronome state
    private MetronomePlayer? _metronome;
    private bool _suppressTempoSync = false;

    public SectionEditorWindow(Song song, AudioEngine? audioEngine = null)
    {
        InitializeComponent();

        _song = song;
        _audioEngine = audioEngine;
        TitleText.Text = $"Section Editor - {song.Title}";
        SongInfoText.Text = $"BPM: {song.Bpm:F2} | Beat: {song.BeatIntervalMs:F3} ms | Bar: {song.BarDurationMs:F3} ms | Anchor: {MsToTimecode(song.GridAnchorMs)}";

        // Create current grid from song settings
        _currentGrid = new BeatDetectionService.BeatGrid
        {
            Bpm = song.Bpm,
            BeatIntervalMs = song.BeatIntervalMs,
            BarDurationMs = song.BarDurationMs,
            GridAnchorMs = song.GridAnchorMs,
            TimeSignatureNumerator = song.TimeSignatureNumerator
        };

        // Load existing sections
        for (int i = 0; i < song.Sections.Count; i++)
        {
            var s = song.Sections[i];
            _sections.Add(new SectionViewModel
            {
                Index = i + 1,
                Name = s.Name,
                LoopInMs = s.LoopInMs,
                LoopOutMs = s.LoopOutMs,
                StartBar = s.StartBar,
                EndBar = s.EndBar,
                Color = s.Color,
                LightingPresetIndex = s.LightingPresetIndex,
                Text = s.Text
            });
        }

        SectionsGrid.ItemsSource = _sections;

        // Initialize downbeat display
        DownbeatTimecodeBox.Text = MsToTimecode(song.GridAnchorMs);
        DownbeatDisplayText.Text = $"Beat 1 @ {MsToTimecode(song.GridAnchorMs)} ({song.GridAnchorMs:F1} ms)";

        // Show quality/BPM panels for single-track songs (Demucs separation)
        _isSingleTrack = DetectSingleTrack();
        if (_isSingleTrack)
        {
            QualityPanel.Visibility = Visibility.Visible;
            BpmOverridePanel.Visibility = Visibility.Visible;
        }

        InitializeManualTempoControls();
    }

    /// <summary>
    /// Seed the manual click-track controls (BPM box/slider/echo + time-sig picker) from the
    /// current song. Runs under the _suppressTempoSync guard so no handler fires during init.
    /// </summary>
    private void InitializeManualTempoControls()
    {
        _suppressTempoSync = true;
        try
        {
            double bpm = _song.Bpm > 0 ? _song.Bpm : 120.0;
            bpm = ClampBpm(bpm);

            ManualBpmBox.Text = bpm.ToString("0.#");
            ManualBpmSlider.Value = bpm;
            ManualBpmEcho.Text = $"Current: {bpm:0.#} BPM";

            // Select the time-signature item matching the song (default 4/4).
            int num = _song.TimeSignatureNumerator > 0 ? _song.TimeSignatureNumerator : 4;
            int den = _song.TimeSignatureDenominator > 0 ? _song.TimeSignatureDenominator : 4;
            string sig = $"{num}/{den}";
            TimeSigComboBox.SelectedIndex = sig switch
            {
                "3/4" => 0,
                "6/8" => 2,
                _ => 1 // 4/4 default
            };
        }
        finally
        {
            _suppressTempoSync = false;
        }
    }

    private static double ClampBpm(double bpm)
    {
        if (double.IsNaN(bpm) || double.IsInfinity(bpm)) return 120.0;
        if (bpm < 40) return 40;
        if (bpm > 220) return 220;
        return bpm;
    }

    /// <summary>
    /// Read the currently-selected time signature from the picker as (numerator, denominator).
    /// Defaults to 4/4 if nothing is selected.
    /// </summary>
    private (int Numerator, int Denominator) GetSelectedTimeSignature()
    {
        if (TimeSigComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            var parts = tag.Split('/');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int n) && int.TryParse(parts[1], out int d) &&
                n > 0 && d > 0)
            {
                return (n, d);
            }
        }
        return (4, 4);
    }

    /// <summary>
    /// Stop + dispose the metronome so it never keeps playing after the editor closes.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _metronome?.Stop();
            _metronome?.Dispose();
        }
        catch { /* never throw during teardown */ }
        finally
        {
            _metronome = null;
        }

        // Also detach the tap keydown handler if tapping was left active.
        if (_tapping)
        {
            try { this.KeyDown -= TapMode_KeyDown; } catch { }
            _tapping = false;
        }

        base.OnClosed(e);
    }

    private bool _isSingleTrack = false;

    /// <summary>
    /// Detect if this song is a single-track (one audio file, needs Demucs separation).
    /// </summary>
    private bool DetectSingleTrack()
    {
        // If song has 0-1 tracks, it's single-track
        if (_song.Tracks.Count <= 1) return true;

        // If the folder only has one audio file, it's single-track
        if (!string.IsNullOrEmpty(_song.FolderPath) && Directory.Exists(_song.FolderPath))
        {
            var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a" };
            int audioCount = Directory.GetFiles(_song.FolderPath)
                .Count(f => audioExtensions.Contains(Path.GetExtension(f).ToLower()));
            if (audioCount == 1) return true;
        }

        return false;
    }

    private void BpmMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard against firing during XAML initialization
        if (BpmOverrideBox == null) return;

        if (BpmModeComboBox?.SelectedIndex == 1) // Manual
            BpmOverrideBox.Visibility = Visibility.Visible;
        else
            BpmOverrideBox.Visibility = Visibility.Collapsed;
    }

    private string GetSelectedQuality()
    {
        if (QualityComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag;
        return "default";
    }

    private double? GetBpmOverride()
    {
        if (BpmModeComboBox?.SelectedIndex == 1 && !string.IsNullOrEmpty(BpmOverrideBox?.Text))
        {
            if (double.TryParse(BpmOverrideBox.Text, out double bpm) && bpm > 0)
                return bpm;
        }
        return null;
    }

    /// <summary>
    /// Get section hints from fetched lyrics (section names in order).
    /// These are passed to the Python script so the LLM uses them as constraints
    /// for section boundary detection.
    /// </summary>
    private List<string>? GetSectionHints()
    {
        if (_fetchedLyrics == null || _fetchedLyrics.Sections.Count == 0)
            return null;

        return _fetchedLyrics.Sections
            .Where(s => !string.IsNullOrEmpty(s.Name))
            .Select(s => s.Name)
            .ToList();
    }

    /// <summary>
    /// Get section lyrics text (for lyrics-based boundary validation).
    /// Maps section name → lyrics text (used by Python to verify boundaries
    /// match where those lyrics are actually being sung).
    /// </summary>
    private Dictionary<string, string>? GetSectionLyrics()
    {
        if (_fetchedLyrics == null || _fetchedLyrics.Sections.Count == 0)
            return null;

        var result = new Dictionary<string, string>();
        foreach (var section in _fetchedLyrics.Sections)
        {
            if (!string.IsNullOrEmpty(section.Name) && !string.IsNullOrEmpty(section.Text))
            {
                result[section.Name] = section.Text;
            }
        }

        return result.Count > 0 ? result : null;
    }

    // ─── LYRICS IMPORT ────────────────────────────────────────────────────

    private ChordProParserService _chordProParser = new();
    private ChordProParserService.ParseResult? _fetchedLyrics;

    private async void FetchLyrics_Click(object sender, RoutedEventArgs e)
    {
        string input = LyricsUrlBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(input))
        {
            MessageBox.Show("Enter a URL or file path to fetch lyrics from.",
                "No Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LyricsFetchStatus.Text = "Fetching...";
        LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.Orange;

        try
        {
            string content = "";

            if (File.Exists(input))
            {
                // Local file
                content = await File.ReadAllTextAsync(input);
            }
            else if (input.StartsWith("http://") || input.StartsWith("https://"))
            {
                // Check if it's Ultimate Guitar (requires JavaScript — can't fetch directly)
                if (input.Contains("ultimate-guitar.com") || input.Contains("tabs.ultimate-guitar"))
                {
                    LyricsFetchStatus.Text = "Ultimate Guitar requires JavaScript.\n" +
                        "Use the PASTE button instead:\n" +
                        "1. Open the URL in your browser\n" +
                        "2. Select all chord text (Ctrl+A)\n" +
                        "3. Copy (Ctrl+C)\n" +
                        "4. Click the green PASTE button here";
                    LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.Orange;
                    return;
                }

                // URL — fetch content
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "WorshipPlayer/1.0");
                var response = await client.GetStringAsync(input);

                // Try to extract chord chart from HTML
                content = ExtractChordChartFromHtml(response) ?? response;
            }
            else
            {
                // Treat as pasted text directly
                content = input;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                LyricsFetchStatus.Text = "No content found at URL.";
                LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            // Parse the content
            _fetchedLyrics = _chordProParser.Parse(content);

            if (_fetchedLyrics.Sections.Count > 0)
            {
                string sectionNames = string.Join(", ",
                    _fetchedLyrics.Sections.Select(s => string.IsNullOrEmpty(s.Name) ? "(unnamed)" : s.Name));
                LyricsFetchStatus.Text = $"Parsed: {_fetchedLyrics.Sections.Count} sections\n{sectionNames}";
                LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.LightGreen;

                // Apply directives if found
                if (_fetchedLyrics.Directives.TryGetValue("key", out string? key) && !string.IsNullOrEmpty(key))
                    _song.OriginalKey = key;
                if (_fetchedLyrics.Directives.TryGetValue("title", out string? title) && !string.IsNullOrEmpty(title))
                    _song.Title = title;
            }
            else
            {
                LyricsFetchStatus.Text = "Parsed but no sections found.";
                LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }
        catch (Exception ex)
        {
            LyricsFetchStatus.Text = $"Error: {ex.Message}";
            LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            _fetchedLyrics = null;
        }
    }

    private void BrowseLyricsFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select lyrics/chord chart file",
            Filter = "Text files (*.txt)|*.txt|ChordPro files (*.cho;*.chordpro)|*.cho;*.chordpro|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            LyricsUrlBox.Text = dialog.FileName;
        }
    }

    private void PasteLyrics_Click(object sender, RoutedEventArgs e)
    {
        // Read multi-line text from clipboard (preserves line breaks)
        string? clipboardText = null;
        try
        {
            if (Clipboard.ContainsText())
                clipboardText = Clipboard.GetText();
        }
        catch { }

        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            LyricsFetchStatus.Text = "Clipboard is empty. Copy chord chart text first.";
            LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        LyricsFetchStatus.Text = "Parsing clipboard...";
        LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.Orange;

        try
        {
            _fetchedLyrics = _chordProParser.Parse(clipboardText);

            if (_fetchedLyrics.Sections.Count > 0)
            {
                string sectionNames = string.Join(", ",
                    _fetchedLyrics.Sections.Select(s => string.IsNullOrEmpty(s.Name) ? "(unnamed)" : s.Name));
                LyricsFetchStatus.Text = $"Parsed: {_fetchedLyrics.Sections.Count} sections\n{sectionNames}";
                LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.LightGreen;

                // Apply directives if found
                if (_fetchedLyrics.Directives.TryGetValue("key", out string? key) && !string.IsNullOrEmpty(key))
                    _song.OriginalKey = key;
                if (_fetchedLyrics.Directives.TryGetValue("title", out string? title) && !string.IsNullOrEmpty(title))
                    _song.Title = title;
            }
            else
            {
                LyricsFetchStatus.Text = "Parsed but no sections found.\nMake sure text has [Verse 1], [Chorus], etc.";
                LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }
        catch (Exception ex)
        {
            LyricsFetchStatus.Text = $"Parse error: {ex.Message}";
            LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            _fetchedLyrics = null;
        }
    }

    /// <summary>
    /// Try to extract chord chart text from an HTML page (e.g., Ultimate Guitar).
    /// UG stores the chord chart in a JS variable or specific div.
    /// </summary>
    private string? ExtractChordChartFromHtml(string html)
    {
        // Ultimate Guitar stores content in a JSON blob within the page
        // Look for the content between specific markers
        try
        {
            // Method 1: Look for <pre> tags (common in chord sites)
            var preMatch = System.Text.RegularExpressions.Regex.Match(html,
                @"<pre[^>]*class=""[^""]*chord[^""]*""[^>]*>(.*?)</pre>",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (preMatch.Success)
            {
                return System.Net.WebUtility.HtmlDecode(
                    System.Text.RegularExpressions.Regex.Replace(preMatch.Groups[1].Value, @"<[^>]+>", ""));
            }

            // Method 2: Look for "content" field in UG's JSON data
            var contentMatch = System.Text.RegularExpressions.Regex.Match(html,
                @"""content""\s*:\s*""((?:[^""\\]|\\.)*)""",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (contentMatch.Success)
            {
                string encoded = contentMatch.Groups[1].Value;
                // Unescape JSON string
                string decoded = encoded.Replace("\\n", "\n").Replace("\\r", "")
                    .Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
                // Remove [tab] and [ch] tags (UG format)
                decoded = System.Text.RegularExpressions.Regex.Replace(decoded, @"\[/?(?:tab|ch)\]", "");
                return decoded;
            }

            // Method 3: Look for plain text between <code> or <pre> tags
            var codeMatch = System.Text.RegularExpressions.Regex.Match(html,
                @"<(?:pre|code)[^>]*>(.*?)</(?:pre|code)>",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (codeMatch.Success)
            {
                return System.Net.WebUtility.HtmlDecode(
                    System.Text.RegularExpressions.Regex.Replace(codeMatch.Groups[1].Value, @"<[^>]+>", ""));
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Apply fetched lyrics (from URL/file) to the detected sections.
    /// Matches sections by name (case-insensitive) and assigns the inline [chord] text.
    /// Repeated sections get numbered names: "VERSE 1", "VERSE 1 (2)".
    /// </summary>
    private void ApplyFetchedLyricsToSections()
    {
        if (_fetchedLyrics == null) return;

        var fetchedSections = _fetchedLyrics.Sections;
        int appliedCount = 0;

        // Track how many times each section name has been used (for repeat numbering)
        var nameUsageCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Try to match fetched sections to detected sections in order
        int fetchIdx = 0;
        for (int i = 0; i < _sections.Count && fetchIdx < fetchedSections.Count; i++)
        {
            var detectedSection = _sections[i];
            var fetchedSection = fetchedSections[fetchIdx];

            // Match by name similarity (if fetched has names)
            bool matched = false;
            if (!string.IsNullOrEmpty(fetchedSection.Name))
            {
                // Check if detected section name contains the fetched name (or vice versa)
                if (detectedSection.Name.Contains(fetchedSection.Name, StringComparison.OrdinalIgnoreCase) ||
                    fetchedSection.Name.Contains(detectedSection.Name, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeSectionName(detectedSection.Name) == NormalizeSectionName(fetchedSection.Name))
                {
                    matched = true;
                }
            }

            // If no name match, assign in order (fallback)
            if (!matched && string.IsNullOrEmpty(fetchedSection.Name))
            {
                matched = true; // Unnamed sections match by position
            }

            if (matched || fetchIdx < fetchedSections.Count)
            {
                // Apply the fetched text to this section
                detectedSection.Text = fetchedSection.Text;

                // Handle repeated section naming
                string baseName = !string.IsNullOrEmpty(fetchedSection.Name)
                    ? fetchedSection.Name
                    : detectedSection.Name;

                if (nameUsageCount.ContainsKey(baseName))
                {
                    nameUsageCount[baseName]++;
                    detectedSection.Name = $"{baseName} ({nameUsageCount[baseName]})";
                }
                else
                {
                    nameUsageCount[baseName] = 1;
                    if (!string.IsNullOrEmpty(fetchedSection.Name))
                        detectedSection.Name = fetchedSection.Name;
                }

                appliedCount++;
                fetchIdx++;
            }
        }

        // Update the song's sections with the new text
        _song.Sections.Clear();
        foreach (var vm in _sections)
        {
            _song.Sections.Add(new Section
            {
                Name = vm.Name,
                LoopInMs = vm.LoopInMs,
                LoopOutMs = vm.LoopOutMs,
                StartBar = vm.StartBar,
                EndBar = vm.EndBar,
                Color = vm.Color,
                LightingPresetIndex = vm.LightingPresetIndex,
                Text = vm.Text
            });
        }

        LyricsFetchStatus.Text = $"Applied lyrics to {appliedCount}/{_sections.Count} sections";
        LyricsFetchStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
    }

    private static string NormalizeSectionName(string name)
    {
        return name.ToUpper().Trim()
            .Replace("VERSE ", "V").Replace("CHORUS ", "C").Replace("BRIDGE ", "B")
            .Replace("PRE-CHORUS", "PC").Replace("PRECHORUS", "PC")
            .Replace("OUTRO", "O").Replace("INTRO", "I");
    }

    // ─── SECTION MANAGEMENT ───────────────────────────────────────────────

    private void AddSection_Click(object sender, RoutedEventArgs e)
    {
        double lastEnd = _sections.Count > 0 ? _sections.Last().LoopOutMs : _song.GridAnchorMs;

        var newSection = new SectionViewModel
        {
            Index = _sections.Count + 1,
            Name = "New Section",
            LoopInMs = lastEnd,
            LoopOutMs = lastEnd + _song.BarDurationMs * 4,
            StartBar = _song.GetBarAtMs(lastEnd),
            EndBar = _song.GetBarAtMs(lastEnd) + 3,
            Color = "#4A90D9"
        };

        // Snap to bar boundaries
        if (_currentGrid != null)
        {
            newSection.LoopInMs = _beatService.SnapToBar(newSection.LoopInMs, _currentGrid);
            newSection.LoopOutMs = _beatService.SnapToBar(newSection.LoopOutMs, _currentGrid);
        }

        _sections.Add(newSection);
        ReindexSections();
    }

    private void RemoveSection_Click(object sender, RoutedEventArgs e)
    {
        if (SectionsGrid.SelectedItem is SectionViewModel selected)
        {
            _sections.Remove(selected);
            ReindexSections();
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        int index = SectionsGrid.SelectedIndex;
        if (index > 0)
        {
            var item = _sections[index];
            _sections.RemoveAt(index);
            _sections.Insert(index - 1, item);
            SectionsGrid.SelectedIndex = index - 1;
            ReindexSections();
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        int index = SectionsGrid.SelectedIndex;
        if (index >= 0 && index < _sections.Count - 1)
        {
            var item = _sections[index];
            _sections.RemoveAt(index);
            _sections.Insert(index + 1, item);
            SectionsGrid.SelectedIndex = index + 1;
            ReindexSections();
        }
    }

    // ─── DRAG-AND-DROP SECTION REORDER ────────────────────────────────────

    private Point _dragStartPoint;
    private int _dragSourceIndex = -1;

    private void SectionsGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        var row = GetDataGridRowFromPoint(e);
        _dragSourceIndex = row?.GetIndex() ?? -1;
    }

    private void SectionsGrid_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _dragSourceIndex < 0)
            return;

        Point currentPos = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPos;

        // Only start drag if moved enough (prevent accidental drags)
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (_dragSourceIndex >= 0 && _dragSourceIndex < _sections.Count)
            {
                var data = new DataObject("SectionIndex", _dragSourceIndex);
                DragDrop.DoDragDrop(SectionsGrid, data, DragDropEffects.Move);
            }
        }
    }

    private void SectionsGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("SectionIndex")) return;

        int sourceIndex = (int)e.Data.GetData("SectionIndex");
        var targetRow = GetDataGridRowFromPoint(e);
        int targetIndex = targetRow?.GetIndex() ?? -1;

        if (targetIndex < 0 || targetIndex >= _sections.Count || sourceIndex == targetIndex)
            return;

        // Move the section
        var item = _sections[sourceIndex];
        _sections.RemoveAt(sourceIndex);
        _sections.Insert(targetIndex, item);
        ReindexSections();
        SectionsGrid.SelectedIndex = targetIndex;
    }

    private DataGridRow? GetDataGridRowFromPoint(RoutedEventArgs e)
    {
        var point = (e is MouseButtonEventArgs mbe) ? mbe.GetPosition(SectionsGrid) :
                    (e is DragEventArgs de) ? de.GetPosition(SectionsGrid) : new Point(0, 0);

        var hit = System.Windows.Media.VisualTreeHelper.HitTest(SectionsGrid, point);
        if (hit?.VisualHit == null) return null;

        DependencyObject? dep = hit.VisualHit;
        while (dep != null && dep is not DataGridRow)
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);

        return dep as DataGridRow;
    }

    // ─── WAVEFORM EDITOR (double-click section) ───────────────────────────

    private void SectionsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SectionsGrid.SelectedItem is not SectionViewModel selected) return;

        try
        {
            // Commit any pending edit before opening the editor
            SectionsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            // Open waveform editor for this section
            var editor = new WaveformEditorWindow(_song, selected, _currentGrid, _audioEngine);
            if (editor.ShowDialog() == true)
            {
                // Update section with edited values — use InvalidateVisual instead of Refresh
                SectionsGrid.Items.Refresh();

                // Auto-save to song.json immediately (don't wait for user to click SAVE + CLOSE)
                ApplySectionsToSong();
            }
        }
        catch (InvalidOperationException)
        {
            // DataGrid was in edit mode — cancel and retry
            try
            {
                SectionsGrid.CancelEdit(DataGridEditingUnit.Row);
                SectionsGrid.CancelEdit(DataGridEditingUnit.Cell);
                SectionsGrid.Items.Refresh();
            }
            catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Waveform editor error:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── MANUAL DOWNBEAT SELECTION ────────────────────────────────────────

    private void SetDownbeat_Click(object sender, RoutedEventArgs e)
    {
        string input = DownbeatTimecodeBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            MessageBox.Show("Enter a timecode (M:SS:MMM) or millisecond value for the downbeat position.",
                "No Value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double newAnchorMs = ParseTimecodeOrMs(input);
        if (newAnchorMs < 0)
        {
            MessageBox.Show("Invalid format. Use timecode (M:SS:MMM) or milliseconds.",
                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyManualDownbeat(newAnchorMs);
    }

    private void NudgeDownbeatLeft_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGrid == null || _currentGrid.BeatIntervalMs <= 0)
        {
            MessageBox.Show("BPM not set. Run Auto-Detect or set BPM first.",
                "No BPM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double newAnchor = _song.GridAnchorMs - _currentGrid.BeatIntervalMs;
        if (newAnchor < 0) newAnchor = 0;
        ApplyManualDownbeat(newAnchor);
    }

    private void NudgeDownbeatRight_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGrid == null || _currentGrid.BeatIntervalMs <= 0)
        {
            MessageBox.Show("BPM not set. Run Auto-Detect or set BPM first.",
                "No BPM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double newAnchor = _song.GridAnchorMs + _currentGrid.BeatIntervalMs;
        ApplyManualDownbeat(newAnchor);
    }

    /// <summary>
    /// Apply a new manual downbeat position. Rebuilds the grid from the new anchor
    /// while keeping the current BPM and time signature.
    /// </summary>
    private void ApplyManualDownbeat(double newAnchorMs)
    {
        double bpm = _song.Bpm > 0 ? _song.Bpm : 120.0;
        int timeSigNum = _song.TimeSignatureNumerator > 0 ? _song.TimeSignatureNumerator : 4;
        double totalDuration = EstimateTotalDuration();

        // Calculate the offset from old anchor to new anchor
        double oldAnchorMs = _song.GridAnchorMs;
        double offsetMs = newAnchorMs - oldAnchorMs;

        // Use the BeatDetectionService to rebuild grid from manual anchor
        var grid = _beatService.BuildGridFromManualAnchor(newAnchorMs, bpm, timeSigNum, totalDuration);

        _currentGrid = grid;
        _song.GridAnchorMs = grid.GridAnchorMs;
        _song.BeatIntervalMs = grid.BeatIntervalMs;
        _song.BarDurationMs = grid.BarDurationMs;

        // CRITICAL: Also shift the actual beat positions array
        // (This is what the waveform editor uses to draw grid lines)
        if (_song.BeatPositionsMs != null && _song.BeatPositionsMs.Count > 0)
        {
            for (int i = 0; i < _song.BeatPositionsMs.Count; i++)
            {
                _song.BeatPositionsMs[i] += offsetMs;
            }
        }

        // Also shift all section loop points by the same offset
        foreach (var sec in _sections)
        {
            sec.LoopInMs += offsetMs;
            sec.LoopOutMs += offsetMs;
        }

        // Update UI
        DownbeatTimecodeBox.Text = MsToTimecode(grid.GridAnchorMs);
        DownbeatDisplayText.Text = $"Beat 1 @ {MsToTimecode(grid.GridAnchorMs)} ({grid.GridAnchorMs:F1} ms)";
        SongInfoText.Text = $"BPM: {_song.Bpm:F2} | Key: {_song.OriginalKey} | Beat: {_song.BeatIntervalMs:F3} ms | Bar: {_song.BarDurationMs:F3} ms | Anchor: {MsToTimecode(_song.GridAnchorMs)}";

        // Refresh the sections grid to show updated loop points
        SectionsGrid.Items.Refresh();
    }

    /// <summary>
    /// Parse a timecode string (M:SS:MMM) or raw milliseconds value.
    /// Returns -1 if invalid.
    /// </summary>
    private static double ParseTimecodeOrMs(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return -1;

        // Try raw milliseconds first (no colon)
        if (!input.Contains(':') && double.TryParse(input, out double rawMs))
            return rawMs >= 0 ? rawMs : -1;

        // Try timecode format M:SS:MMM or M:SS.MMM
        var parts = input.Replace('.', ':').Split(':');
        try
        {
            if (parts.Length == 3)
            {
                int minutes = int.Parse(parts[0]);
                int seconds = int.Parse(parts[1]);
                int millis = int.Parse(parts[2].PadRight(3, '0')[..3]);
                return minutes * 60000.0 + seconds * 1000.0 + millis;
            }
            else if (parts.Length == 2)
            {
                int seconds = int.Parse(parts[0]);
                int millis = int.Parse(parts[1].PadRight(3, '0')[..3]);
                return seconds * 1000.0 + millis;
            }
        }
        catch { }
        return -1;
    }

    // ─── SNAP TO BAR ──────────────────────────────────────────────────────

    private void SnapToBar_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGrid == null || _currentGrid.BarDurationMs <= 0)
        {
            MessageBox.Show("Cannot snap: BPM/grid not set. Run Auto-Detect first or set BPM manually.",
                "No Grid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int beatsPerBar = _song.TimeSignatureNumerator > 0 ? _song.TimeSignatureNumerator : 4;

        foreach (var section in _sections)
        {
            // Prefer actual beat positions for snapping
            if (_song.BeatPositionsMs != null && _song.BeatPositionsMs.Count > 0)
            {
                var downbeats = _song.BeatPositionsMs
                    .Where((b, i) => i % beatsPerBar == 0).ToList();
                if (downbeats.Count > 0)
                {
                    section.LoopInMs = downbeats.OrderBy(b => Math.Abs(b - section.LoopInMs)).First();
                    section.LoopOutMs = downbeats.OrderBy(b => Math.Abs(b - section.LoopOutMs)).First();
                }
            }
            else
            {
                section.LoopInMs = _beatService.SnapToBar(section.LoopInMs, _currentGrid);
                section.LoopOutMs = _beatService.SnapToBar(section.LoopOutMs, _currentGrid);
            }
            section.StartBar = _song.GetBarAtMs(section.LoopInMs);
            section.EndBar = _song.GetBarAtMs(section.LoopOutMs) - 1;
            if (section.EndBar < section.StartBar) section.EndBar = section.StartBar;
        }

        SectionsGrid.Items.Refresh();
        MessageBox.Show("All section boundaries snapped to nearest bar downbeat!",
            "Snapped", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ─── AUTO-DETECT (Click Track Analysis or Drum Detection) ─────────────

    private async void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        // Determine Python path
        // Priority: system PATH python first (where user installs packages via pip),
        // then common manual install locations
        string pythonPath = "python";

        // Check if system python has the required packages
        string[] possiblePaths = {
            "python",  // System PATH (most likely where user ran pip install)
            @"C:\AI\Python3-14\python.exe",
            @"C:\AI\Python3-13-13\python.exe",
            @"C:\Python314\python.exe",
            @"C:\Python313\python.exe",
            @"C:\Python312\python.exe",
        };

        // Find first Python that exists (for file paths) or use "python" for PATH-based
        foreach (var path in possiblePaths)
        {
            if (path == "python" || File.Exists(path))
            {
                pythonPath = path;
                break;
            }
        }

        // Find the analysis script — prefer new multitrack analyzer
        string appFolder = AppDomain.CurrentDomain.BaseDirectory;
        string scriptPath = Path.Combine(appFolder, "Scripts", "analyze_multitrack.py");

        // Also check relative paths
        if (!File.Exists(scriptPath))
        {
            string[] scriptSearchPaths = {
                Path.Combine(appFolder, "Scripts", "analyze_multitrack.py"),
                Path.Combine(appFolder, "..", "Scripts", "analyze_multitrack.py"),
                Path.Combine(appFolder, "..", "..", "Scripts", "analyze_multitrack.py"),
                Path.Combine(appFolder, "..", "..", "..", "Scripts", "analyze_multitrack.py"),
            };
            scriptPath = scriptSearchPaths.FirstOrDefault(File.Exists) ?? "";
        }

        // Fall back to old script if new one not found
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.Combine(appFolder, "Scripts", "analyze_song.py");
            string[] oldScriptPaths = {
                Path.Combine(appFolder, "Scripts", "analyze_song.py"),
                Path.Combine(appFolder, "..", "Scripts", "analyze_song.py"),
                Path.Combine(appFolder, "..", "..", "Scripts", "analyze_song.py"),
                Path.Combine(appFolder, "..", "..", "..", "Scripts", "analyze_song.py"),
            };
            scriptPath = oldScriptPaths.FirstOrDefault(File.Exists) ?? scriptPath;
        }

        if (!File.Exists(scriptPath))
        {
            MessageBox.Show(
                $"Analysis script not found at:\n{scriptPath}\n\n" +
                "Make sure the Scripts folder is next to the .exe",
                "Script Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);

            // Fall back to C# basic detection
            FallbackAutoDetect();
            return;
        }

        // Run Python analysis
        var service = new WorshipPlayer.Core.Services.PythonAnalysisService(pythonPath, scriptPath);

        // Check Python is available
        var (pyOk, pyMsg) = service.CheckPythonAvailable();
        if (!pyOk)
        {
            MessageBox.Show(
                $"Python check failed:\n{pyMsg}\n\n" +
                "Falling back to basic detection.\n\n" +
                "For best results, install Python 3.13 and librosa:\n" +
                "  pip install librosa numpy scipy scikit-learn",
                "Python Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
            FallbackAutoDetect();
            return;
        }

        // ─── GPU/CUDA AUTO-SETUP (one-time check for single-track songs) ───
        // Single-track songs use Demucs AI separation which is 5-10x faster on GPU.
        // Check if this is a single-track song and offer CUDA install if not yet done.
        bool isSingleTrack = _song.Tracks.Count <= 1 ||
            (_song.Tracks.Count == 0 && Directory.Exists(_song.FolderPath) &&
             Directory.GetFiles(_song.FolderPath, "*.*")
                .Count(f => new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a" }
                    .Contains(Path.GetExtension(f).ToLower())) == 1);

        string cudaFlagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_cuda_checked.flag");
        if (isSingleTrack && !File.Exists(cudaFlagPath))
        {
            // First time single-track analysis — check for GPU
            var (hasGpu, hasCuda, gpuInfo) = service.CheckCudaAvailability();

            if (hasGpu && !hasCuda)
            {
                // GPU exists but PyTorch doesn't have CUDA — offer to install
                var result = MessageBox.Show(
                    $"NVIDIA GPU Detected: {gpuInfo}\n\n" +
                    "AI Source Separation (Demucs) can use your GPU to run 5-10x faster:\n\n" +
                    "  CPU mode: 3-10 minutes per song\n" +
                    "  GPU mode: 30-60 seconds per song\n\n" +
                    "This requires downloading CUDA-enabled PyTorch (~2.5GB).\n" +
                    "This is a ONE-TIME download — all future songs will be faster.\n\n" +
                    "Would you like to enable GPU acceleration now?",
                    "Enable GPU Acceleration?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Show progress while installing
                    TapStatusText.Text = "Installing GPU acceleration (one-time)...";
                    TapStatusText.Visibility = Visibility.Visible;
                    ProgressPanel.Visibility = Visibility.Visible;
                    AnalysisProgressBar.IsIndeterminate = true;
                    ProgressStepText.Text = "Downloading CUDA PyTorch (~2.5GB)...";
                    ProgressPercentText.Text = "";

                    Action<string> onStatus = (status) =>
                    {
                        Dispatcher.Invoke(() => ProgressStepText.Text = status);
                    };

                    var (installOk, installMsg) = await service.InstallCudaPyTorchAsync(onStatus);

                    AnalysisProgressBar.IsIndeterminate = false;
                    ProgressPanel.Visibility = Visibility.Collapsed;
                    TapStatusText.Visibility = Visibility.Collapsed;

                    if (installOk)
                    {
                        MessageBox.Show(
                            $"{installMsg}\n\n" +
                            "Demucs will now run 5-10x faster for all future songs.",
                            "GPU Acceleration Enabled",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(
                            $"GPU setup failed:\n{installMsg}\n\n" +
                            "Analysis will continue using CPU (slower but still works).",
                            "GPU Setup Failed",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // Mark as checked (don't ask again regardless of result)
                try { File.WriteAllText(cudaFlagPath, $"checked:{DateTime.Now:yyyy-MM-dd}|gpu:{gpuInfo}|cuda:{hasCuda}"); }
                catch { }
            }
            else if (hasGpu && hasCuda)
            {
                // Already have CUDA — just mark as checked
                try { File.WriteAllText(cudaFlagPath, $"checked:{DateTime.Now:yyyy-MM-dd}|gpu:{gpuInfo}|cuda:true"); }
                catch { }
            }
            else
            {
                // No GPU — mark as checked (no point asking again)
                try { File.WriteAllText(cudaFlagPath, $"checked:{DateTime.Now:yyyy-MM-dd}|gpu:none"); }
                catch { }
            }
        }

        // Show progress
        TapStatusText.Text = "Analyzing multitrack stems + LLM...";
        TapStatusText.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Visible;
        AnalysisProgressBar.Value = 0;
        ProgressStepText.Text = "Starting analysis...";
        ProgressPercentText.Text = "0%";

        // Progress callback updates the UI from the Python script's output
        Action<int, string> onProgress = (percent, description) =>
        {
            Dispatcher.Invoke(() =>
            {
                AnalysisProgressBar.Value = percent;
                ProgressStepText.Text = description;
                ProgressPercentText.Text = $"{percent}%";
                TapStatusText.Text = description;
            });
        };

        var (song, message) = await service.AnalyzeSongAsync(
            _song.FolderPath,
            onProgress: onProgress,
            quality: _isSingleTrack ? GetSelectedQuality() : "default",
            bpmOverride: GetBpmOverride(),
            sectionHints: GetSectionHints(),
            sectionLyrics: GetSectionLyrics());

        TapStatusText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;

        if (song != null)
        {
            // Update song with analysis results
            _song.Bpm = song.Bpm;
            _song.BeatIntervalMs = song.BeatIntervalMs;
            _song.BarDurationMs = song.BarDurationMs;
            _song.GridAnchorMs = song.GridAnchorMs;
            _song.OriginalKey = song.OriginalKey;
            _song.ChordChart = song.ChordChart;
            _song.BeatPositionsMs = song.BeatPositionsMs;

            // For single-track songs: update FolderPath to stems folder and reload tracks
            if (_isSingleTrack && song.Tracks.Count > 1)
            {
                // Find the stems folder (named after the audio file)
                string? stemFolderPath = null;
                if (Directory.Exists(_song.FolderPath))
                {
                    var audioExts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a" };
                    var audioFile = Directory.GetFiles(_song.FolderPath)
                        .FirstOrDefault(f => audioExts.Contains(Path.GetExtension(f).ToLower()));
                    if (audioFile != null)
                    {
                        string stemName = Path.GetFileNameWithoutExtension(audioFile);
                        string stemDir = Path.Combine(_song.FolderPath, stemName);
                        // Case-insensitive match
                        if (!Directory.Exists(stemDir))
                        {
                            foreach (var dir in Directory.GetDirectories(_song.FolderPath))
                            {
                                if (Path.GetFileName(dir).Equals(stemName, StringComparison.OrdinalIgnoreCase))
                                {
                                    stemDir = dir;
                                    break;
                                }
                            }
                        }
                        if (Directory.Exists(stemDir))
                            stemFolderPath = stemDir;
                    }
                }

                if (stemFolderPath != null)
                {
                    // Update song folder to stems folder (where song.json and stems live)
                    _song.FolderPath = stemFolderPath;

                    // Replace track list with separated stems
                    _song.Tracks.Clear();
                    foreach (var track in song.Tracks)
                    {
                        _song.Tracks.Add(track);
                    }
                }
            }

            // Update grid
            _currentGrid = new BeatDetectionService.BeatGrid
            {
                Bpm = song.Bpm,
                BeatIntervalMs = song.BeatIntervalMs,
                BarDurationMs = song.BarDurationMs,
                GridAnchorMs = song.GridAnchorMs
            };

            // Populate sections
            _sections.Clear();
            for (int i = 0; i < song.Sections.Count; i++)
            {
                var s = song.Sections[i];
                _sections.Add(new SectionViewModel
                {
                    Index = i + 1,
                    Name = s.Name,
                    LoopInMs = s.LoopInMs,
                    LoopOutMs = s.LoopOutMs,
                    StartBar = s.StartBar,
                    EndBar = s.EndBar,
                    Color = s.Color,
                    LightingPresetIndex = s.LightingPresetIndex,
                Text = s.Text
                });
            }
            ReindexSections();

            // Apply fetched lyrics to matching sections
            // NOTE: Python's assign_lyrics_by_content() already matched lyrics
            // to sections by content (what's being sung). The section text from
            // Python is correct. Only apply if Python returned empty text.
            if (_fetchedLyrics != null && _fetchedLyrics.Sections.Count > 0)
            {
                // Check if Python already assigned text (content-matched)
                bool pythonAssignedText = song.Sections.Any(s => !string.IsNullOrEmpty(s.Text));
                if (!pythonAssignedText)
                {
                    // Python didn't assign text (maybe no Whisper) — use order-based fallback
                    ApplyFetchedLyricsToSections();
                }
            }

            // Update header
            SongInfoText.Text = $"BPM: {_song.Bpm:F2} | Key: {_song.OriginalKey} | Beat: {_song.BeatIntervalMs:F3} ms | Bar: {_song.BarDurationMs:F3} ms | Anchor: {MsToTimecode(_song.GridAnchorMs)}";

            string trackInfo = _isSingleTrack && song.Tracks.Count > 1
                ? $"\nTracks: {song.Tracks.Count} (AI-separated stems)\nFolder: {_song.FolderPath}"
                : "";

            MessageBox.Show(
                $"Analysis complete!\n\n" +
                $"BPM: {_song.Bpm:F2}\n" +
                $"Key: {_song.OriginalKey}\n" +
                $"Sections: {_sections.Count}\n" +
                $"All loop points are snapped to bar downbeats.{trackInfo}",
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"{message}\n\nFalling back to basic detection.",
                "Analysis Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            FallbackAutoDetect();
        }
    }

    /// <summary>
    /// Fallback: use C# basic beat detection when Python is not available.
    /// </summary>
    private void FallbackAutoDetect()
    {
        // Try click track analysis with C# service
        string? clickPath = FindTrackFile("click");
        string? drumPath = FindTrackFile("drum");
        BeatDetectionService.BeatGrid? grid = null;

        if (clickPath != null)
            grid = _beatService.AnalyzeClickTrack(clickPath);
        if (grid == null && drumPath != null)
            grid = _beatService.DetectBpmFromAudio(drumPath);
        if (grid == null)
        {
            // Try ANY track (single track mode)
            string? anyTrack = _song.Tracks
                .Select(t => Path.Combine(_song.FolderPath, t.FileName))
                .FirstOrDefault(File.Exists);

            // Also check the folder directly for any audio file
            if (anyTrack == null && !string.IsNullOrEmpty(_song.FolderPath) && Directory.Exists(_song.FolderPath))
            {
                var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a" };
                anyTrack = Directory.GetFiles(_song.FolderPath)
                    .FirstOrDefault(f => audioExtensions.Contains(Path.GetExtension(f).ToLower()));
            }

            if (anyTrack != null)
                grid = _beatService.DetectBpmFromAudio(anyTrack);
        }

        if (grid == null && _song.Bpm > 0)
        {
            grid = _currentGrid;
            double dur = EstimateTotalDuration();
            if (grid != null && dur > 0)
            {
                grid.TotalDurationMs = dur;
                grid.TotalBars = (int)((dur - grid.GridAnchorMs) / grid.BarDurationMs);
            }
        }

        if (grid == null || grid.TotalBars <= 0)
        {
            MessageBox.Show("Could not detect BPM. Please set BPM manually.",
                "Detection Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentGrid = grid;
        _song.Bpm = grid.Bpm;
        _song.BeatIntervalMs = grid.BeatIntervalMs;
        _song.BarDurationMs = grid.BarDurationMs;
        _song.GridAnchorMs = grid.GridAnchorMs;

        _sections.Clear();
        GenerateWorshipSections(grid);
        ReindexSections();
        SongInfoText.Text = $"BPM: {_song.Bpm:F2} | Beat: {_song.BeatIntervalMs:F3} ms | Bar: {_song.BarDurationMs:F3} ms | Anchor: {MsToTimecode(_song.GridAnchorMs)}";
    }

    // ─── TAP-ALONG MODE ───────────────────────────────────────────────────

    private void TapMode_Click(object sender, RoutedEventArgs e)
    {
        if (!_tapping)
        {
            // Start tap mode
            _tapping = true;
            _tapTimes.Clear();
            _tapStartTime = DateTime.Now;

            // Both the "TAP BEATS" and the manual "Tap Tempo" buttons share this flow, so give
            // whichever one the operator pressed (and its twin) the same "tapping…" feedback —
            // otherwise the button that was clicked looks inert while tapping is active.
            SetTapButtonsTapping(true);
            TapStatusText.Text = "Tapping... Press SPACE on each BEAT. Press STOP when done.";
            TapStatusText.Visibility = Visibility.Visible;

            // Listen for spacebar
            this.KeyDown += TapMode_KeyDown;
        }
        else
        {
            // Stop tap mode
            StopTapping();
        }
    }

    /// <summary>
    /// Applies the "tapping…" / idle visual state to BOTH tap buttons (the "TAP BEATS" button
    /// and the manual "Tap Tempo" button), since they share the same tap flow. This guarantees
    /// the button the operator actually pressed shows feedback instead of staying inert.
    /// </summary>
    private void SetTapButtonsTapping(bool tapping)
    {
        if (tapping)
        {
            if (TapModeBtn != null)
            {
                TapModeBtn.Content = "STOP TAPPING (press SPACE for each beat)";
                TapModeBtn.Background = System.Windows.Media.Brushes.OrangeRed;
            }
            if (TapTempoBtn != null)
            {
                TapTempoBtn.Content = "Stop (SPACE = beat)";
                TapTempoBtn.Background = System.Windows.Media.Brushes.OrangeRed;
            }
        }
        else
        {
            if (TapModeBtn != null)
            {
                TapModeBtn.Content = "TAP BEATS";
                TapModeBtn.Background = System.Windows.Media.Brushes.DarkSlateBlue;
            }
            if (TapTempoBtn != null)
            {
                TapTempoBtn.Content = "Tap Tempo";
                TapTempoBtn.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            }
        }
    }

    private void TapMode_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_tapping) return;

        if (e.Key == Key.Space)
        {
            double ms = (DateTime.Now - _tapStartTime).TotalMilliseconds;
            _tapTimes.Add(ms);
            TapStatusText.Text = $"Taps: {_tapTimes.Count} | Last: {MsToTimecode(ms)}";
            e.Handled = true;
        }
    }

    private void StopTapping()
    {
        _tapping = false;
        this.KeyDown -= TapMode_KeyDown;
        SetTapButtonsTapping(false);
        TapStatusText.Visibility = Visibility.Collapsed;

        if (_tapTimes.Count < 4)
        {
            MessageBox.Show("Need at least 4 taps to calculate BPM. Try again.",
                "Not Enough Taps", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Calculate grid from taps
        double totalDuration = EstimateTotalDuration();
        var grid = _beatService.CalculateFromTaps(_tapTimes, totalDuration);

        if (grid != null)
        {
            var result = MessageBox.Show(
                $"From {_tapTimes.Count} taps:\n\n" +
                $"BPM: {grid.Bpm:F2}\n" +
                $"Beat Interval: {grid.BeatIntervalMs:F1} ms\n\n" +
                $"Apply this BPM?",
                "Tap Result", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Route the tapped BPM through the single tempo sync point so the BPM box,
                // slider, echo, shared grid, metronome, and generated WAV all stay in sync.
                ApplyManualTempo(grid.Bpm);
            }
        }
    }

    // ─── MANUAL CLICK TRACK: TEMPO SYNC + METRONOME + GENERATE ────────────

    /// <summary>
    /// SINGLE sync point for every tempo input (type, nudge, slider, tap, time-sig picker).
    /// Clamps/validates the BPM, rebuilds the shared _currentGrid via the extended
    /// BuildGridFromManualAnchor so sections/bars/anchor recalc, then refreshes ALL UI inputs
    /// without re-triggering each other (guarded by _suppressTempoSync). If the metronome is
    /// playing, its live loop is retuned without stacking players.
    /// </summary>
    private void ApplyManualTempo(double bpm, bool rebuildGrid = true)
    {
        bpm = ClampBpm(bpm);

        var (num, den) = GetSelectedTimeSignature();

        // Update the song's tempo + time signature.
        _song.Bpm = bpm;
        _song.BeatIntervalMs = 60000.0 / bpm;
        _song.TimeSignatureNumerator = num;
        _song.TimeSignatureDenominator = den;

        if (rebuildGrid)
        {
            double totalDuration = EstimateTotalDuration();
            var grid = _beatService.BuildGridFromManualAnchor(
                _song.GridAnchorMs, bpm, num, den, totalDuration);

            _currentGrid = grid;
            _song.BeatIntervalMs = grid.BeatIntervalMs;
            _song.BarDurationMs = grid.BarDurationMs;
            _song.GridAnchorMs = grid.GridAnchorMs;
        }

        // Refresh all tempo UI inputs so they always reflect the SAME selected tempo.
        _suppressTempoSync = true;
        try
        {
            ManualBpmBox.Text = bpm.ToString("0.#");
            ManualBpmSlider.Value = bpm;
            ManualBpmEcho.Text = $"Current: {bpm:0.#} BPM  ({num}/{den})";
            SongInfoText.Text = $"BPM: {_song.Bpm:F2} | Key: {_song.OriginalKey} | Beat: {_song.BeatIntervalMs:F3} ms | Bar: {_song.BarDurationMs:F3} ms | Anchor: {MsToTimecode(_song.GridAnchorMs)}";
        }
        finally
        {
            _suppressTempoSync = false;
        }

        // Keep sections/bars view in sync with the rebuilt grid.
        SectionsGrid.Items.Refresh();

        // If the metronome is live, retune it without stacking players.
        if (_metronome != null && _metronome.IsPlaying)
        {
            _metronome.Update(bpm, num, den);
        }
    }

    /// <summary>Current BPM from the song, falling back to 120 and clamped to range.</summary>
    private double CurrentManualBpm() => ClampBpm(_song.Bpm > 0 ? _song.Bpm : 120.0);

    private void ManualBpmBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressTempoSync) return;
        CommitManualBpmBox();
    }

    private void ManualBpmBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitManualBpmBox();
        }
    }

    private void CommitManualBpmBox()
    {
        if (ManualBpmBox == null) return;

        string text = ManualBpmBox.Text?.Trim() ?? "";
        if (double.TryParse(text, out double bpm) && !double.IsNaN(bpm) && bpm > 0)
        {
            ApplyManualTempo(bpm);
        }
        else
        {
            // Invalid/empty — revert to the current value, no crash.
            _suppressTempoSync = true;
            try { ManualBpmBox.Text = CurrentManualBpm().ToString("0.#"); }
            finally { _suppressTempoSync = false; }
        }
    }

    private void BpmNudgePlus1_Click(object sender, RoutedEventArgs e) => ApplyManualTempo(CurrentManualBpm() + 1);
    private void BpmNudgeMinus1_Click(object sender, RoutedEventArgs e) => ApplyManualTempo(CurrentManualBpm() - 1);
    private void BpmNudgePlusTenth_Click(object sender, RoutedEventArgs e) => ApplyManualTempo(Math.Round(CurrentManualBpm() + 0.1, 1));
    private void BpmNudgeMinusTenth_Click(object sender, RoutedEventArgs e) => ApplyManualTempo(Math.Round(CurrentManualBpm() - 0.1, 1));

    private void ManualBpmSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressTempoSync) return;
        ApplyManualTempo(e.NewValue);
    }

    private void TimeSig_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard against firing during XAML init (controls not yet wired).
        if (_suppressTempoSync || ManualBpmBox == null) return;
        ApplyManualTempo(CurrentManualBpm());
    }

    // ─── METRONOME LIVE PREVIEW ───────────────────────────────────────────

    private void Metronome_Click(object sender, RoutedEventArgs e)
    {
        _metronome ??= new MetronomePlayer();

        if (_metronome.IsPlaying)
        {
            _metronome.Stop();
            MetronomeToggleBtn.Content = "Start Metronome";
            ManualClickStatus.Text = "Metronome stopped.";
            ManualClickStatus.Foreground = System.Windows.Media.Brushes.LightGray;
            return;
        }

        double bpm = CurrentManualBpm();
        var (num, den) = GetSelectedTimeSignature();

        try
        {
            _metronome.Start(bpm, num, den);
        }
        catch (Exception ex)
        {
            // Defensive — MetronomePlayer.Start should not throw, but never crash the editor.
            ManualClickStatus.Text = $"Metronome error: {ex.Message}";
            ManualClickStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            MetronomeToggleBtn.Content = "Start Metronome";
            return;
        }

        if (_metronome.IsPlaying)
        {
            MetronomeToggleBtn.Content = "Stop Metronome";
            ManualClickStatus.Text = $"Metronome playing at {bpm:0.#} BPM ({num}/{den}).";
            ManualClickStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        else
        {
            // No audio device or degenerate tempo — friendly status, button stays in "Start".
            MetronomeToggleBtn.Content = "Start Metronome";
            ManualClickStatus.Text = _metronome.LastError != null
                ? $"Cannot start metronome: {_metronome.LastError}"
                : "Cannot start metronome (no audio output device).";
            ManualClickStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
    }

    // ─── GENERATE MANUAL CLICK ────────────────────────────────────────────

    private void GenerateManualClick_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGrid == null || _song.Bpm <= 0 || _currentGrid.Bpm <= 0)
        {
            MessageBox.Show("Set a BPM first (type, nudge, slider, or Tap Tempo).",
                "No BPM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(_song.FolderPath))
        {
            MessageBox.Show("No song folder set — cannot write the click WAV.",
                "No Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Always refresh the grid duration from the current song on generate, so a stale
        // positive TotalDurationMs carried over from a prior song/edit can never make the
        // manual WAV run short of or past the current audio length. Fall back to any existing
        // value only if the estimate is unavailable (0).
        double estimatedDuration = EstimateTotalDuration();
        if (estimatedDuration > 0)
            _currentGrid.TotalDurationMs = estimatedDuration;

        if (_currentGrid.TotalDurationMs <= 0)
        {
            MessageBox.Show("Cannot determine song duration. Add audio files first.",
                "No Duration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string outputPath = Path.Combine(_song.FolderPath, "Generated_Click_Manual.wav");

        try
        {
            _beatService.GenerateManualClickTrack(_currentGrid, outputPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to write the click track:\n\n{ex.Message}",
                "Generate Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Idempotently surface the track: remove any prior Generated_Click_Manual.wav entry
        // (case-insensitive) then insert a single fresh one. Do NOT disturb A/B/C entries.
        const string manualFileName = "Generated_Click_Manual.wav";
        var existing = _song.Tracks
            .Where(t => string.Equals(t.FileName, manualFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var t in existing)
            _song.Tracks.Remove(t);

        _song.Tracks.Insert(0, new TrackInfo
        {
            FileName = manualFileName,
            Label = "Click (Manual)",
            Output = TrackOutput.IEM,
            DefaultVolume = 0.8f,
            Color = "#50E3C2"
        });

        var (num, den) = GetSelectedTimeSignature();
        ManualClickStatus.Text = $"Generated {manualFileName} at {_currentGrid.Bpm:0.#} BPM ({num}/{den}).";
        ManualClickStatus.Foreground = System.Windows.Media.Brushes.LightGreen;

        MessageBox.Show(
            $"Generated {manualFileName}\n\n" +
            $"BPM: {_currentGrid.Bpm:0.#}  |  Time Sig: {num}/{den}\n" +
            $"Start (Beat 1): {MsToTimecode(_currentGrid.GridAnchorMs)}\n\n" +
            "Added as the 'Click (Manual)' track (IEM).",
            "Manual Click Generated", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ─── GENERATE CLICK TRACK ─────────────────────────────────────────────

    private void GenerateClick_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGrid == null || _currentGrid.Bpm <= 0)
        {
            MessageBox.Show("No BPM detected. Run Auto-Detect or Tap Beats first.",
                "No BPM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Estimate duration if not set
        if (_currentGrid.TotalDurationMs <= 0)
            _currentGrid.TotalDurationMs = EstimateTotalDuration();

        if (_currentGrid.TotalDurationMs <= 0)
        {
            MessageBox.Show("Cannot determine song duration. Add audio files first.",
                "No Duration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Check if there's an existing click track to sync with
        string? existingClickPath = FindTrackFile("click");

        // Don't sync to a previously generated click (only sync to original recordings)
        if (existingClickPath != null && existingClickPath.Contains("Generated_Click", StringComparison.OrdinalIgnoreCase))
            existingClickPath = null;

        // Generate all three modes (A/B/C) in one action so the operator can A/B/C them.
        string pathA = Path.Combine(_song.FolderPath, "Generated_Click_A.wav");
        string pathB = Path.Combine(_song.FolderPath, "Generated_Click_B.wav");
        string pathC = Path.Combine(_song.FolderPath, "Generated_Click_C.wav");

        try
        {
            _beatService.GenerateClickTrack(_currentGrid, pathA,
                BeatDetectionService.ClickGenerationMode.AdaptiveSync, existingClickTrackPath: existingClickPath);
            _beatService.GenerateClickTrack(_currentGrid, pathB,
                BeatDetectionService.ClickGenerationMode.HybridAnchor, existingClickTrackPath: existingClickPath);
            _beatService.GenerateClickTrack(_currentGrid, pathC,
                BeatDetectionService.ClickGenerationMode.PureGrid);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to generate click tracks:\n\n{ex.Message}",
                "Generate Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Idempotent surfacing: remove the previously-inserted A/B/C generated-click entries,
        // then re-insert A/B/C exactly once each at the top (stable order), so pressing Generate
        // twice overwrites the WAVs without duplicating or stacking track entries. Match the three
        // A/B/C file names EXACTLY so the manual click entry (Generated_Click_Manual.wav), which
        // also starts with "Generated_Click", is never silently dropped by this handler.
        var abcFileNames = new[]
        {
            "Generated_Click_A.wav",
            "Generated_Click_B.wav",
            "Generated_Click_C.wav"
        };
        var existing = _song.Tracks
            .Where(t => abcFileNames.Contains(t.FileName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        foreach (var t in existing)
            _song.Tracks.Remove(t);

        _song.Tracks.Insert(0, new TrackInfo
        {
            FileName = "Generated_Click_C.wav",
            Label = "Click C (Grid)",
            Output = TrackOutput.IEM,
            DefaultVolume = 0.8f,
            Color = "#4A90D9"
        });
        _song.Tracks.Insert(0, new TrackInfo
        {
            FileName = "Generated_Click_B.wav",
            Label = "Click B (Hybrid)",
            Output = TrackOutput.IEM,
            DefaultVolume = 0.8f,
            Color = "#F5A623"
        });
        _song.Tracks.Insert(0, new TrackInfo
        {
            FileName = "Generated_Click_A.wav",
            Label = "Click A (Adaptive Sync)",
            Output = TrackOutput.IEM,
            DefaultVolume = 0.8f,
            Color = "#FF6B6B"
        });

        string syncMsg = existingClickPath != null
            ? $"Existing click found for sync (A) / anchor (B):\n{Path.GetFileName(existingClickPath)}"
            : "No existing click found — A and B fall back to the math grid.";

        MessageBox.Show(
            $"Generated 3 click tracks at {_currentGrid.Bpm:F1} BPM:\n\n" +
            $"• Generated_Click_A.wav — Mode A (Adaptive Sync)\n" +
            $"• Generated_Click_B.wav — Mode B (Hybrid anchor)\n" +
            $"• Generated_Click_C.wav — Mode C (Pure grid)\n\n" +
            $"{syncMsg}",
            "Click Tracks Generated", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ─── SAVE / APPLY / CANCEL ────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ApplySectionsToSong();

        // Ensure FolderPath is set (might not be if song was just analyzed)
        if (string.IsNullOrEmpty(_song.FolderPath))
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select folder to save song.json"
            };
            if (dialog.ShowDialog(this) == true)
                _song.FolderPath = dialog.FolderName;
            else
                return; // User cancelled — don't close
        }

        DialogResult = true;
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ApplySectionsToSong();
        MessageBox.Show("Sections applied! Changes are now active in the player.",
            "Applied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplySectionsToSong()
    {
        _song.Sections.Clear();
        foreach (var vm in _sections)
        {
            _song.Sections.Add(new Section
            {
                Name = vm.Name,
                LoopInMs = vm.LoopInMs,
                LoopOutMs = vm.LoopOutMs,
                StartBar = vm.StartBar,
                EndBar = vm.EndBar,
                Color = vm.Color,
                LightingPresetIndex = vm.LightingPresetIndex,
                Text = vm.Text,
                CanLoop = vm.CanLoop
            });
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ─── RESTORE BACKUP ───────────────────────────────────────────────────

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_song.FolderPath))
        {
            MessageBox.Show("No song folder set — cannot check for backups.",
                "No Backups", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Get available backups
        var songManager = new WorshipPlayer.Core.Services.SongManager("", "");
        var backups = songManager.GetAvailableBackups(_song.FolderPath);

        if (backups.Count == 0)
        {
            MessageBox.Show("No backup versions found for this song.\n\n" +
                "Backups are created automatically each time you save.",
                "No Backups", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Show selection dialog
        string message = "Available backups:\n\n";
        foreach (var (version, modified) in backups)
        {
            message += $"  Version {version}: saved {modified:yyyy-MM-dd HH:mm:ss}\n";
        }
        message += "\nRestore the most recent backup (version 1)?";

        var result = MessageBox.Show(message, "Restore Backup",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var restored = await songManager.RestoreBackupAsync(_song.FolderPath, 1);
            if (restored != null)
            {
                // Apply restored data to current song
                _song.Bpm = restored.Bpm;
                _song.BeatIntervalMs = restored.BeatIntervalMs;
                _song.BarDurationMs = restored.BarDurationMs;
                _song.GridAnchorMs = restored.GridAnchorMs;
                _song.OriginalKey = restored.OriginalKey;
                _song.Sections.Clear();
                foreach (var s in restored.Sections)
                    _song.Sections.Add(s);

                // Refresh UI
                _sections.Clear();
                for (int i = 0; i < _song.Sections.Count; i++)
                {
                    var s = _song.Sections[i];
                    _sections.Add(new SectionViewModel
                    {
                        Index = i + 1,
                        Name = s.Name,
                        LoopInMs = s.LoopInMs,
                        LoopOutMs = s.LoopOutMs,
                        StartBar = s.StartBar,
                        EndBar = s.EndBar,
                        Color = s.Color,
                        LightingPresetIndex = s.LightingPresetIndex,
                Text = s.Text
                    });
                }
                SectionsGrid.Items.Refresh();
                SongInfoText.Text = $"BPM: {_song.Bpm:F2} | Beat: {_song.BeatIntervalMs:F3} ms | Bar: {_song.BarDurationMs:F3} ms | Anchor: {MsToTimecode(_song.GridAnchorMs)}";

                MessageBox.Show("Backup restored successfully!",
                    "Restored", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to restore backup.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ─── RESTORE FROM TRAINING LOG ────────────────────────────────────────

    private void RestoreFromLog_Click(object sender, RoutedEventArgs e)
    {
        // Open file dialog to select a training log JSON file
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select a training log file to restore from",
            Filter = "JSON files (*.json)|*.json",
            InitialDirectory = @"E:\temp\OWClogs"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var record = System.Text.Json.JsonSerializer.Deserialize<WorshipPlayer.Core.Services.TrainingRecord>(
                json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (record == null || record.Sections.Count == 0)
            {
                MessageBox.Show("No section data found in this log file.",
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Confirm
            var confirmResult = MessageBox.Show(
                $"Found training data for: {record.SongTitle}\n" +
                $"Saved: {record.Timestamp:yyyy-MM-dd HH:mm:ss}\n" +
                $"BPM: {record.Bpm:F2} | Key: {record.Key}\n" +
                $"Sections: {record.Sections.Count}\n\n" +
                $"Pattern: {record.SectionPattern}\n\n" +
                "Restore this data?",
                "Restore from Training Log",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            // Apply to current song
            _song.Bpm = record.Bpm;
            _song.OriginalKey = record.Key;
            _song.BeatIntervalMs = 60000.0 / record.Bpm;
            _song.BarDurationMs = _song.BeatIntervalMs * _song.TimeSignatureNumerator;
            _song.BeatPositionsMs = record.BeatPositionsMs;

            // Restore sections
            _song.Sections.Clear();
            _sections.Clear();

            for (int i = 0; i < record.Sections.Count; i++)
            {
                var s = record.Sections[i];
                _song.Sections.Add(new Section
                {
                    Name = s.Name,
                    LoopInMs = s.LoopInMs,
                    LoopOutMs = s.LoopOutMs,
                    StartBar = s.StartBar,
                    EndBar = s.EndBar,
                    Color = GetColorForSection(s.Name)
                });

                _sections.Add(new SectionViewModel
                {
                    Index = i + 1,
                    Name = s.Name,
                    LoopInMs = s.LoopInMs,
                    LoopOutMs = s.LoopOutMs,
                    StartBar = s.StartBar,
                    EndBar = s.EndBar,
                    Color = GetColorForSection(s.Name)
                });
            }

            // Update grid
            if (record.BeatPositionsMs != null && record.BeatPositionsMs.Count > 1)
            {
                double firstBeat = record.BeatPositionsMs[0];
                _song.GridAnchorMs = firstBeat;
                _currentGrid = new BeatDetectionService.BeatGrid
                {
                    Bpm = record.Bpm,
                    BeatIntervalMs = _song.BeatIntervalMs,
                    BarDurationMs = _song.BarDurationMs,
                    GridAnchorMs = firstBeat,
                    TimeSignatureNumerator = _song.TimeSignatureNumerator
                };
            }

            SectionsGrid.Items.Refresh();
            SongInfoText.Text = $"BPM: {_song.Bpm:F2} | Key: {_song.OriginalKey} | Beat: {_song.BeatIntervalMs:F3} ms | Bar: {_song.BarDurationMs:F3} ms | Anchor: {MsToTimecode(_song.GridAnchorMs)}";

            MessageBox.Show($"Restored {record.Sections.Count} sections from training log!",
                "Restored", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error reading log file:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GetColorForSection(string name)
    {
        string upper = name.ToUpper();
        if (upper.Contains("INTRO")) return "#4A90D9";
        if (upper.Contains("VERSE")) return "#7ED321";
        if (upper.Contains("CHORUS")) return "#E74C3C";
        if (upper.Contains("PRE")) return "#F5A623";
        if (upper.Contains("BRIDGE")) return "#9B59B6";
        if (upper.Contains("INSTRUMENTAL")) return "#50E3C2";
        if (upper.Contains("OUTRO") || upper.Contains("END")) return "#4A90D9";
        if (upper.Contains("TAG")) return "#F5A623";
        return "#4A90D9";
    }

    // ─── HELPERS ──────────────────────────────────────────────────────────

    private void ReindexSections()
    {
        for (int i = 0; i < _sections.Count; i++)
            _sections[i].Index = i + 1;
        SectionsGrid.Items.Refresh();
    }

    private string? FindTrackFile(string keyword)
    {
        return _song.Tracks
            .Where(t => t.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        t.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(t => Path.Combine(_song.FolderPath, t.FileName))
            .FirstOrDefault(File.Exists);
    }

    private double EstimateTotalDuration()
    {
        foreach (var track in _song.Tracks)
        {
            var path = Path.Combine(_song.FolderPath, track.FileName);
            if (File.Exists(path))
            {
                try
                {
                    using var reader = new NAudio.Wave.AudioFileReader(path);
                    return reader.TotalTime.TotalMilliseconds;
                }
                catch { }
            }
        }
        // Fallback estimate
        return _song.GridAnchorMs + _song.BarDurationMs * 88;
    }

    private void GenerateWorshipSections(BeatDetectionService.BeatGrid grid)
    {
        double barMs = grid.BarDurationMs;
        double anchor = grid.GridAnchorMs;
        int totalBars = grid.TotalBars;

        if (totalBars <= 0) return;

        var templates = new List<(string Name, int Bars, string Color)>();

        if (totalBars >= 80)
        {
            templates.Add(("INTRO", 5, "#4A90D9"));
            templates.Add(("VERSE 1", 9, "#7ED321"));
            templates.Add(("VERSE 2", 9, "#7ED321"));
            templates.Add(("PRE-CHORUS", 9, "#F5A623"));
            templates.Add(("CHORUS 1", 17, "#E74C3C"));
            templates.Add(("BRIDGE", 3, "#9B59B6"));
            templates.Add(("VERSE 3", 8, "#7ED321"));
            templates.Add(("CHORUS 2 (FINAL)", 21, "#E74C3C"));
            templates.Add(("OUTRO / ENDING", 7, "#4A90D9"));
        }
        else if (totalBars >= 60)
        {
            templates.Add(("INTRO", 4, "#4A90D9"));
            templates.Add(("VERSE 1", 8, "#7ED321"));
            templates.Add(("CHORUS 1", 8, "#E74C3C"));
            templates.Add(("VERSE 2", 8, "#7ED321"));
            templates.Add(("CHORUS 2", 8, "#E74C3C"));
            templates.Add(("BRIDGE", 4, "#9B59B6"));
            templates.Add(("CHORUS 3 (FINAL)", 8, "#E74C3C"));
            int remaining = totalBars - 48;
            if (remaining > 0) templates.Add(("OUTRO", remaining, "#4A90D9"));
        }
        else if (totalBars >= 40)
        {
            templates.Add(("INTRO", 4, "#4A90D9"));
            templates.Add(("VERSE 1", 8, "#7ED321"));
            templates.Add(("CHORUS", 8, "#E74C3C"));
            templates.Add(("VERSE 2", 8, "#7ED321"));
            templates.Add(("CHORUS 2", 8, "#E74C3C"));
            int remaining = totalBars - 36;
            if (remaining > 0) templates.Add(("OUTRO", remaining, "#4A90D9"));
        }
        else
        {
            int currentBar = 1;
            int sectionNum = 1;
            while (currentBar <= totalBars)
            {
                int bars = Math.Min(4, totalBars - currentBar + 1);
                templates.Add(($"Section {sectionNum}", bars, "#4A90D9"));
                currentBar += bars;
                sectionNum++;
            }
        }

        int barCursor = 1;
        foreach (var (name, bars, color) in templates)
        {
            if (barCursor > totalBars) break;
            int actualBars = Math.Min(bars, totalBars - barCursor + 1);
            int startBar = barCursor;
            int endBar = barCursor + actualBars - 1;
            double loopInMs = anchor + (startBar - 1) * barMs;
            double loopOutMs = anchor + endBar * barMs;

            _sections.Add(new SectionViewModel
            {
                Index = _sections.Count + 1,
                Name = name,
                LoopInMs = Math.Round(loopInMs, 3),
                LoopOutMs = Math.Round(loopOutMs, 3),
                StartBar = startBar,
                EndBar = endBar,
                Color = color
            });

            barCursor = endBar + 1;
        }
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
/// ViewModel for section editing grid.
/// </summary>
public class SectionViewModel
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public double LoopInMs { get; set; }
    public double LoopOutMs { get; set; }
    public int StartBar { get; set; }
    public int EndBar { get; set; }
    public string Color { get; set; } = "#4A90D9";
    public int LightingPresetIndex { get; set; } = -1;
    public string Text { get; set; } = "";
    public bool CanLoop { get; set; } = true;

    public string LoopInTimecode
    {
        get => MsToTimecode(LoopInMs);
        set => LoopInMs = TimecodeToMs(value);
    }

    public string LoopOutTimecode
    {
        get => MsToTimecode(LoopOutMs);
        set => LoopOutMs = TimecodeToMs(value);
    }

    public string DurationDisplay
    {
        get
        {
            double durationMs = LoopOutMs - LoopInMs;
            int bars = EndBar - StartBar + 1;
            return $"{MsToTimecode(durationMs)} ({bars} bars)";
        }
    }

    public string BarRange => $"{StartBar}-{EndBar}";

    private static string MsToTimecode(double ms)
    {
        int totalMs = (int)Math.Round(ms);
        int minutes = totalMs / 60000;
        int seconds = (totalMs % 60000) / 1000;
        int millis = totalMs % 1000;
        return $"{minutes}:{seconds:00}:{millis:000}";
    }

    private static double TimecodeToMs(string timecode)
    {
        if (string.IsNullOrWhiteSpace(timecode)) return 0;
        if (double.TryParse(timecode, out double rawMs) && !timecode.Contains(':'))
            return rawMs;

        var parts = timecode.Replace('.', ':').Split(':');
        try
        {
            if (parts.Length == 3)
            {
                int minutes = int.Parse(parts[0]);
                int seconds = int.Parse(parts[1]);
                int millis = int.Parse(parts[2].PadRight(3, '0')[..3]);
                return minutes * 60000.0 + seconds * 1000.0 + millis;
            }
            else if (parts.Length == 2)
            {
                int first = int.Parse(parts[0]);
                int second = int.Parse(parts[1].PadRight(3, '0')[..3]);
                return first >= 60 ? first * 1000.0 + second : first * 60000.0 + second * 1000.0;
            }
        }
        catch { }
        return 0;
    }
}
