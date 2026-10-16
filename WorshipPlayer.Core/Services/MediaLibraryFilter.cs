using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure, UI-independent composition of the media-library filters (type, tag, name/tag search).
/// The <see cref="MediaLibraryService"/> only exposes single-dimension queries
/// (GetByType OR GetByTag OR Search), so combined filtering (type AND tag AND search) is
/// composed here over the full item list, reusing the same matching semantics as the service.
/// Lives in Core so it can be unit-tested without WPF.
/// </summary>
public static class MediaLibraryFilter
{
    /// <summary>
    /// Returns the items matching all supplied filters. A null/empty dimension is a no-op,
    /// so passing all nulls returns the full list (the reset state).
    /// </summary>
    /// <param name="items">Source items (typically the result of <c>GetAllAsync</c>).</param>
    /// <param name="type">Media type to keep, or null for all types.</param>
    /// <param name="tag">Exact tag to keep (case-insensitive membership), or null/empty for all tags.</param>
    /// <param name="search">Substring matched against name OR any tag (case-insensitive), or null/empty for no search.</param>
    public static List<MediaLibraryItem> Apply(
        IEnumerable<MediaLibraryItem> items,
        MediaLibraryType? type,
        string? tag,
        string? search)
    {
        if (items == null) return new List<MediaLibraryItem>();

        IEnumerable<MediaLibraryItem> query = items;

        // Type equality — mirrors MediaLibraryService.GetByTypeAsync.
        if (type.HasValue)
            query = query.Where(item => item.MediaType == type.Value);

        // Case-insensitive exact-tag membership — mirrors MediaLibraryService.GetByTagAsync.
        if (!string.IsNullOrWhiteSpace(tag))
            query = query.Where(item => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

        // Case-insensitive name/tag substring — mirrors MediaLibraryService.SearchAsync.
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item =>
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        return query.ToList();
    }

    /// <summary>
    /// Returns the sorted, case-insensitive-distinct set of tags present across the items,
    /// for populating the Tag filter dropdown.
    /// </summary>
    public static IReadOnlyList<string> DistinctTags(IEnumerable<MediaLibraryItem> items)
    {
        if (items == null) return Array.Empty<string>();

        return items
            .SelectMany(item => item.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
