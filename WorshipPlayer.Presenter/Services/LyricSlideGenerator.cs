using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Generates slides from song lyrics based on the configured display mode.
/// 
/// Supports:
/// - TwoLines: 2 lines per slide (ProPresenter default)
/// - FullVerse: All lines of a section on one slide
/// - OneLine: 1 line per slide (karaoke style)
/// - Custom: N lines per slide (configurable)
/// 
/// Also handles:
/// - Section-aware splitting (respects section boundaries)
/// - Title slide generation
/// - Copyright/CCLI footer
/// </summary>
public class LyricSlideGenerator
{
    private readonly SlideTheme _theme;

    public LyricSlideGenerator(SlideTheme theme)
    {
        _theme = theme;
    }

    /// <summary>
    /// Generate slides for a song based on its lyrics and sections.
    /// Lyrics can come from the Song's section text or from a
    /// separate lyrics string (ChordPro, plain text, etc.).
    /// If ChartSlide timing data is available, assigns TimestampMs to slides.
    /// </summary>
    public List<Slide> GenerateFromSong(Song song, string? lyricsText = null)
    {
        var slides = new List<Slide>();
        int order = 0;

        // Optional title slide
        if (_theme.ShowSongTitle)
        {
            slides.Add(CreateTitleSlide(song, order++));
        }

        // Generate lyrics slides from sections, walking the shared slide-order
        // resolver (đợt 2 T9 / R6.1): no custom order → the original section order;
        // with one, sections repeat/skip per the order and Blank units become one
        // blank slide each — the same units PresenterEngine.GenerateSlidesForSong uses.
        if (song.Sections.Count > 0)
        {
            // Same repeated-name rule as PresenterEngine: count section
            // occurrences in order so chart timing pairs the Nth section
            // with the Nth chart entry of that name (field fix 2026-10-25).
            var sectionOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var unit in SongSlideOrderResolver.ResolveUnits(song))
            {
                if (unit.IsBlank)
                {
                    slides.Add(new Slide
                    {
                        Type = SlideType.Blank,
                        Content = new SlideContent(),
                        Order = order++,
                        Transition = SlideTransition.Fade,
                        TransitionDurationMs = 500
                    });
                    continue;
                }

                var section = unit.Section!;
                string sectionLyrics = section.Text;
                if (string.IsNullOrWhiteSpace(sectionLyrics))
                    continue;

                int sectionOccurrence = sectionOccurrences.TryGetValue(section.Name, out var seen)
                    ? seen : 0;
                sectionOccurrences[section.Name] = sectionOccurrence + 1;

                var sectionSlides = SplitLyricsToSlides(
                    sectionLyrics,
                    section.Name,
                    _theme.LyricDisplayMode,
                    _theme.LyricsLinesPerSlide);

                // Assign timing from ChartSlide data if available
                if (song.ChordChart?.Slides != null)
                {
                    AssignChartSlideTimings(sectionSlides, section.Name, song.ChordChart.Slides, sectionOccurrence);
                }

                foreach (var slide in sectionSlides)
                {
                    slide.Order = order++;
                    slides.Add(slide);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(lyricsText))
        {
            // No sections defined — split raw lyrics text
            var lyricsSlides = SplitLyricsToSlides(
                lyricsText,
                "",
                _theme.LyricDisplayMode,
                _theme.LyricsLinesPerSlide);

            foreach (var slide in lyricsSlides)
            {
                slide.Order = order++;
                slides.Add(slide);
            }
        }

        // Safety net (field fix 2026-10-25): timestamps must increase with
        // slide order, or the position-update path jumps to the last slide
        // carrying an old time and sticks there. Drop any timestamp that
        // steps back; those slides still advance by section change / tap.
        double lastTimedMs = double.MinValue;
        foreach (var slide in slides)
        {
            if (slide.TimestampMs.HasValue)
            {
                if (slide.TimestampMs.Value <= lastTimedMs)
                    slide.TimestampMs = null;
                else
                    lastTimedMs = slide.TimestampMs.Value;
            }
        }

        return slides;
    }

    /// <summary>
    /// Generate slides from plain lyrics text (for songs without section data).
    /// </summary>
    public List<Slide> GenerateFromText(string lyrics, string songTitle = "")
    {
        var slides = new List<Slide>();
        int order = 0;

        if (!string.IsNullOrEmpty(songTitle) && _theme.ShowSongTitle)
        {
            slides.Add(new Slide
            {
                Type = SlideType.Title,
                Content = new SlideContent { Text = songTitle },
                Order = order++,
                Transition = SlideTransition.Fade,
                TransitionDurationMs = 500
            });
        }

        // Split by blank lines (sections/stanzas)
        var stanzas = SplitIntoStanzas(lyrics);

        foreach (var stanza in stanzas)
        {
            var stanzaSlides = SplitLyricsToSlides(
                stanza,
                "",
                _theme.LyricDisplayMode,
                _theme.LyricsLinesPerSlide);

            foreach (var slide in stanzaSlides)
            {
                slide.Order = order++;
                slides.Add(slide);
            }
        }

        return slides;
    }

    /// <summary>
    /// Split lyrics text into slides based on display mode.
    /// </summary>
    private List<Slide> SplitLyricsToSlides(
        string lyrics,
        string sectionName,
        LyricDisplayMode mode,
        int linesPerSlide)
    {
        var slides = new List<Slide>();
        var lines = lyrics
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0) return slides;

        int chunkSize = mode switch
        {
            LyricDisplayMode.OneLine => 1,
            LyricDisplayMode.TwoLines => 2,
            LyricDisplayMode.FullVerse => lines.Count, // All lines on one slide
            LyricDisplayMode.Custom => Math.Max(1, linesPerSlide),
            _ => 2
        };

        for (int i = 0; i < lines.Count; i += chunkSize)
        {
            var chunk = lines.Skip(i).Take(chunkSize).ToList();
            var text = string.Join("\n", chunk);

            slides.Add(new Slide
            {
                Type = SlideType.Lyrics,
                Content = new SlideContent
                {
                    Text = text,
                    Subtitle = sectionName
                },
                SectionName = sectionName,
                Transition = SlideTransition.Fade,
                TransitionDurationMs = 500
            });
        }

        return slides;
    }

    /// <summary>
    /// Split raw lyrics into stanzas (separated by blank lines).
    /// </summary>
    private List<string> SplitIntoStanzas(string lyrics)
    {
        var stanzas = new List<string>();
        var lines = lyrics.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var currentStanza = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentStanza.Count > 0)
                {
                    stanzas.Add(string.Join("\n", currentStanza));
                    currentStanza.Clear();
                }
            }
            else
            {
                currentStanza.Add(line);
            }
        }

        if (currentStanza.Count > 0)
            stanzas.Add(string.Join("\n", currentStanza));

        return stanzas;
    }

    /// <summary>
    /// Create a title slide for a song.
    /// </summary>
    private Slide CreateTitleSlide(Song song, int order)
    {
        var subtitle = "";
        if (!string.IsNullOrEmpty(song.Artist))
            subtitle = song.Artist;
        if (!string.IsNullOrEmpty(song.OriginalKey))
            subtitle += string.IsNullOrEmpty(subtitle) ? $"Key: {song.OriginalKey}" : $" | Key: {song.OriginalKey}";

        return new Slide
        {
            Type = SlideType.Title,
            Content = new SlideContent
            {
                Text = song.Title,
                Subtitle = subtitle
            },
            Order = order,
            Transition = SlideTransition.Fade,
            TransitionDurationMs = 700
        };
    }

    /// <summary>
    /// Assign timing data from ChartSlides to generated slides.
    /// Matches by section name and slide index within the section, taking
    /// the <paramref name="occurrence"/>-th chart entry for repeated section
    /// names — never reuse an earlier occurrence's time (field fix 2026-10-25).
    /// </summary>
    private void AssignChartSlideTimings(List<Slide> sectionSlides, string sectionName, List<ChartSlide> chartSlides, int occurrence)
    {
        for (int i = 0; i < sectionSlides.Count; i++)
        {
            var matchingChart = chartSlides
                .Where(cs => cs.SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase) &&
                             cs.SlideIndex == i)
                .Skip(occurrence)
                .FirstOrDefault();

            if (matchingChart != null && matchingChart.StartMs > 0)
            {
                sectionSlides[i].TimestampMs = matchingChart.StartMs;
            }
        }
    }
}
