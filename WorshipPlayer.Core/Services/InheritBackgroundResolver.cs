using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Chain resolution for <see cref="ItemBackgroundKind.Inherit"/> (đợt 2 T2 / R2).
/// Pure and UI-agnostic, like <see cref="ItemBackgroundResolver"/> (which this
/// extends: non-Inherit items delegate to it directly).
///
/// Rules:
///  1. An item whose explicit <see cref="ServiceItem.BackgroundKind"/> is Inherit
///     walks UP the live list (skipping other Inherit items) to the first item
///     with a real background (any non-Inherit effective kind) and renders that
///     item's resolved background.
///  2. If no such item exists (Inherit on the first item, or an empty list),
///     the item follows the theme.
///  3. Resolution is computed at call time from the list's CURRENT order and is
///     never written back into child items — reordering, removing, or changing
///     the source item is picked up on the next resolve.
/// </summary>
public static class InheritBackgroundResolver
{
    /// <summary>
    /// True when the item explicitly asks to inherit (only the new T2 value;
    /// the legacy <see cref="ServiceItem.BackgroundType"/> never maps to Inherit).
    /// </summary>
    public static bool IsInherit(ServiceItem? item) =>
        item?.BackgroundKind == ItemBackgroundKind.Inherit;

    /// <summary>
    /// Resolve the effective background of the item at <paramref name="index"/>
    /// within the live <paramref name="items"/> list under <paramref name="theme"/>.
    /// </summary>
    public static ResolvedItemBackground ResolveItem(
        IList<ServiceItem> items, int index, SlideTheme? theme)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (index < 0 || index >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var item = items[index];
        if (!IsInherit(item))
        {
            return ItemBackgroundResolver.Resolve(item, theme);
        }

        // Walk up past other Inherit items to the first item with a real background.
        for (var j = index - 1; j >= 0; j--)
        {
            if (!IsInherit(items[j]))
            {
                return ItemBackgroundResolver.Resolve(items[j], theme);
            }
        }

        // No source above (first item, or everything above is Inherit) → theme.
        return ItemBackgroundResolver.Resolve(null, theme);
    }

    /// <summary>
    /// Resolve every item of the live <paramref name="items"/> list in order.
    /// An empty list resolves to an empty list (no error).
    /// </summary>
    public static IReadOnlyList<ResolvedItemBackground> ResolveService(
        IList<ServiceItem> items, SlideTheme? theme)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        var result = new List<ResolvedItemBackground>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            result.Add(ResolveItem(items, i, theme));
        }
        return result;
    }
}
