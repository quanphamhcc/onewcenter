using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Phase 5 SQL migration: the one-time, defensive, non-duplicating JSON → app.db mirror import.
/// Each test injects a temp ServiceOrders folder plus a temp app.db and clears SQLite connection
/// pools in teardown so the temp .db file can be deleted on Windows (mirrors
/// MediaLibraryServiceTests discipline). The on-disk JSON files stay authoritative; app.db is a
/// reconciled mirror.
/// </summary>
public class ServiceOrderSqliteImportTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;

    public ServiceOrderSqliteImportTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ServiceOrderSqliteImportTests_{Guid.NewGuid():N}");
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

    /// <summary>
    /// Write 2 valid orders (via a first manager's SaveAsync, into a DIFFERENT temp db so the
    /// mirror under test starts empty) plus a hand-written corrupt.json, then construct a fresh
    /// manager pointed at the same folder + the mirror db: it imports exactly the 2 valid orders,
    /// preserving items/groups/settings, skips the corrupt file, and never throws.
    /// </summary>
    [Fact]
    public async Task Construct_WithExistingJsonFiles_ImportsExactlyValidOrders_PreservingGraph()
    {
        // Arrange: write two valid files to disk via a writer that uses a SEPARATE throwaway db,
        // so the mirror db under test (_dbPath) is still empty and the import path runs.
        var writerDb = Path.Combine(_tempFolder, "writer.db");

        var writer = new ServiceOrderManager(_tempFolder, writerDb);
        writer.CreateNew("Service A", "2026-01-01");
        var itemA = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Song A",
            AutoAdvanceSettings = new ServiceItemSettings
            {
                AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
                AutoAdvanceDelayMs = 12000
            }
        };
        writer.AddItem(itemA);
        var group = writer.CreateGroup("Opening")!;
        writer.AssignItemToGroup(itemA.Id, group.Id);
        await writer.SaveAsync();

        writer.CreateNew("Service B", "2026-02-01");
        writer.AddItem(new ServiceItem { Type = ServiceItemType.Scripture, Title = "Reading B" });
        await writer.SaveAsync();

        // A corrupt file that must be skipped.
        var corruptPath = Path.Combine(_tempFolder, "corrupt.json");
        await File.WriteAllTextAsync(corruptPath, "{ this is not valid json");

        // Act: a fresh manager against the (empty) mirror db triggers the one-time import.
        var manager = new ServiceOrderManager(_tempFolder, _dbPath);
        // ListServiceFilesAsync forces EnsureInitializedAsync (schema + import) + reconcile.
        var files = await manager.ListServiceFilesAsync();

        // Assert: exactly the two valid orders ended up as mirror rows; corrupt skipped.
        Assert.Equal(2, CountRows());

        // Public behavior unchanged: the list still returns the two orders (corrupt skipped).
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Order.Name == "Service A");
        Assert.Contains(files, f => f.Order.Name == "Service B");

        // Graph preserved through the DataJson round-trip (items/groups/settings).
        var orderA = files.First(f => f.Order.Name == "Service A").Order;
        var reloadedItem = Assert.Single(orderA.Items);
        Assert.Equal("Song A", reloadedItem.Title);
        Assert.NotNull(reloadedItem.AutoAdvanceSettings);
        Assert.Equal(AutoAdvanceAfterCondition.Timer, reloadedItem.AutoAdvanceSettings!.AutoAdvanceAfterCondition);
        Assert.Equal(12000, reloadedItem.AutoAdvanceSettings.AutoAdvanceDelayMs);
        var reloadedGroup = Assert.Single(orderA.Groups);
        Assert.Equal("Opening", reloadedGroup.Name);
        Assert.Contains(reloadedItem.Id, reloadedGroup.Items);
    }

    /// <summary>
    /// Re-constructing a manager against an already-populated mirror db does NOT duplicate rows:
    /// the empty-table guard makes the import non-duplicating.
    /// </summary>
    [Fact]
    public async Task Reconstruct_AgainstPopulatedDb_DoesNotDuplicateRows()
    {
        var writerDb = Path.Combine(_tempFolder, "writer.db");
        var writer = new ServiceOrderManager(_tempFolder, writerDb);
        writer.CreateNew("Service A", "2026-01-01");
        writer.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "A" });
        await writer.SaveAsync();
        writer.CreateNew("Service B", "2026-02-01");
        writer.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "B" });
        await writer.SaveAsync();

        // First construct imports the two orders into the mirror.
        var first = new ServiceOrderManager(_tempFolder, _dbPath);
        await first.ListServiceFilesAsync();
        Assert.Equal(2, CountRows());

        // A brand-new manager against the already-populated db must NOT re-import / duplicate.
        var second = new ServiceOrderManager(_tempFolder, _dbPath);
        await second.ListServiceFilesAsync();
        Assert.Equal(2, CountRows());
    }

    /// <summary>
    /// A missing folder / empty folder must never throw and must leave the table empty.
    /// </summary>
    [Fact]
    public async Task Construct_WithNoJsonFiles_NeverThrows_LeavesTableEmpty()
    {
        var manager = new ServiceOrderManager(_tempFolder, _dbPath);
        var files = await manager.ListServiceFilesAsync();

        Assert.Empty(files);
        Assert.Equal(0, CountRows());
    }
}
