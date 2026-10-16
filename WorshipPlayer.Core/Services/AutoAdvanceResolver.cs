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
    /// Decide what should happen when the current item's auto-advance trigger fires.
    /// </summary>
    /// <param name="order">The active service order (may be null → <see cref="AutoAdvanceAction.None"/>).</param>
    /// <param name="currentItemId">Stable id of the item that is currently active.</param>
    public static Decision Resolve(ServiceOrder? order, string? currentItemId)
    {
        if (order == null || string.IsNullOrEmpty(currentItemId))
            return Decision.None;

        int currentIndex = order.Items.FindIndex(i => i.Id == currentItemId);
        if (currentIndex < 0)
            return Decision.None;

        var current = order.Items[currentIndex];
        var settings = current.AutoAdvanceSettings;

        // No settings, or a condition we don't trigger on, means "behave exactly as today".
        // Manual is the explicit "do nothing" default; AudioEnd/VideoEnd are DEFERRED
        // (no per-item media-ended signal exists yet) so they are treated as no-op here.
        if (settings == null || settings.AutoAdvanceAfterCondition != AutoAdvanceAfterCondition.Timer)
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
