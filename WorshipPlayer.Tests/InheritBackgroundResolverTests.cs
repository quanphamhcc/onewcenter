using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for Inherit background chain resolution (đợt 2 T2 / R2).
/// Covers the spec acceptance criteria: single-item chain; a chain of 3 items
/// inheriting one background; changing the source item's background is picked up
/// on re-resolve; Inherit on the first item → theme; removing the source item
/// re-resolves the chain correctly; empty list → no error; a non-Inherit item
/// breaks the chain; resolution is computed at call time (never persisted).
/// </summary>
public class InheritBackgroundResolverTests
{
    private static SlideTheme Theme() => new()
    {
        BackgroundType = BackgroundType.SolidColor,
        BackgroundColor = "#112233"
    };

    private static ServiceItem ColorItem(string id, string color) => new()
    {
        Id = id,
        Title = id,
        BackgroundKind = ItemBackgroundKind.Color,
        BackgroundColor = color
    };

    private static ServiceItem InheritItem(string id) => new()
    {
        Id = id,
        Title = id,
        BackgroundKind = ItemBackgroundKind.Inherit
    };

    // ─── Non-Inherit items resolve as before (delegate to T1 resolver) ───

    [Fact]
    public void SingleItem_WithOwnBackground_ResolvesOwnBackground()
    {
        var items = new List<ServiceItem> { ColorItem("a", "#FF0000") };

        var resolved = InheritBackgroundResolver.ResolveItem(items, 0, Theme());

        Assert.Equal(ItemBackgroundKind.Color, resolved.Kind);
        Assert.Equal("#FF0000", resolved.Color);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }

    [Fact]
    public void SingleItem_WithNothingSet_ResolvesToTheme()
    {
        var items = new List<ServiceItem> { new ServiceItem { Id = "a", Title = "a" } };

        var resolved = InheritBackgroundResolver.ResolveItem(items, 0, Theme());

        Assert.Equal(ItemBackgroundKind.Theme, resolved.Kind);
        Assert.Equal(ItemBackgroundSource.Theme, resolved.Source);
    }

    // ─── Inherit chains ──────────────────────────────────────────────────

    [Fact]
    public void ChainOfThree_InheritSingleSourceBackground()
    {
        var items = new List<ServiceItem>
        {
            ColorItem("src", "#00AA00"),
            InheritItem("c1"),
            InheritItem("c2"),
            InheritItem("c3")
        };

        var resolved = InheritBackgroundResolver.ResolveService(items, Theme());

        Assert.Equal(4, resolved.Count);
        Assert.Equal("#00AA00", resolved[0].Color);
        for (var i = 1; i <= 3; i++)
        {
            Assert.Equal(ItemBackgroundKind.Color, resolved[i].Kind);
            Assert.Equal("#00AA00", resolved[i].Color);
            Assert.Equal(ItemBackgroundSource.Item, resolved[i].Source);
        }
    }

    [Fact]
    public void SourceBackgroundChanged_ChainFollowsOnReResolve()
    {
        var items = new List<ServiceItem>
        {
            ColorItem("src", "#00AA00"),
            InheritItem("c1"),
            InheritItem("c2")
        };

        // Change the source item's background AFTER the first resolve.
        var before = InheritBackgroundResolver.ResolveService(items, Theme());
        Assert.Equal("#00AA00", before[2].Color);

        items[0].BackgroundColor = "#0000FF";

        var after = InheritBackgroundResolver.ResolveService(items, Theme());
        Assert.Equal("#0000FF", after[1].Color);
        Assert.Equal("#0000FF", after[2].Color);

        // The change was NOT written into the inheriting items.
        Assert.Equal(ItemBackgroundKind.Inherit, items[1].BackgroundKind);
        Assert.Null(items[1].BackgroundColor);
        Assert.Equal(ItemBackgroundKind.Inherit, items[2].BackgroundKind);
    }

    [Fact]
    public void FirstItemInherit_ResolvesToTheme()
    {
        var items = new List<ServiceItem>
        {
            InheritItem("first"),
            InheritItem("second")
        };

        var resolved = InheritBackgroundResolver.ResolveService(items, Theme());

        Assert.Equal(ItemBackgroundKind.Theme, resolved[0].Kind);
        Assert.Equal(ItemBackgroundSource.Theme, resolved[0].Source);
        Assert.Equal(ItemBackgroundKind.Theme, resolved[1].Kind);
        Assert.Equal(ItemBackgroundSource.Theme, resolved[1].Source);
    }

    [Fact]
    public void SourceItemRemoved_ChainReResolvesCorrectly()
    {
        var items = new List<ServiceItem>
        {
            ColorItem("src", "#00AA00"),
            InheritItem("c1"),
            InheritItem("c2")
        };

        // Remove the source: the chain now has no real background above → theme.
        items.RemoveAt(0);

        var resolved = InheritBackgroundResolver.ResolveService(items, Theme());

        Assert.Equal(ItemBackgroundKind.Theme, resolved[0].Kind);
        Assert.Equal(ItemBackgroundSource.Theme, resolved[0].Source);
        Assert.Equal(ItemBackgroundKind.Theme, resolved[1].Kind);
    }

    [Fact]
    public void EmptyList_ResolvesToEmptyWithoutError()
    {
        var resolved = InheritBackgroundResolver.ResolveService(new List<ServiceItem>(), Theme());

        Assert.NotNull(resolved);
        Assert.Empty(resolved);
    }

    [Fact]
    public void NonInheritItem_BreaksChain_WithOwnBackground()
    {
        var items = new List<ServiceItem>
        {
            ColorItem("src", "#00AA00"),
            InheritItem("c1"),
            ColorItem("other", "#FF0000"),
            InheritItem("c2")
        };

        var resolved = InheritBackgroundResolver.ResolveService(items, Theme());

        Assert.Equal("#00AA00", resolved[1].Color); // c1 inherits src
        Assert.Equal("#FF0000", resolved[2].Color); // other has its own
        Assert.Equal("#FF0000", resolved[3].Color); // c2 inherits other, not src
    }

    [Fact]
    public void InheritFromNoneSource_ResolvesToNone()
    {
        var items = new List<ServiceItem>
        {
            new ServiceItem { Id = "src", BackgroundKind = ItemBackgroundKind.None },
            InheritItem("c1")
        };

        var resolved = InheritBackgroundResolver.ResolveService(items, Theme());

        Assert.Equal(ItemBackgroundKind.None, resolved[1].Kind);
        Assert.Equal(ItemBackgroundSource.Item, resolved[1].Source);
    }

    [Fact]
    public void InheritSkipsInheritItems_ToNearestRealSource()
    {
        var items = new List<ServiceItem>
        {
            ColorItem("far", "#111111"),
            ColorItem("near", "#222222"),
            InheritItem("c1")
        };

        var resolved = InheritBackgroundResolver.ResolveService(items, Theme());

        Assert.Equal("#222222", resolved[2].Color); // nearest real background above
    }

    [Fact]
    public void ResolveItem_IndexOutOfRange_Throws()
    {
        var items = new List<ServiceItem> { new ServiceItem { Id = "a" } };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => InheritBackgroundResolver.ResolveItem(items, 5, Theme()));
    }

    [Fact]
    public void IsInherit_TrueOnlyForExplicitInheritKind()
    {
        Assert.True(InheritBackgroundResolver.IsInherit(InheritItem("x")));
        Assert.False(InheritBackgroundResolver.IsInherit(new ServiceItem { Id = "y" }));
        Assert.False(InheritBackgroundResolver.IsInherit(null));
        // Legacy BackgroundType never counts as Inherit.
        Assert.False(InheritBackgroundResolver.IsInherit(
            new ServiceItem { Id = "z", BackgroundType = BackgroundType.Image }));
    }
}

/// <summary>
/// Bulk-edit tests for the đợt 2 T2 "Inherit Background" option:
/// applying Inherit touches ONLY the selected items and leaves every other
/// field (legacy background, transition, auto-advance, title) exactly as-is.
/// </summary>
public class BulkEditInheritBackgroundTests
{
    private static List<ServiceItem> ThreeItems() => new()
    {
        new ServiceItem { Id = "a", Title = "A", TransitionTimeMs = 100 },
        new ServiceItem { Id = "b", Title = "B", TransitionTimeMs = 200 },
        new ServiceItem { Id = "c", Title = "C", TransitionTimeMs = 300 }
    };

    [Fact]
    public void Apply_InheritBackground_SetsKindOnSelectedItemsOnly()
    {
        var items = ThreeItems();
        var options = new BulkEditOptions
        {
            ApplyBackground = true,
            ApplyBackgroundInherit = true
        };

        BulkEditService.Apply(options, items.Take(2));

        Assert.Equal(ItemBackgroundKind.Inherit, items[0].BackgroundKind);
        Assert.Equal(ItemBackgroundKind.Inherit, items[1].BackgroundKind);
        Assert.Null(items[2].BackgroundKind); // not selected → untouched
    }

    [Fact]
    public void Apply_InheritBackground_LeavesOtherFieldsUntouched()
    {
        var items = ThreeItems();
        items[0].BackgroundType = BackgroundType.Image;
        items[0].AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer
        };

        var options = new BulkEditOptions
        {
            ApplyBackground = true,
            ApplyBackgroundInherit = true
        };

        BulkEditService.Apply(options, new[] { items[0] });

        Assert.Equal(ItemBackgroundKind.Inherit, items[0].BackgroundKind);
        Assert.Equal(BackgroundType.Image, items[0].BackgroundType); // legacy preserved
        Assert.Equal(100, items[0].TransitionTimeMs); // transition preserved
        Assert.Equal(AutoAdvanceAfterCondition.Timer,
            items[0].AutoAdvanceSettings!.AutoAdvanceAfterCondition); // auto-advance preserved
        Assert.Equal("A", items[0].Title); // title preserved
    }

    [Fact]
    public void Apply_InheritBackground_WinsOverLegacyBackgroundChoice()
    {
        var items = ThreeItems();
        var options = new BulkEditOptions
        {
            ApplyBackground = true,
            BackgroundType = BackgroundType.Video,
            ApplyBackgroundInherit = true
        };

        BulkEditService.Apply(options, items);

        Assert.All(items, i => Assert.Equal(ItemBackgroundKind.Inherit, i.BackgroundKind));
        Assert.All(items, i => Assert.Null(i.BackgroundType)); // legacy NOT touched
    }

    [Fact]
    public void Apply_InheritNotOptedIn_LeavesBackgroundKindAlone()
    {
        var items = ThreeItems();
        var options = new BulkEditOptions { ApplyBackground = false };

        BulkEditService.Apply(options, items);

        Assert.All(items, i => Assert.Null(i.BackgroundKind));
    }
}
