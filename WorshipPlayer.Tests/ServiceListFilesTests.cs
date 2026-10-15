using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for ServiceOrderManager.ListServiceFilesAsync against a real temp directory.
/// Follows the IDisposable temp-folder pattern from ServiceOrderPersistenceTests.
/// </summary>
public class ServiceListFilesTests : IDisposable
{
    private readonly string _tempFolder;

    public ServiceListFilesTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ServiceListFilesTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public async Task ListServiceFilesAsync_ReturnsOneInfoPerValidFile_SkipsCorrupt()
    {
        // Two valid service-order files written through the manager's own save path.
        var writer = new ServiceOrderManager(_tempFolder);
        writer.CreateNew("Service A", "2026-01-01");
        await writer.SaveAsync();

        writer.CreateNew("Service B", "2026-02-01");
        await writer.SaveAsync();

        // One corrupt file that must be skipped.
        var corruptPath = Path.Combine(_tempFolder, "corrupt.json");
        await File.WriteAllTextAsync(corruptPath, "{ this is not valid json");

        var manager = new ServiceOrderManager(_tempFolder);
        var files = await manager.ListServiceFilesAsync();

        Assert.Equal(2, files.Count);
        Assert.DoesNotContain(files, f => f.FilePath == corruptPath);

        foreach (var info in files)
        {
            Assert.True(File.Exists(info.FilePath));
            Assert.NotEqual(default, info.LastWriteTimeUtc);
            Assert.NotNull(info.Order);
        }

        Assert.Contains(files, f => f.Order.Name == "Service A");
        Assert.Contains(files, f => f.Order.Name == "Service B");
    }

    [Fact]
    public async Task ListServiceFilesAsync_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(_tempFolder, "does-not-exist-yet");
        var manager = new ServiceOrderManager(missing);

        var files = await manager.ListServiceFilesAsync();

        Assert.Empty(files);
    }
}
