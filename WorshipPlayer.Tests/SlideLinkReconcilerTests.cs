using System;
using System.Collections.Generic;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for <see cref="SlideLinkReconciler"/> — the pure, UI-free rules that (a) find the
/// indices of DANGLING Slides live links (presentation deleted out from under the item) so the host
/// can remove them, and (b) count how many service items link a given presentation (for the delete
/// confirm). Cross-platform (net8.0, no WPF). Mirrors the <see cref="ServiceItemDisplayNameTests"/>
/// style.
/// </summary>
public class SlideLinkReconcilerTests
{
    private static ServiceItem Slides(string? presentationId, string title = "Deck") => new()
    {
        Type = ServiceItemType.Slides,
        Title = title,
        SlidePresentationId = presentationId
    };

    private static ServiceOrder OrderWith(params ServiceItem[] items) =>
        new() { Items = new List<ServiceItem>(items) };

    // ─── FindOrphanedSlideItemIndices ──────────────────────────────

    [Fact]
    public void FindOrphaned_SlidesItemWithMissingPresentation_IsReturned()
    {
        var order = OrderWith(Slides("gone"));
        var existing = new HashSet<string>(); // "gone" is not present

        var result = SlideLinkReconciler.FindOrphanedSlideItemIndices(order, existing);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void FindOrphaned_SlidesItemWithResolvablePresentation_IsNotReturned()
    {
        var order = OrderWith(Slides("pres-1"));
        var existing = new HashSet<string> { "pres-1" };

        var result = SlideLinkReconciler.FindOrphanedSlideItemIndices(order, existing);

        Assert.Empty(result);
    }

    [Fact]
    public void FindOrphaned_SlidesItemWithEmptyLinkId_IsNotReturned()
    {
        var order = OrderWith(Slides(""), Slides(null));
        var existing = new HashSet<string>();

        var result = SlideLinkReconciler.FindOrphanedSlideItemIndices(order, existing);

        Assert.Empty(result);
    }

    [Fact]
    public void FindOrphaned_NonSlidesItemWithDanglingId_IsNotReturned()
    {
        var song = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Amazing Grace",
            SlidePresentationId = "gone"
        };
        var scripture = new ServiceItem
        {
            Type = ServiceItemType.Scripture,
            Title = "John 3:16",
            SlidePresentationId = "gone"
        };
        var order = OrderWith(song, scripture);
        var existing = new HashSet<string>();

        var result = SlideLinkReconciler.FindOrphanedSlideItemIndices(order, existing);

        Assert.Empty(result);
    }

    [Fact]
    public void FindOrphaned_MixedOrder_ReturnsOnlyOrphanIndicesAscending()
    {
        var order = OrderWith(
            Slides("pres-1"),                                   // 0 resolvable
            Slides("gone-a"),                                   // 1 orphan
            new ServiceItem { Type = ServiceItemType.Song, Title = "Song" }, // 2 non-Slides
            Slides(""),                                         // 3 unlinked
            Slides("gone-b"));                                  // 4 orphan
        var existing = new HashSet<string> { "pres-1", "pres-2" };

        var result = SlideLinkReconciler.FindOrphanedSlideItemIndices(order, existing);

        Assert.Equal(new[] { 1, 4 }, result);
    }

    [Fact]
    public void FindOrphaned_NullOrder_ReturnsEmpty()
    {
        var result = SlideLinkReconciler.FindOrphanedSlideItemIndices(null!, new HashSet<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void FindOrphaned_EmptyItems_ReturnsEmpty()
    {
        var order = new ServiceOrder { Items = new List<ServiceItem>() };

        var result = SlideLinkReconciler.FindOrphanedSlideItemIndices(order, new HashSet<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void FindOrphaned_NullExistingSet_Throws()
    {
        var order = OrderWith(Slides("gone"));

        Assert.Throws<ArgumentNullException>(
            () => SlideLinkReconciler.FindOrphanedSlideItemIndices(order, null!));
    }

    // ─── CountItemsLinkingPresentation ─────────────────────────────

    [Fact]
    public void Count_CountsOnlyMatchingSlidesItems()
    {
        var order = OrderWith(
            Slides("pres-1"),
            Slides("pres-1"),
            Slides("pres-2"),
            new ServiceItem { Type = ServiceItemType.Song, SlidePresentationId = "pres-1" });

        var result = SlideLinkReconciler.CountItemsLinkingPresentation(order, "pres-1");

        Assert.Equal(2, result);
    }

    [Fact]
    public void Count_NonSlidesMatch_IsNotCounted()
    {
        var order = OrderWith(
            new ServiceItem { Type = ServiceItemType.Song, SlidePresentationId = "pres-1" });

        var result = SlideLinkReconciler.CountItemsLinkingPresentation(order, "pres-1");

        Assert.Equal(0, result);
    }

    [Fact]
    public void Count_NullOrder_ReturnsZero()
    {
        var result = SlideLinkReconciler.CountItemsLinkingPresentation(null!, "pres-1");

        Assert.Equal(0, result);
    }

    [Fact]
    public void Count_EmptyId_ReturnsZero()
    {
        var order = OrderWith(Slides("pres-1"));

        Assert.Equal(0, SlideLinkReconciler.CountItemsLinkingPresentation(order, ""));
        Assert.Equal(0, SlideLinkReconciler.CountItemsLinkingPresentation(order, null!));
    }
}
