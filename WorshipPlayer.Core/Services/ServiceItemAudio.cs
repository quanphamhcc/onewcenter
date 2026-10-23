using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// A service item's resolved attached audio: the effective file list plus the
/// item's loop flag (đợt 2 T17 / R9). Pure value; the list is the same defensive
/// copy <see cref="ServiceItemAudio.Resolve"/> produces.
/// </summary>
public sealed record ResolvedItemAudio(List<string> Files, bool Loop);

/// <summary>
/// Pure read model for a service item's attached audio. Bridges the additive
/// <see cref="ServiceItem.AudioAttachments"/> list and the legacy single
/// <see cref="ServiceItem.AudioAttachmentPath"/> without mutating the model on
/// deserialization (System.Text.Json offers no clean hook for that, and mutating would risk
/// rewriting legacy files unexpectedly).
/// </summary>
public static class ServiceItemAudio
{
    /// <summary>
    /// Effective audio list for an item: AudioAttachments when present (non-null, non-empty);
    /// otherwise a single-element list from a non-empty legacy AudioAttachmentPath;
    /// otherwise empty. Pure; never mutates the item.
    /// </summary>
    public static List<string> Resolve(ServiceItem item)
    {
        if (item.AudioAttachments is { Count: > 0 }) return new(item.AudioAttachments);
        if (!string.IsNullOrWhiteSpace(item.AudioAttachmentPath)) return new() { item.AudioAttachmentPath };
        return new();
    }

    /// <summary>
    /// Resolve the item's audio together with its loop flag (đợt 2 T17 / R9).
    /// Same file-list rules as <see cref="Resolve"/>; the loop flag comes from
    /// <see cref="ServiceItem.Loop"/> (false for pre-T17 JSON, which has no key).
    /// </summary>
    public static ResolvedItemAudio ResolvePlayback(ServiceItem item)
        => new(Resolve(item), item.Loop);

    /// <summary>
    /// Whether an item's attached audio may start when the item becomes current
    /// (đợt 2 T17 / R9.4). False for a null item, and for a Song item whose
    /// backtrack is in use (<see cref="ServiceItem.UseBacktrack"/>): the attached
    /// audio must never overlap the backtrack. All other items may play.
    /// </summary>
    public static bool ShouldPlayAttachedAudio(ServiceItem? item)
        => item is not null
           && !(item.Type == ServiceItemType.Song && item.UseBacktrack);
}
