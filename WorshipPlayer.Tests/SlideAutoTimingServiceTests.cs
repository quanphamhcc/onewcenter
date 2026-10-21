using System.Collections.Generic;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the pure SlideAutoTimingService: first-word match → StartMs − lead, clamp to
/// section start, monotonic ordering, no-match → null, lead never producing negative/pre-section
/// times, and ordered word matching.
/// </summary>
public class SlideAutoTimingServiceTests
{
    private static SlideGroupingService.SlideBlock Block(int idx, params string[] lines)
        => new SlideGroupingService.SlideBlock(idx, lines, string.Join("\n", lines));

    private static TimedWord W(string word, double startMs)
        => new TimedWord { Word = word, StartMs = startMs, EndMs = startMs + 100 };

    [Fact]
    public void FirstWord_MatchesWordStart_MinusLead()
    {
        var blocks = new[] { Block(0, "Amazing grace how sweet") };
        var words = new List<TimedWord> { W("amazing", 10000), W("grace", 10500) };

        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, words, sectionStartMs: 8000, sectionEndMs: 20000, leadMs: 500);

        Assert.Single(times);
        Assert.Equal(9500, times[0]!.Value, 1); // 10000 - 500 lead
    }

    [Fact]
    public void Lead_ClampedToSectionStart()
    {
        // Word at 8100, lead 500 => 7600, but section starts at 8000 => clamp to 8000.
        var blocks = new[] { Block(0, "Christ is my reward") };
        var words = new List<TimedWord> { W("christ", 8100) };

        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, words, sectionStartMs: 8000, sectionEndMs: 20000, leadMs: 500);

        Assert.Equal(8000, times[0]!.Value, 1);
    }

    [Fact]
    public void Times_AreMonotonicNonDecreasing_AcrossSlides()
    {
        var blocks = new[]
        {
            Block(0, "first line here"),
            Block(1, "second line now"),
            Block(2, "third and final"),
        };
        var words = new List<TimedWord>
        {
            W("first", 10000),
            W("second", 12000),
            W("third", 14000),
        };

        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, words, sectionStartMs: 9000, sectionEndMs: 20000, leadMs: 500);

        Assert.Equal(9500, times[0]!.Value, 1);
        Assert.Equal(11500, times[1]!.Value, 1);
        Assert.Equal(13500, times[2]!.Value, 1);
        Assert.True(times[0] <= times[1] && times[1] <= times[2]);
    }

    [Fact]
    public void NoMatch_YieldsNull_DoesNotCrash()
    {
        var blocks = new[] { Block(0, "unmatchable xyzzy words") };
        var words = new List<TimedWord> { W("completely", 10000), W("different", 11000) };

        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, words, sectionStartMs: 8000, sectionEndMs: 20000, leadMs: 500);

        Assert.Single(times);
        Assert.Null(times[0]);
    }

    [Fact]
    public void WordsOutsideSection_AreIgnored()
    {
        // The only "grace" is BEFORE the section window — must not match.
        var blocks = new[] { Block(0, "grace flows down") };
        var words = new List<TimedWord> { W("grace", 2000) };

        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, words, sectionStartMs: 8000, sectionEndMs: 20000, leadMs: 500);

        Assert.Null(times[0]);
    }

    [Fact]
    public void OrderedMatching_SecondSlideSearchesAfterFirstMatch()
    {
        // "holy" appears twice; slide 1 takes the first, slide 2 the second.
        var blocks = new[]
        {
            Block(0, "holy is the Lord"),
            Block(1, "holy forever and ever"),
        };
        var words = new List<TimedWord>
        {
            W("holy", 10000),
            W("holy", 15000),
        };

        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, words, sectionStartMs: 9000, sectionEndMs: 20000, leadMs: 0);

        Assert.Equal(10000, times[0]!.Value, 1);
        Assert.Equal(15000, times[1]!.Value, 1);
    }

    [Fact]
    public void ChordAnnotations_AreStrippedBeforeMatching()
    {
        var blocks = new[] { Block(0, "[G]Christ is [C]my reward") };
        var words = new List<TimedWord> { W("christ", 10000) };

        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, words, sectionStartMs: 8000, sectionEndMs: 20000, leadMs: 0);

        Assert.Equal(10000, times[0]!.Value, 1);
    }

    [Fact]
    public void ZeroLead_UsesExactWordStart()
    {
        var blocks = new[] { Block(0, "now there's nothing") };
        var words = new List<TimedWord> { W("now", 12345) };

        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, words, sectionStartMs: 8000, sectionEndMs: 20000, leadMs: 0);

        Assert.Equal(12345, times[0]!.Value, 1);
    }

    [Fact]
    public void EmptyBlocks_ReturnEmpty_NoThrow()
    {
        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            new SlideGroupingService.SlideBlock[0], new List<TimedWord>(), 0, 1000, 500);
        Assert.Empty(times);
    }

    [Fact]
    public void NullWords_ReturnsNullsForEachBlock_NoThrow()
    {
        var blocks = new[] { Block(0, "a b c"), Block(1, "d e f") };
        var times = SlideAutoTimingService.ComputeSlideStartTimes(
            blocks, null!, 0, 10000, 500);

        Assert.Equal(2, times.Count);
        Assert.Null(times[0]);
        Assert.Null(times[1]);
    }
}
