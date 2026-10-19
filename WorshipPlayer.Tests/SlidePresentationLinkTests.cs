using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the Slides "Add to current service" LIVE LINK path on
/// <see cref="SlidePresentationMapper"/> — the pure, UI-free helpers that (a) convert a
/// <see cref="SlidePresentation"/>'s CURRENT slides on demand and (b) produce a link-form
/// <see cref="ServiceItem"/> that stores only <see cref="ServiceItem.SlidePresentationId"/> and no
/// snapshot. Cross-platform (net8.0, no WPF). Mirrors <see cref="SlidePresentationMapperTests"/>.
/// </summary>
public class SlidePresentationLinkTests
{
    [Fact]
    public void ToSlidesFromPresentation_MapsSlidesInOrder_WithTextPreserved()
    {
        var presentation = new SlidePresentation
        {
            Name = "My Deck",
            Slides = new List<PresentationSlide>
            {
                new() { Order = 2, Text = "Second" },
                new() { Order = 0, Text = "First" },
                new() { Order = 1, Text = "Middle" }
            }
        };

        var slides = SlidePresentationMapper.ToSlides(presentation);

        Assert.Equal(3, slides.Count);
        Assert.Equal("First", slides[0].Content.Text);
        Assert.Equal("Middle", slides[1].Content.Text);
        Assert.Equal("Second", slides[2].Content.Text);
        Assert.Equal(new[] { 0, 1, 2 }, slides.Select(s => s.Order).ToArray());
    }

    [Fact]
    public void ToSlidesFromPresentation_NullPresentation_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SlidePresentationMapper.ToSlides((SlidePresentation)null!));
    }

    [Fact]
    public void ToSlidesFromPresentation_NullOrEmptySlides_YieldsNonNullEmptyList()
    {
        var presentation = new SlidePresentation { Name = "Empty", Slides = new List<PresentationSlide>() };

        var slides = SlidePresentationMapper.ToSlides(presentation);

        Assert.NotNull(slides);
        Assert.Empty(slides);
    }

    [Fact]
    public void ToServiceItemLink_SetsTypeTitleAndLinkId_WithEmptySlides()
    {
        var presentation = new SlidePresentation
        {
            Name = "Welcome Loop",
            Slides = new List<PresentationSlide>
            {
                new() { Order = 0, Text = "A" },
                new() { Order = 1, Text = "B" }
            }
        };

        var item = SlidePresentationMapper.ToServiceItemLink(presentation);

        Assert.Equal(ServiceItemType.Slides, item.Type);
        Assert.Equal("Welcome Loop", item.Title);
        Assert.Equal(presentation.Id, item.SlidePresentationId);
        Assert.NotNull(item.Slides);
        Assert.Empty(item.Slides);
    }

    [Fact]
    public void ToServiceItemLink_BlankName_FallsBackToSlidesItem()
    {
        var presentation = new SlidePresentation { Name = "   " };

        var item = SlidePresentationMapper.ToServiceItemLink(presentation);

        Assert.Equal("Slides Item", item.Title);
    }

    [Fact]
    public void ToServiceItemLink_NullPresentation_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SlidePresentationMapper.ToServiceItemLink(null!));
    }

    /// <summary>
    /// Add-flow: the link item appended via <see cref="ServiceOrderManager.AddItem"/> must carry
    /// the SlidePresentationId and leave Slides empty (no snapshot). No SaveAsync, so no data files
    /// are touched.
    /// </summary>
    [Fact]
    public void AddLinkedPresentationToService_ProducesLinkWithNoSnapshot()
    {
        var manager = new ServiceOrderManager(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Sunday", "2024-01-07");

        var presentation = new SlidePresentation
        {
            Name = "Announcements",
            Slides = new List<PresentationSlide>
            {
                new() { Order = 0, Text = "Slide 1" },
                new() { Order = 1, Text = "Slide 2" }
            }
        };

        var item = SlidePresentationMapper.ToServiceItemLink(presentation);
        manager.AddItem(item);

        var added = Assert.Single(manager.CurrentServiceOrder!.Items);
        Assert.Equal(ServiceItemType.Slides, added.Type);
        Assert.Equal("Announcements", added.Title);
        Assert.Equal(presentation.Id, added.SlidePresentationId);
        Assert.Empty(added.Slides);
    }
}
