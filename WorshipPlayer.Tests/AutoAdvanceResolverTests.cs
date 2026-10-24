using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the pure per-item auto-advance decision logic (roadmap C4).
/// These exercise AutoAdvanceResolver.Resolve directly, with in-memory ServiceOrder
/// fixtures and no manager, timer, or UI — the resolver is UI-agnostic by design.
///
/// Documented edge choices under test:
///  - SpecificItem with a missing/invalid/null target → fall back to NextItem (else None if last).
///  - NextItem on the last item → None (no wraparound).
///  - AudioEnd stays deferred → None on every trigger. VideoEnd → None on the
///    Timer path (the original 2-arg Resolve), advances only on the VideoEnd
///    trigger (đợt 2 T4).
/// </summary>
public class AutoAdvanceResolverTests
{
    private static ServiceOrder OrderWith(params ServiceItem[] items)
    {
        var order = new ServiceOrder();
        order.Items.AddRange(items);
        return order;
    }

    private static ServiceItem Item(string title) => new() { Title = title };

    [Fact]
    public void NullSettings_ReturnsNone()
    {
        var a = Item("A");
        var b = Item("B");
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void ManualCondition_ReturnsNone()
    {
        var a = Item("A");
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Manual,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void TimerNextItem_WithFollowingItem_ReturnsNextItem()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Text; // đợt 2 T3: đường Timer chỉ chạy với item hợp lệ
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.NextItem, decision.Action);
        Assert.Null(decision.TargetItemId);
    }

    [Fact]
    public void TimerNextSlide_TreatedLikeNextItem()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Text; // đợt 2 T3: đường Timer chỉ chạy với item hợp lệ
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextSlide
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.NextItem, decision.Action);
    }

    [Fact]
    public void TimerSpecificItem_ValidTarget_ReturnsGoToTarget()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Text; // đợt 2 T3: đường Timer chỉ chạy với item hợp lệ
        var b = Item("B");
        var c = Item("C");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = c.Id
        };
        var order = OrderWith(a, b, c);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.GoToItem, decision.Action);
        Assert.Equal(c.Id, decision.TargetItemId);
    }

    [Fact]
    public void TimerSpecificItem_MissingTarget_FallsBackToNextItem()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Text; // đợt 2 T3: đường Timer chỉ chạy với item hợp lệ
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = "does-not-exist"
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.NextItem, decision.Action);
    }

    [Fact]
    public void TimerSpecificItem_NullTarget_FallsBackToNextItem()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Text; // đợt 2 T3: đường Timer chỉ chạy với item hợp lệ
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = null
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.NextItem, decision.Action);
    }

    [Fact]
    public void TimerSpecificItem_InvalidTargetOnLastItem_ReturnsNone()
    {
        var a = Item("A");
        var b = Item("B");
        b.ForegroundType = ForegroundType.Text; // đợt 2 T3: đường Timer chỉ chạy với item hợp lệ
        b.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = "nope"
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, b.Id);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void TimerNextItem_OnLastItem_ReturnsNone_NoWraparound()
    {
        var a = Item("A");
        var b = Item("B");
        b.ForegroundType = ForegroundType.Text; // đợt 2 T3: đường Timer chỉ chạy với item hợp lệ
        b.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, b.Id);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void TimerLoop_ReturnsGoToCurrentItem()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Text; // đợt 2 T3: đường Timer chỉ chạy với item hợp lệ
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.Loop
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.GoToItem, decision.Action);
        Assert.Equal(a.Id, decision.TargetItemId);
    }

    [Theory]
    [InlineData(AutoAdvanceAfterCondition.AudioEnd)]
    [InlineData(AutoAdvanceAfterCondition.VideoEnd)]
    public void DeferredMediaConditions_ReturnNone(AutoAdvanceAfterCondition condition)
    {
        var a = Item("A");
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = condition,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void UnknownCurrentId_ReturnsNone()
    {
        var a = Item("A");
        var order = OrderWith(a);

        var decision = AutoAdvanceResolver.Resolve(order, "missing");

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void NullOrder_ReturnsNone()
    {
        var decision = AutoAdvanceResolver.Resolve(null, "any");

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    // ─── Đợt 2 T3: eligibility Lyrics/Scripture ──────────────────────────────

    [Fact]
    public void IsEligible_NullItem_ReturnsFalse()
    {
        Assert.False(AutoAdvanceResolver.IsEligible(null));
    }

    [Theory]
    [InlineData(ForegroundType.Lyrics)]
    [InlineData(ForegroundType.Scripture)]
    public void IsEligible_LyricsOrScripture_ReturnsFalse(ForegroundType foreground)
    {
        var item = new ServiceItem { Title = "X", ForegroundType = foreground };

        Assert.False(AutoAdvanceResolver.IsEligible(item));
    }

    [Theory]
    [InlineData(ForegroundType.Text)]
    [InlineData(ForegroundType.Media)]
    [InlineData(ForegroundType.Blank)]
    public void IsEligible_TextMediaBlank_ReturnsTrue(ForegroundType foreground)
    {
        var item = new ServiceItem { Title = "X", ForegroundType = foreground };

        Assert.True(AutoAdvanceResolver.IsEligible(item));
    }

    [Fact]
    public void IsEligible_SlidesTypeWithLyricsForeground_ReturnsTrue()
    {
        // Slides-library item: eligible via Type even when its foreground is Lyrics.
        var item = new ServiceItem
        {
            Title = "X",
            Type = ServiceItemType.Slides,
            ForegroundType = ForegroundType.Lyrics
        };

        Assert.True(AutoAdvanceResolver.IsEligible(item));
    }

    [Fact]
    public void Timer_WithLyricsItem_ReturnsNone()
    {
        var a = Item("A"); // default ForegroundType = Lyrics
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void Timer_WithScriptureItem_ReturnsNone_EvenSpecificTarget()
    {
        var a = Item("A");
        var b = Item("B");
        a.ForegroundType = ForegroundType.Scripture;
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = b.Id
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void Timer_WithTextItem_StillAdvancesNextItem()
    {
        var a = Item("A");
        var b = Item("B");
        a.ForegroundType = ForegroundType.Text;
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.NextItem, decision.Action);
    }

    [Fact]
    public void Timer_WithSlidesTypeItem_AdvancesNextItem()
    {
        var a = Item("A");
        var b = Item("B");
        a.Type = ServiceItemType.Slides; // eligible via Type despite Lyrics foreground
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id);

        Assert.Equal(AutoAdvanceAction.NextItem, decision.Action);
    }

    // ─── Đợt 2 T4: trigger VideoEnd ──────────────────────────────────────────

    [Fact]
    public void VideoEnd_NextItem_WithVideoEndTrigger_ReturnsNextItem()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Media; // item hợp lệ theo T3
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.NextItem, decision.Action);
        Assert.Null(decision.TargetItemId);
    }

    [Fact]
    public void VideoEnd_SpecificItem_WithVideoEndTrigger_ReturnsGoToTarget()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Media;
        var b = Item("B");
        var c = Item("C");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = c.Id
        };
        var order = OrderWith(a, b, c);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.GoToItem, decision.Action);
        Assert.Equal(c.Id, decision.TargetItemId);
    }

    [Fact]
    public void VideoEnd_Loop_WithVideoEndTrigger_ReturnsGoToCurrentItem()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Text;
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.Loop
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.GoToItem, decision.Action);
        Assert.Equal(a.Id, decision.TargetItemId);
    }

    [Fact]
    public void VideoEnd_SpecificItem_InvalidTarget_WithVideoEndTrigger_FallsBackToNextItem()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Media;
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = "does-not-exist"
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.NextItem, decision.Action);
    }

    [Fact]
    public void VideoEnd_NextItem_OnLastItem_WithVideoEndTrigger_ReturnsNone()
    {
        var a = Item("A");
        var b = Item("B");
        b.ForegroundType = ForegroundType.Media;
        b.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, b.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void VideoEnd_WithTimerTrigger_ReturnsNone_NoFallbackToTimer()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Media;
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.Timer);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void Timer_WithVideoEndTrigger_ReturnsNone()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Text;
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void AudioEnd_WithVideoEndTrigger_StillDeferred_ReturnsNone()
    {
        var a = Item("A");
        a.ForegroundType = ForegroundType.Media;
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.AudioEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void VideoEnd_WithLyricsItem_ReturnsNone()
    {
        var a = Item("A"); // default ForegroundType = Lyrics → không hợp lệ theo T3
        var b = Item("B");
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.NextItem
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }

    [Fact]
    public void VideoEnd_WithScriptureItem_ReturnsNone_EvenSpecificTarget()
    {
        var a = Item("A");
        var b = Item("B");
        a.ForegroundType = ForegroundType.Scripture;
        a.AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = b.Id
        };
        var order = OrderWith(a, b);

        var decision = AutoAdvanceResolver.Resolve(order, a.Id, AutoAdvanceTrigger.VideoEnd);

        Assert.Equal(AutoAdvanceAction.None, decision.Action);
    }
}
