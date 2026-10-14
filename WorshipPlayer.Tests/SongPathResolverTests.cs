using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for SongPathResolver — the song-resolution/dedup logic behind the
/// Songs-tab selection fix (Bug B). Cross-platform (net8.0, no WPF).
/// </summary>
public class SongPathResolverTests
{
    [Fact]
    public void PathsEqual_IgnoresTrailingSeparator()
    {
        var root = Path.Combine(Path.GetTempPath(), "Songs", "TheBlessing");
        Assert.True(SongPathResolver.PathsEqual(root, root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void PathsEqual_IgnoresCase()
    {
        var a = Path.Combine(Path.GetTempPath(), "Songs", "TheBlessing");
        var b = a.ToUpperInvariant();
        Assert.True(SongPathResolver.PathsEqual(a, b));
    }

    [Fact]
    public void PathsEqual_DifferentPaths_NotEqual()
    {
        var a = Path.Combine(Path.GetTempPath(), "Songs", "TheBlessing");
        var b = Path.Combine(Path.GetTempPath(), "Songs", "OpenTheHeavens");
        Assert.False(SongPathResolver.PathsEqual(a, b));
    }

    [Fact]
    public void Resolve_ByExactFolderPath_ReturnsSong()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Songs", "TheBlessing");
        var song = new Song { Title = "The Blessing", FolderPath = folder };

        var result = SongPathResolver.Resolve(new[] { song }, folder);

        Assert.Same(song, result);
    }

    [Fact]
    public void Resolve_ByCaseAndSeparatorDifferentPath_StillResolves()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Songs", "TheBlessing");
        var song = new Song { Title = "The Blessing", FolderPath = folder };

        // Id differs only by casing + trailing separator — the exact symptom of
        // folder-scan vs. registry path mismatch on Windows.
        var id = folder.ToUpperInvariant() + Path.DirectorySeparatorChar;

        var result = SongPathResolver.Resolve(new[] { song }, id);

        Assert.Same(song, result);
    }

    [Fact]
    public void Resolve_PathFails_FallsBackToTitle()
    {
        var song = new Song
        {
            Title = "The Blessing",
            FolderPath = Path.Combine(Path.GetTempPath(), "Songs", "TheBlessing")
        };

        var result = SongPathResolver.Resolve(new[] { song }, "no-such-path", "The Blessing");

        Assert.Same(song, result);
    }

    [Fact]
    public void Resolve_ByFolderName_ReturnsSong()
    {
        var song = new Song
        {
            Title = "The Blessing",
            FolderPath = Path.Combine(Path.GetTempPath(), "Songs", "TheBlessing")
        };

        var result = SongPathResolver.Resolve(new[] { song }, "TheBlessing");

        Assert.Same(song, result);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        var song = new Song
        {
            Title = "The Blessing",
            FolderPath = Path.Combine(Path.GetTempPath(), "Songs", "TheBlessing")
        };

        var result = SongPathResolver.Resolve(new[] { song }, "missing", "Also Missing");

        Assert.Null(result);
    }
}

/// <summary>
/// Verifies SongManager de-duplicates a song that is present both as a folder-scan
/// result and as a registry entry whose stored path differs only by casing — the
/// root cause of the duplicate "TheBlessing" listing (Bug B).
/// </summary>
public class SongManagerDedupTests : IDisposable
{
    private readonly string _root;

    public SongManagerDedupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"SongDedup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public async Task LoadAllSongs_SongInFolderAndRegistry_LoadedOnce()
    {
        var songsFolder = Path.Combine(_root, "Songs");
        var setlistsFolder = Path.Combine(_root, "Setlists");
        Directory.CreateDirectory(songsFolder);
        Directory.CreateDirectory(setlistsFolder);

        // Create a song folder with song.json (discovered by folder scan).
        var songFolder = Path.Combine(songsFolder, "TheBlessing");
        Directory.CreateDirectory(songFolder);
        await File.WriteAllTextAsync(
            Path.Combine(songFolder, "song.json"),
            "{\"title\":\"The Blessing\"}");

        // Registry references the SAME folder but with upper-cased path (casing-only diff).
        await File.WriteAllTextAsync(
            Path.Combine(songsFolder, "_song_registry.json"),
            System.Text.Json.JsonSerializer.Serialize(new[] { songFolder.ToUpperInvariant() }));

        var manager = new SongManager(songsFolder, setlistsFolder);
        await manager.LoadAllSongsAsync();

        Assert.Single(manager.Songs);
        Assert.Equal("The Blessing", manager.Songs[0].Title);
    }
}
