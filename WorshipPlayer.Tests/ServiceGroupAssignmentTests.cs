using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the đợt 2 T6 pure group-assignment logic
/// (<see cref="ServiceGroupAssignment"/>) and its manager integration
/// (<see cref="ServiceOrderManager.MoveGroup"/> + load-time normalization).
/// Covers the T6 acceptance criteria: assigning an item that already has a group moves
/// it (old group loses the id), deleting an item leaves no orphan ids anywhere,
/// deleting a group keeps its items (they become ungrouped), the collapse flag
/// round-trips through JSON — plus the two gaps T6 closes: header reorder
/// (<c>MoveGroup</c>) and healing of orphan/duplicate memberships when a service file
/// is loaded (<c>Normalize</c>).
/// </summary>
public class ServiceGroupAssignmentTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _filePath;

    public ServiceGroupAssignmentTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"GroupAssignTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _filePath = Path.Combine(_tempFolder, "service.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    // ─── Fixture helpers ──────────────────────────────────────────────

    private static ServiceOrder OrderWithItems(params string[] itemIds)
    {
        var order = new ServiceOrder { Name = "T6 Test" };
        foreach (var id in itemIds)
            order.Items.Add(new ServiceItem { Id = id, Title = id });
        return order;
    }

    private static ServiceGroup Group(string id, string name, int order, params string[] itemIds)
        => new() { Id = id, Name = name, Order = order, Items = itemIds.ToList() };

    // ─── Assign ───────────────────────────────────────────────────────

    [Fact]
    public void Assign_ItemAlreadyInGroup_MovesIt_OldGroupLosesId()
    {
        var order = OrderWithItems("i1", "i2");
        var g1 = Group("g1", "Opening", 0, "i1");
        var g2 = Group("g2", "Message", 1);
        order.Groups.AddRange(new[] { g1, g2 });

        var ok = ServiceGroupAssignment.AssignItem(order, "i1", "g2");

        Assert.True(ok);
        Assert.DoesNotContain("i1", g1.Items);
        Assert.Contains("i1", g2.Items);
    }

    [Fact]
    public void Assign_NullTarget_Ungroups_AndUnknownIds_ReturnFalse()
    {
        var order = OrderWithItems("i1");
        var g1 = Group("g1", "Opening", 0, "i1");
        order.Groups.Add(g1);

        Assert.True(ServiceGroupAssignment.AssignItem(order, "i1", null));
        Assert.Empty(g1.Items);

        Assert.False(ServiceGroupAssignment.AssignItem(order, "no-such-item", "g1"));
        Assert.False(ServiceGroupAssignment.AssignItem(order, "i1", "no-such-group"));
    }

    // ─── Item deletion / orphan purge ─────────────────────────────────

    [Fact]
    public void RemoveItemFromAllGroups_RemovesEveryMembership_NoOrphans()
    {
        var order = OrderWithItems("i2");
        // Corrupt starting point on purpose: "i1" (already deleted from Items) is listed
        // by two groups; the purge must remove every trace of it.
        var g1 = Group("g1", "Opening", 0, "i1", "i2");
        var g2 = Group("g2", "Message", 1, "i1");
        order.Groups.AddRange(new[] { g1, g2 });

        var removed = ServiceGroupAssignment.RemoveItemFromAllGroups(order, "i1");

        Assert.Equal(2, removed);
        Assert.Equal(new[] { "i2" }, g1.Items);
        Assert.Empty(g2.Items);
        Assert.Equal(0, ServiceGroupAssignment.RemoveItemFromAllGroups(order, "i1"));
    }

    // ─── Delete group ─────────────────────────────────────────────────

    [Fact]
    public void DeleteGroup_ItemsSurviveAndBecomeUngrouped_OrderCompacted()
    {
        var order = OrderWithItems("i1", "i2", "i3");
        var g1 = Group("g1", "Opening", 0, "i1", "i2");
        var g2 = Group("g2", "Message", 1, "i3");
        order.Groups.AddRange(new[] { g1, g2 });

        var ok = ServiceGroupAssignment.DeleteGroup(order, "g1");

        Assert.True(ok);
        Assert.Single(order.Groups);
        Assert.Equal(3, order.Items.Count); // items are never deleted with the group
        Assert.Equal(0, g2.Order);          // dense Order after the delete
        // i1/i2 are in no group now (ungrouped); i3 still belongs to g2.
        Assert.DoesNotContain(order.Groups, g => g.Items.Contains("i1"));
        Assert.DoesNotContain(order.Groups, g => g.Items.Contains("i2"));
        Assert.Contains("i3", g2.Items);
        Assert.False(ServiceGroupAssignment.DeleteGroup(order, "g1")); // already gone
    }

    // ─── Move / reorder ───────────────────────────────────────────────

    [Fact]
    public void MoveGroup_SwapsPositions_AndKeepsDenseOrder()
    {
        var order = OrderWithItems();
        var g1 = Group("g1", "A", 0);
        var g2 = Group("g2", "B", 1);
        var g3 = Group("g3", "C", 2);
        order.Groups.AddRange(new[] { g1, g2, g3 });

        Assert.True(ServiceGroupAssignment.MoveGroup(order, "g3", 0));

        Assert.Equal(0, g3.Order);
        Assert.Equal(1, g1.Order);
        Assert.Equal(2, g2.Order);
    }

    [Fact]
    public void MoveGroup_ClampsIndex_AndRejectsUnknownId()
    {
        var order = OrderWithItems();
        var g1 = Group("g1", "A", 0);
        var g2 = Group("g2", "B", 1);
        order.Groups.AddRange(new[] { g1, g2 });

        // Way past the end → clamps to last.
        Assert.True(ServiceGroupAssignment.MoveGroup(order, "g1", 99));
        Assert.Equal(1, g1.Order);
        Assert.Equal(0, g2.Order);

        // Negative → clamps to first.
        Assert.True(ServiceGroupAssignment.MoveGroup(order, "g1", -5));
        Assert.Equal(0, g1.Order);
        Assert.Equal(1, g2.Order);

        Assert.False(ServiceGroupAssignment.MoveGroup(order, "no-such-group", 0));
    }

    // ─── Normalize (load-time healing) ────────────────────────────────

    [Fact]
    public void Normalize_PurgesOrphanIds_KeepsRealMemberships()
    {
        var order = OrderWithItems("i1", "i2");
        var g1 = Group("g1", "Opening", 0, "i1", "ghost-id", "i2");
        order.Groups.Add(g1);

        ServiceGroupAssignment.Normalize(order);

        Assert.Equal(new[] { "i1", "i2" }, g1.Items);
    }

    [Fact]
    public void Normalize_DuplicateMembership_StaysOnlyInFirstGroupByOrder()
    {
        var order = OrderWithItems("i1");
        var later = Group("g2", "Later", 7, "i1");
        var first = Group("g1", "First", 3, "i1");
        order.Groups.AddRange(new[] { later, first });

        ServiceGroupAssignment.Normalize(order);

        Assert.Contains("i1", first.Items);
        Assert.DoesNotContain("i1", later.Items);
    }

    [Fact]
    public void Normalize_CompactsMessyOrder_PreservesCollapsedAndNames()
    {
        var order = OrderWithItems();
        var g1 = Group("g1", "A", 10);
        var g2 = Group("g2", "B", 42);
        g2.IsCollapsed = true;
        order.Groups.AddRange(new[] { g1, g2 });

        ServiceGroupAssignment.Normalize(order);

        Assert.Equal(0, g1.Order);
        Assert.Equal(1, g2.Order);
        Assert.True(g2.IsCollapsed);
        Assert.Equal("B", g2.Name);
    }

    // ─── JSON round-trip of the collapse flag ─────────────────────────

    [Fact]
    public void CollapseFlag_RoundTripsThroughJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
        var order = OrderWithItems("i1");
        var group = Group("g1", "Opening", 0, "i1");
        group.IsCollapsed = true;
        order.Groups.Add(group);

        var json = JsonSerializer.Serialize(order, options);
        var back = JsonSerializer.Deserialize<ServiceOrder>(json, options);

        Assert.NotNull(back);
        var reloaded = Assert.Single(back!.Groups);
        Assert.True(reloaded.IsCollapsed);
        Assert.Equal("Opening", reloaded.Name);
        Assert.Equal(new[] { "i1" }, reloaded.Items);
    }

    // ─── Manager integration ──────────────────────────────────────────

    [Fact]
    public void Manager_MoveGroup_ReordersGroups()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("T6", "2026-10-23");
        var g1 = manager.CreateGroup("A")!;
        var g2 = manager.CreateGroup("B")!;
        var g3 = manager.CreateGroup("C")!;

        Assert.True(manager.MoveGroup(g3.Id, 0));

        Assert.Equal(0, g3.Order);
        Assert.Equal(1, g1.Order);
        Assert.Equal(2, g2.Order);
        Assert.False(manager.MoveGroup("no-such-group", 0));
    }

    [Fact]
    public async Task Manager_LoadAsync_HealsOrphanAndDuplicateGroupIds()
    {
        // A service file whose groups reference a deleted item ("ghost") and list the
        // same item in two groups — as a hand-edited / older-build file could contain.
        var dirtyJson = """
        {
          "id": "svc-t6",
          "name": "Dirty Groups",
          "date": "2026-10-23",
          "items": [
            { "id": "item-1", "type": "Song", "title": "Song One" },
            { "id": "item-2", "type": "Song", "title": "Song Two" }
          ],
          "groups": [
            { "id": "g1", "name": "Opening", "order": 5, "items": ["item-1", "ghost"], "isCollapsed": true },
            { "id": "g2", "name": "Message", "order": 9, "items": ["item-1", "item-2"], "isCollapsed": false }
          ]
        }
        """;
        await File.WriteAllTextAsync(_filePath, dirtyJson);

        var manager = new ServiceOrderManager(_tempFolder);
        var loaded = await manager.LoadAsync(_filePath);

        Assert.NotNull(loaded);
        var g1 = loaded!.Groups.Single(g => g.Id == "g1");
        var g2 = loaded.Groups.Single(g => g.Id == "g2");
        Assert.Equal(new[] { "item-1" }, g1.Items);        // orphan purged
        Assert.Equal(new[] { "item-2" }, g2.Items);        // duplicate kept only in g1
        Assert.Equal(0, g1.Order);                          // Order re-densified
        Assert.Equal(1, g2.Order);
        Assert.True(g1.IsCollapsed);                        // flags preserved by healing
    }
}
