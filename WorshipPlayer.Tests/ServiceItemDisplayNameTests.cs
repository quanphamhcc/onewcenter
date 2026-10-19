using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for <see cref="ServiceItemDisplayName.ResolveDisplayName"/> — the pure, UI-free rule
/// that gives a LIVE-LINKED Slides item its DISPLAY name from the linked presentation's CURRENT
/// <see cref="SlidePresentation.Name"/> (so a rename shows live), while leaving every other item on
/// its stored <see cref="ServiceItem.Title"/>. Cross-platform (net8.0, no WPF). Mirrors the
/// <see cref="SlidePresentationLinkTests"/> style.
/// </summary>
public class ServiceItemDisplayNameTests
{
    [Fact]
    public void SlidesLink_WithLinkedPresentation_ReturnsPresentationNameTrimmed()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Slides,
            Title = "Old Name",
            SlidePresentationId = "pres-1"
        };
        var linked = new SlidePresentation { Id = "pres-1", Name = "  New Name  " };

        var result = ServiceItemDisplayName.ResolveDisplayName(item, linked);

        Assert.Equal("New Name", result);
    }

    [Fact]
    public void SlidesLink_MissingPresentation_FallsBackToStoredTitle()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Slides,
            Title = "Old Name",
            SlidePresentationId = "pres-1"
        };

        var result = ServiceItemDisplayName.ResolveDisplayName(item, null);

        Assert.Equal("Old Name", result);
    }

    [Fact]
    public void SlidesLink_BlankPresentationName_FallsBackToStoredTitle()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Slides,
            Title = "Old Name",
            SlidePresentationId = "pres-1"
        };
        var linked = new SlidePresentation { Id = "pres-1", Name = "   " };

        var result = ServiceItemDisplayName.ResolveDisplayName(item, linked);

        Assert.Equal("Old Name", result);
    }

    [Fact]
    public void SlidesItem_NoLinkId_ReturnsStoredTitle()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Slides,
            Title = "Snapshot Deck",
            SlidePresentationId = ""
        };
        var linked = new SlidePresentation { Id = "pres-1", Name = "Something Else" };

        var result = ServiceItemDisplayName.ResolveDisplayName(item, linked);

        Assert.Equal("Snapshot Deck", result);
    }

    [Fact]
    public void NonSlidesItem_ReturnsStoredTitle()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Amazing Grace",
            SlidePresentationId = "pres-1"
        };
        var linked = new SlidePresentation { Id = "pres-1", Name = "Deck Name" };

        var result = ServiceItemDisplayName.ResolveDisplayName(item, linked);

        Assert.Equal("Amazing Grace", result);
    }

    [Fact]
    public void NullItem_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceItemDisplayName.ResolveDisplayName(null!, null));
    }
}
