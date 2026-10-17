using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for MediaLibraryService (metadata now SQLite-backed via app.db; asset binaries
/// stay on disk). Tests path sanitization, file type validation, search/filter parity, and the
/// one-time *_metadata.json sidecar import. Each test injects a temp media folder plus a temp
/// app.db and clears SQLite connection pools in teardown so the temp .db file can be deleted on
/// Windows (mirrors AlertServiceTests discipline).
/// </summary>
public class MediaLibraryServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;
    private readonly MediaLibraryService _service;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public MediaLibraryServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"MediaLibTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
        _service = new MediaLibraryService(_tempFolder, _dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempFolder))
        {
            try
            {
                Directory.Delete(_tempFolder, true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp folder; ignore a transient OS lock.
            }
        }
    }

    [Fact]
    public void Constructor_CreatesLibraryFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"MediaLib_New_{Guid.NewGuid():N}");
        var dbPath = Path.Combine(folder, "app.db");
        try
        {
            var service = new MediaLibraryService(folder, dbPath);
            Assert.True(Directory.Exists(folder));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task ImportAsync_PathTraversal_ThrowsException()
    {
        var maliciousPath = "../../../etc/passwd";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ImportAsync(maliciousPath));
    }

    [Fact]
    public async Task ImportAsync_WindowsAbsolutePathTraversal_ThrowsException()
    {
        var maliciousPath = @"..\..\..\..\Windows\system32\config";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ImportAsync(maliciousPath));
    }

    [Fact]
    public async Task ImportAsync_NullBytesInPath_ThrowsException()
    {
        var maliciousPath = "/valid/path\0/evil";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ImportAsync(maliciousPath));
    }

    [Fact]
    public async Task ImportAsync_EmptyPath_ThrowsException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ImportAsync(""));
    }

    [Fact]
    public async Task ImportAsync_WhitespacePath_ThrowsException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ImportAsync("   "));
    }

    [Fact]
    public async Task ImportAsync_UnsupportedFileType_ThrowsNotSupported()
    {
        var testFile = Path.Combine(_tempFolder, "test.exe");
        await File.WriteAllTextAsync(testFile, "fake executable");

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _service.ImportAsync(testFile));
    }

    [Fact]
    public async Task ImportAsync_ValidImage_ImportsSuccessfully()
    {
        var testFile = CreateTestFile("test_image.png");

        var item = await _service.ImportAsync(testFile);

        Assert.NotNull(item);
        Assert.Equal("test_image", item.Name);
        Assert.Equal(MediaLibraryType.Image, item.MediaType);
        Assert.True(item.FileSize > 0);
    }

    [Fact]
    public async Task ImportAsync_ValidVideo_ImportsSuccessfully()
    {
        var testFile = CreateTestFile("test_video.mp4");

        var item = await _service.ImportAsync(testFile);

        Assert.NotNull(item);
        Assert.Equal(MediaLibraryType.Video, item.MediaType);
    }

    [Fact]
    public async Task ImportAsync_WithTags_StoresTags()
    {
        var testFile = CreateTestFile("tagged.jpg");
        var tags = new List<string> { "worship", "background" };

        var item = await _service.ImportAsync(testFile, tags);

        Assert.Equal(2, item.Tags.Count);
        Assert.Contains("worship", item.Tags);
        Assert.Contains("background", item.Tags);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsAll()
    {
        var file1 = CreateTestFile("sunset.jpg");
        var file2 = CreateTestFile("mountain.png");
        await _service.ImportAsync(file1);
        await _service.ImportAsync(file2);

        var results = await _service.SearchAsync("");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_ByName_FindsMatch()
    {
        var file1 = CreateTestFile("sunset.jpg");
        var file2 = CreateTestFile("mountain.png");
        await _service.ImportAsync(file1);
        await _service.ImportAsync(file2);

        var results = await _service.SearchAsync("sunset");

        Assert.Single(results);
        Assert.Equal("sunset", results[0].Name);
    }

    [Fact]
    public async Task SearchAsync_ByTag_FindsMatch()
    {
        var file1 = CreateTestFile("image1.jpg");
        var file2 = CreateTestFile("image2.png");
        await _service.ImportAsync(file1, new List<string> { "nature" });
        await _service.ImportAsync(file2, new List<string> { "urban" });

        var results = await _service.SearchAsync("nature");

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_CaseInsensitive()
    {
        var file = CreateTestFile("SunsetBeach.jpg");
        await _service.ImportAsync(file);

        var results = await _service.SearchAsync("SUNSET");

        Assert.Single(results);
    }

    [Fact]
    public async Task GetByTypeAsync_FiltersCorrectly()
    {
        var img = CreateTestFile("photo.jpg");
        var vid = CreateTestFile("clip.mp4");
        await _service.ImportAsync(img);
        await _service.ImportAsync(vid);

        var images = await _service.GetByTypeAsync(MediaLibraryType.Image);
        var videos = await _service.GetByTypeAsync(MediaLibraryType.Video);

        Assert.Single(images);
        Assert.Single(videos);
    }

    [Fact]
    public async Task DeleteAsync_RemovesItem()
    {
        var file = CreateTestFile("toDelete.jpg");
        var item = await _service.ImportAsync(file);

        await _service.DeleteAsync(item.Id);

        var all = await _service.GetAllAsync();
        Assert.DoesNotContain(all, i => i.Id == item.Id);
    }

    [Fact]
    public async Task GetAllAsync_EmptyLibrary_ReturnsEmpty()
    {
        var results = await _service.GetAllAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task UpdateTagsAsync_UpdatesSuccessfully()
    {
        var file = CreateTestFile("updateTags.jpg");
        var item = await _service.ImportAsync(file, new List<string> { "old" });

        await _service.UpdateTagsAsync(item.Id, new List<string> { "new1", "new2" });

        var all = await _service.GetAllAsync();
        var updated = all.First(i => i.Id == item.Id);
        Assert.Equal(2, updated.Tags.Count);
        Assert.Contains("new1", updated.Tags);
    }

    [Fact]
    public void GetLibraryPath_ReturnsConfiguredPath()
    {
        Assert.Equal(_tempFolder, _service.GetLibraryPath());
    }

    // ─── One-time sidecar import tests ─────────────────────────────────────

    [Fact]
    public async Task ImportsLegacySidecarsOnFirstUse_AndLeavesSidecarAndAssetOnDisk()
    {
        // Fresh media folder + db, no service touched yet.
        var folder = Path.Combine(Path.GetTempPath(), $"MediaLibSidecar_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "app.db");
        try
        {
            // Hand-write an asset file plus its *_metadata.json sidecar.
            var assetPath = Path.Combine(folder, "legacy.png");
            await File.WriteAllBytesAsync(assetPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var sidecarPath = assetPath + "_metadata.json";
            var item = new MediaLibraryItem
            {
                Id = "legacy-media-1",
                Name = "legacy",
                FilePath = assetPath,
                MediaType = MediaLibraryType.Image,
                Tags = new List<string> { "worship", "background" }
            };
            await File.WriteAllTextAsync(sidecarPath, JsonSerializer.Serialize(item, JsonOptions));

            var service = new MediaLibraryService(folder, dbPath);
            var all = await service.GetAllAsync();

            var loaded = Assert.Single(all);
            Assert.Equal("legacy", loaded.Name);
            Assert.Equal(MediaLibraryType.Image, loaded.MediaType);
            Assert.Contains("worship", loaded.Tags);

            // The sidecar file is left on disk (least-destructive) and the asset is untouched.
            Assert.True(File.Exists(sidecarPath));
            Assert.True(File.Exists(assetPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task ImportsLegacySidecars_SecondInstance_DoesNotDuplicate()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"MediaLibSidecarDup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "app.db");
        try
        {
            var assetPath = Path.Combine(folder, "once.png");
            await File.WriteAllBytesAsync(assetPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            var sidecarPath = assetPath + "_metadata.json";
            var item = new MediaLibraryItem
            {
                Id = "once-1",
                Name = "once",
                FilePath = assetPath,
                MediaType = MediaLibraryType.Image
            };
            await File.WriteAllTextAsync(sidecarPath, JsonSerializer.Serialize(item, JsonOptions));

            var first = new MediaLibraryService(folder, dbPath);
            var afterFirst = await first.GetAllAsync();
            Assert.Single(afterFirst);

            // A second service against the SAME app.db with the SAME sidecar present must not
            // re-import (empty-table guard).
            var second = new MediaLibraryService(folder, dbPath);
            var afterSecond = await second.GetAllAsync();
            Assert.Single(afterSecond);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task ImportsLegacySidecars_MalformedFile_IsSkipped_DoesNotThrow()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"MediaLibSidecarBad_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "app.db");
        try
        {
            // One valid sidecar...
            var goodAsset = Path.Combine(folder, "good.png");
            await File.WriteAllBytesAsync(goodAsset, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            await File.WriteAllTextAsync(
                goodAsset + "_metadata.json",
                JsonSerializer.Serialize(
                    new MediaLibraryItem { Id = "good-1", Name = "good", FilePath = goodAsset },
                    JsonOptions));

            // ...and one malformed sidecar alongside it.
            await File.WriteAllTextAsync(
                Path.Combine(folder, "bad.png_metadata.json"),
                "{ not valid json ]");

            var service = new MediaLibraryService(folder, dbPath);
            var all = await service.GetAllAsync();

            var loaded = Assert.Single(all);
            Assert.Equal("good", loaded.Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task NewImports_DoNotWriteSidecarFiles()
    {
        var testFile = CreateTestFile("newimport.jpg");

        var item = await _service.ImportAsync(testFile);

        // The copied asset exists on disk, but no *_metadata.json sidecar is written for it.
        Assert.True(File.Exists(item.FilePath));
        Assert.False(File.Exists(item.FilePath + "_metadata.json"));
    }

    /// <summary>
    /// Helper to create a test file with minimal content in a source folder.
    /// </summary>
    private string CreateTestFile(string fileName)
    {
        var sourceFolder = Path.Combine(_tempFolder, "_sources");
        if (!Directory.Exists(sourceFolder))
            Directory.CreateDirectory(sourceFolder);

        var filePath = Path.Combine(sourceFolder, fileName);
        File.WriteAllBytes(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // Minimal bytes
        return filePath;
    }
}
