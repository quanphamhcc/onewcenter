using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Issue #5 (28/10): chord chart vs audience section sync — SECOND fix.
///
/// NOTE: this file supersedes the 1f360e8 anchor tests (same filename,
/// kept so the push tool can update it). The anchoring approach is
/// REVERTED by this change, deliberately: it made the audience follow
/// section LoopInMs while the chord window follows the chord-slide
/// table, and the two tables disagree in section-start gaps (BRIDGE 1:
/// LoopIn 146448 vs first chord slide 156678) — so forward play through
/// a section intro still showed two sections. Worse, the field log after
/// 1f360e8 shows the audience running CLEAN through the whole song, so
/// the screenshots of "chord BRIDGE 1 + audience CHORUS" cannot come from
/// forward play at all: they come from the chord window's old "keep the
/// previous page" rule going STALE after a backward seek into a gap
/// (seek from BRIDGE 1 back to just before 2:26: chord holds BRIDGE 1,
/// audience correctly shows CHORUS).
///
/// The sync contract now (Kiro's demand, implemented literally):
/// - The audience slides carry the chord table's numbers (each slide's
///   timestamp is its chord block's StartMs; later chunks interpolate
///   inside the block), so the audience's section-change points ARE the
///   chord table's section-change points.
/// - The chord window uses ChordChartSlideSelector: interval hit, else
///   last slide already started (seek-proof), else keep the page.
/// Both displays then agree on section at every position, forward and
/// after seeks, modulo the chord window's documented 500ms pre-roll.
/// </summary>
public class PresenterEngineChordSyncTests
{
    private static PresenterEngine NewEngine()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        return new PresenterEngine(manager, new PresenterSettings());
    }

    private static Song SongWith(params (string Name, double LoopIn, double LoopOut, string Text)[] sections)
    {
        var song = new Song { Title = "Sync Song", OriginalKey = "C", ChordChart = new ChordChartData { Slides = new List<ChartSlide>() } };
        var list = new List<Section>();
        foreach (var (name, loopIn, loopOut, text) in sections)
            list.Add(new Section { Name = name, LoopInMs = loopIn, LoopOutMs = loopOut, Text = text });
        song.Sections = list;
        return song;
    }

    private static void AddChart(Song song, string section, int index, double start, double end)
    {
        song.ChordChart!.Slides.Add(new ChartSlide { SectionName = section, SlideIndex = index, StartMs = start, EndMs = end });
    }

    [Fact]
    public void GenerationUsesChordTableTimes_NotLoopIn()
    {
        // Anchoring is gone: a section whose chart slides start 10s after
        // its LoopIn keeps the chord-table time (the chord window's number).
        var engine = NewEngine();
        var song = SongWith(
            ("AAA", 10000, 40000, "A1\nA2"),
            ("BBB", 40000, 75000, "B1\nB2\nB3\nB4"));
        AddChart(song, "AAA", 0, 10000, 40000);
        AddChart(song, "BBB", 0, 50000, 60000);

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(3, slides.Count);
        Assert.Equal(10000, slides[0].TimestampMs);
        Assert.Equal(50000, slides[1].TimestampMs);
        Assert.Equal(55000, slides[2].TimestampMs);
    }

    [Fact]
    public void Selector_IntervalHit()
    {
        var slides = new List<ChartSlide>
        {
            new() { SectionName = "AAA", SlideIndex = 0, StartMs = 10000, EndMs = 20000 },
            new() { SectionName = "BBB", SlideIndex = 0, StartMs = 30000, EndMs = 40000 },
        };

        Assert.Equal(0, ChordChartSlideSelector.SelectSlideIndex(slides, 15000));
        Assert.Equal(1, ChordChartSlideSelector.SelectSlideIndex(slides, 35000));
    }

    [Fact]
    public void Selector_GapFallsBackToLastStarted_NotStale()
    {
        var slides = new List<ChartSlide>
        {
            new() { SectionName = "AAA", SlideIndex = 0, StartMs = 10000, EndMs = 20000 },
            new() { SectionName = "BBB", SlideIndex = 0, StartMs = 30000, EndMs = 40000 },
        };

        // Forward through the gap: same page the old hold showed.
        Assert.Equal(0, ChordChartSlideSelector.SelectSlideIndex(slides, 25000));
        // Backward seek into the gap while the window showed slide 1: the
        // old hold kept 1 (STALE section); the fallback returns 0.
        Assert.Equal(0, ChordChartSlideSelector.SelectSlideIndex(slides, 25000));
        // Past the last slide: last slide.
        Assert.Equal(1, ChordChartSlideSelector.SelectSlideIndex(slides, 50000));
        // Before the first slide: -1 (caller keeps the current page).
        Assert.Equal(-1, ChordChartSlideSelector.SelectSlideIndex(slides, 5000));
    }

    [Fact]
    public void Selector_OverlapKeepsFirstInterval()
    {
        var slides = new List<ChartSlide>
        {
            new() { SectionName = "AAA", SlideIndex = 0, StartMs = 10000, EndMs = 30000 },
            new() { SectionName = "BBB", SlideIndex = 0, StartMs = 20000, EndMs = 40000 },
        };

        Assert.Equal(0, ChordChartSlideSelector.SelectSlideIndex(slides, 25000));
    }

    [Fact]
    public void AudienceAndChordAgreeOnSection_ForwardAndSeek()
    {
        var engine = NewEngine();
        var song = SongWith(
            ("AAA", 10000, 40000, "A1\nA2"),
            ("BBB", 40000, 75000, "B1\nB2\nB3\nB4"),
            ("CCC", 80000, 100000, "C1\nC2"));
        AddChart(song, "AAA", 0, 10000, 40000);
        AddChart(song, "BBB", 0, 50000, 60000);
        AddChart(song, "BBB", 1, 60000, 75000);
        AddChart(song, "CCC", 0, 80000, 100000);

        var slides = engine.GenerateSlidesForSong(song);
        Assert.Equal(4, slides.Count);
        var chordSlides = song.ChordChart!.Slides;
        // Section-change points (first chord slide per section). Positions
        // within 600ms below one are skipped: the chord window's 500ms
        // pre-roll legitimately flips up to 500ms before the audience.
        double[] boundaries = { 10000, 50000, 80000 };
        bool NearBoundary(double pos) => boundaries.Any(b => pos >= b - 600 && pos < b);

        engine.LoadItemSlides(slides);
        int chordIdx = 0;
        void Check(double pos)
        {
            if (NearBoundary(pos)) return;
            engine.OnPlaybackPositionUpdate(pos);
            string audSection = slides[engine.CurrentSlideIndex].SectionName;
            int sel = ChordChartSlideSelector.SelectSlideIndex(chordSlides, pos + 500);
            if (sel >= 0) chordIdx = sel;
            string chordSection = chordSlides[chordIdx].SectionName;
            Assert.True(audSection == chordSection,
                $"pos={pos}: audience={audSection} chord={chordSection}");
        }

        // Forward play, whole song.
        for (double pos = 0; pos <= 100000; pos += 1000) Check(pos);
        // Backward seeks, including back into a gap (the screenshot case:
        // from a later section back to just before a section boundary).
        foreach (double pos in new double[] { 90000, 76000, 50000, 46000, 20000, 0 }) Check(pos);
    }
}
