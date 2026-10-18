using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// End-to-end test for the ChordPro import-and-persist path exercised by the
/// "Import ChordPro…" entry point: SongLibraryService.ImportChordProAsync parses
/// the file, creates a Song, and persists it via SongManager.SaveSongAsync (song.json).
/// </summary>
public class ChordProImportTests : IDisposable
{
    private readonly string _root;
    private readonly string _songsDir;
    private readonly string _setlistsDir;
    private readonly string _usagePath;
    private readonly string _appDbPath;

    public ChordProImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "owc-chordpro-test-" + Guid.NewGuid().ToString("N"));
        _songsDir = Path.Combine(_root, "Songs");
        _setlistsDir = Path.Combine(_root, "Setlists");
        _usagePath = Path.Combine(_root, "usage.json");
        _appDbPath = Path.Combine(_root, "app.db");
        Directory.CreateDirectory(_songsDir);
        Directory.CreateDirectory(_setlistsDir);
    }

    [Fact]
    public async Task ImportChordProAsync_CreatesAndPersistsSong_WithChordsAndSections()
    {
        // Arrange: a SongManager seeded with one existing song so ImportChordProAsync can
        // derive the new song's folder from an existing song's folder (SongLibraryService.cs).
        var songManager = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        var seedFolder = Path.Combine(_songsDir, "Seed Song");
        Directory.CreateDirectory(seedFolder);
        songManager.Songs.Add(new Song { Title = "Seed Song", FolderPath = seedFolder });

        var usageService = new SongUsageService(_usagePath);
        var service = new SongLibraryService(songManager, usageService);

        var choproPath = Path.Combine(_root, "test.chopro");
        await File.WriteAllTextAsync(choproPath,
            "{title: Amazing Grace}\n" +
            "{artist: John Newton}\n" +
            "{key: G}\n" +
            "{start_of_verse}\n" +
            "[G]Amazing [C]grace how [G]sweet the sound\n" +
            "{end_of_verse}\n");

        // Act
        var item = await service.ImportChordProAsync(choproPath);

        // Assert: returned metadata matches directives
        Assert.Equal("Amazing Grace", item.Title);
        Assert.Equal("John Newton", item.Artist);
        Assert.Equal("G", item.Key);

        // In-memory list now contains the imported song
        var saved = Assert.Single(songManager.Songs, s => s.Title == "Amazing Grace");
        Assert.NotEmpty(saved.Sections);
        Assert.Contains(saved.Sections, sec => sec.Text.Contains("[") && sec.Text.Contains("]"));

        // Persisted song.json exists on disk
        var songJson = Path.Combine(saved.FolderPath, "song.json");
        Assert.True(File.Exists(songJson), $"Expected song.json at {songJson}");

        // The persisted file round-trips to a Song with the same title and non-empty sections
        var json = await File.ReadAllTextAsync(songJson);
        var roundTrip = JsonSerializer.Deserialize<Song>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        Assert.NotNull(roundTrip);
        Assert.Equal("Amazing Grace", roundTrip!.Title);
        Assert.NotEmpty(roundTrip.Sections);
    }

    public void Dispose()
    {
        // Release SQLite file handles so the temp .db under _root can be deleted on Windows.
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
