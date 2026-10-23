using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure, UI-agnostic resolution of a service item's effective background (đợt 2 T1 / R1).
/// This is the ONLY place that decides "which background does this item get": renderers
/// (OutputWindow) consume <see cref="ResolvedItemBackground"/> instead of re-deriving
/// the rules per call site. Inherit chains are NOT resolved here — that is đợt 2 T2.
///
/// Resolution rules:
///  1. An explicit <see cref="ServiceItem.BackgroundKind"/> wins.
///  2. Otherwise a legacy <see cref="ServiceItem.BackgroundType"/> (pre-T1 files) maps
///     to the equivalent kind: SolidColor→Color, Image→Image, Video→Video.
///     Gradient has no per-item equivalent (its parameters live on the theme),
///     so it maps to Theme.
///  3. Otherwise the item follows the theme (Kind = Theme, Source = Theme) —
///     exactly the pre-T1 rendering behaviour.
/// Value fallbacks (so legacy data renders identically): a Color item with no color
/// of its own uses the theme's background color; an Image/Video item with no
/// <see cref="ServiceItem.BackgroundFilePath"/> uses the legacy
/// <see cref="ServiceItem.MediaPath"/>, where pre-T1 editors stored the file.
/// </summary>
public static class ItemBackgroundResolver
{
    /// <summary>
    /// The item's effective explicit background kind, honouring the pre-T1 data shape
    /// (see class remarks). A null item resolves to <see cref="ItemBackgroundKind.Theme"/>.
    /// </summary>
    public static ItemBackgroundKind GetItemKind(ServiceItem? item)
    {
        if (item == null) return ItemBackgroundKind.Theme;
        if (item.BackgroundKind.HasValue) return item.BackgroundKind.Value;

        return item.BackgroundType switch
        {
            Models.BackgroundType.SolidColor => ItemBackgroundKind.Color,
            Models.BackgroundType.Image => ItemBackgroundKind.Image,
            Models.BackgroundType.Video => ItemBackgroundKind.Video,
            // Gradient (and null): no per-item equivalent → follow the theme.
            _ => ItemBackgroundKind.Theme
        };
    }

    /// <summary>
    /// Resolve the effective background for <paramref name="item"/> under its effective
    /// <paramref name="theme"/> (may be null). When the result's
    /// <see cref="ResolvedItemBackground.Source"/> is Theme, the renderer applies the
    /// theme exactly as it did before T1; when it is Item, the renderer applies the
    /// returned kind + value instead.
    /// </summary>
    public static ResolvedItemBackground Resolve(ServiceItem? item, SlideTheme? theme)
    {
        switch (GetItemKind(item))
        {
            case ItemBackgroundKind.None:
                return new ResolvedItemBackground(
                    ItemBackgroundKind.None, null, null, ItemBackgroundSource.Item);

            case ItemBackgroundKind.Color:
                return new ResolvedItemBackground(
                    ItemBackgroundKind.Color,
                    FirstNonEmpty(item?.BackgroundColor, theme?.BackgroundColor),
                    null,
                    ItemBackgroundSource.Item);

            case ItemBackgroundKind.Image:
                return new ResolvedItemBackground(
                    ItemBackgroundKind.Image,
                    null,
                    FirstNonEmpty(item?.BackgroundFilePath, item?.MediaPath),
                    ItemBackgroundSource.Item);

            case ItemBackgroundKind.Video:
                return new ResolvedItemBackground(
                    ItemBackgroundKind.Video,
                    null,
                    FirstNonEmpty(item?.BackgroundFilePath, item?.MediaPath),
                    ItemBackgroundSource.Item);

            default: // Theme — no per-item background; the theme decides.
                return new ResolvedItemBackground(
                    ItemBackgroundKind.Theme, null, null, ItemBackgroundSource.Theme);
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }
}

/// <summary>Where an item's effective background came from.</summary>
public enum ItemBackgroundSource
{
    /// <summary>The item carries its own background; render the resolved kind/value.</summary>
    Item,

    /// <summary>The item follows the theme; render the theme background as before.</summary>
    Theme
}

/// <summary>
/// The resolved background for one item: the final kind, the concrete value
/// (<paramref name="Color"/> hex for Color, <paramref name="FilePath"/> for
/// Image/Video; both null for Theme/None), and where it came from.
/// </summary>
public sealed record ResolvedItemBackground(
    ItemBackgroundKind Kind,
    string? Color,
    string? FilePath,
    ItemBackgroundSource Source);
