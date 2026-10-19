using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for <see cref="SlidePresentationMapper"/> — the pure SlidePresentation ->
/// ServiceItem (type Slides) projection behind the Slides "Add to current service" action.
/// Cross-platform (net8.0, no WPF). Mirrors <see cref="ServiceItemMapperTests"/>.
/// </summary>
public class SlidePresentationMapperTests
{
    [Fact]
    public void ToServiceItem_MapsSlidesInOrder_WithTextPreserved()
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

        var item = SlidePresentationMapper.ToServiceItem(presentation);

        Assert.Equal(3, item.Slides.Count);
        Assert.Equal("First", item.Slides[0].Content.Text);
        Assert.Equal("Middle", item.Slides[1].Content.Text);
        Assert.Equal("Second", item.Slides[2].Content.Text);
        Assert.Equal(new[] { 0, 1, 2 }, item.Slides.Select(s => s.Order).ToArray());
    }

    [Fact]
    public void ToServiceItem_SetsSlidesTypeAndTitleFromName()
    {
        var presentation = new SlidePresentation { Name = "Welcome Loop" };

        var item = SlidePresentationMapper.ToServiceItem(presentation);

        Assert.Equal(ServiceItemType.Slides, item.Type);
        Assert.Equal("Welcome Loop", item.Title);
    }

    [Fact]
    public void ToServiceItem_BlankName_FallsBackToSlidesItem()
    {
        var presentation = new SlidePresentation { Name = "   " };

        var item = SlidePresentationMapper.ToServiceItem(presentation);

        Assert.Equal("Slides Item", item.Title);
    }

    [Fact]
    public void ToServiceItem_BackgroundImage_MapsToContentBackgroundImage()
    {
        var presentation = new SlidePresentation
        {
            Name = "Deck",
            Slides = new List<PresentationSlide>
            {
                new() { Order = 0, Text = "A", BackgroundImage = "C:/bg/image.png" }
            }
        };

        var item = SlidePresentationMapper.ToServiceItem(presentation);

        Assert.Equal("C:/bg/image.png", item.Slides[0].Content.BackgroundImage);
        Assert.Null(item.Slides[0].ThemeOverride);
    }

    [Fact]
    public void ToServiceItem_BackgroundColor_MapsToSolidColorThemeOverride()
    {
        var presentation = new SlidePresentation
        {
            Name = "Deck",
            Slides = new List<PresentationSlide>
            {
                new() { Order = 0, Text = "A", BackgroundColor = "#112233" }
            }
        };

        var item = SlidePresentationMapper.ToServiceItem(presentation);

        var slide = item.Slides[0];
        Assert.NotNull(slide.ThemeOverride);
        Assert.Equal(BackgroundType.SolidColor, slide.ThemeOverride!.BackgroundType);
        Assert.Equal("#112233", slide.ThemeOverride.BackgroundColor);
        Assert.Equal("", slide.Content.BackgroundImage);
    }

    [Fact]
    public void ToServiceItem_NoBackground_LeavesBothUnset()
    {
        var presentation = new SlidePresentation
        {
            Name = "Deck",
            Slides = new List<PresentationSlide> { new() { Order = 0, Text = "A" } }
        };

        var item = SlidePresentationMapper.ToServiceItem(presentation);

        Assert.Equal("", item.Slides[0].Content.BackgroundImage);
        Assert.Null(item.Slides[0].ThemeOverride);
    }

    [Fact]
    public void ToServiceItem_NullOrEmptySlides_YieldsNonNullEmptyList()
    {
        var presentation = new SlidePresentation { Name = "Empty", Slides = new List<PresentationSlide>() };

        var item = SlidePresentationMapper.ToServiceItem(presentation);

        Assert.NotNull(item.Slides);
        Assert.Empty(item.Slides);
    }

    [Fact]
    public void ToServiceItem_NullPresentation_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SlidePresentationMapper.ToServiceItem(null!));
    }

    [Fact]
    public void ToServiceItem_SlideType_IsCustom()
    {
        var presentation = new SlidePresentation
        {
            Name = "Deck",
            Slides = new List<PresentationSlide> { new() { Order = 0, Text = "A" } }
        };

        var item = SlidePresentationMapper.ToServiceItem(presentation);

        Assert.Equal(SlideType.Custom, item.Slides[0].Type);
    }

    /// <summary>
    /// Mirrors the Songs-tab "+ Add" round-trip (ServiceItemMapperTests): the item produced by the
    /// mapper and appended via <see cref="ServiceOrderManager.AddItem"/> must carry the expected
    /// Title/Type and slide count. No SaveAsync, so no data files are touched.
    /// </summary>
    [Fact]
    public void AddPresentationToService_ProducesExpectedTitleTypeAndSlideCount()
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

        var item = SlidePresentationMapper.ToServiceItem(presentation);
        manager.AddItem(item);

        var added = Assert.Single(manager.CurrentServiceOrder!.Items);
        Assert.Equal(ServiceItemType.Slides, added.Type);
        Assert.Equal("Announcements", added.Title);
        Assert.Equal(2, added.Slides.Count);
    }
}
