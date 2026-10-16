using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the C2 "Service order groups" logic on <see cref="ServiceOrderManager"/>.
/// Covers group create/rename/delete/assign, ungrouped tracking, collapse-state round-trip
/// through save/reload, legacy (no-groups) JSON loading, and RemoveItem purging group references.
/// Follows the temp-folder save/reload pattern used by the existing service tests.
/// </summary>
public class ServiceOrderManagerGroupTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _filePath;

    public ServiceOrderManagerGroupTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"GroupTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _filePath = Path.Combine(_tempFolder, "service.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    private ServiceOrderManager NewManagerWithItems(params string[] titles)
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("Test Service", "2025-01-05");
        foreach (var title in titles)
            manager.AddItem(new ServiceItem { Title = title });
        return manager;
    }

    [Fact]
    public void CreateGroup_AddsNamedGroup()
    {
        var manager = NewManagerWithItems("A");

        var group = manager.CreateGroup("Opening Worship");

        Assert.NotNull(group);
        Assert.Equal("Opening Worship", group!.Name);
        Assert.Single(manager.CurrentServiceOrder!.Groups);
        Assert.Equal(0, group.Order);
    }

    [Fact]
    public void CreateGroup_AssignsSequentialOrder()
    {
        var manager = NewManagerWithItems();

        var g0 = manager.CreateGroup("First");
        var g1 = manager.CreateGroup("Second");

        Assert.Equal(0, g0!.Order);
        Assert.Equal(1, g1!.Order);
    }

    [Fact]
    public void AssignItemToGroup_PutsItemInTargetAndRemovesFromPrior()
    {
        var manager = NewManagerWithItems("A");
        var itemId = manager.CurrentServiceOrder!.Items[0].Id;
        var g1 = manager.CreateGroup("G1")!;
        var g2 = manager.CreateGroup("G2")!;

        manager.AssignItemToGroup(itemId, g1.Id);
        Assert.Contains(itemId, g1.Items);
        Assert.DoesNotContain(itemId, g2.Items);

        // Moving to another group removes it from the first.
        manager.AssignItemToGroup(itemId, g2.Id);
        Assert.DoesNotContain(itemId, g1.Items);
        Assert.Contains(itemId, g2.Items);
    }

    [Fact]
    public void AssignItemToGroup_NullTarget_Ungroups()
    {
        var manager = NewManagerWithItems("A");
        var itemId = manager.CurrentServiceOrder!.Items[0].Id;
        var g1 = manager.CreateGroup("G1")!;
        manager.AssignItemToGroup(itemId, g1.Id);

        manager.AssignItemToGroup(itemId, null);

        Assert.DoesNotContain(itemId, g1.Items);
        Assert.Contains(itemId, manager.GetUngroupedItemIds());
    }

    [Fact]
    public void AssignItemToGroup_UnknownIds_ReturnFalse()
    {
        var manager = NewManagerWithItems("A");
        var itemId = manager.CurrentServiceOrder!.Items[0].Id;

        Assert.False(manager.AssignItemToGroup("no-such-item", null));
        Assert.False(manager.AssignItemToGroup(itemId, "no-such-group"));
    }

    [Fact]
    public void DeleteGroup_KeepsItems_TheyBecomeUngrouped()
    {
        var manager = NewManagerWithItems("A", "B");
        var items = manager.CurrentServiceOrder!.Items;
        var g1 = manager.CreateGroup("G1")!;
        manager.AssignItemToGroup(items[0].Id, g1.Id);
        manager.AssignItemToGroup(items[1].Id, g1.Id);

        manager.DeleteGroup(g1.Id);

        // Group gone, but the items are still in the service order.
        Assert.Empty(manager.CurrentServiceOrder!.Groups);
        Assert.Equal(2, manager.CurrentServiceOrder!.Items.Count);
        var ungrouped = manager.GetUngroupedItemIds();
        Assert.Contains(items[0].Id, ungrouped);
        Assert.Contains(items[1].Id, ungrouped);
    }

    [Fact]
    public void DeleteGroup_CompactsRemainingOrder()
    {
        var manager = NewManagerWithItems();
        var g0 = manager.CreateGroup("A")!;
        var g1 = manager.CreateGroup("B")!;
        var g2 = manager.CreateGroup("C")!;

        manager.DeleteGroup(g1.Id);

        Assert.Equal(0, g0.Order);
        Assert.Equal(1, g2.Order);
    }

    [Fact]
    public void RenameGroup_ChangesName()
    {
        var manager = NewManagerWithItems();
        var g = manager.CreateGroup("Old Name")!;

        var ok = manager.RenameGroup(g.Id, "New Name");

        Assert.True(ok);
        Assert.Equal("New Name", g.Name);
    }

    [Fact]
    public async Task CollapseState_RoundTripsThroughSaveReload()
    {
        var manager = NewManagerWithItems("A");
        var g = manager.CreateGroup("Opening")!;
        manager.AssignItemToGroup(manager.CurrentServiceOrder!.Items[0].Id, g.Id);
        manager.SetGroupCollapsed(g.Id, true);
        await manager.SaveAsync(_filePath);

        var reloaded = new ServiceOrderManager(_tempFolder);
        var loaded = await reloaded.LoadAsync(_filePath);

        Assert.NotNull(loaded);
        var reloadedGroup = Assert.Single(loaded!.Groups);
        Assert.True(reloadedGroup.IsCollapsed);
        Assert.Equal("Opening", reloadedGroup.Name);
        Assert.Single(reloadedGroup.Items);
    }

    [Fact]
    public async Task LegacyJson_NoGroupsKey_LoadsAllItemsUngrouped()
    {
        // A service file written before the groups feature existed: no "groups" key at all.
        var legacyJson = """
        {
          "id": "svc-legacy",
          "name": "Legacy Service",
          "date": "2024-12-01",
          "items": [
            { "id": "item-1", "type": "Song", "title": "Song One" },
            { "id": "item-2", "type": "Scripture", "title": "Reading" }
          ]
        }
        """;
        await File.WriteAllTextAsync(_filePath, legacyJson);

        var manager = new ServiceOrderManager(_tempFolder);
        var loaded = await manager.LoadAsync(_filePath);

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Groups);
        Assert.Equal(2, loaded.Items.Count);
        var ungrouped = manager.GetUngroupedItemIds();
        Assert.Equal(2, ungrouped.Count);
        Assert.Contains("item-1", ungrouped);
        Assert.Contains("item-2", ungrouped);
    }

    [Fact]
    public void RemoveItem_PurgesIdFromItsGroup()
    {
        var manager = NewManagerWithItems("A", "B");
        var items = manager.CurrentServiceOrder!.Items;
        var removedId = items[0].Id;
        var g = manager.CreateGroup("G1")!;
        manager.AssignItemToGroup(items[0].Id, g.Id);
        manager.AssignItemToGroup(items[1].Id, g.Id);

        manager.RemoveItem(0);

        Assert.DoesNotContain(removedId, g.Items);
        Assert.Single(g.Items); // only the surviving item's id remains
        Assert.Single(manager.CurrentServiceOrder!.Items);
    }

    [Fact]
    public void GetUngroupedItemIds_PreservesItemOrder()
    {
        var manager = NewManagerWithItems("A", "B", "C");
        var items = manager.CurrentServiceOrder!.Items;
        var g = manager.CreateGroup("G1")!;
        // Group the middle item; A and C stay ungrouped and must keep their order.
        manager.AssignItemToGroup(items[1].Id, g.Id);

        var ungrouped = manager.GetUngroupedItemIds();

        Assert.Equal(new[] { items[0].Id, items[2].Id }, ungrouped);
    }
}
