using Xunit;
using WorshipPlayer.Presenter.Models;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for VideoLibrary model - folder scanning logic.
/// </summary>
public class VideoLibraryTests : IDisposable
{
    private readonly string _tempFolder;

    public VideoLibraryTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"VideoLibraryTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public void ScanFolder_EmptyPath_ReturnsEmptyList()
    {
        var library = new VideoLibrary();
        var result = library.ScanFolder("");
        Assert.Empty(result);
    }

    [Fact]
    public void ScanFolder_NullPath_ReturnsEmptyList()
    {
        var library = new VideoLibrary();
        var result = library.ScanFolder(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void ScanFolder_NonExistentPath_ReturnsEmptyList()
    {
        var library = new VideoLibrary();
        var result = library.ScanFolder("/nonexistent/path/that/does/not/exist");
        Assert.Empty(result);
    }

    [Fact]
    public void ScanFolder_EmptyFolder_ReturnsEmptyList()
    {
        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);
        Assert.Empty(result);
    }

    [Fact]
    public void ScanFolder_RootVideos_CreatesUncategorized()
    {
        // Create video files in root
        File.WriteAllText(Path.Combine(_tempFolder, "test1.mp4"), "fake video");
        File.WriteAllText(Path.Combine(_tempFolder, "test2.mov"), "fake video");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Single(result);
        Assert.Equal("Uncategorized", result[0].Name);
        Assert.Equal(2, result[0].Videos.Count);
    }

    [Fact]
    public void ScanFolder_SubfolderCategories_CreatesCategories()
    {
        // Create category subfolders with videos
        var natureDir = Path.Combine(_tempFolder, "Nature");
        var abstractDir = Path.Combine(_tempFolder, "Abstract");
        Directory.CreateDirectory(natureDir);
        Directory.CreateDirectory(abstractDir);

        File.WriteAllText(Path.Combine(natureDir, "ocean.mp4"), "fake");
        File.WriteAllText(Path.Combine(natureDir, "forest.webm"), "fake");
        File.WriteAllText(Path.Combine(abstractDir, "particles.mp4"), "fake");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Equal(2, result.Count);

        var abstractCategory = result.First(c => c.Name == "Abstract");
        Assert.Single(abstractCategory.Videos);
        Assert.Equal("particles.mp4", abstractCategory.Videos[0].FileName);

        var natureCategory = result.First(c => c.Name == "Nature");
        Assert.Equal(2, natureCategory.Videos.Count);
    }

    [Fact]
    public void ScanFolder_MixedRootAndSubfolders_ReturnsBoth()
    {
        // Root video
        File.WriteAllText(Path.Combine(_tempFolder, "intro.mp4"), "fake");

        // Subfolder
        var worshipDir = Path.Combine(_tempFolder, "Worship");
        Directory.CreateDirectory(worshipDir);
        File.WriteAllText(Path.Combine(worshipDir, "cross.mov"), "fake");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Name == "Uncategorized");
        Assert.Contains(result, c => c.Name == "Worship");
    }

    [Fact]
    public void ScanFolder_IgnoresNonVideoFiles()
    {
        File.WriteAllText(Path.Combine(_tempFolder, "readme.txt"), "text");
        File.WriteAllText(Path.Combine(_tempFolder, "photo.jpg"), "image");
        File.WriteAllText(Path.Combine(_tempFolder, "data.json"), "json");
        File.WriteAllText(Path.Combine(_tempFolder, "video.mp4"), "video");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Single(result);
        Assert.Single(result[0].Videos);
        Assert.Equal("video.mp4", result[0].Videos[0].FileName);
    }

    [Fact]
    public void ScanFolder_SupportsAllVideoExtensions()
    {
        File.WriteAllText(Path.Combine(_tempFolder, "a.mp4"), "");
        File.WriteAllText(Path.Combine(_tempFolder, "b.mov"), "");
        File.WriteAllText(Path.Combine(_tempFolder, "c.webm"), "");
        File.WriteAllText(Path.Combine(_tempFolder, "d.avi"), "");
        File.WriteAllText(Path.Combine(_tempFolder, "e.mkv"), "");
        File.WriteAllText(Path.Combine(_tempFolder, "f.wmv"), "");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Single(result);
        Assert.Equal(6, result[0].Videos.Count);
    }

    [Fact]
    public void ScanFolder_FindsThumbnail_WhenImageExistsWithSameName()
    {
        File.WriteAllText(Path.Combine(_tempFolder, "ocean.mp4"), "video");
        File.WriteAllText(Path.Combine(_tempFolder, "ocean.jpg"), "thumb");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Single(result);
        var video = result[0].Videos[0];
        Assert.NotNull(video.ThumbnailPath);
        Assert.Contains("ocean.jpg", video.ThumbnailPath);
    }

    [Fact]
    public void ScanFolder_NoThumbnail_WhenNoMatchingImage()
    {
        File.WriteAllText(Path.Combine(_tempFolder, "ocean.mp4"), "video");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Single(result);
        var video = result[0].Videos[0];
        Assert.Null(video.ThumbnailPath);
    }

    [Fact]
    public void ScanFolder_EmptySubfolders_AreExcluded()
    {
        var emptyDir = Path.Combine(_tempFolder, "EmptyCategory");
        Directory.CreateDirectory(emptyDir);

        File.WriteAllText(Path.Combine(_tempFolder, "video.mp4"), "fake");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        // Only the Uncategorized category should be returned
        Assert.Single(result);
        Assert.Equal("Uncategorized", result[0].Name);
    }

    [Fact]
    public void ScanFolder_VideosAreSortedByFileName()
    {
        File.WriteAllText(Path.Combine(_tempFolder, "zebra.mp4"), "");
        File.WriteAllText(Path.Combine(_tempFolder, "apple.mp4"), "");
        File.WriteAllText(Path.Combine(_tempFolder, "mango.mp4"), "");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Equal("apple.mp4", result[0].Videos[0].FileName);
        Assert.Equal("mango.mp4", result[0].Videos[1].FileName);
        Assert.Equal("zebra.mp4", result[0].Videos[2].FileName);
    }

    [Fact]
    public void ScanFolder_VideoFile_HasCorrectFullPath()
    {
        var filePath = Path.Combine(_tempFolder, "test.mp4");
        File.WriteAllText(filePath, "fake");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Equal(Path.GetFullPath(filePath), result[0].Videos[0].FullPath);
    }

    [Fact]
    public void ScanFolder_VideoFile_DurationIsNull()
    {
        File.WriteAllText(Path.Combine(_tempFolder, "test.mp4"), "fake");

        var library = new VideoLibrary();
        var result = library.ScanFolder(_tempFolder);

        Assert.Null(result[0].Videos[0].Duration);
    }

    [Fact]
    public void SupportedExtensions_ContainsExpectedFormats()
    {
        Assert.Contains(".mp4", VideoLibrary.SupportedExtensions);
        Assert.Contains(".mov", VideoLibrary.SupportedExtensions);
        Assert.Contains(".webm", VideoLibrary.SupportedExtensions);
        Assert.Contains(".avi", VideoLibrary.SupportedExtensions);
        Assert.Contains(".mkv", VideoLibrary.SupportedExtensions);
        Assert.Contains(".wmv", VideoLibrary.SupportedExtensions);
    }
}
