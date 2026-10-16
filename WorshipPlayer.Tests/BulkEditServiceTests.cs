using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the C3 "Bulk edit" pure operation (<see cref="BulkEditService"/>).
/// Covers: chosen fields applied to EVERY selected item, unchosen fields left untouched,
/// a no-op selection changing nothing, and the label→enum mapping edge cases.
/// </summary>
public class BulkEditServiceTests
{
    private static List<ServiceItem> ThreeItems() => new()
    {
        new ServiceItem { Title = "A" },
        new ServiceItem { Title = "B" },
        new ServiceItem { Title = "C" }
    };

    // ─── (a) chosen fields applied to EVERY selected item ───────────────────────

    [Fact]
    public void Apply_ChosenBackground_IsAppliedToEveryItem()
    {
        var items = ThreeItems();
        var options = new BulkEditOptions
        {
            ApplyBackground = true,
            BackgroundType = BackgroundType.Image
        };

        BulkEditService.Apply(options, items);

        Assert.All(items, i => Assert.Equal(BackgroundType.Image, i.BackgroundType));
    }

    [Fact]
    public void Apply_ChosenTransition_IsAppliedToEveryItem()
    {
        var items = ThreeItems();
        var options = new BulkEditOptions
        {
            ApplyTransition = true,
            TransitionTimeMs = 1200
        };

        BulkEditService.Apply(options, items);

        Assert.All(items, i => Assert.Equal(1200, i.TransitionTimeMs));
    }

    [Fact]
    public void Apply_ChosenAutoAdvance_IsAppliedToEveryItem_CreatingSettingsWhenMissing()
    {
        var items = ThreeItems();
        var options = new BulkEditOptions
        {
            ApplyAutoAdvance = true,
            AutoAdvanceCondition = AutoAdvanceAfterCondition.Timer
        };

        BulkEditService.Apply(options, items);

        Assert.All(items, i =>
        {
            Assert.NotNull(i.AutoAdvanceSettings);
            Assert.Equal(AutoAdvanceAfterCondition.Timer, i.AutoAdvanceSettings!.AutoAdvanceAfterCondition);
        });
    }

    // ─── (b) unchosen fields are left untouched ─────────────────────────────────

    [Fact]
    public void Apply_UnchosenFields_AreLeftUntouched()
    {
        var item = new ServiceItem
        {
            BackgroundType = BackgroundType.Gradient,
            TransitionTimeMs = 777,
            AutoAdvanceSettings = new ServiceItemSettings
            {
                AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
                AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
                AutoAdvanceDelayMs = 9000,
                TransitionFadeTimeMs = 250,
                GoToItemId = "target-item"
            }
        };

        // Everything opted out → nothing should change.
        BulkEditService.Apply(new BulkEditOptions(), new[] { item });

        Assert.Equal(BackgroundType.Gradient, item.BackgroundType);
        Assert.Equal(777, item.TransitionTimeMs);
        Assert.Equal(AutoAdvanceAfterCondition.VideoEnd, item.AutoAdvanceSettings!.AutoAdvanceAfterCondition);
    }

    [Fact]
    public void Apply_AutoAdvance_PreservesOtherSettingsFields()
    {
        var item = new ServiceItem
        {
            AutoAdvanceSettings = new ServiceItemSettings
            {
                AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Manual,
                AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
                AutoAdvanceDelayMs = 9000,
                TransitionFadeTimeMs = 250,
                GoToItemId = "target-item"
            }
        };

        var options = new BulkEditOptions
        {
            ApplyAutoAdvance = true,
            AutoAdvanceCondition = AutoAdvanceAfterCondition.AudioEnd
        };

        BulkEditService.Apply(options, new[] { item });

        // Only the after-condition changes; GoTo/delay/fade/GoToItemId are per-item and preserved.
        Assert.Equal(AutoAdvanceAfterCondition.AudioEnd, item.AutoAdvanceSettings!.AutoAdvanceAfterCondition);
        Assert.Equal(AutoAdvanceGoTo.SpecificItem, item.AutoAdvanceSettings.AutoAdvanceGoTo);
        Assert.Equal(9000, item.AutoAdvanceSettings.AutoAdvanceDelayMs);
        Assert.Equal(250, item.AutoAdvanceSettings.TransitionFadeTimeMs);
        Assert.Equal("target-item", item.AutoAdvanceSettings.GoToItemId);
    }

    [Fact]
    public void Apply_BackgroundOptedInButNoMappedValue_LeavesBackgroundAsIs()
    {
        // ApplyBackground true but BackgroundType null (panel "None"/unmapped) → no change.
        var item = new ServiceItem { BackgroundType = BackgroundType.SolidColor };
        var options = new BulkEditOptions { ApplyBackground = true, BackgroundType = null };

        BulkEditService.Apply(options, new[] { item });

        Assert.Equal(BackgroundType.SolidColor, item.BackgroundType);
    }

    // ─── (c) a no-op selection changes nothing ──────────────────────────────────

    [Fact]
    public void Apply_NoOpOptions_OverItems_ChangesNothing()
    {
        var items = ThreeItems();
        // Capture the defaults.
        Assert.All(items, i => Assert.Null(i.BackgroundType));
        Assert.All(items, i => Assert.Equal(500, i.TransitionTimeMs));
        Assert.All(items, i => Assert.Null(i.AutoAdvanceSettings));

        BulkEditService.Apply(new BulkEditOptions(), items);

        Assert.All(items, i => Assert.Null(i.BackgroundType));
        Assert.All(items, i => Assert.Equal(500, i.TransitionTimeMs));
        Assert.All(items, i => Assert.Null(i.AutoAdvanceSettings));
    }

    [Fact]
    public void Apply_EmptySelection_DoesNotThrow()
    {
        var options = new BulkEditOptions { ApplyTransition = true, TransitionTimeMs = 100 };

        // No items → no-op, no exception.
        BulkEditService.Apply(options, Array.Empty<ServiceItem>());
    }

    // ─── mapping edge cases ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("None", null)]
    [InlineData("", null)]
    [InlineData("something else", null)]
    [InlineData("Solid Color", BackgroundType.SolidColor)]
    [InlineData("Image", BackgroundType.Image)]
    [InlineData("Video", BackgroundType.Video)]
    [InlineData("Gradient", BackgroundType.Gradient)]
    public void ParseBackground_MapsLabelsOrReturnsNull(string label, BackgroundType? expected)
    {
        Assert.Equal(expected, BulkEditService.ParseBackground(label));
    }

    [Fact]
    public void ParseBackground_Null_ReturnsNull()
    {
        Assert.Null(BulkEditService.ParseBackground(null));
    }

    [Theory]
    [InlineData("Time Delay", AutoAdvanceAfterCondition.Timer)]
    [InlineData("End of Media", AutoAdvanceAfterCondition.AudioEnd)]
    [InlineData("Manual", AutoAdvanceAfterCondition.Manual)]
    [InlineData("anything else", AutoAdvanceAfterCondition.Manual)]
    public void ParseAfterCondition_MapsLabelsWithManualFallback(string label, AutoAdvanceAfterCondition expected)
    {
        Assert.Equal(expected, BulkEditService.ParseAfterCondition(label));
    }
}
