using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure (UI-free) mapping between a picked library <see cref="Song"/> or a Scripture
/// reference and the fields of a <see cref="ServiceItem"/>.
///
/// This lives in Core so the picker logic — the part that makes the service-order
/// sidebar show the real song/scripture name instead of a placeholder — is directly
/// unit-testable (the Tests project is net8.0 and cannot reference the WPF Presenter).
///
/// The link field for a Song item is <see cref="ServiceItem.SongFolder"/>, set to the
/// song's <see cref="Song.FolderPath"/>. That is the exact field the playback chain
/// (PresenterIntegration -> MainWindow song resolution) reads, and it mirrors the
/// existing <c>ServiceOrderManager.CreateFromSetlist</c> precedent.
/// </summary>
public static class ServiceItemMapper
{
    /// <summary>
    /// Link a chosen library song to a service item: marks it as a Song, stores the
    /// song's folder path as the link, and sets the Title to the song's Title.
    /// If the song has no Title, the item's existing Title is kept.
    /// </summary>
    public static void ApplySong(ServiceItem item, Song song)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (song == null) throw new ArgumentNullException(nameof(song));

        item.Type = ServiceItemType.Song;
        item.SongFolder = song.FolderPath ?? "";
        if (!string.IsNullOrWhiteSpace(song.Title))
            item.Title = song.Title;
    }

    /// <summary>
    /// Link a Scripture reference to a service item: marks it as Scripture, stores the
    /// trimmed reference, and sets the Title to that reference. A blank reference leaves
    /// the existing Title untouched (but still clears the stored reference to the trim).
    /// </summary>
    public static void ApplyScripture(ServiceItem item, string reference)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        var trimmed = (reference ?? "").Trim();
        item.Type = ServiceItemType.Scripture;
        item.ScriptureReference = trimmed;
        if (!string.IsNullOrWhiteSpace(trimmed))
            item.Title = trimmed;
    }

    /// <summary>
    /// Single source of truth for a service item's final Title. Precedence:
    /// <list type="number">
    ///   <item>Song type with a picked song that has a Title -> the song's Title.</item>
    ///   <item>Scripture type with a non-blank reference -> the trimmed reference.</item>
    ///   <item>A non-blank typed name -> the trimmed typed name.</item>
    ///   <item>Otherwise -> the fallback "{Type} Item".</item>
    /// </list>
    /// </summary>
    public static string BuildTitle(
        ServiceItemType type,
        string? typedName,
        Song? pickedSong,
        string? scriptureReference)
    {
        if (type == ServiceItemType.Song &&
            pickedSong != null &&
            !string.IsNullOrWhiteSpace(pickedSong.Title))
        {
            return pickedSong.Title;
        }

        if (type == ServiceItemType.Scripture &&
            !string.IsNullOrWhiteSpace(scriptureReference))
        {
            return scriptureReference.Trim();
        }

        if (!string.IsNullOrWhiteSpace(typedName))
            return typedName.Trim();

        return $"{type} Item";
    }
}
