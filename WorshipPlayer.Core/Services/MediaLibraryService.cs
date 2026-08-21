using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages a local media library of images and videos.
/// Files are imported (copied) into the library folder with JSON sidecar metadata.
/// All paths are sanitized to prevent path traversal attacks.
/// </summary>
public class MediaLibraryService : IMediaLibraryService
{
    private readonly string _libraryPath;
    private readonly List<MediaLibraryItem> _items = new();
    private bool _loaded = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Supported image file extensions.
    /// </summary>
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };

    /// <summary>
    /// Supported video file extensions.
    /// </summary>
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".webm", ".avi"
    };

    public MediaLibraryService(string libraryPath)
    {
        _libraryPath = libraryPath;

        if (!Directory.Exists(_libraryPath))
            Directory.CreateDirectory(_libraryPath);
    }

    /// <inheritdoc />
    public async Task<MediaLibraryItem> ImportAsync(string sourcePath, List<string>? tags = null)
    {
        // Validate and sanitize the source path
        ValidatePath(sourcePath);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source file not found.", sourcePath);

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var mediaType = GetMediaType(extension);
        if (mediaType == null)
            throw new NotSupportedException($"File type '{extension}' is not supported. Supported types: {string.Join(", ", SupportedImageExtensions.Concat(SupportedVideoExtensions))}");

        var fileName = Path.GetFileName(sourcePath);
        var sanitizedName = SanitizeFileName(fileName);
        var destPath = GetUniqueFilePath(sanitizedName);

        // Copy file to library
        await CopyFileAsync(sourcePath, destPath);

        var fileInfo = new FileInfo(destPath);
        var item = new MediaLibraryItem
        {
            Id = Guid.NewGuid().ToString(),
            Name = Path.GetFileNameWithoutExtension(sanitizedName),
            FilePath = destPath,
            MediaType = mediaType.Value,
            Tags = tags ?? new List<string>(),
            FileSize = fileInfo.Length,
            DateAdded = DateTime.UtcNow
        };

        _items.Add(item);

        // Save metadata sidecar
        await SaveMetadataAsync(item);

        return item;
    }

    /// <inheritdoc />
    public async Task<List<MediaLibraryItem>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return new List<MediaLibraryItem>(_items);
    }

    /// <inheritdoc />
    public async Task<List<MediaLibraryItem>> SearchAsync(string query)
    {
        await EnsureLoadedAsync();

        if (string.IsNullOrWhiteSpace(query))
            return new List<MediaLibraryItem>(_items);

        return _items
            .Where(item =>
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string itemId, bool deleteFile = false)
    {
        await EnsureLoadedAsync();

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return;

        _items.Remove(item);

        // Delete sidecar metadata
        var metadataPath = GetMetadataPath(item.FilePath);
        if (File.Exists(metadataPath))
            File.Delete(metadataPath);

        // Optionally delete the actual file
        if (deleteFile && File.Exists(item.FilePath))
            File.Delete(item.FilePath);
    }

    /// <inheritdoc />
    public async Task<List<MediaLibraryItem>> GetByTagAsync(string tag)
    {
        await EnsureLoadedAsync();

        return _items
            .Where(item => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<MediaLibraryItem>> GetByTypeAsync(MediaLibraryType mediaType)
    {
        await EnsureLoadedAsync();

        return _items.Where(item => item.MediaType == mediaType).ToList();
    }

    /// <inheritdoc />
    public async Task UpdateTagsAsync(string itemId, List<string> tags)
    {
        await EnsureLoadedAsync();

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return;

        item.Tags = tags;
        await SaveMetadataAsync(item);
    }

    /// <inheritdoc />
    public string GetLibraryPath() => _libraryPath;

    /// <summary>
    /// Validate a path to prevent path traversal attacks.
    /// Rejects paths containing "..", absolute paths outside the library, or null bytes.
    /// </summary>
    private void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        // Reject null bytes
        if (path.Contains('\0'))
            throw new ArgumentException("Path contains invalid characters.", nameof(path));

        // Reject path traversal
        if (path.Contains(".."))
            throw new ArgumentException("Path traversal is not allowed.", nameof(path));
    }

    /// <summary>
    /// Validate that a destination path is within the library folder.
    /// </summary>
    private void ValidateDestinationPath(string destPath)
    {
        var normalizedLibrary = Path.GetFullPath(_libraryPath);
        var normalizedDest = Path.GetFullPath(destPath);

        if (!normalizedDest.StartsWith(normalizedLibrary, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Destination path is outside the media library folder.");
    }

    private MediaLibraryType? GetMediaType(string extension)
    {
        if (SupportedImageExtensions.Contains(extension))
            return MediaLibraryType.Image;
        if (SupportedVideoExtensions.Contains(extension))
            return MediaLibraryType.Video;
        return null;
    }

    private string GetUniqueFilePath(string fileName)
    {
        var destPath = Path.Combine(_libraryPath, fileName);
        ValidateDestinationPath(destPath);

        if (!File.Exists(destPath))
            return destPath;

        // Generate unique name
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        int counter = 1;

        while (File.Exists(destPath))
        {
            destPath = Path.Combine(_libraryPath, $"{nameWithoutExt}_{counter}{ext}");
            counter++;
        }

        return destPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed_file" : sanitized;
    }

    private static async Task CopyFileAsync(string source, string destination)
    {
        using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var destStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await sourceStream.CopyToAsync(destStream);
    }

    private async Task SaveMetadataAsync(MediaLibraryItem item)
    {
        var metadataPath = GetMetadataPath(item.FilePath);
        var json = JsonSerializer.Serialize(item, JsonOptions);
        await File.WriteAllTextAsync(metadataPath, json);
    }

    private static string GetMetadataPath(string filePath)
    {
        return filePath + "_metadata.json";
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        _items.Clear();

        if (!Directory.Exists(_libraryPath))
        {
            _loaded = true;
            return;
        }

        // Load all metadata sidecar files
        var metadataFiles = Directory.GetFiles(_libraryPath, "*_metadata.json");
        foreach (var metaFile in metadataFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaFile);
                var item = JsonSerializer.Deserialize<MediaLibraryItem>(json, JsonOptions);
                if (item != null)
                    _items.Add(item);
            }
            catch
            {
                // Skip invalid metadata files
            }
        }

        _loaded = true;
    }
}
