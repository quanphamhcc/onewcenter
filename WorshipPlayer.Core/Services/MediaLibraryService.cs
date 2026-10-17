using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages a local media library of images and videos.
/// Asset files are imported (copied) into the library folder on disk; their metadata is
/// persisted as rows in the shared SQLite application database (app.db, MediaItems table),
/// migrated from the legacy per-asset JSON sidecars ("&lt;assetpath&gt;_metadata.json").
/// All paths are sanitized to prevent path traversal attacks.
/// </summary>
/// <remarks>
/// Backing store for METADATA migrated from JSON sidecars to SQLite, mirroring
/// <see cref="SongUsageService"/> and <see cref="AppDatabase"/>. The actual media binaries
/// (images/videos/audio) STAY ON DISK under the library folder — only the per-asset metadata
/// moves into the MediaItems table. The public <see cref="IMediaLibraryService"/> surface and
/// all observable behavior are preserved: path-traversal validation, unique-name copy with
/// FileMode.CreateNew, the <see cref="DeleteAsync"/> deleteFile flag, and
/// <see cref="GetLibraryPath"/>. The in-memory <c>_items</c> cache is kept so that
/// Search/GetByTag/GetByType and <see cref="MediaLibraryFilter"/> keep operating over the exact
/// same list with byte-for-byte identical LINQ semantics; only the SOURCE of that list changed
/// (SQLite rows instead of sidecar globbing). On first use, if "*_metadata.json" sidecars exist
/// in the Media folder and the MediaItems table is empty, they are imported once (defensively —
/// never throws, non-duplicating). Existing sidecar files are LEFT ON DISK (least-destructive);
/// new imports write a row and NO sidecar.
/// </remarks>
public class MediaLibraryService : IMediaLibraryService
{
    private readonly string _libraryPath;
    private readonly AppDatabase _database;
    private readonly List<MediaLibraryItem> _items = new();
    private bool _loaded = false;
    private bool _initialized = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string CreateSchemaSql = @"
CREATE TABLE IF NOT EXISTS MediaItems (
    Id              TEXT PRIMARY KEY,
    Name            TEXT NOT NULL DEFAULT '',
    FilePath        TEXT NOT NULL DEFAULT '',
    MediaType       TEXT NOT NULL DEFAULT 'Image',
    Tags            TEXT NOT NULL DEFAULT '[]',
    OrganizationId  TEXT NOT NULL DEFAULT '',
    DataJson        TEXT NOT NULL
);
";

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

    /// <summary>
    /// Create the service against the media library folder (where asset binaries are copied, as
    /// today) and a database path for metadata (conceptually &lt;appFolder&gt;/app.db — the caller
    /// passes the path, exactly like <see cref="SongUsageService"/> and
    /// <see cref="AlertService"/>). When <paramref name="databasePath"/> is null, the standard
    /// app-folder <c>app.db</c> (<see cref="AppDatabase.DefaultPath"/>) is used.
    /// </summary>
    public MediaLibraryService(string libraryPath, string? databasePath = null)
    {
        _libraryPath = libraryPath;
        _database = new AppDatabase(databasePath ?? AppDatabase.DefaultPath);

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

        // Copy file to library (asset binary stays on disk)
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

        await EnsureInitializedAsync();

        _items.Add(item);

        // Persist metadata row (replaces the former JSON sidecar)
        await UpsertAsync(item);

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

        // Delete the metadata row
        using (var connection = _database.CreateConnection())
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM MediaItems WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", item.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        // Optionally delete the actual on-disk asset file
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
        await UpsertAsync(item);
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

    /// <summary>
    /// Insert or update the metadata row for an item. Promotes the queried/filtered fields
    /// (Id, Name, FilePath, MediaType, Tags, OrganizationId) to real columns and keeps the whole
    /// item serialized in DataJson so remaining fields (ThumbnailPath, FileSize, DateAdded)
    /// round-trip losslessly. Mirrors <see cref="AlertService"/>'s upsert shape.
    /// </summary>
    private async Task UpsertAsync(MediaLibraryItem item)
    {
        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO MediaItems (Id, Name, FilePath, MediaType, Tags, OrganizationId, DataJson)
            VALUES (@Id, @Name, @FilePath, @MediaType, @Tags, @OrganizationId, @DataJson)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                FilePath = excluded.FilePath,
                MediaType = excluded.MediaType,
                Tags = excluded.Tags,
                OrganizationId = excluded.OrganizationId,
                DataJson = excluded.DataJson";
        BindItem(cmd, item);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void BindItem(SqliteCommand cmd, MediaLibraryItem item)
    {
        cmd.Parameters.AddWithValue("@Id", item.Id);
        cmd.Parameters.AddWithValue("@Name", item.Name ?? "");
        cmd.Parameters.AddWithValue("@FilePath", item.FilePath ?? "");
        cmd.Parameters.AddWithValue("@MediaType", item.MediaType.ToString());
        cmd.Parameters.AddWithValue("@Tags", JsonSerializer.Serialize(item.Tags ?? new List<string>(), JsonOptions));
        cmd.Parameters.AddWithValue("@OrganizationId", item.OrganizationId ?? "");
        cmd.Parameters.AddWithValue("@DataJson", JsonSerializer.Serialize(item, JsonOptions));
    }

    /// <summary>
    /// Create the MediaItems schema (idempotent) and run the one-time sidecar import.
    /// Runs once per process via the <see cref="_initialized"/> guard.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        _database.EnsureDirectory();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText = CreateSchemaSql;
            await schemaCmd.ExecuteNonQueryAsync();
        }

        await ImportSidecarsIfNeededAsync(connection);

        _initialized = true;
    }

    /// <summary>
    /// Defensive one-time import: if the MediaItems table is empty AND "*_metadata.json" sidecar
    /// files exist in the media folder, import each as a metadata row once. Preserves historical
    /// ids (assigns a GUID only when missing). Tolerates a missing folder, missing/malformed files
    /// (skips the single bad file and continues) — never throws. The empty-table guard makes this
    /// non-duplicating. Existing sidecar files are LEFT ON DISK and asset binaries are untouched.
    /// </summary>
    private async Task ImportSidecarsIfNeededAsync(SqliteConnection connection)
    {
        try
        {
            if (!Directory.Exists(_libraryPath))
                return;

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM MediaItems";
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
                if (count > 0)
                    return;
            }

            var metadataFiles = Directory.GetFiles(_libraryPath, "*_metadata.json");
            if (metadataFiles.Length == 0)
                return;

            using var transaction = connection.BeginTransaction();
            foreach (var metaFile in metadataFiles)
            {
                MediaLibraryItem? item;
                try
                {
                    var json = await File.ReadAllTextAsync(metaFile);
                    item = JsonSerializer.Deserialize<MediaLibraryItem>(json, JsonOptions);
                }
                catch
                {
                    // Skip a single malformed sidecar and continue with the rest.
                    continue;
                }

                if (item == null)
                    continue;

                if (string.IsNullOrEmpty(item.Id))
                    item.Id = Guid.NewGuid().ToString();

                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT OR IGNORE INTO MediaItems (Id, Name, FilePath, MediaType, Tags, OrganizationId, DataJson)
                    VALUES (@Id, @Name, @FilePath, @MediaType, @Tags, @OrganizationId, @DataJson)";
                BindItem(insertCmd, item);
                await insertCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            // A malformed/locked media folder or sidecar must never throw — matches the old
            // EnsureLoadedAsync catch behavior. Leave the table as-is.
        }
    }

    /// <summary>
    /// Load the in-memory <c>_items</c> cache from the MediaItems table (deserializing DataJson).
    /// Keeps the former cache design so Search/GetByTag/GetByType/<see cref="MediaLibraryFilter"/>
    /// run with byte-for-byte identical LINQ semantics; only the SOURCE changed from sidecar
    /// globbing to SQLite rows.
    /// </summary>
    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await EnsureInitializedAsync();

        _items.Clear();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DataJson FROM MediaItems";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            try
            {
                var item = JsonSerializer.Deserialize<MediaLibraryItem>(reader.GetString(0), JsonOptions);
                if (item != null)
                    _items.Add(item);
            }
            catch
            {
                // Skip invalid metadata rows (matches the old "skip invalid metadata files").
            }
        }

        _loaded = true;
    }
}
