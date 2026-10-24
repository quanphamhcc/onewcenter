using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure (UI-free) decision helper for cleaning up LIVE-LINKED Slides items whose linked
/// presentation no longer exists.
///
/// A Slides service item is a live link to a library presentation via
/// <see cref="ServiceItem.SlidePresentationId"/> (see <see cref="ServiceItemDisplayName"/>). When the
/// presentation is deleted the link dangles: it resolves to no presentation, so the sidebar/center
/// keep the stale stored <see cref="ServiceItem.Title"/> forever. The host feeds this helper the open
/// <see cref="ServiceOrder"/> plus the set of presentation ids that still exist (built from the
/// primed cache) and applies the returned indices through the existing
/// <c>ServiceOrderManager.RemoveItem</c> + save flow.
///
/// Lives in Core (mirrors <see cref="ServiceItemDisplayName"/>'s placement + rationale) so the
/// removal decision is directly unit-testable — the Tests project is net8.0 and cannot reference the
/// WPF Presenter. This type never touches any cache, service, or UI; it only reads the order.
/// </summary>
public static class SlideLinkReconciler
{
    /// <summary>
    /// Return the ascending indices (into <see cref="ServiceOrder.Items"/>) of every item that is a
    /// DANGLING Slides live link: <see cref="ServiceItem.Type"/> is
    /// <see cref="ServiceItemType.Slides"/> AND <see cref="ServiceItem.SlidePresentationId"/> is
    /// non-empty AND that id is NOT in <paramref name="existingPresentationIds"/>. Items with an
    /// empty/null link id (legacy snapshots), items whose presentation still exists, and all
    /// non-Slides items are never returned. Returns an empty list for a null <paramref name="order"/>
    /// or an empty items list. Throws <see cref="ArgumentNullException"/> when
    /// <paramref name="existingPresentationIds"/> is null (matches the null-guard convention in
    /// <see cref="ServiceItemDisplayName"/>/<see cref="SlidePresentationMapper"/>).
    /// </summary>
    public static IReadOnlyList<int> FindOrphanedSlideItemIndices(
        ServiceOrder? order, ISet<string> existingPresentationIds)
    {
        if (existingPresentationIds == null) throw new ArgumentNullException(nameof(existingPresentationIds));

        var orphans = new List<int>();
        var items = order?.Items;
        if (items == null || items.Count == 0)
            return orphans;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Type == ServiceItemType.Slides
                && !string.IsNullOrEmpty(item.SlidePresentationId)
                && !existingPresentationIds.Contains(item.SlidePresentationId))
            {
                orphans.Add(i);
            }
        }

        return orphans;
    }

    /// <summary>
    /// Count how many items in <paramref name="order"/> are LIVE-LINKED Slides items pointing at
    /// <paramref name="presentationId"/> (<see cref="ServiceItemType.Slides"/> with
    /// <see cref="ServiceItem.SlidePresentationId"/> == the id). Used by the delete confirm to warn
    /// the user how many service items will also be removed. Returns 0 for a null order, a
    /// null/empty id, or when no item matches. Non-Slides items are never counted even if their
    /// <see cref="ServiceItem.SlidePresentationId"/> happens to match.
    /// </summary>
    public static int CountItemsLinkingPresentation(ServiceOrder? order, string presentationId)
    {
        if (order?.Items == null || string.IsNullOrEmpty(presentationId))
            return 0;

        int count = 0;
        foreach (var item in order.Items)
        {
            if (item.Type == ServiceItemType.Slides
                && item.SlidePresentationId == presentationId)
            {
                count++;
            }
        }

        return count;
    }
}
