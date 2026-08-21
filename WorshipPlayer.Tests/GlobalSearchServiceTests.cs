using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for GlobalSearchService.
/// Tests searching across songs and empty result handling.
/// </summary>
public class GlobalSearchServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly SongManager _songManager;
    private readonly ServiceOrderManager _serviceOrderManager;
    private readonly GlobalSearchService _service;

    public GlobalSearchServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"GlobalSearchTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);

        var songsFolder = Path.Combine(_tempFolder, "Songs");
        var setlistsFolder = Path.Combine(_tempFolder, "Setlists");
        var ordersFolder = Path.Combine(_tempFolder, "ServiceOrders");

        Directory.CreateDirectory(songsFolder);
        Directory.CreateDirectory(setlistsFolder);
        Directory.CreateDirectory(ordersFolder);

        _songManager = new SongManager(songsFolder, setlistsFolder);
        _serviceOrderManager = new ServiceOrderManager(ordersFolder);
        _service = new GlobalSearchService(_songManager, _serviceOrderManager);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public async Task SearchAllAsync_EmptyQuery_ReturnsEmptyResults()
    {
        var results = await _service.SearchAllAsync("");

        Assert.NotNull(results);
        Assert.Empty(results.Songs);
        Assert.Empty(results.Services);
        Assert.Empty(results.Slides);
    }

    [Fact]
    public async Task SearchAllAsync_WhitespaceQuery_ReturnsEmptyResults()
    {
        var results = await _service.SearchAllAsync("   ");

        Assert.Empty(results.Songs);
    }

    [Fact]
    public async Task SearchAllAsync_MatchesSongByTitle()
    {
        AddTestSong("Amazing Grace", "John Newton");
        AddTestSong("How Great Thou Art", "Stuart Hine");

        var results = await _service.SearchAllAsync("Amazing");

        Assert.Single(results.Songs);
        Assert.Equal("Amazing Grace", results.Songs[0].Title);
    }

    [Fact]
    public async Task SearchAllAsync_MatchesSongByArtist()
    {
        AddTestSong("Amazing Grace", "John Newton");

        var results = await _service.SearchAllAsync("Newton");

        Assert.Single(results.Songs);
    }

    [Fact]
    public async Task SearchAllAsync_MatchesSongByLyrics()
    {
        var song = new Song
        {
            Title = "Test Song",
            Artist = "Artist",
            FolderPath = "/songs/test"
        };
        song.Sections.Add(new Section { Name = "Verse 1", Text = "Then sings my soul" });
        _songManager.Songs.Add(song);

        var results = await _service.SearchAllAsync("sings my soul");

        Assert.Single(results.Songs);
    }

    [Fact]
    public async Task SearchSongsAsync_EmptyQuery_ReturnsEmpty()
    {
        AddTestSong("Song", "Artist");

        var results = await _service.SearchSongsAsync("");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchSongsAsync_NoMatches_ReturnsEmpty()
    {
        AddTestSong("Amazing Grace", "John Newton");

        var results = await _service.SearchSongsAsync("nonexistent term xyz");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchSongsAsync_CaseInsensitive()
    {
        AddTestSong("Amazing Grace", "John Newton");

        var results = await _service.SearchSongsAsync("AMAZING");

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchSongsAsync_MultipleSongs_ReturnsAllMatches()
    {
        AddTestSong("Amazing Grace", "Artist");
        AddTestSong("Amazing Love", "Artist");
        AddTestSong("How Great", "Artist");

        var results = await _service.SearchSongsAsync("Amazing");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchSongsAsync_ResultsIncludeMetadata()
    {
        _songManager.Songs.Add(new Song
        {
            Title = "Test Song",
            Artist = "Test Artist",
            OriginalKey = "G",
            Bpm = 120,
            FolderPath = "/test/path"
        });

        var results = await _service.SearchSongsAsync("Test");

        Assert.Single(results);
        Assert.Equal("Test Artist", results[0].Artist);
        Assert.Equal("G", results[0].Key);
    }

    [Fact]
    public async Task SearchServicesAsync_EmptyQuery_ReturnsEmpty()
    {
        var results = await _service.SearchServicesAsync("");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchSlidesAsync_EmptyQuery_ReturnsEmpty()
    {
        var results = await _service.SearchSlidesAsync("");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAllAsync_EmptyLibrary_ReturnsEmptyGrouped()
    {
        var results = await _service.SearchAllAsync("anything");

        Assert.Empty(results.Songs);
        Assert.Empty(results.Services);
        Assert.Empty(results.Slides);
    }

    private void AddTestSong(string title, string artist)
    {
        _songManager.Songs.Add(new Song
        {
            Title = title,
            Artist = artist,
            FolderPath = $"/songs/{title.Replace(" ", "_")}"
        });
    }
}
