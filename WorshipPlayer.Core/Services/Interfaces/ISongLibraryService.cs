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
    /// Persist edits made in the slide editor. Copies the per-section lyric
    /// <see cref="Section.Text"/>, <see cref="Section.BackgroundImage"/>, and
    /// <see cref="Section.TemplateId"/> (matched by index) onto the stored song's
    /// sections, leaving loop points, bars, color, vocal pickup, chords, timestamps,
    /// MIDI cues, and metadata untouched, then re-saves song.json.
    /// </summary>
    /// <param name="songId">The song's id (folder path or folder name).</param>
    /// <param name="editedSections">The edited sections, in the song's section order.</param>
    Task UpdateSlidesAsync(string songId, List<Section> editedSections);

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
