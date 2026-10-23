using System.Text.Json;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Đợt 2 T10 / R6.2 — Service Item Order at service level. Covers the pure
/// <see cref="ServiceItemSlideOrder"/> (identity when unset, reorder, new slides
/// appended at the end, dead ids skipped, move/duplicate helpers), the R6.2
/// ISOLATION guarantee (editing item A never changes the Song or item B), the
/// JSON persistence of <see cref="ServiceItem.SlideOrder"/> (additive, omitted
/// when null, old files load as null), and the live wiring: the source-linked
/// <see cref="PresenterEngine"/> loads the current item's slides through the
/// item order. Priority T10 &gt; T9 &gt; original is structural: the engine and
/// the operator preview resolve the song's slides through the existing pipeline
/// first (T9 lives inside generation), then apply this order on top.
/// </summary>
public class ServiceItemSlideOrderTests
{
    private static Slide S(string id, string text) => new()
    {
        Id = id,
        Type = SlideType.Lyrics,
        SectionName = "VERSE 1",
        Content = new SlideContent { Text = text }
    };

    private static List<Slide> ThreeSlides() => new() { S("a", "A"), S("b", "B"), S("c", "C") };

    private static List<string> Ids(IReadOnlyList<Slide> slides) => slides.Select(s => s.Id).ToList();

    // ─── Apply: no order → pre-T10 identity ─────────────────────────────

    [Fact]
    public void NoOrder_ReturnsIdenticalSequenceAndObjects()
    {
        var slides = ThreeSlides();
        var item = new ServiceItem { Slides = slides };
        Assert.Null(item.SlideOrder);
        Assert.False(ServiceItemSlideOrder.HasCustomOrder(item));

        var result = ServiceItemSlideOrder.Apply(slides, item.SlideOrder);

        Assert.Equal(new[] { "a", "b", "c" }, Ids(result));
        Assert.Same(slides[0], result[0]);
        Assert.Same(slides[1], result[1]);
        Assert.Same(slides[2], result[2]);
    }

    [Fact]
    public void Apply_NullOrEmptySlides_ReturnsEmpty()
    {
        Assert.Empty(ServiceItemSlideOrder.Apply(null, new List<string> { "a" }));
        Assert.Empty(ServiceItemSlideOrder.Apply(new List<Slide>(), new List<string> { "a" }));
    }

    // ─── Apply: stored order semantics ──────────────────────────────────

    [Fact]
    public void StoredOrder_ReordersSlides()
    {
        var slides = ThreeSlides();

        var result = ServiceItemSlideOrder.Apply(slides, new List<string> { "c", "a", "b" });

        Assert.Equal(new[] { "c", "a", "b" }, Ids(result));
        Assert.Same(slides[2], result[0]);
        Assert.Same(slides[0], result[1]);
        Assert.Same(slides[1], result[2]);
    }

    [Fact]
    public void NewSlideNotInOrder_AppendsAtEndInOriginalOrder()
    {
        var slides = ThreeSlides();
        slides.Add(S("d", "D")); // generated after the order was authored

        var result = ServiceItemSlideOrder.Apply(slides, new List<string> { "c", "a", "b" });

        Assert.Equal(new[] { "c", "a", "b", "d" }, Ids(result));
    }

    [Fact]
    public void DeadIdInOrder_IsSkippedWithoutException()
    {
        var slides = ThreeSlides();

        var result = ServiceItemSlideOrder.Apply(slides, new List<string> { "c", "ghost", "a" });

        // "ghost" is gone; "b" was never referenced → appended at the end.
        Assert.Equal(new[] { "c", "a", "b" }, Ids(result));
    }

    [Fact]
    public void EmptyOrderList_KeepsAllSlidesInOriginalOrder()
    {
        var slides = ThreeSlides();

        var result = ServiceItemSlideOrder.Apply(slides, new List<string>());

        Assert.Equal(new[] { "a", "b", "c" }, Ids(result));
    }

    [Fact]
    public void RepeatedIdInOrder_EmitsSlideTwice()
    {
        var slides = ThreeSlides();

        var result = ServiceItemSlideOrder.Apply(slides, new List<string> { "a", "b", "b", "c" });

        Assert.Equal(new[] { "a", "b", "b", "c" }, Ids(result));
        Assert.Same(result[1], result[2]);
    }

    // ─── Move / CaptureOrder ────────────────────────────────────────────

    [Fact]
    public void Move_SwapsAndClampsAtBothEnds()
    {
        var order = new List<string> { "a", "b", "c" };

        Assert.Equal(new[] { "b", "a", "c" }, ServiceItemSlideOrder.Move(order, 1, -1));
        Assert.Equal(new[] { "a", "c", "b" }, ServiceItemSlideOrder.Move(order, 1, 1));
        // Clamped: moving the first entry left / last entry right is a no-op copy.
        Assert.Equal(new[] { "a", "b", "c" }, ServiceItemSlideOrder.Move(order, 0, -1));
        Assert.Equal(new[] { "a", "b", "c" }, ServiceItemSlideOrder.Move(order, 2, 5));
        // Out of range: unchanged; input never mutated.
        Assert.Equal(new[] { "a", "b", "c" }, ServiceItemSlideOrder.Move(order, 9, 1));
        Assert.Equal(new[] { "a", "b", "c" }, order);
    }

    [Fact]
    public void CaptureOrder_MaterializesCurrentSequence()
    {
        Assert.Equal(new[] { "a", "b", "c" }, ServiceItemSlideOrder.CaptureOrder(ThreeSlides()));
        Assert.Empty(ServiceItemSlideOrder.CaptureOrder(null));
    }

    // ─── Duplicate ──────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_TwoSlidesSameContentDifferentIds_BothInOrder()
    {
        var slides = ThreeSlides();

        var (newSlides, newOrder) = ServiceItemSlideOrder.Duplicate(slides, null, "b");

        Assert.Equal(4, newSlides.Count);
        Assert.Equal(4, newOrder.Count);

        var original = newSlides[1];
        var copy = newSlides[2];
        Assert.Equal("b", original.Id);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.False(string.IsNullOrEmpty(copy.Id));
        Assert.Equal(original.Content.Text, copy.Content.Text);
        Assert.Equal(original.SectionName, copy.SectionName);
        Assert.NotSame(original, copy);
        Assert.NotSame(original.Content, copy.Content);

        // Both ids are in the order, copy immediately after the original —
        // and applying the result shows exactly that sequence.
        int pos = newOrder.IndexOf("b");
        Assert.True(pos >= 0);
        Assert.Equal(copy.Id, newOrder[pos + 1]);
        Assert.Equal(Ids(newSlides), Ids(ServiceItemSlideOrder.Apply(newSlides, newOrder)));

        // Inputs untouched.
        Assert.Equal(3, slides.Count);
        Assert.Equal(new[] { "a", "b", "c" }, Ids(slides));
    }

    [Fact]
    public void Duplicate_WithPartialStoredOrder_CopyLandsAfterOriginalInEffectiveOrder()
    {
        var slides = ThreeSlides();
        // Stored order mentions only "c" and "a" ("b" displays via the append rule).
        var (newSlides, newOrder) = ServiceItemSlideOrder.Duplicate(
            slides, new List<string> { "c", "a" }, "b");

        var applied = ServiceItemSlideOrder.Apply(newSlides, newOrder);
        Assert.Equal(4, applied.Count);
        int pos = Ids(applied).IndexOf("b");
        Assert.True(pos >= 0);
        Assert.Equal(applied[pos].Content.Text, applied[pos + 1].Content.Text);
        Assert.NotEqual(applied[pos].Id, applied[pos + 1].Id);
    }

    [Fact]
    public void Duplicate_UnknownSlideId_ReturnsInputsUnchanged()
    {
        var slides = ThreeSlides();

        var (newSlides, newOrder) = ServiceItemSlideOrder.Duplicate(
            slides, new List<string> { "c", "a", "b" }, "nope");

        Assert.Equal(new[] { "a", "b", "c" }, Ids(newSlides));
        Assert.Equal(new[] { "c", "a", "b" }, newOrder);
    }

    // ─── Isolation (R6.2): item A edits never touch Song or item B ─────

    [Fact]
    public void EditingItemA_DoesNotChangeSongOrItemB()
    {
        var song = new Song
        {
            Title = "Shared Song",
            Sections = new List<Section>
            {
                new() { Name = "Verse 1", Text = "Verse line" },
                new() { Name = "Chorus", Text = "Chorus line" }
            },
            CustomSlideOrder = new List<SongSlideOrderEntry>
            {
                new() { SectionName = "Verse 1" },
                new() { SectionName = "Chorus" }
            }
        };
        string songJsonBefore = JsonSerializer.Serialize(song);

        var itemA = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Shared Song",
            SongFolder = "shared",
            Slides = ThreeSlides()
        };
        var itemB = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Shared Song",
            SongFolder = "shared",
            Slides = ThreeSlides()
        };
        var itemBResultBefore = Ids(ServiceItemSlideOrder.ApplyForItem(itemB, itemB.Slides));

        // Edit item A the way the operator UI does: duplicate a slide, then move it.
        var (aSlides, aOrder) = ServiceItemSlideOrder.Duplicate(itemA.Slides, itemA.SlideOrder, "a");
        aOrder = ServiceItemSlideOrder.Move(aOrder, 1, 1);
        itemA.Slides = ServiceItemSlideOrder.Apply(aSlides, aOrder);
        itemA.SlideOrder = aOrder;

        // The song (incl. its T9 custom order) is byte-identical.
        Assert.Equal(songJsonBefore, JsonSerializer.Serialize(song));

        // Item B is untouched: still no order, same resolved sequence.
        Assert.Null(itemB.SlideOrder);
        Assert.Equal(new[] { "a", "b", "c" }, Ids(itemB.Slides));
        Assert.Equal(itemBResultBefore, Ids(ServiceItemSlideOrder.ApplyForItem(itemB, itemB.Slides)));

        // And item A really did change (the edit wasn't a no-op).
        Assert.NotNull(itemA.SlideOrder);
        Assert.Equal(4, itemA.Slides.Count);
    }

    // ─── Engine wiring: the live item loads through its item order ─────

    [Fact]
    public void Engine_CurrentItemSlides_FollowItemOrder()
    {
        var manager = new ServiceOrderManager(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        var engine = new PresenterEngine(manager, new PresenterSettings());

        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Ordered Song",
            Slides = ThreeSlides(),
            SlideOrder = new List<string> { "c", "a", "b" }
        };
        manager.AddItem(item); // first item → ItemChanged → engine loads its slides

        Assert.Equal(new[] { "c", "a", "b" }, Ids(engine.CurrentSlides));
    }

    [Fact]
    public void Engine_CurrentItemWithoutOrder_LoadsSlidesAsBefore()
    {
        var manager = new ServiceOrderManager(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        var engine = new PresenterEngine(manager, new PresenterSettings());

        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Plain Song",
            Slides = ThreeSlides()
        };
        manager.AddItem(item);

        Assert.Equal(new[] { "a", "b", "c" }, Ids(engine.CurrentSlides));
    }
}

/// <summary>
/// Persistence of <see cref="ServiceItem.SlideOrder"/> through the real
/// <see cref="ServiceOrderManager"/> save/load: the field round-trips, appears
/// in the JSON only when set, and pre-T10 service files (no "slideOrder" key)
/// load with the order null — exactly the pre-T10 behavior.
/// </summary>
public class ServiceItemSlideOrderPersistenceTests : IDisposable
{
    private readonly string _tempFolder;

    public ServiceItemSlideOrderPersistenceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ItemSlideOrderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public async Task SlideOrder_SurvivesSaveLoadRoundTrip_AndIsOmittedWhenNull()
    {
        var manager = new ServiceOrderManager(_tempFolder);
        manager.CreateNew("Order Service", DateTime.Today.ToString("yyyy-MM-dd"));
        manager.AddItem(new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Ordered",
            SlideOrder = new List<string> { "s2", "s1" }
        });
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "Plain" });
        await manager.SaveAsync();

        var savedFile = Assert.Single(Directory.GetFiles(_tempFolder, "*.json"));
        var rawJson = await File.ReadAllTextAsync(savedFile);
        Assert.Contains("\"slideOrder\"", rawJson);
        // The plain item must not carry the key at all (WhenWritingNull convention).
        var plainSegment = rawJson.Substring(rawJson.IndexOf("Plain", StringComparison.Ordinal));
        Assert.DoesNotContain("slideOrder", plainSegment);

        var reloaded = new ServiceOrderManager(_tempFolder);
        var order = await reloaded.LoadAsync(savedFile);

        Assert.NotNull(order);
        Assert.Equal(new[] { "s2", "s1" }, order!.Items[0].SlideOrder);
        Assert.Null(order.Items[1].SlideOrder);
    }

    [Fact]
    public async Task OldServiceJson_WithoutSlideOrder_LoadsWithNullOrder()
    {
        var json = """
        {
          "id": "svc-old",
          "name": "Old Service",
          "date": "2025-01-05",
          "items": [
            { "id": "item-1", "type": "Song", "title": "Old Song" }
          ]
        }
        """;
        var path = Path.Combine(_tempFolder, "old-service.json");
        await File.WriteAllTextAsync(path, json);

        var manager = new ServiceOrderManager(_tempFolder);
        var order = await manager.LoadAsync(path);

        Assert.NotNull(order);
        Assert.Null(order!.Items[0].SlideOrder);
        Assert.False(ServiceItemSlideOrder.HasCustomOrder(order.Items[0]));
    }
}
