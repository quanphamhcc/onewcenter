using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// The all-sections center pane previews every service item's slides without
/// disturbing the live slide. It leans on the public, PURE generator
/// <see cref="PresenterEngine.GenerateSlidesForSong"/>, which must return a fresh
/// slide list that matches the live generation AND must not mutate engine state.
/// </summary>
public class GenerateSlidesForSongTests
{
    private static PresenterEngine NewEngine()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        return new PresenterEngine(manager, new PresenterSettings());
    }

    private static Song TwoSectionSong()
    {
        return new Song
        {
            Title = "Two Section Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                // Two blocks (separated by a blank line) in the verse.
                new Section { Name = "Verse 1", Text = "Line one\nLine two\n\nLine three\nLine four" },
                new Section { Name = "Chorus 1", Text = "Chorus line one\nChorus line two" }
            }
        };
    }

    [Fact]
    public void GenerateSlidesForSong_ReturnsSlidesForEachBlock_WithSectionNames()
    {
        var engine = NewEngine();
        var song = TwoSectionSong();

        var slides = engine.GenerateSlidesForSong(song);

        // Verse 1 has two blocks, Chorus 1 has one block => 3 slides.
        Assert.Equal(3, slides.Count);
        Assert.Equal("Verse 1", slides[0].SectionName);
        Assert.Equal("Verse 1", slides[1].SectionName);
        Assert.Equal("Chorus 1", slides[2].SectionName);
        Assert.All(slides, s => Assert.Equal(SlideType.Lyrics, s.Type));
        Assert.All(slides, s => Assert.False(string.IsNullOrWhiteSpace(s.Content.Text)));
    }

    [Fact]
    public void GenerateSlidesForSong_DoesNotMutateEngineState()
    {
        var engine = NewEngine();
        var song = TwoSectionSong();

        // Fresh engine with no current item has no live slides.
        Assert.Equal(0, engine.TotalSlides);

        var slides = engine.GenerateSlidesForSong(song);

        // Purity: the generator returned slides but left live state untouched.
        Assert.True(slides.Count > 0);
        Assert.Equal(0, engine.TotalSlides);
        Assert.Null(engine.CurrentSlide);
    }

    [Fact]
    public void GenerateSlidesForSong_EmptySong_ReturnsEmptyList_NoMutation()
    {
        var engine = NewEngine();
        var song = new Song { Title = "Empty", OriginalKey = "C" };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Empty(slides);
        Assert.Equal(0, engine.TotalSlides);
    }

    private static PresenterEngine NewEngineWithSettings(PresenterSettings settings)
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        return new PresenterEngine(manager, settings);
    }

    // Field feature 2026-10-24: a chorus typed as 4 consecutive lines (no blank
    // line) used to project as ONE slide in tiny text. With the default of 2
    // lines per slide it must become 2 slides of 2 lines, in order.
    [Fact]
    public void GenerateSlidesForSong_FourLineBlock_SplitsIntoTwoLineSlides()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Chorus Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section
                {
                    Name = "CHORUS",
                    Text = "Christ is enough for me\nEverything I need is in You\n" +
                           "Christ is enough for me\nEverything I need is in You"
                }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(2, slides.Count);
        Assert.Equal("Christ is enough for me\nEverything I need is in You", slides[0].Content.Text);
        Assert.Equal("Christ is enough for me\nEverything I need is in You", slides[1].Content.Text);
        Assert.All(slides, s => Assert.Equal("CHORUS", s.SectionName));
        Assert.All(slides, s => Assert.Equal("CHORUS", s.Content.Subtitle));
    }

    // Chunks never cross a section boundary: when the chorus ends, the next
    // slide is the next section's first lines — never a chorus+verse mix.
    [Fact]
    public void GenerateSlidesForSong_ChunksNeverCrossSectionBoundary()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Boundary Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "VERSE", Text = "V line 1\nV line 2\nV line 3" },
                new Section { Name = "CHORUS", Text = "C line 1\nC line 2\nC line 3\nC line 4" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(4, slides.Count);
        Assert.Equal("V line 1\nV line 2", slides[0].Content.Text);
        Assert.Equal("V line 3", slides[1].Content.Text);
        Assert.Equal("C line 1\nC line 2", slides[2].Content.Text);
        Assert.Equal("C line 3\nC line 4", slides[3].Content.Text);
        Assert.Equal("VERSE", slides[1].SectionName);
        Assert.Equal("CHORUS", slides[2].SectionName);
    }

    // A blank line still starts a new slide, and chunking applies inside each
    // block: 2 lines, then a blank-line break, then 3 lines -> 2 + 2 + 1.
    [Fact]
    public void GenerateSlidesForSong_BlankLineStillStartsNewSlide()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Blank Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "VERSE", Text = "A1\nA2\n\nB1\nB2\nB3" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(3, slides.Count);
        Assert.Equal("A1\nA2", slides[0].Content.Text);
        Assert.Equal("B1\nB2", slides[1].Content.Text);
        Assert.Equal("B3", slides[2].Content.Text);
    }

    // The per-section override wins over the global setting.
    [Fact]
    public void GenerateSlidesForSong_SectionOverride_WinsOverGlobalSetting()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Override Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "CHORUS", LinesPerSlideOverride = 3, Text = "L1\nL2\nL3\nL4\nL5" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(2, slides.Count);
        Assert.Equal("L1\nL2\nL3", slides[0].Content.Text);
        Assert.Equal("L4\nL5", slides[1].Content.Text);
    }

    // A user who explicitly sets the global setting to 4 keeps the old
    // one-slide-per-block behavior for a 4-line block.
    [Fact]
    public void GenerateSlidesForSong_GlobalSetting4_KeepsFourLinesOnOneSlide()
    {
        var engine = NewEngineWithSettings(new PresenterSettings { LinesPerSlide = 4 });
        var song = new Song
        {
            Title = "Dense Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "CHORUS", Text = "L1\nL2\nL3\nL4" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Single(slides);
        Assert.Equal("L1\nL2\nL3\nL4", slides[0].Content.Text);
    }

    // Auto-advance fix 2026-10-24 (user correction: "nhac phat thi tu nhay,
    // dau can cho bam"): OnPlaybackPositionUpdate only sees slides WITH a
    // timestamp, so a timed block's later chunks must carry interpolated
    // timestamps inside the block's window — otherwise the music would stall
    // on the block's first chunk waiting for a tap that never comes.
    [Fact]
    public void GenerateSlidesForSong_ChartTimedBlock_ChunksGetInterpolatedTimestamps()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Timed Song",
            OriginalKey = "C",
            ChordChart = new ChordChartData
            {
                Slides = new List<ChartSlide>
                {
                    new ChartSlide { SectionName = "CHORUS", SlideIndex = 0, StartMs = 10000, EndMs = 18000 }
                }
            },
            Sections = new List<Section>
            {
                new Section { Name = "CHORUS", Text = "L1\nL2\nL3\nL4" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(2, slides.Count);
        Assert.Equal(10000, slides[0].TimestampMs);
        Assert.Equal(14000, slides[1].TimestampMs);
    }

    // Chart SlideIndex counts BLOCKS, not chunks: a 4-line first block (2
    // chunks) followed by a 2-line second block must NOT hand the first
    // block's second chunk the second block's start time.
    [Fact]
    public void GenerateSlidesForSong_ChartIndexMatchesBlocksNotChunks()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Block Song",
            OriginalKey = "C",
            ChordChart = new ChordChartData
            {
                Slides = new List<ChartSlide>
                {
                    new ChartSlide { SectionName = "CHORUS", SlideIndex = 0, StartMs = 10000, EndMs = 18000 },
                    new ChartSlide { SectionName = "CHORUS", SlideIndex = 1, StartMs = 20000, EndMs = 24000 }
                }
            },
            Sections = new List<Section>
            {
                new Section { Name = "CHORUS", Text = "L1\nL2\nL3\nL4\n\nL5\nL6" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(3, slides.Count);
        Assert.Equal(10000, slides[0].TimestampMs);
        Assert.Equal(14000, slides[1].TimestampMs);
        Assert.Equal(20000, slides[2].TimestampMs);
    }

    // Embedded {M:SS.mmm} block timestamps interpolate to the next timed
    // block's start when there is no ChordChart end.
    [Fact]
    public void GenerateSlidesForSong_EmbeddedTimestampBlock_InterpolatesToNextBlockStart()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Embedded Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "CHORUS", Text = "{0:10.000}L1\nL2\nL3\nL4\n\n{0:20.000}L5\nL6" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(3, slides.Count);
        Assert.Equal(10000, slides[0].TimestampMs);
        Assert.Equal(15000, slides[1].TimestampMs);
        Assert.Equal(20000, slides[2].TimestampMs);
    }

    // Untimed blocks keep untimed chunks (no invented timing): section-change
    // / MIDI / manual advance still work for them, exactly as before chunking.
    [Fact]
    public void GenerateSlidesForSong_UntimedBlock_ChunksStayUntimed()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Untimed Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "CHORUS", Text = "L1\nL2\nL3\nL4" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(2, slides.Count);
        Assert.All(slides, s => Assert.Null(s.TimestampMs));
    }

    // End-to-end at engine level: with interpolated chunk timestamps, the
    // position-update path (the "music playing" path) moves through chunks
    // by itself — no tap needed.
    [Fact]
    public void GenerateSlidesForSong_PositionUpdate_AutoAdvancesThroughChunks()
    {
        var engine = NewEngine();
        var song = new Song
        {
            Title = "Auto Song",
            OriginalKey = "C",
            ChordChart = new ChordChartData
            {
                Slides = new List<ChartSlide>
                {
                    new ChartSlide { SectionName = "CHORUS", SlideIndex = 0, StartMs = 10000, EndMs = 18000 }
                }
            },
            Sections = new List<Section>
            {
                new Section { Name = "CHORUS", Text = "L1\nL2\nL3\nL4" }
            }
        };

        var slides = engine.GenerateSlidesForSong(song);
        engine.LoadItemSlides(slides);
        Assert.Equal(0, engine.CurrentSlideIndex);

        engine.OnPlaybackPositionUpdate(14500);

        Assert.Equal(1, engine.CurrentSlideIndex);
    }
}
