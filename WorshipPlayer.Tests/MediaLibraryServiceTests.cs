using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for MediaLibraryService.
/// Tests path sanitization, file type validation, and search capabilities.
/// </summary>
public class MediaLibraryServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly MediaLibraryService _service;

    public MediaLibraryServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"MediaLibTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _service = new MediaLibraryService(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public void Constructor_CreatesLibraryFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"MediaLib_New_{Guid.NewGuid():N}");
        try
        {
            var service = new MediaLibraryService(folder);
            Assert.True(Directory.Exists(folder));
        }
        finally
        {
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
