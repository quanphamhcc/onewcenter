using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Cloud;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Implements global search across songs, service orders, and slides.
/// Aggregates results from SongManager and ServiceOrderManager.
/// </summary>
public class GlobalSearchService : ISearchService
{
    private readonly SongManager _songManager;
    private readonly ServiceOrderManager _serviceOrderManager;

    public GlobalSearchService(SongManager songManager, ServiceOrderManager serviceOrderManager)
    {
        _songManager = songManager;
        _serviceOrderManager = serviceOrderManager;
    }

    /// <inheritdoc />
    public async Task<SearchResults> SearchAllAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResults();

        var results = new SearchResults
        {
            Songs = await SearchSongsAsync(query),
            Services = await SearchServicesAsync(query),
            Slides = await SearchSlidesAsync(query)
        };

        return results;
    }

    /// <inheritdoc />
    public Task<List<SongLibraryItem>> SearchSongsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(new List<SongLibraryItem>());

        var results = _songManager.Songs
            .Where(s =>
                s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Sections.Any(sec => sec.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Select(s => new SongLibraryItem
            {
                Id = s.FolderPath,
                Title = s.Title,
                Artist = s.Artist,
                Key = s.OriginalKey,
                Bpm = s.Bpm,
                HasBacktracks = s.Tracks.Count > 0,
                HasLyrics = s.Sections.Count > 0
            })
            .ToList();

        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public async Task<List<ServiceOrder>> SearchServicesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<ServiceOrder>();

        var allOrders = await _serviceOrderManager.LoadAllAsync();

        return allOrders
            .Where(o =>
                o.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.Items.Any(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<Slide>> SearchSlidesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<Slide>();

        var allOrders = await _serviceOrderManager.LoadAllAsync();
        var results = new List<Slide>();

        foreach (var order in allOrders)
        {
            foreach (var item in order.Items)
            {
                var matchingSlides = item.Slides
                    .Where(s =>
                        s.Content.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        s.Content.Header.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        s.Content.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase));

                results.AddRange(matchingSlides);
            }
        }

        return results;
    }
}
