using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages loading, saving, and organizing songs and setlists.
/// </summary>
public class SongManager
{
    private readonly string _songsBasePath;
    private readonly string _setlistsBasePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public List<Song> Songs { get; private set; } = new();
    public List<Setlist> Setlists { get; private set; } = new();

    public SongManager(string songsBasePath, string setlistsBasePath)
    {
        _songsBasePath = songsBasePath;
        _setlistsBasePath = setlistsBasePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
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
}
