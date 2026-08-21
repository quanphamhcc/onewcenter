using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Cloud;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// Manages the song library - searching, filtering, and metadata management.
/// </summary>
public interface ISongLibraryService
{
    /// <summary>
    /// Get all songs in the library.
    /// </summary>
    Task<List<SongLibraryItem>> GetAllAsync();

    /// <summary>
    /// Search songs by title, artist, or lyrics content.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <returns>Matching songs.</returns>
    Task<List<SongLibraryItem>> SearchAsync(string query);

    /// <summary>
    /// Get a song by its ID.
    /// </summary>
    Task<SongLibraryItem?> GetByIdAsync(string songId);

    /// <summary>
    /// Filter songs by key.
    /// </summary>
    Task<List<SongLibraryItem>> FilterByKeyAsync(string key);

    /// <summary>
    /// Filter songs by tag.
    /// </summary>
    Task<List<SongLibraryItem>> FilterByTagAsync(string tag);

    /// <summary>
    /// Update song metadata (title, artist, key, BPM, CCLI number, tags).
    /// </summary>
    Task UpdateMetadataAsync(SongLibraryItem song);

    /// <summary>
    /// Record a song usage event for CCLI reporting.
    /// </summary>
    Task RecordUsageAsync(SongUsageRecord record);

    /// <summary>
    /// Get song usage history within a date range.
    /// </summary>
    /// <param name="startDate">Start date of the range.</param>
    /// <param name="endDate">End date of the range.</param>
    Task<List<SongUsageRecord>> GetUsageHistoryAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// Import a song from a ChordPro file.
    /// </summary>
    /// <param name="filePath">Path to the .chopro/.chordpro file.</param>
    Task<SongLibraryItem> ImportChordProAsync(string filePath);

    /// <summary>
    /// Delete a song from the library.
    /// </summary>
    Task DeleteAsync(string songId);
}
