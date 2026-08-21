using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Cloud;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// Global search across songs, services, and slides.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Search across all content types (songs, service orders, slides).
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <returns>Grouped search results.</returns>
    Task<SearchResults> SearchAllAsync(string query);

    /// <summary>
    /// Search only within songs.
    /// </summary>
    Task<List<SongLibraryItem>> SearchSongsAsync(string query);

    /// <summary>
    /// Search only within service orders.
    /// </summary>
    Task<List<ServiceOrder>> SearchServicesAsync(string query);

    /// <summary>
    /// Search only within slide content.
    /// </summary>
    Task<List<Slide>> SearchSlidesAsync(string query);
}

/// <summary>
/// Grouped results from a global search operation.
/// </summary>
public class SearchResults
{
    /// <summary>
    /// Matching songs.
    /// </summary>
    public List<SongLibraryItem> Songs { get; set; } = new();

    /// <summary>
    /// Matching service orders.
    /// </summary>
    public List<ServiceOrder> Services { get; set; } = new();

    /// <summary>
    /// Matching slides.
    /// </summary>
    public List<Slide> Slides { get; set; } = new();

    /// <summary>
    /// Total number of results across all categories.
    /// </summary>
    public int TotalCount => Songs.Count + Services.Count + Slides.Count;
}
