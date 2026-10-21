using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

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
}
