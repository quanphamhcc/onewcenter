using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for SongLibraryService.
/// Tests search (case-insensitive), filter by key, and sorting.
/// </summary>
public class SongLibraryServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly SongManager _songManager;
    private readonly SongUsageService _usageService;
    private readonly SongLibraryService _service;

    public SongLibraryServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"SongLibTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);

        var songsFolder = Path.Combine(_tempFolder, "Songs");
        var setlistsFolder = Path.Combine(_tempFolder, "Setlists");
        var usagePath = Path.Combine(_tempFolder, "usage.json");

        Directory.CreateDirectory(songsFolder);
        Directory.CreateDirectory(setlistsFolder);

        _songManager = new SongManager(songsFolder, setlistsFolder);
        _usageService = new SongUsageService(usagePath);
        _service = new SongLibraryService(_songManager, _usageService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public async Task GetAllAsync_EmptyLibrary_ReturnsEmpty()
    {
        var results = await _service.GetAllAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetAllAsync_WithSongs_ReturnsSortedByTitle()
    {
        AddTestSong("Zebra Song", "Artist A", "G");
        AddTestSong("Alpha Song", "Artist B", "C");
        AddTestSong("Middle Song", "Artist C", "D");

        var results = await _service.GetAllAsync();

        Assert.Equal(3, results.Count);
        Assert.Equal("Alpha Song", results[0].Title);
        Assert.Equal("Middle Song", results[1].Title);
        Assert.Equal("Zebra Song", results[2].Title);
    }

    [Fact]
    public async Task SearchAsync_ByTitle_CaseInsensitive()
    {
        AddTestSong("Amazing Grace", "John Newton", "G");
        AddTestSong("How Great Thou Art", "Stuart Hine", "Bb");

        var results = await _service.SearchAsync("amazing");

        Assert.Single(results);
        Assert.Equal("Amazing Grace", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_ByTitle_UpperCase()
    {
        AddTestSong("Amazing Grace", "John Newton", "G");

        var results = await _service.SearchAsync("AMAZING");

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_ByArtist_CaseInsensitive()
    {
        AddTestSong("Amazing Grace", "John Newton", "G");
        AddTestSong("10000 Reasons", "Matt Redman", "G");

        var results = await _service.SearchAsync("redman");

        Assert.Single(results);
        Assert.Equal("10000 Reasons", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsAll()
    {
        AddTestSong("Song One", "Artist", "C");
        AddTestSong("Song Two", "Artist", "D");

        var results = await _service.SearchAsync("");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_NoMatches_ReturnsEmpty()
    {
        AddTestSong("Amazing Grace", "John Newton", "G");

        var results = await _service.SearchAsync("nonexistent");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FilterByKeyAsync_MatchingKey_ReturnsFiltered()
    {
        AddTestSong("Song in G", "Artist", "G");
        AddTestSong("Song in C", "Artist", "C");
        AddTestSong("Another in G", "Artist", "G");

        var results = await _service.FilterByKeyAsync("G");

        Assert.Equal(2, results.Count);
        Assert.All(results, item => Assert.Equal("G", item.Key));
    }

    [Fact]
    public async Task FilterByKeyAsync_NoMatch_ReturnsEmpty()
    {
        AddTestSong("Song in G", "Artist", "G");

        var results = await _service.FilterByKeyAsync("Bb");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FilterByKeyAsync_CaseInsensitive()
    {
        AddTestSong("Song", "Artist", "G");

        var results = await _service.FilterByKeyAsync("g");

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_ByLyrics_FindsMatch()
    {
        var song = AddTestSong("How Great", "Artist", "C");
        song.Sections.Add(new Section { Name = "Verse 1", Text = "Then sings my soul" });

        var results = await _service.SearchAsync("sings my soul");

        Assert.Single(results);
    }

    [Fact]
    public async Task GetByIdAsync_ValidId_ReturnsSong()
    {
        var song = AddTestSong("Test Song", "Artist", "C");

        var result = await _service.GetByIdAsync(song.FolderPath);

        Assert.NotNull(result);
        Assert.Equal("Test Song", result!.Title);
    }

    [Fact]
    public async Task GetByIdAsync_InvalidId_ReturnsNull()
    {
        var result = await _service.GetByIdAsync("nonexistent-id");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSong()
    {
        var song = AddTestSong("ToDelete", "Artist", "C");

        await _service.DeleteAsync(song.FolderPath);

        var all = await _service.GetAllAsync();
        Assert.DoesNotContain(all, s => s.Title == "ToDelete");
    }

    [Fact]
    public async Task RecordUsageAsync_StoresRecord()
    {
        var record = new SongUsageRecord
        {
            SongTitle = "Test Song",
            SongId = "test-id",
            ServiceName = "Sunday AM"
        };

        await _service.RecordUsageAsync(record);

        var history = await _service.GetUsageHistoryAsync(
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(1));

        Assert.Single(history);
    }

    [Fact]
    public async Task UpdateMetadataAsync_PersistsMetadata_PreservesSections()
    {
        // (a) Create a song on disk with a known section (lyrics), then save song.json.
        var folder = Path.Combine(_tempFolder, "Songs", "Then_Sings");
        Directory.CreateDirectory(folder);
        var song = new Song
        {
            Title = "Old Title",
            Artist = "Old Artist",
            OriginalKey = "C",
            Bpm = 100,
            FolderPath = folder
        };
        song.Sections.Add(new Section { Name = "Verse 1", Text = "Then sings my soul" });
        _songManager.Songs.Add(song);
        await _songManager.SaveSongAsync(song);

        // (b) Update metadata via the service save path.
        await _service.UpdateMetadataAsync(new WorshipPlayer.Core.Models.Cloud.SongLibraryItem
        {
            Id = folder,
            Title = "New Title",
            Artist = "New Artist",
            Key = "D",
            Bpm = 140,
            CcliNumber = "7050546",
            Tags = new List<string> { "Grace", "Easter" }
        });

        // (c) Reload from disk via a fresh SongManager on the same folders.
        var reloadManager = new SongManager(
            Path.Combine(_tempFolder, "Songs"),
            Path.Combine(_tempFolder, "Setlists"));
        await reloadManager.LoadAllSongsAsync();

        var reloaded = reloadManager.Songs.Single(s => Path.GetFileName(s.FolderPath) == "Then_Sings");

        // (d) Metadata changed...
        Assert.Equal("New Title", reloaded.Title);
        Assert.Equal("New Artist", reloaded.Artist);
        Assert.Equal("D", reloaded.OriginalKey);
        Assert.Equal(140, reloaded.Bpm);
        Assert.Equal("7050546", reloaded.Ccli);
        Assert.Contains("Grace", reloaded.Themes);
        Assert.Contains("Easter", reloaded.Themes);

        // ...and sections/lyrics preserved.
        var section = Assert.Single(reloaded.Sections);
        Assert.Equal("Verse 1", section.Name);
        Assert.Equal("Then sings my soul", section.Text);
    }

    private Song AddTestSong(string title, string artist, string key)
    {
        var folder = Path.Combine(_tempFolder, "Songs", title.Replace(" ", "_"));
        var song = new Song
        {
            Title = title,
            Artist = artist,
            OriginalKey = key,
            FolderPath = folder
        };
        _songManager.Songs.Add(song);
        return song;
    }
}
