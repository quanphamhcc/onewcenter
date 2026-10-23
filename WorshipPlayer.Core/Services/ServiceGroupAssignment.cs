using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure group-assignment logic for <see cref="ServiceOrder.Groups"/> (đợt 2 T6 / R4).
///
/// A group owns an ordered list of <see cref="ServiceItem.Id"/> values
/// (<see cref="ServiceGroup.Items"/>); an item whose id appears in no group's list is
/// "ungrouped". There is a single source of truth (the group side) — ServiceItem has no
/// group reference. These helpers are the ONE place that mutates group membership, so the
/// invariants below hold no matter which caller (manager, bulk tooling, future UI) runs:
///
///   1. An item id belongs to AT MOST ONE group at a time.
///   2. A group's Items list never keeps an id whose item no longer exists in the order
///      (no orphan ids) — enforced by <see cref="Normalize"/> at load time and by
///      <see cref="RemoveItemFromAllGroups"/> when an item is deleted.
///   3. <see cref="ServiceGroup.Order"/> is always a dense 0..n-1 sequence after any
///      structural change (create/delete/move/normalize).
///   4. Deleting a group NEVER deletes its items — they become ungrouped.
///
/// All methods operate in place on the passed <see cref="ServiceOrder"/> and never throw
/// on unknown ids; they report success/failure via their return value instead.
/// </summary>
public static class ServiceGroupAssignment
{
    /// <summary>
    /// Assign an item to a group, or ungroup it when <paramref name="targetGroupId"/> is
    /// null. The id is first removed from every group (invariant 1), then appended to the
    /// target group. Returns false when the item id is unknown, or the target group id is
    /// non-null but unknown.
    /// </summary>
    public static bool AssignItem(ServiceOrder order, string itemId, string? targetGroupId)
    {
        if (!order.Items.Any(i => i.Id == itemId)) return false;

        ServiceGroup? target = null;
        if (targetGroupId != null)
        {
            target = order.Groups.FirstOrDefault(g => g.Id == targetGroupId);
            if (target == null) return false;
        }

        RemoveItemFromAllGroups(order, itemId);
        target?.Items.Add(itemId);
        return true;
    }

    /// <summary>
    /// Remove an item id from every group's Items list. Returns how many entries were
    /// removed (0 when the id was ungrouped). Used when an item is deleted from the
    /// service so no group keeps a dangling reference (invariant 2).
    /// </summary>
    public static int RemoveItemFromAllGroups(ServiceOrder order, string itemId)
    {
        var removed = 0;
        foreach (var group in order.Groups)
            removed += group.Items.RemoveAll(id => id == itemId);
        return removed;
    }

    /// <summary>
    /// Delete a group WITHOUT deleting its items (invariant 4): the group's item ids are
    /// simply dropped with the group, so those items become ungrouped. Remaining groups'
    /// Order values are compacted (invariant 3). Returns false on unknown id.
    /// </summary>
    public static bool DeleteGroup(ServiceOrder order, string groupId)
    {
        var group = order.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;

        order.Groups.Remove(group);
        CompactGroupOrder(order);
        return true;
    }

    /// <summary>
    /// Move a group to a new position among the groups (header reorder). Groups are first
    /// sorted by their current Order; the target group is removed and re-inserted at
    /// <paramref name="newIndex"/> (clamped to the valid range), then Order is rewritten
    /// as a dense 0..n-1 sequence (invariant 3). Returns false on unknown id.
    /// </summary>
    public static bool MoveGroup(ServiceOrder order, string groupId, int newIndex)
    {
        var ordered = order.Groups.OrderBy(g => g.Order).ToList();
        var group = ordered.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;

        ordered.Remove(group);
        var clamped = Math.Clamp(newIndex, 0, ordered.Count);
        ordered.Insert(clamped, group);

        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
        return true;
    }

    /// <summary>
    /// Restore the group invariants on data that came from outside the manager's own
    /// mutations (e.g. a service file edited by hand, or written by an older build):
    /// drop group item ids that no longer exist in <see cref="ServiceOrder.Items"/>
    /// (orphans), drop duplicate memberships (an id listed by several groups stays only
    /// in the first group by Order), and compact group Order to a dense 0..n-1 sequence.
    /// The ungrouped/grouped status of every EXISTING item is otherwise untouched, and
    /// group names / collapsed flags are preserved.
    /// </summary>
    public static void Normalize(ServiceOrder order)
    {
        var knownIds = new HashSet<string>(order.Items.Select(i => i.Id));
        var claimed = new HashSet<string>();

        foreach (var group in order.Groups.OrderBy(g => g.Order))
        {
            group.Items.RemoveAll(id => !knownIds.Contains(id) || !claimed.Add(id));
        }

        CompactGroupOrder(order);
    }

    /// <summary>
    /// Rewrite <see cref="ServiceGroup.Order"/> as a dense 0..n-1 sequence, preserving the
    /// groups' current relative order (stable by Order, ties keep list position).
    /// </summary>
    public static void CompactGroupOrder(ServiceOrder order)
    {
        var ordered = order.Groups.OrderBy(g => g.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
    }
}
