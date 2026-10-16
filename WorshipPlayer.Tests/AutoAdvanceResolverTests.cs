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
///  - Deferred AudioEnd/VideoEnd conditions → None.
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
}
