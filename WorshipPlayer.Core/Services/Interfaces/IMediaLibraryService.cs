using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// Manages the local media library (images, videos, audio files).
/// Handles import, search, tagging, and retrieval of media items.
/// </summary>
public interface IMediaLibraryService
{
    /// <summary>
    /// Import a media file into the library. Copies the file to the library folder.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="tags">Optional tags to apply.</param>
    /// <returns>The created media library item.</returns>
    Task<MediaLibraryItem> ImportAsync(string sourcePath, List<string>? tags = null);

    /// <summary>
    /// Get all media items in the library.
    /// </summary>
    Task<List<MediaLibraryItem>> GetAllAsync();

    /// <summary>
    /// Search media items by name.
    /// </summary>
    /// <param name="query">Search query (name substring match).</param>
    /// <returns>Matching media items.</returns>
    Task<List<MediaLibraryItem>> SearchAsync(string query);

    /// <summary>
    /// Delete a media item from the library and optionally from disk.
    /// </summary>
    /// <param name="itemId">ID of the item to delete.</param>
    /// <param name="deleteFile">Whether to also delete the file from disk.</param>
    Task DeleteAsync(string itemId, bool deleteFile = false);

    /// <summary>
    /// Get media items matching a specific tag.
    /// </summary>
    /// <param name="tag">Tag to filter by.</param>
    /// <returns>Media items with the specified tag.</returns>
    Task<List<MediaLibraryItem>> GetByTagAsync(string tag);

    /// <summary>
    /// Get media items filtered by type.
    /// </summary>
    /// <param name="mediaType">Type of media to filter by.</param>
    Task<List<MediaLibraryItem>> GetByTypeAsync(MediaLibraryType mediaType);

    /// <summary>
    /// Update tags for a media item.
    /// </summary>
    /// <param name="itemId">ID of the item to update.</param>
    /// <param name="tags">New tag list.</param>
    Task UpdateTagsAsync(string itemId, List<string> tags);

    /// <summary>
    /// Get the library storage path.
    /// </summary>
    string GetLibraryPath();
}
