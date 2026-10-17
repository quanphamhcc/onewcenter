using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Phase 5 SQL migration: proves the path-oriented public contract still holds after the SQLite
/// mirror was added, and that file identity (the normalized path) and the mirror stay consistent
/// across save / rename-by-save / out-of-band delete. The JSON files remain authoritative; app.db
/// is a reconciled mirror keyed by the normalized file path. Temp folder + temp app.db + 
/// SqliteConnection.ClearAllPools() in teardown (Windows file-lock discipline).
/// </summary>
public class ServiceOrderPathContractTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;

    public ServiceOrderPathContractTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ServiceOrderPathContractTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
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

    private long CountRows()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ServiceOrders";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private bool RowExistsForPath(string path)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ServiceOrders WHERE FilePath = @p";
        cmd.Parameters.AddWithValue("@p", Path.GetFullPath(path));
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// SaveAsync() with no path writes exactly one .json at GetFilePathFor(order) AND creates a
    /// mirror row keyed by that normalized path. LoadAsync(that path) round-trips the order.
    /// </summary>
    [Fact]
    public async Task SaveWithDefaultPath_WritesFile_AndMirrorRow_AndLoadRoundTrips()
    {
        var manager = new ServiceOrderManager(_tempFolder, _dbPath);
        var order = manager.CreateNew("Default Path Service", "2026-03-01");
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "Only Song" });

        var expectedPath = manager.GetFilePathFor(order);
        await manager.SaveAsync();

        // Exactly one JSON file on disk, at the resolved default path.
        var files = Directory.GetFiles(_tempFolder, "*.json");
        Assert.Single(files);
        Assert.True(File.Exists(expectedPath));

        // A mirror row exists keyed by the normalized identity path.
        Assert.True(RowExistsForPath(expectedPath));
        Assert.Equal(1, CountRows());

        // Path contract: LoadAsync(GetFilePathFor(...)) round-trips the order after migration.
        var reloadManager = new ServiceOrderManager(_tempFolder, _dbPath);
        var reloaded = await reloadManager.LoadAsync(expectedPath);
        Assert.NotNull(reloaded);
        Assert.Equal("Default Path Service", reloaded!.Name);
        Assert.Single(reloaded.Items);
        Assert.Equal("Only Song", reloaded.Items[0].Title);
    }

    /// <summary>
    /// Rename-by-save (what the UI does at OperatorWindow:445): SaveAsync(newPath) to a second
    /// path leaves both files on disk and both mirror rows present.
    /// </summary>
    [Fact]
    public async Task SaveToSecondPath_LeavesBothFiles_AndBothMirrorRows()
    {
        var manager = new ServiceOrderManager(_tempFolder, _dbPath);
        var order = manager.CreateNew("Rename Me", "2026-04-01");
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "Song" });

        var firstPath = manager.GetFilePathFor(order);
        await manager.SaveAsync();

        var secondPath = Path.Combine(_tempFolder, "renamed-copy.json");
        await manager.SaveAsync(secondPath);

        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.True(RowExistsForPath(firstPath));
        Assert.True(RowExistsForPath(secondPath));
        Assert.Equal(2, CountRows());
    }

    /// <summary>
    /// Out-of-band delete (what the UI does at OperatorWindow:463 via File.Delete): after the file
    /// is gone, a subsequent ListServiceFilesAsync prunes the stale mirror row while the public
    /// list reflects only the remaining file.
    /// </summary>
    [Fact]
    public async Task OutOfBandFileDelete_IsPrunedFromMirror_OnNextList()
    {
        var manager = new ServiceOrderManager(_tempFolder, _dbPath);

        var orderA = manager.CreateNew("Service A", "2026-05-01");
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "A" });
        var pathA = manager.GetFilePathFor(orderA);
        await manager.SaveAsync();

        var orderB = manager.CreateNew("Service B", "2026-06-01");
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "B" });
        var pathB = manager.GetFilePathFor(orderB);
        await manager.SaveAsync();

        // Both mirror rows present after saving.
        await manager.ListServiceFilesAsync();
        Assert.Equal(2, CountRows());

        // UI deletes a service by removing its file directly, bypassing the manager.
        File.Delete(pathA);

        // The next listing prunes the stale mirror row and returns only the remaining file.
        var files = await manager.ListServiceFilesAsync();

        Assert.Single(files);
        Assert.Equal("Service B", files[0].Order.Name);
        Assert.Equal(1, CountRows());
        Assert.False(RowExistsForPath(pathA));
        Assert.True(RowExistsForPath(pathB));
    }
}
