using System.Linq;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the pure SlideGroupingService: N-lines-per-slide chunking, blank-line hard breaks,
/// oversize-block sub-split, trailing remainder, empty-line handling, and clamping of linesPerSlide.
/// </summary>
public class SlideGroupingServiceTests
{
    [Fact]
    public void NoBlankLines_ChunksByLinesPerSlide_4()
    {
        string text = "l1\nl2\nl3\nl4\nl5\nl6";
        var slides = SlideGroupingService.GroupSection(text, 4);

        Assert.Equal(2, slides.Count);
        Assert.Equal(new[] { "l1", "l2", "l3", "l4" }, slides[0].Lines);
        Assert.Equal(new[] { "l5", "l6" }, slides[1].Lines); // remainder
        Assert.Equal(0, slides[0].SlideIndex);
        Assert.Equal(1, slides[1].SlideIndex);
    }

    [Theory]
    [InlineData(2, 3)] // 6 lines / 2 = 3 slides
    [InlineData(3, 2)] // 6 lines / 3 = 2 slides
    [InlineData(4, 2)] // 6 lines / 4 = 2 slides (4 + remainder 2)
    public void ChunkCount_MatchesLinesPerSlide(int perSlide, int expectedSlides)
    {
        string text = "l1\nl2\nl3\nl4\nl5\nl6";
        var slides = SlideGroupingService.GroupSection(text, perSlide);
        Assert.Equal(expectedSlides, slides.Count);
    }

    [Fact]
    public void TrailingRemainder_FormsFinalShortSlide_2PerSlide()
    {
        string text = "a\nb\nc\nd\ne"; // 5 lines at 2/slide => 2,2,1
        var slides = SlideGroupingService.GroupSection(text, 2);

        Assert.Equal(3, slides.Count);
        Assert.Equal(2, slides[0].Lines.Count);
        Assert.Equal(2, slides[1].Lines.Count);
        Assert.Single(slides[2].Lines);
        Assert.Equal("e", slides[2].Lines[0]);
    }

    [Fact]
    public void BlankLineBreaks_AreHardSlideBoundaries()
    {
        // Two blank-line blocks, each 2 lines; at 4/slide they must NOT be merged into one slide.
        string text = "a\nb\n\nc\nd";
        var slides = SlideGroupingService.GroupSection(text, 4);

        Assert.Equal(2, slides.Count);
        Assert.Equal(new[] { "a", "b" }, slides[0].Lines);
        Assert.Equal(new[] { "c", "d" }, slides[1].Lines);
    }

    [Fact]
    public void OversizeBlock_IsSubSplitWithinHardBoundary()
    {
        // One blank-line block of 5 lines at 2/slide => 2,2,1 (3 slides from one block).
        string text = "a\nb\nc\nd\ne";
        var slides = SlideGroupingService.GroupSection(text, 2);
        Assert.Equal(3, slides.Count);
    }

    [Fact]
    public void BlankLineBlocks_ThenSubSplit_IndexesAreSequential()
    {
        // Block 1: 3 lines, block 2: 3 lines; at 2/slide => (2,1)+(2,1) = 4 slides, idx 0..3
        string text = "a\nb\nc\n\nd\ne\nf";
        var slides = SlideGroupingService.GroupSection(text, 2);

        Assert.Equal(4, slides.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, slides.Select(s => s.SlideIndex).ToArray());
        Assert.Equal(new[] { "a", "b" }, slides[0].Lines);
        Assert.Equal(new[] { "c" }, slides[1].Lines);
        Assert.Equal(new[] { "d", "e" }, slides[2].Lines);
        Assert.Equal(new[] { "f" }, slides[3].Lines);
    }

    [Fact]
    public void WhitespaceLines_AreIgnored()
    {
        string text = "a\n   \nb\n\t\nc"; // whitespace-only lines are breaks, not content
        var slides = SlideGroupingService.GroupSection(text, 4);
        // "   " and "\t" are blank => three hard blocks a | b | c
        Assert.Equal(3, slides.Count);
        Assert.Equal("a", slides[0].Lines.Single());
        Assert.Equal("b", slides[1].Lines.Single());
        Assert.Equal("c", slides[2].Lines.Single());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void LinesPerSlide_IsClampedTo2Through4(int requested)
    {
        // 8 lines; clamp(0|1)->2 => 4 slides, clamp(7)->4 => 2 slides.
        string text = string.Join("\n", Enumerable.Range(1, 8).Select(i => "l" + i));
        var slides = SlideGroupingService.GroupSection(text, requested);
        int expectedPerSlide = System.Math.Clamp(requested, 2, 4);
        Assert.Equal((8 + expectedPerSlide - 1) / expectedPerSlide, slides.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void EmptyOrWhitespaceText_ReturnsNoSlides(string? text)
    {
        var slides = SlideGroupingService.GroupSection(text, 4);
        Assert.Empty(slides);
    }

    [Fact]
    public void CrlfLineEndings_AreHandled()
    {
        string text = "a\r\nb\r\n\r\nc\r\nd";
        var slides = SlideGroupingService.GroupSection(text, 4);
        Assert.Equal(2, slides.Count); // blank line between => two blocks
        Assert.Equal(new[] { "a", "b" }, slides[0].Lines);
        Assert.Equal(new[] { "c", "d" }, slides[1].Lines);
    }
}
