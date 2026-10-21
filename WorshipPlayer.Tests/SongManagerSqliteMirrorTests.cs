using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Verifies the Phase-6 SQLite MIRROR that SongManager keeps in app.db for the Songs and
/// Setlists stores. The on-disk song.json / setlist *.json files stay authoritative; app.db is a
/// reconciled mirror keyed by SongPathResolver.Normalize of the (folder/file) path. These tests
/// assert the one-time import is non-duplicating on a second load and that the mirror key is the
/// normalized FolderPath (a case/separator variant upserts the SAME row).
/// </summary>
public class SongManagerSqliteMirrorTests : IDisposable
{
    private readonly string _root;
    private readonly string _songsDir;
    private readonly string _setlistsDir;
    private readonly string _appDbPath;

    // Mirror DataJson is written with a JsonStringEnumConverter (TrackOutput / TransitionType are
    // serialized as strings), so round-trip deserialization must opt into the same converter.
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SongManagerSqliteMirrorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"owc-sqlite-mirror-{Guid.NewGuid():N}");
        _songsDir = Path.Combine(_root, "Songs");
        _setlistsDir = Path.Combine(_root, "Setlists");
        _appDbPath = Path.Combine(_root, "app.db");
        Directory.CreateDirectory(_songsDir);
        Directory.CreateDirectory(_setlistsDir);
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
        catch (IOException)
        {
            // Best-effort cleanup of a temp folder.
        }
    }

    [Fact]
    public async Task ImportsOnDiskSongsAndSetlists_IntoMirror_NonDuplicating()
    {
        // Arrange: two song folders (metadata + a section + a track) and one setlist file.
        WriteSong("Amazing_Grace", "Amazing Grace", "John Newton", "G");
        WriteSong("Grace_Flows", "Grace Flows Down", "Christy Nockels", "D");

        var setlist = new Setlist
        {
            Name = "Sunday Morning",
            Date = "2024-01-07",
            Entries =
            {
                new SetlistEntry { SongFolder = "Amazing_Grace", KeyShift = 0 },
                new SetlistEntry { SongFolder = "Grace_Flows", KeyShift = 2 },
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(_setlistsDir, "Sunday Morning.json"),
            JsonSerializer.Serialize(setlist));

        // Act: first load imports the on-disk files into the mirror.
        var manager = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await manager.LoadAllSongsAsync();
        await manager.LoadAllSetlistsAsync();

        // Assert: one mirror row per song and per setlist.
        Assert.Equal(2, CountRows("Songs"));
        Assert.Equal(1, CountRows("Setlists"));

        // A Song DataJson round-trips with its section and track intact.
        var json = ReadSingleDataJson("Songs", "Title", "Amazing Grace");
        var roundTrip = JsonSerializer.Deserialize<Song>(json, ReadOptions);
        Assert.NotNull(roundTrip);
        Assert.Equal("Amazing Grace", roundTrip!.Title);
        Assert.Equal("John Newton", roundTrip.Artist);
        var section = Assert.Single(roundTrip.Sections);
        Assert.Equal("Verse 1", section.Name);
        Assert.Equal("Amazing grace how sweet the sound", section.Text);
        var track = Assert.Single(roundTrip.Tracks);
        Assert.Equal("click.wav", track.FileName);
        Assert.Equal(TrackOutput.IEM, track.Output);

        // A SECOND manager over the SAME folders + db loads again without duplicating rows.
        var manager2 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await manager2.LoadAllSongsAsync();
        await manager2.LoadAllSetlistsAsync();

        Assert.Equal(2, CountRows("Songs"));
        Assert.Equal(1, CountRows("Setlists"));
    }

    [Fact]
    public async Task MirrorKey_IsNormalizedFolderPath_UpsertNotDuplicate()
    {
        var folder = Path.Combine(_songsDir, "The_Blessing");
        Directory.CreateDirectory(folder);

        var manager = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        var song = new Song
        {
            Title = "The Blessing",
            Artist = "Kari Jobe",
            OriginalKey = "B",
            FolderPath = folder
        };
        manager.Songs.Add(song);
        await manager.SaveSongAsync(song);

        // Save again with a case/separator variant of the SAME folder path.
        song.FolderPath = folder.ToUpperInvariant() + Path.DirectorySeparatorChar;
        await manager.SaveSongAsync(song);

        // The variant must upsert the SAME row, not create a second one.
        Assert.Equal(1, CountRows("Songs"));

        // And the row's key is the normalized folder path (FolderPath identity contract).
        var storedKey = ReadSingleColumn("Songs", "FolderPath");
        Assert.Equal(SongPathResolver.Normalize(folder), storedKey);
    }

    // ─── SQL-AUTHORITATIVE cutover (song.json is now a BACKUP, app.db is the source of truth) ──

    [Fact]
    public async Task Load_ReadsFromDb_AndSurvivesDeletedSongJsonBackup()
    {
        // Seed one song via its song.json backup, then first-load imports it into app.db.
        WriteSong("Amazing_Grace", "Amazing Grace", "John Newton", "G");
        var folder = Path.Combine(_songsDir, "Amazing_Grace");

        var m1 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await m1.LoadAllSongsAsync();
        Assert.Single(m1.Songs);
        Assert.Equal("Amazing Grace", m1.Songs[0].Title);
        Assert.Equal(1, CountRows("Songs"));

        // Delete the song.json BACKUP. Because SQL is now authoritative, the song must STILL load.
        File.Delete(Path.Combine(folder, "song.json"));
        Assert.False(File.Exists(Path.Combine(folder, "song.json")));

        var m2 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await m2.LoadAllSongsAsync();
        Assert.Single(m2.Songs); // loaded from app.db, not the (deleted) json
        Assert.Equal("Amazing Grace", m2.Songs[0].Title);
        Assert.Equal(folder, m2.Songs[0].FolderPath, ignoreCase: true);
    }

    [Fact]
    public async Task Save_PersistsToDb_AndRewritesJsonBackup()
    {
        WriteSong("Amazing_Grace", "Amazing Grace", "John Newton", "G");
        var folder = Path.Combine(_songsDir, "Amazing_Grace");

        var m1 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await m1.LoadAllSongsAsync();
        var song = m1.Songs[0];

        // Edit + save. The DB is authoritative; the json backup should also be refreshed.
        song.Bpm = 123.45;
        song.Title = "Amazing Grace (edited)";
        await m1.SaveSongAsync(song);

        // json backup rewritten with the new value.
        var backup = await File.ReadAllTextAsync(Path.Combine(folder, "song.json"));
        Assert.Contains("123.45", backup);
        Assert.Contains("Amazing Grace (edited)", backup);

        // A fresh manager (DB-authoritative load) reflects the saved change.
        var m2 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await m2.LoadAllSongsAsync();
        var reloaded = Assert.Single(m2.Songs);
        Assert.Equal("Amazing Grace (edited)", reloaded.Title);
        Assert.Equal(123.45, reloaded.Bpm, 2);
    }

    [Fact]
    public async Task Remove_DeletesDbRow_SongDoesNotReappearOnReload()
    {
        WriteSong("Amazing_Grace", "Amazing Grace", "John Newton", "G");
        WriteSong("Grace_Flows", "Grace Flows Down", "Christy Nockels", "D");

        var m1 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await m1.LoadAllSongsAsync();
        Assert.Equal(2, m1.Songs.Count);

        var toRemove = m1.Songs.First(s => s.Title == "Amazing Grace");
        await m1.RemoveSongAsync(toRemove, deleteFiles: false);

        Assert.Single(m1.Songs);
        Assert.Equal(1, CountRows("Songs")); // row deleted from the authoritative table

        // Even though the song.json backup still exists on disk, the removed song must NOT
        // reappear — the DB is authoritative and the row is gone.
        Assert.True(File.Exists(Path.Combine(_songsDir, "Amazing_Grace", "song.json")));
        var m2 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await m2.LoadAllSongsAsync();
        var remaining = Assert.Single(m2.Songs);
        Assert.Equal("Grace Flows Down", remaining.Title);
    }

    [Fact]
    public async Task Load_SkipsSongsWhoseFolderNoLongerExists()
    {
        WriteSong("Amazing_Grace", "Amazing Grace", "John Newton", "G");
        var folder = Path.Combine(_songsDir, "Amazing_Grace");

        var m1 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await m1.LoadAllSongsAsync();
        Assert.Single(m1.Songs);

        // Remove the whole folder (audio stems gone). The DB row still exists, but the song can't
        // play, so a reload must NOT surface it.
        Directory.Delete(folder, recursive: true);

        var m2 = new SongManager(_songsDir, _setlistsDir, _appDbPath);
        await m2.LoadAllSongsAsync();
        Assert.Empty(m2.Songs);
    }

    private void WriteSong(string folderName, string title, string artist, string key)
    {
        var folder = Path.Combine(_songsDir, folderName);
        Directory.CreateDirectory(folder);
        var song = new Song
        {
            Title = title,
            Artist = artist,
            OriginalKey = key,
        };
        song.Sections.Add(new Section { Name = "Verse 1", Text = "Amazing grace how sweet the sound" });
        song.Tracks.Add(new TrackInfo { FileName = "click.wav", Label = "Click", Output = TrackOutput.IEM });

        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(Path.Combine(folder, "song.json"), JsonSerializer.Serialize(song, options));
    }

    private int CountRows(string table)
    {
        using var connection = new SqliteConnection($"Data Source={_appDbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private string ReadSingleColumn(string table, string column)
    {
        using var connection = new SqliteConnection($"Data Source={_appDbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM {table}";
        return (string)cmd.ExecuteScalar()!;
    }

    private string ReadSingleDataJson(string table, string whereColumn, string whereValue)
    {
        using var connection = new SqliteConnection($"Data Source={_appDbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT DataJson FROM {table} WHERE {whereColumn} = @v";
        cmd.Parameters.AddWithValue("@v", whereValue);
        return (string)cmd.ExecuteScalar()!;
    }
}
