using WorshipPlayer.Core.Models.Cloud;

namespace WorshipPlayer.Core.Services.Cloud;

/// <summary>
/// Filters for searching the cloud song library.
/// </summary>
public class SongSearchFilters
{
    public string? Key { get; set; }
    public double? MinBpm { get; set; }
    public double? MaxBpm { get; set; }
    public List<string>? Tags { get; set; }
    public bool? HasBacktracks { get; set; }
}

/// <summary>
/// Interface for cloud song library API operations.
/// Provides search, download, upload, and version checking capabilities.
/// </summary>
public interface ISongLibraryApi
{
    /// <summary>
    /// Search for songs in the cloud library by query text and optional filters.
    /// </summary>
    Task<List<SongLibraryItem>> SearchSongsAsync(string query, SongSearchFilters? filters = null);

    /// <summary>
    /// Get a single song by its cloud ID.
    /// </summary>
    Task<SongLibraryItem?> GetSongAsync(string songId);

    /// <summary>
    /// Get all available versions of a song.
    /// </summary>
    Task<List<SongLibraryItem>> GetSongVersionsAsync(string songId);

    /// <summary>
    /// Download a song and its selected components to a local folder.
    /// Returns the local path where the song was saved.
    /// </summary>
    Task<string> DownloadSongAsync(DownloadRequest request);

    /// <summary>
    /// Upload a local song folder to the cloud library.
    /// </summary>
    Task<SongLibraryItem> UploadSongAsync(string songFolderPath);

    /// <summary>
    /// Check which local songs have updates available in the cloud.
    /// </summary>
    Task<List<SyncInfo>> CheckForUpdatesAsync(List<string> localSongIds);
}
