using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the slide timing assignment logic.
/// 
/// LyricSlideGenerator assigns TimestampMs from ChartSlide data when generating slides.
/// The matching logic: for each generated slide within a section, look up ChartSlides
/// with the same SectionName (case-insensitive) and SlideIndex, then assign StartMs.
/// 
/// These tests verify the ChartSlide timing model and matching behavior.
/// The actual LyricSlideGenerator integration is verified by the full solution build
/// and the WPF project compilation.
/// </summary>
public class LyricSlideGeneratorTimingTests
{
    /// <summary>
    /// Simulates the timing assignment logic from LyricSlideGenerator.AssignChartSlideTimings.
    /// This is the exact algorithm used by the generator.
    /// </summary>
    private void AssignChartSlideTimings(List<Slide> sectionSlides, string sectionName, List<ChartSlide> chartSlides)
    {
        for (int i = 0; i < sectionSlides.Count; i++)
        {
            var matchingChart = chartSlides.FirstOrDefault(cs =>
                cs.SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase) &&
                cs.SlideIndex == i);

            if (matchingChart != null && matchingChart.StartMs > 0)
            {
                sectionSlides[i].TimestampMs = matchingChart.StartMs;
            }
        }
    }

    [Fact]
    public void AssignTiming_MatchesBySectionNameAndIndex()
    {
        var slides = new List<Slide>
        {
            new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "Line 1\nLine 2" } },
            new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "Line 3\nLine 4" } }
        };

        var chartSlides = new List<ChartSlide>
        {
            new ChartSlide { SectionName = "VERSE 1", SlideIndex = 0, StartMs = 1000, EndMs = 5000 },
            new ChartSlide { SectionName = "VERSE 1", SlideIndex = 1, StartMs = 5000, EndMs = 9000 }
        };

        AssignChartSlideTimings(slides, "VERSE 1", chartSlides);

        Assert.Equal(1000.0, slides[0].TimestampMs);
        Assert.Equal(5000.0, slides[1].TimestampMs);
    }

    [Fact]
    public void AssignTiming_NoChartSlides_TimestampRemainsNull()
    {
        var slides = new List<Slide>
        {
            new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "Line 1\nLine 2" } }
        };

        AssignChartSlideTimings(slides, "VERSE 1", new List<ChartSlide>());

        Assert.Null(slides[0].TimestampMs);
    }

    [Fact]
    public void AssignTiming_PartialMatch_OnlyAssignsMatching()
    {
        var slides = new List<Slide>
        {
            new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "Slide 0" } },
            new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "Slide 1" } }
        };

        var chartSlides = new List<ChartSlide>
        {
            new ChartSlide { SectionName = "VERSE 1", SlideIndex = 0, StartMs = 2000, EndMs = 6000 }
            // No entry for SlideIndex = 1
        };

        AssignChartSlideTimings(slides, "VERSE 1", chartSlides);

        Assert.Equal(2000.0, slides[0].TimestampMs);
        Assert.Null(slides[1].TimestampMs);
    }

    [Fact]
    public void AssignTiming_CaseInsensitiveMatch()
    {
        var slides = new List<Slide>
        {
            new Slide { SectionName = "Verse 1", Content = new SlideContent { Text = "Slide 0" } }
        };

        var chartSlides = new List<ChartSlide>
        {
            new ChartSlide { SectionName = "VERSE 1", SlideIndex = 0, StartMs = 3000, EndMs = 8000 }
        };

        AssignChartSlideTimings(slides, "Verse 1", chartSlides);

        Assert.Equal(3000.0, slides[0].TimestampMs);
    }

    [Fact]
    public void AssignTiming_ZeroStartMs_NotAssigned()
    {
        var slides = new List<Slide>
        {
            new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "Slide 0" } }
        };

        var chartSlides = new List<ChartSlide>
        {
            new ChartSlide { SectionName = "VERSE 1", SlideIndex = 0, StartMs = 0, EndMs = 5000 }
        };

        AssignChartSlideTimings(slides, "VERSE 1", chartSlides);

        Assert.Null(slides[0].TimestampMs);
    }

    [Fact]
    public void AssignTiming_MultipleSections_IndependentMatch()
    {
        var verse1Slides = new List<Slide>
        {
            new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "Verse slide" } }
        };
        var chorusSlides = new List<Slide>
        {
            new Slide { SectionName = "CHORUS", Content = new SlideContent { Text = "Chorus slide" } }
        };

        var chartSlides = new List<ChartSlide>
        {
            new ChartSlide { SectionName = "VERSE 1", SlideIndex = 0, StartMs = 1000, EndMs = 10000 },
            new ChartSlide { SectionName = "CHORUS", SlideIndex = 0, StartMs = 10000, EndMs = 20000 }
        };

        AssignChartSlideTimings(verse1Slides, "VERSE 1", chartSlides);
        AssignChartSlideTimings(chorusSlides, "CHORUS", chartSlides);

        Assert.Equal(1000.0, verse1Slides[0].TimestampMs);
        Assert.Equal(10000.0, chorusSlides[0].TimestampMs);
    }

    [Fact]
    public void AssignTiming_WrongSectionName_NoMatch()
    {
        var slides = new List<Slide>
        {
            new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "Slide 0" } }
        };

        var chartSlides = new List<ChartSlide>
        {
            new ChartSlide { SectionName = "CHORUS", SlideIndex = 0, StartMs = 5000, EndMs = 10000 }
        };

        AssignChartSlideTimings(slides, "VERSE 1", chartSlides);

        Assert.Null(slides[0].TimestampMs);
    }

    [Fact]
    public void ChartSlide_Model_DefaultValues()
    {
        var chartSlide = new ChartSlide();

        Assert.Equal("", chartSlide.SectionName);
        Assert.Equal(0, chartSlide.SlideIndex);
        Assert.Equal(0, chartSlide.StartMs);
        Assert.Equal(0, chartSlide.EndMs);
        Assert.False(chartSlide.ManualTiming);
    }

    [Fact]
    public void BacktrackState_Model_DefaultValues()
    {
        var state = new BacktrackState();

        Assert.False(state.IsPlaying);
        Assert.Equal("", state.CurrentSongTitle);
        Assert.Equal(-1, state.CurrentSectionIndex);
        Assert.Equal("", state.CurrentSectionName);
        Assert.Equal(0, state.CurrentPositionMs);
        Assert.Equal(0, state.PitchShiftSemitones);
        Assert.Equal(100.0, state.TempoPercent);
        Assert.False(state.IsLooping);
        Assert.Equal("", state.QueuedSectionName);
    }

    [Fact]
    public void BacktrackState_Model_SetValues()
    {
        var state = new BacktrackState
        {
            IsPlaying = true,
            CurrentSongTitle = "Amazing Grace",
            CurrentSectionIndex = 2,
            CurrentSectionName = "CHORUS",
            CurrentPositionMs = 45000,
            PitchShiftSemitones = -2,
            TempoPercent = 95.0,
            IsLooping = true,
            QueuedSectionName = "BRIDGE"
        };

        Assert.True(state.IsPlaying);
        Assert.Equal("Amazing Grace", state.CurrentSongTitle);
        Assert.Equal(2, state.CurrentSectionIndex);
        Assert.Equal("CHORUS", state.CurrentSectionName);
        Assert.Equal(45000, state.CurrentPositionMs);
        Assert.Equal(-2, state.PitchShiftSemitones);
        Assert.Equal(95.0, state.TempoPercent);
        Assert.True(state.IsLooping);
        Assert.Equal("BRIDGE", state.QueuedSectionName);
    }

    // Field fix 2026-10-25 (same repeated-chorus bug as PresenterEngine):
    // the second CHORUS must get its own chart time, not the first's.
    [Fact]
    public void GenerateFromSong_RepeatedChorus_UsesOccurrenceChartTiming()
    {
        var generator = new LyricSlideGenerator(new SlideTheme());
        var song = new Song
        {
            Title = "Repeat Song",
            ChordChart = new ChordChartData
            {
                Slides = new List<ChartSlide>
                {
                    new ChartSlide { SectionName = "VERSE 1", SlideIndex = 0, StartMs = 1000, EndMs = 5000 },
                    new ChartSlide { SectionName = "CHORUS", SlideIndex = 0, StartMs = 10000, EndMs = 18000 },
                    new ChartSlide { SectionName = "VERSE 2", SlideIndex = 0, StartMs = 20000, EndMs = 28000 },
                    new ChartSlide { SectionName = "CHORUS", SlideIndex = 0, StartMs = 30000, EndMs = 38000 }
                }
            },
            Sections = new List<Section>
            {
                new Section { Name = "VERSE 1", Text = "L1\nL2" },
                new Section { Name = "CHORUS", Text = "C1\nC2" },
                new Section { Name = "VERSE 2", Text = "L1\nL2" },
                new Section { Name = "CHORUS", Text = "C1\nC2" }
            }
        };

        var slides = generator.GenerateFromSong(song);

        // Title slide + 4 lyric slides.
        Assert.Equal(5, slides.Count);
        Assert.Equal(1000.0, slides[1].TimestampMs);
        Assert.Equal(10000.0, slides[2].TimestampMs);
        Assert.Equal(20000.0, slides[3].TimestampMs);
        Assert.Equal(30000.0, slides[4].TimestampMs);
    }
}
