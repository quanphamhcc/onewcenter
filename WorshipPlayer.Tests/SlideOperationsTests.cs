using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for <see cref="SlideOperations"/> — the pure slide-list helper. Every assertion
/// confirms the list stays dense/0-based (Order == index) and that out-of-range indices are
/// no-ops rather than throwing.
/// </summary>
public class SlideOperationsTests
{
    private static List<PresentationSlide> BuildSlides(int count)
    {
        var slides = new List<PresentationSlide>();
        for (var i = 0; i < count; i++)
            slides.Add(new PresentationSlide { Order = i, Text = $"Slide {i}" });
        return slides;
    }

    private static void AssertDense(List<PresentationSlide> slides)
    {
        for (var i = 0; i < slides.Count; i++)
            Assert.Equal(i, slides[i].Order);
    }

    [Fact]
    public void AddSlide_Appends_WithOrderEqualPreviousCount()
    {
        var slides = BuildSlides(3);

        var added = SlideOperations.AddSlide(slides);

        Assert.Equal(4, slides.Count);
        Assert.Same(added, slides[3]);
        Assert.Equal(3, added.Order);
        Assert.Equal("", added.Text);
        AssertDense(slides);
    }

    [Fact]
    public void AddSlide_OnEmptyList_OrderIsZero()
    {
        var slides = new List<PresentationSlide>();

        var added = SlideOperations.AddSlide(slides);

        Assert.Equal(0, added.Order);
        Assert.Single(slides);
    }

    [Fact]
    public void DeleteSlide_Middle_RemovesItAndRedensifies()
    {
        var slides = BuildSlides(4);
        var middleText = slides[1].Text;

        SlideOperations.DeleteSlide(slides, 1);

        Assert.Equal(3, slides.Count);
        Assert.DoesNotContain(slides, s => s.Text == middleText);
        AssertDense(slides);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void DeleteSlide_OutOfRange_IsNoOp(int index)
    {
        var slides = BuildSlides(3);

        SlideOperations.DeleteSlide(slides, index);

        Assert.Equal(3, slides.Count);
        AssertDense(slides);
    }

    [Fact]
    public void MoveUp_SwapsOrderOfNeighbors()
    {
        var slides = BuildSlides(3);
        var moved = slides[2];
        var displaced = slides[1];

        SlideOperations.MoveUp(slides, 2);

        Assert.Same(moved, slides[1]);
        Assert.Same(displaced, slides[2]);
        Assert.Equal(1, moved.Order);
        Assert.Equal(2, displaced.Order);
        AssertDense(slides);
    }

    [Fact]
    public void MoveUp_AtIndexZero_IsNoOp()
    {
        var slides = BuildSlides(3);
        var first = slides[0];

        SlideOperations.MoveUp(slides, 0);

        Assert.Same(first, slides[0]);
        AssertDense(slides);
    }

    [Fact]
    public void MoveDown_SwapsOrderOfNeighbors()
    {
        var slides = BuildSlides(3);
        var moved = slides[0];
        var displaced = slides[1];

        SlideOperations.MoveDown(slides, 0);

        Assert.Same(moved, slides[1]);
        Assert.Same(displaced, slides[0]);
        Assert.Equal(1, moved.Order);
        Assert.Equal(0, displaced.Order);
        AssertDense(slides);
    }

    [Fact]
    public void MoveDown_AtLastIndex_IsNoOp()
    {
        var slides = BuildSlides(3);
        var last = slides[2];

        SlideOperations.MoveDown(slides, 2);

        Assert.Same(last, slides[2]);
        AssertDense(slides);
    }

    [Fact]
    public void Normalize_ReSortsShuffledOrder_AndRedensifies()
    {
        var a = new PresentationSlide { Order = 20, Text = "A" };
        var b = new PresentationSlide { Order = 5, Text = "B" };
        var c = new PresentationSlide { Order = 12, Text = "C" };
        var slides = new List<PresentationSlide> { a, b, c };

        SlideOperations.Normalize(slides);

        Assert.Same(b, slides[0]);
        Assert.Same(c, slides[1]);
        Assert.Same(a, slides[2]);
        AssertDense(slides);
    }

    [Fact]
    public void Operations_DoNotTouchBackgroundFields()
    {
        var slides = BuildSlides(3);

        SlideOperations.AddSlide(slides);
        SlideOperations.MoveUp(slides, 1);
        SlideOperations.MoveDown(slides, 0);
        SlideOperations.DeleteSlide(slides, 0);
        SlideOperations.Normalize(slides);

        Assert.All(slides, s =>
        {
            Assert.Null(s.BackgroundImage);
            Assert.Null(s.BackgroundColor);
        });
    }
}
