using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.UI.Views;

/// <summary>
/// PowerPoint-style chord chart window.
/// Shows lyrics with chords positioned above the specific words where chord changes occur.
/// Slides advance automatically based on playback position.
/// </summary>
public partial class ChordChartWindow : Window
{
    private ChordChartData? _chartData;
    private Song? _song;
    private int _currentSlideIndex = -1;
    private readonly DispatcherTimer _syncTimer;
    private Func<double>? _getPositionMs;

    // Colors
    private static readonly SolidColorBrush ChordBrush = new(Color.FromRgb(126, 211, 33));   // Green
    private static readonly SolidColorBrush LyricBrush = new(Color.FromRgb(255, 255, 255));  // White
    private static readonly SolidColorBrush ActiveWordBrush = new(Color.FromRgb(74, 144, 217)); // Blue highlight
    private static readonly SolidColorBrush DimBrush = new(Color.FromRgb(108, 108, 128));    // Dim gray

    /// <summary>
    /// Current transposition in semitones. Applied to all [chord] display at render time.
    /// Does NOT modify stored data — only affects visual display.
    /// </summary>
    public int TransposeSemitones { get; set; } = 0;

    public ChordChartWindow()
    {
        InitializeComponent();

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100) // 10 FPS sync
        };
        _syncTimer.Tick += SyncTimer_Tick;

        Closed += (s, e) => _syncTimer.Stop();
    }

    /// <summary>
    /// Initialize the chord chart with song data and a position callback.
    /// </summary>
    public void SetData(Song song, ChordChartData chartData, Func<double> getPositionMs)
    {
        _song = song;
        _chartData = chartData;
        _getPositionMs = getPositionMs;

        SongTitleText.Text = $"{song.Title} - {song.OriginalKey}";
        KeyBpmText.Text = $"Key: {song.OriginalKey} | BPM: {song.Bpm:F0}";

        if (_chartData.Slides.Count > 0)
        {
            ShowSlide(0);
        }

        _syncTimer.Start();
    }

    /// <summary>
    /// Timer tick: check playback position and advance slide if needed.
    /// </summary>
    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (_chartData == null || _getPositionMs == null) return;

        // Add a small pre-roll offset (500ms) so slides appear BEFORE the vocal
        // starts singing the next line. Musicians need to see lyrics slightly early.
        double currentMs = _getPositionMs() + 500;

        // Find which slide should be showing based on current position.
        // Shared rule with the audience slides (Issue #5, 28/10): interval
        // hit, else last slide already started (seek-proof — the old
        // "keep the previous page" held a STALE section after a backward
        // seek into a gap), else keep the current page.
        int targetSlide = ChordChartSlideSelector.SelectSlideIndex(_chartData.Slides, currentMs);

        if (targetSlide >= 0 && targetSlide != _currentSlideIndex)
        {
            ShowSlide(targetSlide);
        }

        // Update word highlighting on current slide
        if (_currentSlideIndex >= 0 && _currentSlideIndex < _chartData.Slides.Count)
        {
            UpdateWordHighlight(currentMs);
        }
    }

    /// <summary>
    /// Display a specific slide (transition to it).
    /// </summary>
    private void ShowSlide(int slideIndex)
    {
        if (_chartData == null || slideIndex < 0 || slideIndex >= _chartData.Slides.Count)
            return;

        _currentSlideIndex = slideIndex;
        var slide = _chartData.Slides[slideIndex];

        // Update header
        SectionNameText.Text = slide.SectionName;
        SlideCountText.Text = $"{slideIndex + 1} / {_chartData.Slides.Count}";

        // Update next section preview
        if (!string.IsNullOrEmpty(slide.NextSectionName))
        {
            NextSectionText.Text = slide.NextSectionName;
            NextChordText.Text = !string.IsNullOrEmpty(slide.NextSectionChord)
                ? $"({slide.NextSectionChord})" : "";
        }
        else
        {
            NextSectionText.Text = "END";
            NextChordText.Text = "";
        }

        // Build lyrics display
        BuildSlideContent(slide);
    }

    /// <summary>
    /// Build the visual content for a slide.
    /// NEW SYSTEM: Reads from sections[].text with inline [chord] format.
    /// LEGACY FALLBACK: Uses slide.Lines if section text is empty.
    /// 
    /// Styled display:
    /// - Chords: green, bold, monospaced, positioned above lyrics
    /// - Lyrics: white, large, readable font
    /// - Previous chord carries over to lines without new chord
    /// </summary>
    private void BuildSlideContent(ChartSlide slide)
    {
        LyricsPanel.Children.Clear();

        try
        {
            // Try NEW system first: read from section text (inline [chord] format)
            string? sectionText = GetSectionText(slide.SectionName, slide.SlideIndex);

            if (!string.IsNullOrWhiteSpace(sectionText))
            {
                BuildFromInlineChords(sectionText);
                return;
            }

            // LEGACY fallback: use slide.Lines (old song.json format)
            var lines = slide.Lines;
            if (lines != null && lines.Count > 0)
            {
                BuildFromLegacyLines(lines);
                return;
            }

            // FINAL FALLBACK: Try to show ANY text from the matching section
            if (_song != null)
            {
                var section = _song.Sections.FirstOrDefault(s =>
                    s.Name.Equals(slide.SectionName, StringComparison.OrdinalIgnoreCase));
                if (section == null)
                    section = _song.Sections.FirstOrDefault(s =>
                        s.Name.Contains(slide.SectionName, StringComparison.OrdinalIgnoreCase));

                if (section != null && !string.IsNullOrWhiteSpace(section.Text))
                {
                    BuildFromInlineChords(section.Text);
                    return;
                }
            }

            // Nothing to show
            LyricsPanel.Children.Add(new TextBlock
            {
                Text = $"({slide.SectionName})\nNo lyrics available.",
                Foreground = DimBrush,
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        catch (Exception ex)
        {
            LyricsPanel.Children.Clear();
            LyricsPanel.Children.Add(new TextBlock
            {
                Text = $"Error displaying chord chart: {ex.Message}",
                Foreground = System.Windows.Media.Brushes.OrangeRed,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    /// <summary>
    /// Build display from legacy ChartLine objects (old song.json format).
    /// </summary>
    private void BuildFromLegacyLines(List<ChartLine> lines)
    {
        foreach (var line in lines)
        {
            var lineContainer = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };

            var chordTextBlock = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = ChordBrush,
                FontFamily = new FontFamily("Consolas, Courier New, monospace")
            };

            if (line.Chords != null && line.Chords.Count > 0)
            {
                string chordLine = BuildChordLine(line.Text, line.Chords);
                chordTextBlock.Text = chordLine;
            }
            else
            {
                chordTextBlock.Visibility = Visibility.Collapsed;
            }

            var lyricTextBlock = new TextBlock
            {
                FontSize = 22,
                Foreground = LyricBrush,
                FontFamily = new FontFamily("Segoe UI, Arial"),
                TextWrapping = TextWrapping.Wrap
            };

            if (line.Words != null && line.Words.Count > 0)
            {
                foreach (var word in line.Words)
                {
                    var run = new Run(word.Word + " ") { Tag = word };
                    lyricTextBlock.Inlines.Add(run);
                }
            }
            else
            {
                lyricTextBlock.Text = line.Text ?? "";
            }

            lineContainer.Children.Add(chordTextBlock);
            lineContainer.Children.Add(lyricTextBlock);
            LyricsPanel.Children.Add(lineContainer);
        }
    }

    /// <summary>
    /// Build styled display from inline [chord] text.
    /// Parses [B], [E], etc. and renders chords above lyrics in color.
    /// </summary>
    private void BuildFromInlineChords(string text)
    {
        var parser = new WorshipPlayer.Core.Services.ChordProParserService();

        // Apply transposition if pitch has been shifted
        if (TransposeSemitones != 0)
            text = parser.Transpose(text, TransposeSemitones);

        // Use the shared Core builder as the single source of truth for the chord-over-lyric
        // strings; this view keeps its own TextBlock creation/coloring (incl. the dim "(chord)"
        // style via CarriedOver).
        foreach (var line in parser.BuildChordOverLyric(text))
        {
            var lineContainer = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };

            // Build chord display line
            var chordTextBlock = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = ChordBrush,
                FontFamily = new FontFamily("Consolas, Courier New, monospace")
            };

            if (line.CarriedOver)
            {
                // No chord on this line — show carried-over chord (dim)
                chordTextBlock.Text = line.ChordLine;
                chordTextBlock.Foreground = DimBrush;
                chordTextBlock.FontSize = 16;
            }
            else if (!string.IsNullOrEmpty(line.ChordLine))
            {
                chordTextBlock.Text = line.ChordLine;
            }
            else
            {
                chordTextBlock.Visibility = Visibility.Collapsed;
            }

            // Build lyric line (large, white, readable)
            var lyricTextBlock = new TextBlock
            {
                Text = line.LyricLine,
                FontSize = 24,
                Foreground = LyricBrush,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                TextWrapping = TextWrapping.Wrap
            };

            lineContainer.Children.Add(chordTextBlock);
            lineContainer.Children.Add(lyricTextBlock);
            LyricsPanel.Children.Add(lineContainer);
        }
    }

    /// <summary>
    /// Get the text for a specific slide from sections[].text.
    /// Slides are defined by empty lines in the section text.
    /// </summary>
    private string? GetSectionText(string sectionName, int slideIndex)
    {
        if (_song == null || _song.Sections == null || _song.Sections.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(sectionName)) return null;

        // Find the section by name (try exact match first, then contains)
        var section = _song.Sections.FirstOrDefault(s =>
            s.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase));

        // Fallback: partial match
        if (section == null)
        {
            section = _song.Sections.FirstOrDefault(s =>
                s.Name.Contains(sectionName, StringComparison.OrdinalIgnoreCase) ||
                sectionName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (section == null || string.IsNullOrWhiteSpace(section.Text))
            return null;

        // If text has no empty lines (no slide breaks), return all text for slide 0
        if (!section.Text.Contains("\n\n") && !section.Text.Contains("\r\n\r\n"))
        {
            return slideIndex == 0 ? section.Text.Trim() : null;
        }

        // Split section text by empty lines (slide breaks)
        var slideBlocks = section.Text.Split(new[] { "\n\n", "\r\n\r\n" },
            StringSplitOptions.None);

        if (slideIndex >= 0 && slideIndex < slideBlocks.Length)
            return slideBlocks[slideIndex].Trim();

        // Fallback: return the first block if index is out of range
        return slideBlocks.Length > 0 ? slideBlocks[0].Trim() : null;
    }

    /// <summary>
    /// Build a chord line string with chords positioned at the correct character offsets.
    /// </summary>
    private string BuildChordLine(string lyricText, List<ChordPosition> chords)
    {
        // Create a char array of spaces the same width as the lyric text
        int lineLength = Math.Max(lyricText.Length + 10, 80);
        char[] chordChars = new char[lineLength];
        Array.Fill(chordChars, ' ');

        foreach (var cp in chords)
        {
            int offset = Math.Clamp(cp.CharOffset, 0, lineLength - cp.Chord.Length - 1);
            for (int i = 0; i < cp.Chord.Length && offset + i < lineLength; i++)
            {
                chordChars[offset + i] = cp.Chord[i];
            }
        }

        return new string(chordChars).TrimEnd();
    }

    /// <summary>
    /// Highlight the currently singing word based on playback position.
    /// </summary>
    private void UpdateWordHighlight(double currentMs)
    {
        foreach (var child in LyricsPanel.Children)
        {
            if (child is not StackPanel lineContainer) continue;

            foreach (var element in lineContainer.Children)
            {
                if (element is not TextBlock tb || tb.Tag is not ChartLine) continue;

                foreach (var inline in tb.Inlines)
                {
                    if (inline is Run run && run.Tag is TimedWord tw)
                    {
                        if (currentMs >= tw.StartMs && currentMs < tw.EndMs)
                        {
                            run.Foreground = ActiveWordBrush;
                            run.FontWeight = FontWeights.Bold;
                        }
                        else if (currentMs > tw.EndMs)
                        {
                            run.Foreground = LyricBrush;
                            run.FontWeight = FontWeights.Normal;
                        }
                        else
                        {
                            run.Foreground = DimBrush;
                            run.FontWeight = FontWeights.Normal;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Manually jump to a specific slide by index.
    /// </summary>
    public void JumpToSlide(int slideIndex)
    {
        if (_chartData == null) return;
        if (slideIndex >= 0 && slideIndex < _chartData.Slides.Count)
        {
            ShowSlide(slideIndex);
        }
    }

    /// <summary>
    /// Manually jump to a specific slide (e.g., when user clicks a section).
    /// </summary>
    public void JumpToSlideForSection(int sectionIndex)
    {
        if (_chartData == null || _song == null) return;

        string sectionName = sectionIndex < _song.Sections.Count
            ? _song.Sections[sectionIndex].Name : "";

        for (int i = 0; i < _chartData.Slides.Count; i++)
        {
            if (_chartData.Slides[i].SectionName == sectionName)
            {
                ShowSlide(i);
                return;
            }
        }
    }

    /// <summary>
    /// Refresh the currently displayed slide (e.g., after transposition change).
    /// </summary>
    public void RefreshCurrentSlide()
    {
        if (_currentSlideIndex >= 0)
            ShowSlide(_currentSlideIndex);
    }
}
