using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure, UI-agnostic decision logic for per-item auto-advance (roadmap C4).
/// Given a <see cref="ServiceOrder"/> and the id of the current item, it decides
/// whether the engine should do nothing, advance to the next item, or jump to a
/// specific item. It never touches timers, WPF, or <see cref="ServiceOrderManager"/>
/// navigation — the engine/trigger layer owns deciding WHEN to call <see cref="Resolve"/>
/// (i.e. when a configured trigger fires) and HOW to execute the returned decision.
///
/// Because the resolver returns an item <em>id</em> (not an index) it stays independent
/// of list positions; the caller translates the id to an index to navigate.
/// </summary>
public static class AutoAdvanceResolver
{
    /// <summary>
    /// Whether the item may ever be auto-advanced (đợt 2 T3 / R3).
    /// Items whose foreground is <see cref="ForegroundType.Lyrics"/> or
    /// <see cref="ForegroundType.Scripture"/> are NEVER auto-advanced, no matter
    /// what their <see cref="ServiceItem.AutoAdvanceSettings"/> say. Items with a
    /// Text/Media/Blank foreground, and Slides-library items
    /// (<see cref="ServiceItemType.Slides"/>), are eligible.
    /// </summary>
    public static bool IsEligible(ServiceItem? item)
    {
        if (item == null)
            return false;

        if (item.Type == ServiceItemType.Slides)
            return true;

        return item.ForegroundType is ForegroundType.Text
            or ForegroundType.Media
            or ForegroundType.Blank;
    }

    /// <summary>
    /// Decide what should happen when the current item's auto-advance trigger fires.
    /// This is the original Timer-path entry point (đợt 2 T4 keeps the signature):
    /// it resolves exactly as <see cref="Resolve(ServiceOrder, string, AutoAdvanceTrigger)"/>
    /// with <see cref="AutoAdvanceTrigger.Timer"/>.
    /// </summary>
    /// <param name="order">The active service order (may be null → <see cref="AutoAdvanceAction.None"/>).</param>
    /// <param name="currentItemId">Stable id of the item that is currently active.</param>
    public static Decision Resolve(ServiceOrder? order, string? currentItemId)
        => Resolve(order, currentItemId, AutoAdvanceTrigger.Timer);

    /// <summary>
    /// Decide what should happen when a concrete trigger fires for the current item
    /// (đợt 2 T4 / R3). The item's configured condition must MATCH the trigger:
    /// a <see cref="AutoAdvanceAfterCondition.Timer"/> item only advances on the
    /// timer, a <see cref="AutoAdvanceAfterCondition.VideoEnd"/> item only when its
    /// video actually ended — a VideoEnd item never falls back to the timer, and a
    /// Timer item never advances on a video-ended signal.
    /// <see cref="AutoAdvanceAfterCondition.AudioEnd"/> stays DEFERRED (no per-item
    /// audio-ended signal is wired yet — R9/đợt sau), and Manual never fires.
    /// </summary>
    /// <param name="order">The active service order (may be null → <see cref="AutoAdvanceAction.None"/>).</param>
    /// <param name="currentItemId">Stable id of the item that is currently active.</param>
    /// <param name="trigger">Which trigger actually fired.</param>
    public static Decision Resolve(ServiceOrder? order, string? currentItemId, AutoAdvanceTrigger trigger)
    {
        if (order == null || string.IsNullOrEmpty(currentItemId))
            return Decision.None;

        int currentIndex = order.Items.FindIndex(i => i.Id == currentItemId);
        if (currentIndex < 0)
            return Decision.None;

        var current = order.Items[currentIndex];

        // Đợt 2 T3: lyrics/scripture items never auto-advance, regardless of settings.
        if (!IsEligible(current))
            return Decision.None;

        var settings = current.AutoAdvanceSettings;

        // No settings, or a configured condition that does not match the trigger
        // that fired, means "behave exactly as today" (no advance).
        if (settings == null)
            return Decision.None;

        bool conditionMatches = (settings.AutoAdvanceAfterCondition, trigger) switch
        {
            (AutoAdvanceAfterCondition.Timer, AutoAdvanceTrigger.Timer) => true,
            (AutoAdvanceAfterCondition.VideoEnd, AutoAdvanceTrigger.VideoEnd) => true,
            _ => false
        };
        if (!conditionMatches)
            return Decision.None;

        switch (settings.AutoAdvanceGoTo)
        {
            // NextSlide is the model default and is not surfaced in the panel; at the
            // item-advance boundary it is treated the same as NextItem.
            case AutoAdvanceGoTo.NextSlide:
            case AutoAdvanceGoTo.NextItem:
                return NextItemOrStop(order, currentIndex);

            case AutoAdvanceGoTo.SpecificItem:
                if (!string.IsNullOrEmpty(settings.GoToItemId) &&
                    order.Items.Any(i => i.Id == settings.GoToItemId))
                {
                    return new Decision(AutoAdvanceAction.GoToItem, settings.GoToItemId);
                }
                // Invalid / missing / null target → documented fallback: advance to next item
                // (or stop if there is none). Never wrap around.
                return NextItemOrStop(order, currentIndex);

            case AutoAdvanceGoTo.Loop:
                // Re-show the current item.
                return new Decision(AutoAdvanceAction.GoToItem, currentItemId);

            default:
                return Decision.None;
        }
    }

    private static Decision NextItemOrStop(ServiceOrder order, int currentIndex)
    {
        return currentIndex + 1 < order.Items.Count
            ? new Decision(AutoAdvanceAction.NextItem, null)
            : Decision.None;
    }

    /// <summary>
    /// The outcome of a resolve: what the engine should do and (for a jump) the target id.
    /// </summary>
    public readonly record struct Decision(AutoAdvanceAction Action, string? TargetItemId)
    {
        /// <summary>Convenience "do nothing" result.</summary>
        public static Decision None => new(AutoAdvanceAction.None, null);
    }
}

/// <summary>
/// Which concrete signal fired an auto-advance check (đợt 2 T4 / R3).
/// The resolver only advances an item when its configured
/// <see cref="AutoAdvanceAfterCondition"/> matches the trigger that fired.
/// </summary>
public enum AutoAdvanceTrigger
{
    /// <summary>The per-item auto-advance timer elapsed.</summary>
    Timer,

    /// <summary>The current item's video (per-item background video or full-screen video) ended.</summary>
    VideoEnd
}

/// <summary>
/// The action the engine should take when an auto-advance trigger fires.
/// </summary>
public enum AutoAdvanceAction
{
    /// <summary>Do nothing (default / unconfigured / last item / deferred condition).</summary>
    None,

    /// <summary>Advance to the next service item in order.</summary>
    NextItem,

    /// <summary>Jump to the item identified by <see cref="AutoAdvanceResolver.Decision.TargetItemId"/>.</summary>
    GoToItem
}
