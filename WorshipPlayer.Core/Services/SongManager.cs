using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages loading, saving, and organizing songs and setlists.
/// </summary>
/// <remarks>
/// Phase 6 SQL migration: Songs now ALSO live in the shared SQLite application database
/// (app.db, <c>Songs</c> table) via <see cref="AppDatabase"/>, following the Phase-5
/// <see cref="ServiceOrderManager"/> MIRROR pattern (no second helper, no EF; bible.db stays
/// separate). The on-disk <c>song.json</c> files, <c>song.json.N</c> backups, and
/// <c>_song_registry.json</c> REMAIN authoritative and are written/read exactly as before.
/// SQLite is a reconciled MIRROR keyed by <see cref="SongPathResolver.Normalize"/> of the song's
/// folder path: <see cref="SaveSongAsync"/> upserts a row after writing the authoritative
/// song.json, and <see cref="LoadAllSongsAsync"/> reconciles the table against the in-memory list
/// (upsert present, prune vanished). All mirror DB work is best-effort/try-catch-swallow so a DB
/// failure never changes the file-based behavior or the in-memory <see cref="Songs"/> list. On
/// first init, if the table is empty and on-disk song folders exist, they are imported once
/// (defensively, non-duplicating, never throwing); the JSON files are LEFT ON DISK.
/// </remarks>
public class SongManager
{
    private readonly string _songsBasePath;
    private readonly string _setlistsBasePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AppDatabase _database;
    private bool _initialized = false;

    private const string CreateSongsSchemaSql = @"
CREATE TABLE IF NOT EXISTS Songs (
    FolderPath    TEXT PRIMARY KEY,
    Title         TEXT NOT NULL DEFAULT '',
    Artist        TEXT NOT NULL DEFAULT '',
    OriginalKey   TEXT NOT NULL DEFAULT '',
    Ccli          TEXT NOT NULL DEFAULT '',
    Themes        TEXT NOT NULL DEFAULT '',
    Bpm           REAL NOT NULL DEFAULT 0,
    LastWriteUtc  TEXT NOT NULL DEFAULT '',
    DataJson      TEXT NOT NULL
);
";

    public List<Song> Songs { get; private set; } = new();
    public List<Setlist> Setlists { get; private set; } = new();

    public SongManager(string songsBasePath, string setlistsBasePath, string? databasePath = null)
    {
        _songsBasePath = songsBasePath;
        _setlistsBasePath = setlistsBasePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
        _database = new AppDatabase(databasePath ?? AppDatabase.DefaultPath);
    }

    /// <summary>
    /// Scan the songs folder and load all song configurations.
    /// </summary>
    public async Task LoadAllSongsAsync()
    {
        Songs.Clear();

        if (!Directory.Exists(_songsBasePath))
        {
            Directory.CreateDirectory(_songsBasePath);
            return;
        }

        // Scan subdirectories of the songs base path
        foreach (var dir in Directory.GetDirectories(_songsBasePath))
        {
            var songJsonPath = Path.Combine(dir, "song.json");
            if (File.Exists(songJsonPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(songJsonPath);
                    var song = JsonSerializer.Deserialize<Song>(json, _jsonOptions);
                    if (song != null && !Songs.Any(s => SongPathResolver.PathsEqual(s.FolderPath, dir)))
                    {
                        song.FolderPath = dir;
                        Songs.Add(song);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading song from {dir}: {ex.Message}");
                }
            }
        }

        // Also load songs from the registry (songs added from outside the base path)
        await LoadSongsFromRegistryAsync();

        // Reconcile the SQLite mirror with the songs that ended up in the in-memory list
        // (dedup already applied above). Best-effort: the in-memory list and its ordering come
        // only from the folder scan + registry load; a DB failure here is swallowed.
        await ReconcileSongsSafeAsync(Songs);
    }

    /// <summary>
    /// Load songs listed in the song registry file (for songs outside the base path).
    /// </summary>
    private async Task LoadSongsFromRegistryAsync()
    {
        var registryPath = Path.Combine(_songsBasePath, "_song_registry.json");
        if (!File.Exists(registryPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(registryPath);
            var paths = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
            if (paths == null) return;

            foreach (var dir in paths)
            {
                if (!Directory.Exists(dir)) continue;
                // Skip if already loaded (it's a subdirectory of base path).
                // Compare by normalized path so a registry entry that differs only by
                // drive-letter case / trailing separator is still treated as a duplicate.
                if (Songs.Any(s => SongPathResolver.PathsEqual(s.FolderPath, dir))) continue;

                var songJsonPath = Path.Combine(dir, "song.json");
                if (File.Exists(songJsonPath))
                {
                    try
                    {
                        var songJson = await File.ReadAllTextAsync(songJsonPath);
                        var song = JsonSerializer.Deserialize<Song>(songJson, _jsonOptions);
                        if (song != null)
                        {
                            song.FolderPath = dir;
                            Songs.Add(song);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Register a song path in the registry (for songs outside the base folder).
    /// </summary>
    public async Task RegisterSongPathAsync(string folderPath)
    {
        var registryPath = Path.Combine(_songsBasePath, "_song_registry.json");
        List<string> paths = new();

        if (File.Exists(registryPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(registryPath);
                paths = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions) ?? new();
            }
            catch { }
        }

        if (!paths.Contains(folderPath))
        {
            paths.Add(folderPath);
            var json = JsonSerializer.Serialize(paths, _jsonOptions);
            await File.WriteAllTextAsync(registryPath, json);
        }
    }

    /// <summary>
    /// Save a song's configuration to its folder.
    /// Auto-backs up the previous version before overwriting (keeps last 5).
    /// </summary>
    public async Task SaveSongAsync(Song song)
    {
        if (string.IsNullOrEmpty(song.FolderPath))
            throw new InvalidOperationException("Song folder path is not set.");

        var songJsonPath = Path.Combine(song.FolderPath, "song.json");

        // Auto-backup before overwriting
        if (File.Exists(songJsonPath))
        {
            BackupSongJson(songJsonPath);
        }

        var json = JsonSerializer.Serialize(song, _jsonOptions);
        await File.WriteAllTextAsync(songJsonPath, json);

        // Mirror the saved song into app.db keyed by the normalized folder path (= identity).
        // Best-effort: song.json is authoritative and already written above.
        await UpsertSongRowSafeAsync(song);
    }

    /// <summary>
    /// Create a backup of song.json before overwriting. Keeps last 5 versions.
    /// Backups are named: song.json.1 (most recent) → song.json.5 (oldest)
    /// </summary>
    private void BackupSongJson(string songJsonPath)
    {
        try
        {
            string dir = Path.GetDirectoryName(songJsonPath) ?? "";
            const int maxBackups = 5;

            // Rotate existing backups: .4→.5, .3→.4, .2→.3, .1→.2
            for (int i = maxBackups - 1; i >= 1; i--)
            {
                string older = Path.Combine(dir, $"song.json.{i}");
                string newer = Path.Combine(dir, $"song.json.{i + 1}");
                if (File.Exists(older))
                {
                    if (File.Exists(newer))
                        File.Delete(newer);
                    File.Move(older, newer);
                }
            }

            // Copy current song.json to song.json.1
            string backup1 = Path.Combine(dir, "song.json.1");
            File.Copy(songJsonPath, backup1, overwrite: true);
        }
        catch { } // Non-critical — don't fail the save if backup fails
    }

    /// <summary>
    /// Restore a song from a specific backup version.
    /// </summary>
    public async Task<Song?> RestoreBackupAsync(string songFolderPath, int version = 1)
    {
        string backupPath = Path.Combine(songFolderPath, $"song.json.{version}");
        if (!File.Exists(backupPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(backupPath);
            var song = JsonSerializer.Deserialize<Song>(json, _jsonOptions);
            if (song != null)
                song.FolderPath = songFolderPath;
            return song;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get list of available backup versions for a song.
    /// Returns list of (version, lastModified) tuples.
    /// </summary>
    public List<(int version, DateTime modified)> GetAvailableBackups(string songFolderPath)
    {
        var backups = new List<(int, DateTime)>();
        for (int i = 1; i <= 5; i++)
        {
            string path = Path.Combine(songFolderPath, $"song.json.{i}");
            if (File.Exists(path))
            {
                backups.Add((i, File.GetLastWriteTime(path)));
            }
        }
        return backups;
    }

    /// <summary>
    /// Create a new song from a folder of audio stems.
    /// </summary>
    public Song CreateSongFromFolder(string folderPath, string title)
    {
        var song = new Song
        {
            Title = title,
            FolderPath = folderPath
        };

        // Auto-detect audio files
        var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac" };
        var audioFiles = Directory.GetFiles(folderPath)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLower()))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in audioFiles)
        {
            var fileName = Path.GetFileName(file);
            var label = GuessTrackLabel(fileName);
            var output = label.ToLower().Contains("click") || label.ToLower().Contains("cue")
                ? TrackOutput.IEM
                : TrackOutput.FOH;

            song.Tracks.Add(new TrackInfo
            {
                FileName = fileName,
                Label = label,
                Output = output,
                DefaultVolume = 1.0f,
                DefaultMute = false
            });
        }

        Songs.Add(song);
        return song;
    }

    /// <summary>
    /// Load all setlists from the setlists folder.
    /// </summary>
    public async Task LoadAllSetlistsAsync()
    {
        Setlists.Clear();

        if (!Directory.Exists(_setlistsBasePath))
        {
            Directory.CreateDirectory(_setlistsBasePath);
            return;
        }

        foreach (var file in Directory.GetFiles(_setlistsBasePath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var setlist = JsonSerializer.Deserialize<Setlist>(json, _jsonOptions);
                if (setlist != null)
                    Setlists.Add(setlist);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading setlist from {file}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Save a setlist to file.
    /// </summary>
    public async Task SaveSetlistAsync(Setlist setlist)
    {
        if (!Directory.Exists(_setlistsBasePath))
            Directory.CreateDirectory(_setlistsBasePath);

        var fileName = SanitizeFileName(setlist.Name) + ".json";
        var filePath = Path.Combine(_setlistsBasePath, fileName);
        var json = JsonSerializer.Serialize(setlist, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Delete a song and optionally its files.
    /// </summary>
    public void RemoveSong(Song song, bool deleteFiles = false)
    {
        Songs.Remove(song);
        if (deleteFiles && Directory.Exists(song.FolderPath))
        {
            Directory.Delete(song.FolderPath, true);
        }
    }

    /// <summary>
    /// Guess a track label from the filename.
    /// </summary>
    private static string GuessTrackLabel(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Remove leading numbers and underscores
        var cleaned = System.Text.RegularExpressions.Regex.Replace(name, @"^\d+[_\-\s]*", "");

        // Common patterns
        if (cleaned.Contains("Click", StringComparison.OrdinalIgnoreCase)) return "Click";
        if (cleaned.Contains("Cue", StringComparison.OrdinalIgnoreCase)) return "Cues";
        if (cleaned.Contains("Drum", StringComparison.OrdinalIgnoreCase)) return "Drums";
        if (cleaned.Contains("Bass", StringComparison.OrdinalIgnoreCase)) return "Bass";
        if (cleaned.Contains("AcousticGuitar", StringComparison.OrdinalIgnoreCase)) return "Acoustic Guitar";
        if (cleaned.Contains("ElectricGuitar", StringComparison.OrdinalIgnoreCase)) return "Electric Guitar";
        if (cleaned.Contains("Guitar", StringComparison.OrdinalIgnoreCase)) return "Guitar";
        if (cleaned.Contains("Piano", StringComparison.OrdinalIgnoreCase)) return "Piano";
        if (cleaned.Contains("Keys", StringComparison.OrdinalIgnoreCase)) return "Keys";
        if (cleaned.Contains("Synth", StringComparison.OrdinalIgnoreCase)) return "Synth";
        if (cleaned.Contains("Pad", StringComparison.OrdinalIgnoreCase)) return "Pad";
        if (cleaned.Contains("Lead", StringComparison.OrdinalIgnoreCase) &&
            cleaned.Contains("Vocal", StringComparison.OrdinalIgnoreCase)) return "Lead Vocal";
        if (cleaned.Contains("Vocal", StringComparison.OrdinalIgnoreCase)) return "Vocal";
        if (cleaned.Contains("BGV", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("BackingVocal", StringComparison.OrdinalIgnoreCase)) return "Backing Vocals";
        if (cleaned.Contains("Strings", StringComparison.OrdinalIgnoreCase)) return "Strings";
        if (cleaned.Contains("Organ", StringComparison.OrdinalIgnoreCase)) return "Organ";

        // Fall back to cleaned name
        return cleaned.Length > 0 ? cleaned : fileName;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }

    // ─── SQLite mirror (Phase 6) ──────────────────────────────────
    // The on-disk song.json files (plus song.json.N backups and _song_registry.json) are
    // authoritative; these helpers keep an app.db mirror in sync. Everything here is best-effort
    // and never allowed to surface an exception into the file-based public behavior or the
    // in-memory Songs list.

    /// <summary>
    /// Create the Songs schema (idempotent) and run the one-time song.json import once per
    /// process via the <see cref="_initialized"/> guard. Mirrors
    /// <see cref="ServiceOrderManager"/>'s EnsureInitializedAsync.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        _database.EnsureDirectory();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText = CreateSongsSchemaSql;
            await schemaCmd.ExecuteNonQueryAsync();
        }

        await ImportSongsIfNeededAsync(connection);

        _initialized = true;
    }

    /// <summary>
    /// Best-effort wrapper: ensure the schema exists then upsert the mirror row for a single
    /// song, swallowing any DB failure so the authoritative song.json path is unaffected.
    /// </summary>
    private async Task UpsertSongRowSafeAsync(Song song)
    {
        try
        {
            await EnsureInitializedAsync();
            using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            await UpsertSongRowAsync(connection, null, song);
        }
        catch
        {
            // song.json is the source of truth and is already written; a mirror failure must
            // never break the public behavior.
        }
    }

    /// <summary>
    /// Best-effort reconcile: upsert every loaded song and prune any mirror row whose FolderPath
    /// is no longer among the loaded set (absorbs out-of-band removals). Swallows any DB failure.
    /// </summary>
    private async Task ReconcileSongsSafeAsync(IReadOnlyList<Song> loaded)
    {
        try
        {
            await EnsureInitializedAsync();
            using var connection = _database.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            var keepKeys = new List<string>(loaded.Count);
            foreach (var song in loaded)
            {
                var normalized = SongPathResolver.Normalize(song.FolderPath);
                keepKeys.Add(normalized);
                await UpsertSongRowAsync(connection, transaction, song);
            }

            // Prune rows whose song is gone. Build a parameterized NOT IN list.
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                if (keepKeys.Count == 0)
                {
                    deleteCmd.CommandText = "DELETE FROM Songs";
                }
                else
                {
                    var paramNames = new List<string>(keepKeys.Count);
                    for (int i = 0; i < keepKeys.Count; i++)
                    {
                        var p = "@keep" + i;
                        paramNames.Add(p);
                        deleteCmd.Parameters.AddWithValue(p, keepKeys[i]);
                    }
                    deleteCmd.CommandText =
                        $"DELETE FROM Songs WHERE FolderPath NOT IN ({string.Join(",", paramNames)})";
                }
                await deleteCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            // song.json files are authoritative; a mirror reconcile failure is non-fatal.
        }
    }

    /// <summary>
    /// Insert or update the mirror row for a song keyed by its normalized folder path. Promotes
    /// the fields listing/search consume (Title, Artist, OriginalKey, Ccli, Themes, Bpm,
    /// LastWriteUtc) and keeps the whole song serialized in DataJson (with the manager's
    /// _jsonOptions) so it round-trips losslessly. Mirrors
    /// <see cref="ServiceOrderManager"/>'s UpsertRowAsync.
    /// </summary>
    private async Task UpsertSongRowAsync(SqliteConnection connection, SqliteTransaction? transaction, Song song)
    {
        var normalized = SongPathResolver.Normalize(song.FolderPath);

        using var cmd = connection.CreateCommand();
        if (transaction != null)
            cmd.Transaction = transaction;

        cmd.CommandText = @"
            INSERT INTO Songs (FolderPath, Title, Artist, OriginalKey, Ccli, Themes, Bpm, LastWriteUtc, DataJson)
            VALUES (@FolderPath, @Title, @Artist, @OriginalKey, @Ccli, @Themes, @Bpm, @LastWriteUtc, @DataJson)
            ON CONFLICT(FolderPath) DO UPDATE SET
                Title = excluded.Title,
                Artist = excluded.Artist,
                OriginalKey = excluded.OriginalKey,
                Ccli = excluded.Ccli,
                Themes = excluded.Themes,
                Bpm = excluded.Bpm,
                LastWriteUtc = excluded.LastWriteUtc,
                DataJson = excluded.DataJson";
        BindSong(cmd, song, normalized);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Bind a song's promoted columns + DataJson. LastWriteUtc comes from the folder's song.json
    /// when it exists else UtcNow, serialized ISO-8601 round-trip ('o').
    /// </summary>
    private void BindSong(SqliteCommand cmd, Song song, string normalizedKey)
    {
        cmd.Parameters.AddWithValue("@FolderPath", normalizedKey);
        cmd.Parameters.AddWithValue("@Title", song.Title ?? "");
        cmd.Parameters.AddWithValue("@Artist", song.Artist ?? "");
        cmd.Parameters.AddWithValue("@OriginalKey", song.OriginalKey ?? "");
        cmd.Parameters.AddWithValue("@Ccli", song.Ccli ?? "");
        cmd.Parameters.AddWithValue("@Themes", song.Themes ?? "");
        cmd.Parameters.AddWithValue("@Bpm", song.Bpm);

        var songJsonPath = string.IsNullOrEmpty(song.FolderPath)
            ? null
            : Path.Combine(song.FolderPath, "song.json");
        var lastWrite = (songJsonPath != null && File.Exists(songJsonPath))
            ? File.GetLastWriteTimeUtc(songJsonPath)
            : DateTime.UtcNow;
        cmd.Parameters.AddWithValue("@LastWriteUtc", lastWrite.ToString("o"));

        cmd.Parameters.AddWithValue("@DataJson", JsonSerializer.Serialize(song, _jsonOptions));
    }

    /// <summary>
    /// Defensive one-time import: if the Songs table is empty AND song folders exist under the
    /// base path (or are referenced by _song_registry.json), import each as a mirror row once.
    /// Tolerates missing folders, missing/malformed files (skips the single bad file and
    /// continues) — never throws. The empty-table guard makes this non-duplicating. JSON files
    /// are LEFT ON DISK and remain authoritative; backups and the registry file are untouched.
    /// Mirrors <see cref="ServiceOrderManager"/>'s ImportFilesIfNeededAsync.
    /// </summary>
    private async Task ImportSongsIfNeededAsync(SqliteConnection connection)
    {
        try
        {
            if (!Directory.Exists(_songsBasePath))
                return;

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM Songs";
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
                if (count > 0)
                    return;
            }

            using var transaction = connection.BeginTransaction();
            var inserted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) Subdirectories of the base path with a song.json.
            foreach (var dir in Directory.GetDirectories(_songsBasePath))
            {
                await ImportOneSongAsync(connection, transaction, dir, inserted);
            }

            // 2) External folders referenced by the registry file.
            var registryPath = Path.Combine(_songsBasePath, "_song_registry.json");
            if (File.Exists(registryPath))
            {
                List<string>? paths = null;
                try
                {
                    var regJson = await File.ReadAllTextAsync(registryPath);
                    paths = JsonSerializer.Deserialize<List<string>>(regJson, _jsonOptions);
                }
                catch
                {
                    // Malformed registry — skip external import, base-path import still applies.
                }

                if (paths != null)
                {
                    foreach (var dir in paths)
                        await ImportOneSongAsync(connection, transaction, dir, inserted);
                }
            }

            transaction.Commit();
        }
        catch
        {
            // A malformed/locked folder or file must never throw — leave the table as-is.
        }
    }

    /// <summary>
    /// Import a single song folder as a mirror row (INSERT OR IGNORE). Skips missing dirs/files,
    /// skips keys already inserted this pass, and swallows a single malformed file.
    /// </summary>
    private async Task ImportOneSongAsync(SqliteConnection connection, SqliteTransaction transaction, string dir, HashSet<string> inserted)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return;

            var normalized = SongPathResolver.Normalize(dir);
            if (!inserted.Add(normalized))
                return;

            var songJsonPath = Path.Combine(dir, "song.json");
            if (!File.Exists(songJsonPath))
                return;

            Song? song;
            try
            {
                var json = await File.ReadAllTextAsync(songJsonPath);
                song = JsonSerializer.Deserialize<Song>(json, _jsonOptions);
            }
            catch
            {
                // Skip a single malformed file and continue with the rest.
                return;
            }

            if (song == null)
                return;

            song.FolderPath = dir;

            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = @"
                INSERT OR IGNORE INTO Songs (FolderPath, Title, Artist, OriginalKey, Ccli, Themes, Bpm, LastWriteUtc, DataJson)
                VALUES (@FolderPath, @Title, @Artist, @OriginalKey, @Ccli, @Themes, @Bpm, @LastWriteUtc, @DataJson)";
            BindSong(insertCmd, song, normalized);
            await insertCmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // One bad folder must not abort the rest of the import.
        }
    }
}
