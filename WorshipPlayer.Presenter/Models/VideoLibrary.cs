using System.IO;
using System.Linq;

namespace WorshipPlayer.Presenter.Models;

/// <summary>
/// Represents a library of video backgrounds organized by category (subfolder).
/// Scans a configured folder structure where each subfolder is a category
/// (e.g., Nature, Abstract, Worship, etc.) containing video files.
/// </summary>
public class VideoLibrary
{
    /// <summary>
    /// Supported video file extensions for motion backgrounds.
    /// </summary>
    public static readonly string[] SupportedExtensions = { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".wmv" };

    /// <summary>
    /// Scan a folder for video files organized into category subfolders.
    /// Each immediate subfolder becomes a VideoCategory.
    /// Video files in the root folder are placed in an "Uncategorized" category.
    /// </summary>
    /// <param name="path">Root folder path to scan.</param>
    /// <returns>List of video categories found.</returns>
    public List<VideoCategory> ScanFolder(string path)
    {
        var categories = new List<VideoCategory>();

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return categories;

        // Scan root-level video files into "Uncategorized"
        var rootVideos = ScanDirectoryForVideos(path);
        if (rootVideos.Count > 0)
        {
            categories.Add(new VideoCategory
            {
                Name = "Uncategorized",
                Videos = rootVideos
            });
        }

        // Scan each subfolder as a category
        foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
        {
            var categoryName = Path.GetFileName(dir);
            var videos = ScanDirectoryForVideos(dir);

            if (videos.Count > 0)
            {
                categories.Add(new VideoCategory
                {
                    Name = categoryName,
                    Videos = videos
                });
            }
        }

        return categories;
    }

    /// <summary>
    /// Scan a single directory (non-recursive) for supported video files.
    /// </summary>
    private List<VideoFile> ScanDirectoryForVideos(string directoryPath)
    {
        var videos = new List<VideoFile>();

        if (!Directory.Exists(directoryPath))
            return videos;

        foreach (var file in Directory.GetFiles(directoryPath))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (SupportedExtensions.Contains(extension))
            {
                videos.Add(new VideoFile
                {
                    FileName = Path.GetFileName(file),
                    FullPath = Path.GetFullPath(file),
                    ThumbnailPath = FindThumbnail(file),
                    Duration = null // Duration requires media parsing, set later if needed
                });
            }
        }

        return videos.OrderBy(v => v.FileName).ToList();
    }

    /// <summary>
    /// Look for a thumbnail file with the same name but image extension.
    /// </summary>
    private string? FindThumbnail(string videoFilePath)
    {
        var directory = Path.GetDirectoryName(videoFilePath);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(videoFilePath);

        if (string.IsNullOrEmpty(directory)) return null;

        string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        foreach (var ext in imageExtensions)
        {
            var thumbPath = Path.Combine(directory, nameWithoutExt + ext);
            if (File.Exists(thumbPath))
                return thumbPath;
        }

        return null;
    }
}

/// <summary>
/// A category of video backgrounds (derived from subfolder name).
/// </summary>
public class VideoCategory
{
    /// <summary>
    /// Category name (from the subfolder name, e.g., "Nature", "Abstract", "Worship").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Video files within this category.
    /// </summary>
    public List<VideoFile> Videos { get; set; } = new();
}

/// <summary>
/// Represents a single video file in the video library.
/// </summary>
public class VideoFile
{
    /// <summary>
    /// File name without path (e.g., "ocean-waves.mp4").
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// Full absolute path to the video file.
    /// </summary>
    public string FullPath { get; set; } = "";

    /// <summary>
    /// Path to thumbnail image (same name, image extension). Null if no thumbnail found.
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Duration in seconds. Null if not yet determined (requires media parsing).
    /// </summary>
    public double? Duration { get; set; }
}
