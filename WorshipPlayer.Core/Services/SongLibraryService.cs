using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Cloud;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Provides song library management capabilities on top of SongManager.
/// Implements searching, filtering, and sorting of the song library.
/// </summary>
public class SongLibraryService : ISongLibraryService
{
    private readonly SongManager _songManager;
    private readonly SongUsageService _usageService;
    private readonly ChordProParserService _chordProParser;

    public SongLibraryService(SongManager songManager, SongUsageService usageService)
    {
        _songManager = songManager;
        _usageService = usageService;
        _chordProParser = new ChordProParserService();
    }

    /// <inheritdoc />
    public Task<List<SongLibraryItem>> GetAllAsync()
    {
        var items = _songManager.Songs
            .Select(MapToLibraryItem)
            .OrderBy(s => s.Title)
            .ToList();

        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public Task<List<SongLibraryItem>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAllAsync();

        var items = _songManager.Songs
            .Where(s =>
                s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Sections.Any(sec => sec.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Select(MapToLibraryItem)
            .ToList();

        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public Task<SongLibraryItem?> GetByIdAsync(string songId)
    {
        var song = _songManager.Songs.FirstOrDefault(s =>
            s.FolderPath == songId || Path.GetFileName(s.FolderPath) == songId);

        return Task.FromResult(song != null ? MapToLibraryItem(song) : null);
    }

    /// <inheritdoc />
    public Task<List<SongLibraryItem>> FilterByKeyAsync(string key)
    {
        var items = _songManager.Songs
            .Where(s => s.OriginalKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(MapToLibraryItem)
            .ToList();

        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public Task<List<SongLibraryItem>> FilterByTagAsync(string tag)
    {
        var items = _songManager.Songs
            .Where(s => s.Themes.Contains(tag, StringComparison.OrdinalIgnoreCase))
            .Select(MapToLibraryItem)
            .ToList();

        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public async Task UpdateMetadataAsync(SongLibraryItem songItem)
    {
        var song = _songManager.Songs.FirstOrDefault(s =>
            s.FolderPath == songItem.Id || Path.GetFileName(s.FolderPath) == songItem.Id);

        if (song == null) return;

        song.Title = songItem.Title;
        song.Artist = songItem.Artist;
        song.OriginalKey = songItem.Key;
        song.Bpm = songItem.Bpm;

        await _songManager.SaveSongAsync(song);
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(SongUsageRecord record)
    {
        await _usageService.LogUsageAsync(record);
    }

    /// <inheritdoc />
    public async Task<List<SongUsageRecord>> GetUsageHistoryAsync(DateTime startDate, DateTime endDate)
    {
        return await _usageService.GetByDateRangeAsync(startDate, endDate);
    }

    /// <inheritdoc />
    public async Task<SongLibraryItem> ImportChordProAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("ChordPro file not found.", filePath);

        var content = await File.ReadAllTextAsync(filePath);
        var parsed = _chordProParser.Parse(content);

        // Extract metadata from directives
        parsed.Directives.TryGetValue("title", out var parsedTitle);
        parsed.Directives.TryGetValue("artist", out var parsedArtist);
        parsed.Directives.TryGetValue("key", out var parsedKey);

        // Create a new song from the parsed ChordPro data
        var songTitle = parsedTitle ?? Path.GetFileNameWithoutExtension(filePath);
        var songFolder = Path.Combine(
            Path.GetDirectoryName(_songManager.Songs.FirstOrDefault()?.FolderPath) ?? "Songs",
            SanitizeFileName(songTitle));

        if (!Directory.Exists(songFolder))
            Directory.CreateDirectory(songFolder);

        var song = new Song
        {
            Title = songTitle,
            Artist = parsedArtist ?? "",
            OriginalKey = parsedKey ?? "C",
            FolderPath = songFolder
        };

        // Convert parsed sections to song sections
        foreach (var section in parsed.Sections)
        {
            song.Sections.Add(new Section
            {
                Name = section.Name,
                Text = section.Text
            });
        }

        await _songManager.SaveSongAsync(song);
        _songManager.Songs.Add(song);

        return MapToLibraryItem(song);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string songId)
    {
        var song = _songManager.Songs.FirstOrDefault(s =>
            s.FolderPath == songId || Path.GetFileName(s.FolderPath) == songId);

        if (song != null)
        {
            _songManager.RemoveSong(song, deleteFiles: false);
        }

        return Task.CompletedTask;
    }

    private static SongLibraryItem MapToLibraryItem(Song song)
    {
        return new SongLibraryItem
        {
            Id = song.FolderPath,
            Title = song.Title,
            Artist = song.Artist,
            Key = song.OriginalKey,
            Bpm = song.Bpm,
            HasBacktracks = song.Tracks.Count > 0,
            HasLyrics = song.Sections.Count > 0,
            CreatedAt = DateTime.MinValue,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }
}
