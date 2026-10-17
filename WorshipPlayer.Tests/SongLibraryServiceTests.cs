using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for SongLibraryService.
/// Tests search (case-insensitive), filter by key, and sorting.
/// SongUsageService now persists to a temp SQLite app.db (unchanged public API).
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
        var dbPath = Path.Combine(_tempFolder, "app.db");

        Directory.CreateDirectory(songsFolder);
        Directory.CreateDirectory(setlistsFolder);

        _songManager = new SongManager(songsFolder, setlistsFolder);
        _usageService = new SongUsageService(dbPath);
        _service = new SongLibraryService(_songManager, _usageService);
    }

    public void Dispose()
    {
        // Release SQLite file handles so the temp .db under _tempFolder can be deleted on Windows.
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempFolder))
        {
            try
            {
                Directory.Delete(_tempFolder, true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp folder.
            }
        }
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

    [Fact]
    public async Task UpdateSlidesAsync_PersistsLyricEdit_PreservesChordsTimestampsMetadata()
    {
        // (a) Create a song on disk with metadata, chords/timestamps inline in the lyrics,
        //     a non-null ChordChart, a MIDI cue, and loop/bar structure on each section.
        var folder = Path.Combine(_tempFolder, "Songs", "Grace_Flows");
        Directory.CreateDirectory(folder);
        var song = new Song
        {
            Title = "Grace Flows Down",
            Artist = "Christy Nockels",
            OriginalKey = "G",
            Bpm = 72,
            Ccli = "3064143",
            Themes = "Grace, Communion",
            ChordChart = new ChordChartData(),
            FolderPath = folder,
        };
        song.MidiCues.Add(new MidiCue { TimestampMs = 1500, Action = MidiCueAction.NextSlide });
        song.Sections.Add(new Section
        {
            Name = "Verse 1",
            Text = "[G]Amazing {0}grace how [C]sweet the sound",
            LoopInMs = 1000,
            LoopOutMs = 5000,
            StartBar = 1,
            EndBar = 8,
            Color = "#AA3366",
            VocalPickupMs = 250,
        });
        song.Sections.Add(new Section
        {
            Name = "Chorus",
            Text = "[D]Grace flows {12000}down and [G]covers me",
            LoopInMs = 5000,
            LoopOutMs = 9000,
            StartBar = 9,
            EndBar = 16,
            CanLoop = false,
        });
        _songManager.Songs.Add(song);
        await _songManager.SaveSongAsync(song);

        // (b) Edit slide 1's lyric text and set per-slide background + template via the service.
        var edited = new List<Section>
        {
            new Section
            {
                Text = "[G]Amazing {0}grace, my chains are gone",
                BackgroundImage = "C:/media/sunrise.jpg",
                TemplateId = "trendy-line",
            },
            new Section
            {
                // Chorus unchanged — proves the untouched section's chords/timestamps survive.
                Text = "[D]Grace flows {12000}down and [G]covers me",
            },
        };
        await _service.UpdateSlidesAsync(folder, edited);

        // (c) Reload from disk via a fresh SongManager.
        var reloadManager = new SongManager(
            Path.Combine(_tempFolder, "Songs"),
            Path.Combine(_tempFolder, "Setlists"));
        await reloadManager.LoadAllSongsAsync();
        var reloaded = reloadManager.Songs.Single(s => Path.GetFileName(s.FolderPath) == "Grace_Flows");

        // (d) Edited lyric changed; per-slide background/template round-tripped.
        Assert.Equal(2, reloaded.Sections.Count);
        var v1 = reloaded.Sections[0];
        Assert.Equal("[G]Amazing {0}grace, my chains are gone", v1.Text);
        Assert.Equal("C:/media/sunrise.jpg", v1.BackgroundImage);
        Assert.Equal("trendy-line", v1.TemplateId);

        // (e) Inline chord [...] and timestamp {...} markers in the UNEDITED section are intact.
        var chorus = reloaded.Sections[1];
        Assert.Contains("[D]", chorus.Text);
        Assert.Contains("{12000}", chorus.Text);
        Assert.Contains("[G]", chorus.Text);

        // (f) Section structure preserved (loop points, bars, color, vocal pickup, CanLoop).
        Assert.Equal(1000, v1.LoopInMs);
        Assert.Equal(5000, v1.LoopOutMs);
        Assert.Equal(1, v1.StartBar);
        Assert.Equal(8, v1.EndBar);
        Assert.Equal("#AA3366", v1.Color);
        Assert.Equal(250, v1.VocalPickupMs);
        Assert.False(chorus.CanLoop);

        // (g) Chords/MIDI/metadata preserved.
        Assert.NotNull(reloaded.ChordChart);
        Assert.Single(reloaded.MidiCues);
        Assert.Equal("Grace Flows Down", reloaded.Title);
        Assert.Equal("Christy Nockels", reloaded.Artist);
        Assert.Equal("G", reloaded.OriginalKey);
        Assert.Equal(72, reloaded.Bpm);
        Assert.Equal("3064143", reloaded.Ccli);
        Assert.Equal("Grace, Communion", reloaded.Themes);
    }

    [Fact]
    public void SectionDeserialization_WithoutNewFields_IsBackwardCompatible()
    {
        // A song.json written BEFORE the additive fields existed must still load:
        // BackgroundImage/TemplateId default to null and deserialization does not throw.
        const string legacyJson = """
        {
            "name": "Verse 1",
            "loopIn_ms": 1000,
            "loopOut_ms": 5000,
            "startBar": 1,
            "endBar": 8,
            "text": "[G]Amazing grace"
        }
        """;

        var section = System.Text.Json.JsonSerializer.Deserialize<Section>(legacyJson);

        Assert.NotNull(section);
        Assert.Equal("Verse 1", section!.Name);
        Assert.Equal("[G]Amazing grace", section.Text);
        Assert.Null(section.BackgroundImage);
        Assert.Null(section.TemplateId);
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
