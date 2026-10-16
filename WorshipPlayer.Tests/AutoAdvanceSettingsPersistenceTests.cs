using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Persistence round-trip tests for per-item AutoAdvanceSettings (roadmap C4).
/// These lock in two guarantees: (1) edited settings survive a save+reload through the
/// existing ServiceOrderManager serialization path, and (2) items without settings do
/// NOT write an autoAdvanceSettings key, so pre-existing service orders are unaffected
/// (backward compatible).
/// </summary>
public class AutoAdvanceSettingsPersistenceTests : IDisposable
{
    private readonly string _tempFolder;

    public AutoAdvanceSettingsPersistenceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"AutoAdvanceTests_{Guid.NewGuid():N}");
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
    public async Task AutoAdvanceSettings_SurviveRoundTrip()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("New Service", DateTime.Today.ToString("yyyy-MM-dd"));
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "First" });
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Announcement, Title = "Second" });

        var target = manager.CurrentServiceOrder!.Items[1];
        manager.CurrentServiceOrder!.Items[0].AutoAdvanceSettings = new ServiceItemSettings
        {
            AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
            AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
            GoToItemId = target.Id,
            AutoAdvanceDelayMs = 3000,
            TransitionFadeTimeMs = 750
        };
        await manager.SaveAsync();

        var reloaded = new ServiceOrderManager(_tempFolder);
        var order = await reloaded.LoadAsync(FindSavedFile());

        Assert.NotNull(order);
        var settings = order!.Items[0].AutoAdvanceSettings;
        Assert.NotNull(settings);
        Assert.Equal(AutoAdvanceAfterCondition.Timer, settings!.AutoAdvanceAfterCondition);
        Assert.Equal(AutoAdvanceGoTo.SpecificItem, settings.AutoAdvanceGoTo);
        Assert.Equal(target.Id, settings.GoToItemId);
        Assert.Equal(3000, settings.AutoAdvanceDelayMs);
        Assert.Equal(750, settings.TransitionFadeTimeMs);
    }

    [Fact]
    public async Task ItemWithoutSettings_OmitsKey_AndReloadsAsNull()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("New Service", DateTime.Today.ToString("yyyy-MM-dd"));
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "Plain" });
        await manager.SaveAsync();

        var path = FindSavedFile();
        var json = await File.ReadAllTextAsync(path);

        // Null settings must not be serialized (WhenWritingNull), so old files stay unchanged.
        Assert.DoesNotContain("autoAdvanceSettings", json);

        var reloaded = new ServiceOrderManager(_tempFolder);
        var order = await reloaded.LoadAsync(path);

        Assert.NotNull(order);
        Assert.Single(order!.Items);
        Assert.Null(order.Items[0].AutoAdvanceSettings);
    }
}
