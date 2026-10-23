using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the pure per-item background resolution (đợt 2 T1 / R1).
/// Covers: item with nothing set → theme; explicit None/Color/Image/Video;
/// value fallbacks (theme color, legacy MediaPath); the legacy mapping for
/// pre-T1 files that only carry <see cref="ServiceItem.BackgroundType"/>;
/// and JSON backward compatibility (old service files load with the new
/// fields null and resolve exactly as they rendered before T1).
/// </summary>
public class ItemBackgroundResolverTests
{
    private static SlideTheme Theme() => new()
    {
        BackgroundType = BackgroundType.SolidColor,
        BackgroundColor = "#112233",
        BackgroundImage = "theme.jpg",
        MotionBackgroundPath = "theme.mp4"
    };

    // ─── Nothing set → theme ───────────────────────────────────────

    [Fact]
    public void ItemWithNothingSet_ResolvesToTheme()
    {
        var resolved = ItemBackgroundResolver.Resolve(new ServiceItem(), Theme());

        Assert.Equal(ItemBackgroundKind.Theme, resolved.Kind);
        Assert.Equal(ItemBackgroundSource.Theme, resolved.Source);
        Assert.Null(resolved.Color);
        Assert.Null(resolved.FilePath);
    }

    [Fact]
    public void NullItem_ResolvesToTheme()
    {
        var resolved = ItemBackgroundResolver.Resolve(null, Theme());

        Assert.Equal(ItemBackgroundKind.Theme, resolved.Kind);
        Assert.Equal(ItemBackgroundSource.Theme, resolved.Source);
    }

    // ─── Explicit kinds ────────────────────────────────────────────

    [Fact]
    public void ExplicitNone_ResolvesToNoneFromItem()
    {
        var item = new ServiceItem { BackgroundKind = ItemBackgroundKind.None };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.None, resolved.Kind);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
        Assert.Null(resolved.Color);
        Assert.Null(resolved.FilePath);
    }

    [Fact]
    public void ExplicitColor_WithOwnColor_UsesItemColor()
    {
        var item = new ServiceItem
        {
            BackgroundKind = ItemBackgroundKind.Color,
            BackgroundColor = "#FF8800"
        };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Color, resolved.Kind);
        Assert.Equal("#FF8800", resolved.Color);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }

    [Fact]
    public void ExplicitColor_WithoutOwnColor_FallsBackToThemeColor()
    {
        var item = new ServiceItem { BackgroundKind = ItemBackgroundKind.Color };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Color, resolved.Kind);
        Assert.Equal("#112233", resolved.Color);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }

    [Fact]
    public void ExplicitImage_WithFilePath_UsesFilePath()
    {
        var item = new ServiceItem
        {
            BackgroundKind = ItemBackgroundKind.Image,
            BackgroundFilePath = "item-bg.jpg",
            MediaPath = "other.mp4"
        };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Image, resolved.Kind);
        Assert.Equal("item-bg.jpg", resolved.FilePath);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }

    [Fact]
    public void ExplicitImage_WithoutFilePath_FallsBackToMediaPath()
    {
        var item = new ServiceItem
        {
            BackgroundKind = ItemBackgroundKind.Image,
            MediaPath = "legacy-bg.png"
        };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Image, resolved.Kind);
        Assert.Equal("legacy-bg.png", resolved.FilePath);
    }

    [Fact]
    public void ExplicitVideo_WithFilePath_UsesFilePath()
    {
        var item = new ServiceItem
        {
            BackgroundKind = ItemBackgroundKind.Video,
            BackgroundFilePath = "item-bg.mp4"
        };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Video, resolved.Kind);
        Assert.Equal("item-bg.mp4", resolved.FilePath);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }

    // ─── Legacy mapping (pre-T1 files: only BackgroundType set) ───

    [Fact]
    public void LegacySolidColor_MapsToColorWithThemeColor()
    {
        var item = new ServiceItem { BackgroundType = BackgroundType.SolidColor };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Color, resolved.Kind);
        Assert.Equal("#112233", resolved.Color);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }

    [Fact]
    public void LegacyImage_MapsToImageWithMediaPath()
    {
        var item = new ServiceItem
        {
            BackgroundType = BackgroundType.Image,
            MediaPath = "old-bg.jpg"
        };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Image, resolved.Kind);
        Assert.Equal("old-bg.jpg", resolved.FilePath);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }

    [Fact]
    public void LegacyVideo_MapsToVideoWithMediaPath()
    {
        var item = new ServiceItem
        {
            BackgroundType = BackgroundType.Video,
            MediaPath = "old-bg.mp4"
        };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Video, resolved.Kind);
        Assert.Equal("old-bg.mp4", resolved.FilePath);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }

    [Fact]
    public void LegacyGradient_MapsToTheme()
    {
        var item = new ServiceItem { BackgroundType = BackgroundType.Gradient };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.Theme, resolved.Kind);
        Assert.Equal(ItemBackgroundSource.Theme, resolved.Source);
    }

    [Fact]
    public void ExplicitKind_OverridesLegacyBackgroundType()
    {
        var item = new ServiceItem
        {
            BackgroundKind = ItemBackgroundKind.None,
            BackgroundType = BackgroundType.Image,
            MediaPath = "old-bg.jpg"
        };

        var resolved = ItemBackgroundResolver.Resolve(item, Theme());

        Assert.Equal(ItemBackgroundKind.None, resolved.Kind);
        Assert.Equal(ItemBackgroundSource.Item, resolved.Source);
    }
}

/// <summary>
/// Persistence / backward-compatibility tests for the T1 background fields,
/// through the real <see cref="ServiceOrderManager"/> serialization path.
/// </summary>
public class ItemBackgroundPersistenceTests : IDisposable
{
    private readonly string _tempFolder;

    public ItemBackgroundPersistenceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ItemBackgroundTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public async Task OldServiceJson_WithoutNewFields_LoadsAndResolvesAsBefore()
    {
        // A pre-T1 service file: no backgroundKind/backgroundColor/backgroundFilePath
        // keys anywhere; one plain item and one legacy item (backgroundType + mediaPath).
        var json = """
        {
          "id": "svc-old",
          "name": "Old Service",
          "date": "2025-01-05",
          "items": [
            { "id": "item-plain", "type": "Song", "title": "Plain" },
            { "id": "item-legacy", "type": "Slides", "title": "Legacy",
              "backgroundType": "Image", "mediaPath": "old-bg.jpg" }
          ]
        }
        """;
        var path = Path.Combine(_tempFolder, "old-service.json");
        await File.WriteAllTextAsync(path, json);

        var manager = new ServiceOrderManager(_tempFolder);
        var order = await manager.LoadAsync(path);

        Assert.NotNull(order);
        Assert.Equal(2, order!.Items.Count);

        var plain = order.Items[0];
        Assert.Null(plain.BackgroundKind);
        Assert.Null(plain.BackgroundColor);
        Assert.Null(plain.BackgroundFilePath);
        var plainResolved = ItemBackgroundResolver.Resolve(plain, new SlideTheme());
        Assert.Equal(ItemBackgroundKind.Theme, plainResolved.Kind);
        Assert.Equal(ItemBackgroundSource.Theme, plainResolved.Source);

        var legacy = order.Items[1];
        Assert.Null(legacy.BackgroundKind);
        Assert.Equal(BackgroundType.Image, legacy.BackgroundType);
        var legacyResolved = ItemBackgroundResolver.Resolve(legacy, new SlideTheme());
        Assert.Equal(ItemBackgroundKind.Image, legacyResolved.Kind);
        Assert.Equal("old-bg.jpg", legacyResolved.FilePath);
        Assert.Equal(ItemBackgroundSource.Item, legacyResolved.Source);
    }

    [Fact]
    public async Task NewBackgroundFields_SurviveSaveLoadRoundTrip()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("New Service", DateTime.Today.ToString("yyyy-MM-dd"));
        manager.AddItem(new ServiceItem
        {
            Type = ServiceItemType.Announcement,
            Title = "Video BG",
            BackgroundKind = ItemBackgroundKind.Video,
            BackgroundFilePath = "bg-loop.mp4"
        });
        manager.AddItem(new ServiceItem
        {
            Type = ServiceItemType.Prayer,
            Title = "Color BG",
            BackgroundKind = ItemBackgroundKind.Color,
            BackgroundColor = "#203040"
        });
        await manager.SaveAsync();

        var savedFile = Assert.Single(Directory.GetFiles(_tempFolder, "*.json"));
        var rawJson = await File.ReadAllTextAsync(savedFile);
        Assert.Contains("\"backgroundKind\": \"Video\"", rawJson);
        Assert.Contains("\"backgroundFilePath\": \"bg-loop.mp4\"", rawJson);
        Assert.Contains("\"backgroundColor\": \"#203040\"", rawJson);

        var reloaded = new ServiceOrderManager(_tempFolder);
        var order = await reloaded.LoadAsync(savedFile);

        Assert.NotNull(order);
        var video = order!.Items[0];
        Assert.Equal(ItemBackgroundKind.Video, video.BackgroundKind);
        Assert.Equal("bg-loop.mp4", video.BackgroundFilePath);

        var color = order.Items[1];
        Assert.Equal(ItemBackgroundKind.Color, color.BackgroundKind);
        Assert.Equal("#203040", color.BackgroundColor);
    }
}
