using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Persistence round-trip tests for ServiceOrderManager. These lock in the behavior
/// the UI data-loss fix depends on: an added/edited/removed item must survive a
/// "restart" (a fresh manager reloading the same ServiceOrders directory).
/// </summary>
public class ServiceOrderPersistenceTests : IDisposable
{
    private readonly string _tempFolder;

    public ServiceOrderPersistenceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ServiceOrderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    private string FindSavedFile()
    {
        var files = Directory.GetFiles(_tempFolder, "*.json");
        Assert.Single(files);
        return files[0];
    }

    [Fact]
    public async Task AddItem_ThenSave_SurvivesRestart()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("New Service", DateTime.Today.ToString("yyyy-MM-dd"));
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "Song Item" });
        await manager.SaveAsync();

        // Simulate restart: a brand-new manager loads the file that was written.
        var reloaded = new ServiceOrderManager(_tempFolder);
        var order = await reloaded.LoadAsync(FindSavedFile());

        Assert.NotNull(order);
        Assert.Single(order!.Items);
        Assert.Equal("Song Item", order.Items[0].Title);
        Assert.Equal(ServiceItemType.Song, order.Items[0].Type);
    }

    [Fact]
    public async Task RemoveItem_ThenSave_SurvivesRestart()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("New Service", DateTime.Today.ToString("yyyy-MM-dd"));
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "First" });
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Scripture, Title = "Second" });
        await manager.SaveAsync();

        manager.RemoveItem(0);
        await manager.SaveAsync();

        var reloaded = new ServiceOrderManager(_tempFolder);
        var order = await reloaded.LoadAsync(FindSavedFile());

        Assert.NotNull(order);
        Assert.Single(order!.Items);
        Assert.Equal("Second", order.Items[0].Title);
    }

    [Fact]
    public async Task EditItem_ThenSave_SurvivesRestart()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("New Service", DateTime.Today.ToString("yyyy-MM-dd"));
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "Original Title" });
        await manager.SaveAsync();

        // Mutate the existing item in place (what the edit flow does), then persist.
        manager.CurrentServiceOrder!.Items[0].Title = "Edited Title";
        manager.CurrentServiceOrder!.Items[0].Type = ServiceItemType.Scripture;
        await manager.SaveAsync();

        var reloaded = new ServiceOrderManager(_tempFolder);
        var order = await reloaded.LoadAsync(FindSavedFile());

        Assert.NotNull(order);
        Assert.Single(order!.Items);
        Assert.Equal("Edited Title", order.Items[0].Title);
        Assert.Equal(ServiceItemType.Scripture, order.Items[0].Type);
    }
}
