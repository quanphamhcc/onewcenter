using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Issue #5 (28/10, field log + screenshot): the audience slide and the chord
/// chart showed DIFFERENT sections at the same music time — chord chart on
/// BRIDGE 1 (17/29) while the audience sat on CHORUS. Root cause: the chord
/// chart shows a section from its chart boundary (LoopInMs, bar-derived),
/// but slide timestamps came from the first SUNG WORD of each block — up to
/// ~10s later (BRIDGE 1: chart 146448, first sung word 156678). In that gap
/// the chord chart had moved on while the audience still showed the previous
/// section, and the position matcher yanked any early section jump back
/// (field log 27/10: 10->9, 11->9 around pos 143k-149k).
///
/// Fix: GenerateSlidesForSong anchors each section's slides to its chart
/// start — shifting the section's timestamps uniformly so its first slide
/// lands on LoopInMs — so section changes coincide with the chord chart,
/// section events (fired at LoopInMs) agree with the matcher, and the
/// display no longer depends on mixer section events that stop firing after
/// PRE-CHORUS 2 in the field logs.
/// </summary>
public class PresenterEngineChartAnchorTests
{
    private static PresenterEngine NewEngine()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        return new PresenterEngine(manager, new PresenterSettings());
    }

    private static Song SongWith(params (string Name, double LoopIn, double LoopOut, string Text)[] sections)
    {
        var song = new Song { Title = "Anchor Song", OriginalKey = "C", ChordChart = new ChordChartData { Slides = new List<ChartSlide>() } };
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
    public void AnchorFirstSlideToLoopIn_ShiftIsUniform()
    {
        var engine = NewEngine();
        var song = SongWith(("CHORUS", 100000, 130000, "L1\nL2\nL3\nL4"));
        AddChart(song, "CHORUS", 0, 110000, 120000);

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(2, slides.Count);
        // Pre-anchor: [110000, 115000]. Shifted by -10000, gap preserved.
        Assert.Equal(100000, slides[0].TimestampMs);
        Assert.Equal(105000, slides[1].TimestampMs);
    }

    [Fact]
    public void BridgeGap_MatcherAndSectionEventAgreeAtChartBoundary()
    {
        var engine = NewEngine();
        var song = SongWith(
            ("CHORUS", 61718, 86238, "C1\nC2"),
            ("BRIDGE", 146448, 207609, "B1\nB2\nB3\nB4"));
        AddChart(song, "CHORUS", 0, 61823, 68934);
        AddChart(song, "BRIDGE", 0, 156678, 168203);

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(3, slides.Count);
        Assert.Equal(61718, slides[0].TimestampMs);
        // Pre-anchor BRIDGE: [156678, 162440.5]. Shifted by -10230.
        Assert.Equal(146448, slides[1].TimestampMs);
        Assert.Equal(152210.5, slides[2].TimestampMs!.Value, 3);

        engine.LoadItemSlides(slides);
        engine.OnPlaybackPositionUpdate(146448);
        Assert.Equal(1, engine.CurrentSlideIndex);

        // Simulate the old fight: a stale/wrong index, then the section
        // event at the chart boundary. The gate must honour it (gap 0) and
        // the matcher must NOT yank it back — before the fix the event was
        // blocked (gap 10230) and pos 147000 still showed CHORUS.
        engine.GoToSlide(0);
        engine.OnSectionChanged("BRIDGE");
        Assert.Equal(1, engine.CurrentSlideIndex);
        engine.OnPlaybackPositionUpdate(147000);
        Assert.Equal(1, engine.CurrentSlideIndex);
    }

    [Fact]
    public void LoopInUnknown_LeavesTimestamps()
    {
        var engine = NewEngine();
        var song = SongWith(("CHORUS", 0, 0, "L1\nL2\nL3\nL4"));
        AddChart(song, "CHORUS", 0, 110000, 120000);

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(110000, slides[0].TimestampMs);
        Assert.Equal(115000, slides[1].TimestampMs);
    }

    [Fact]
    public void EmbeddedTimestamp_SectionNotShifted()
    {
        var engine = NewEngine();
        // Embedded {M:SS.mmm} is explicit user intent: keep it, do not anchor.
        var song = SongWith(("CHORUS", 100000, 130000, "{1:50.000}L1\nL2\nL3\nL4"));
        AddChart(song, "CHORUS", 0, 120000, 130000);

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(110000, slides[0].TimestampMs);
        Assert.Equal(120000, slides[1].TimestampMs);
    }

    [Fact]
    public void MonotonicClamp_SecondSectionNotBelowPreviousTail()
    {
        var engine = NewEngine();
        var song = SongWith(
            ("AAA", 10000, 40000, "A1\nA2\nA3\nA4"),
            ("BBB", 15000, 40000, "B1\nB2"));
        AddChart(song, "AAA", 0, 10000, 30000);
        AddChart(song, "BBB", 0, 16000, 25000);

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(3, slides.Count);
        Assert.Equal(10000, slides[0].TimestampMs);
        Assert.Equal(20000, slides[1].TimestampMs);
        // BBB's chart start (15000) is below AAA's tail (20000): clamp to
        // prevTail + 1 instead of going backwards.
        Assert.Equal(20001, slides[2].TimestampMs);
    }

    [Fact]
    public void RepeatedSection_SecondOccurrenceClamped()
    {
        var engine = NewEngine();
        var song = SongWith(
            ("CHORUS", 50000, 90000, "C1\nC2"),
            ("CHORUS", 50000, 90000, "C3\nC4"));
        AddChart(song, "CHORUS", 0, 55000, 60000);
        AddChart(song, "CHORUS", 0, 65000, 70000);

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(2, slides.Count);
        Assert.Equal(50000, slides[0].TimestampMs);
        // Same LoopIn twice: the second occurrence lands just after the
        // first section's tail instead of reusing the same time.
        Assert.Equal(50001, slides[1].TimestampMs);
    }
}
