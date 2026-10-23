using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Đợt 2 T9 / R6.1 — Custom Slide Order at song level. Covers the pure
/// <see cref="SongSlideOrderResolver"/> (original order when unset, repeats,
/// Blank units, missing sections skipped), the JSON round-trip of
/// <see cref="Song.CustomSlideOrder"/> (additive, omitted when null, old
/// song.json without the field loads as null), and the wiring into the live
/// slide pipeline (<see cref="PresenterEngine.GenerateSlidesForSong"/>) that
/// song items use. (LyricSlideGenerator.GenerateFromSong walks the same
/// resolver; it is not source-linked into this test project.)
/// </summary>
public class SongSlideOrderResolverTests
{
    private static Song ThreeSectionSong() => new()
    {
        Title = "Order Song",
        Sections = new List<Section>
        {
            new() { Name = "Verse 1", Text = "Verse one line" },
            new() { Name = "Chorus", Text = "Chorus line" },
            new() { Name = "Verse 2", Text = "Verse two line" }
        }
    };

    private static List<string?> Names(IReadOnlyList<SongSlideOrderResolver.SlideUnit> units) =>
        units.Select(u => u.Section?.Name).ToList();

    // ─── Resolver: no custom order ──────────────────────────────────────

    [Fact]
    public void NoCustomOrder_ReturnsSectionsInOriginalOrder()
    {
        var song = ThreeSectionSong();
        Assert.Null(song.CustomSlideOrder);
        Assert.False(SongSlideOrderResolver.HasCustomOrder(song));

        var units = SongSlideOrderResolver.ResolveUnits(song);

        Assert.Equal(new string?[] { "Verse 1", "Chorus", "Verse 2" }, Names(units));
        Assert.All(units, u => Assert.False(u.IsBlank));
        // Same section objects, not copies.
        Assert.Same(song.Sections[0], units[0].Section);
        Assert.Same(song.Sections[1], units[1].Section);
        Assert.Same(song.Sections[2], units[2].Section);
    }

    // ─── Resolver: custom order semantics ───────────────────────────────

    [Fact]
    public void CustomOrder_RepeatedChorus_RepeatsUnit()
    {
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>
        {
            SongSlideOrderEntry.ForSection("Verse 1"),
            SongSlideOrderEntry.ForSection("Chorus"),
            SongSlideOrderEntry.ForSection("Verse 2"),
            SongSlideOrderEntry.ForSection("Chorus")
        };
        Assert.True(SongSlideOrderResolver.HasCustomOrder(song));

        var units = SongSlideOrderResolver.ResolveUnits(song);

        Assert.Equal(new string?[] { "Verse 1", "Chorus", "Verse 2", "Chorus" }, Names(units));
        // Both Chorus units reference the one Chorus section object.
        Assert.Same(song.Sections[1], units[1].Section);
        Assert.Same(song.Sections[1], units[3].Section);
    }

    [Fact]
    public void CustomOrder_BlankEntry_YieldsOneBlankUnitAtItsPosition()
    {
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>
        {
            SongSlideOrderEntry.ForSection("Verse 1"),
            SongSlideOrderEntry.BlankSlide(),
            SongSlideOrderEntry.ForSection("Chorus")
        };

        var units = SongSlideOrderResolver.ResolveUnits(song);

        Assert.Equal(3, units.Count);
        Assert.Equal("Verse 1", units[0].Section?.Name);
        Assert.True(units[1].IsBlank);
        Assert.Null(units[1].Section);
        Assert.Equal("Chorus", units[2].Section?.Name);
    }

    [Fact]
    public void CustomOrder_MissingSection_IsSkipped_WithoutException()
    {
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>
        {
            SongSlideOrderEntry.ForSection("Bridge"), // not in the song (deleted/renamed)
            SongSlideOrderEntry.ForSection("Chorus")
        };

        var units = SongSlideOrderResolver.ResolveUnits(song);

        Assert.Equal(new string?[] { "Chorus" }, Names(units));
    }

    [Fact]
    public void CustomOrder_NameMatch_IsCaseInsensitive()
    {
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>
        {
            SongSlideOrderEntry.ForSection("chorus")
        };

        var units = SongSlideOrderResolver.ResolveUnits(song);

        Assert.Equal(new string?[] { "Chorus" }, Names(units));
    }

    [Fact]
    public void CustomOrder_EmptyList_YieldsNoUnits_DifferentFromNull()
    {
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>();

        var units = SongSlideOrderResolver.ResolveUnits(song);

        Assert.Empty(units);
    }

    [Fact]
    public void CustomOrder_BlankAndWhitespaceEntries_AreSkipped()
    {
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>
        {
            new() { SectionName = null },
            new() { SectionName = "   " },
            SongSlideOrderEntry.ForSection("Verse 2")
        };

        var units = SongSlideOrderResolver.ResolveUnits(song);

        Assert.Equal(new string?[] { "Verse 2" }, Names(units));
    }

    [Fact]
    public void ResolveUnits_NullSong_ReturnsEmpty()
    {
        Assert.Empty(SongSlideOrderResolver.ResolveUnits(null));
        Assert.False(SongSlideOrderResolver.HasCustomOrder(null));
    }

    // ─── JSON persistence ───────────────────────────────────────────────

    private static readonly JsonSerializerOptions SongJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    [Fact]
    public void JsonRoundTrip_PreservesCustomSlideOrder()
    {
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>
        {
            SongSlideOrderEntry.ForSection("Verse 1"),
            SongSlideOrderEntry.BlankSlide(),
            SongSlideOrderEntry.ForSection("Chorus"),
            SongSlideOrderEntry.ForSection("Chorus")
        };

        string json = JsonSerializer.Serialize(song, SongJsonOptions);
        Assert.Contains("customSlideOrder", json);

        var loaded = JsonSerializer.Deserialize<Song>(json, SongJsonOptions);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.CustomSlideOrder);
        Assert.Equal(4, loaded.CustomSlideOrder!.Count);
        Assert.Equal("Verse 1", loaded.CustomSlideOrder[0].SectionName);
        Assert.False(loaded.CustomSlideOrder[0].IsBlank);
        Assert.True(loaded.CustomSlideOrder[1].IsBlank);
        Assert.Equal("Chorus", loaded.CustomSlideOrder[2].SectionName);
        Assert.Equal("Chorus", loaded.CustomSlideOrder[3].SectionName);

        // And the loaded order still resolves identically.
        var units = SongSlideOrderResolver.ResolveUnits(loaded);
        Assert.Equal(new string?[] { "Verse 1", null, "Chorus", "Chorus" }, Names(units));
        Assert.True(units[1].IsBlank);
    }

    [Fact]
    public void Json_NullCustomOrder_IsOmitted_AndOldJsonLoadsAsNull()
    {
        var song = ThreeSectionSong(); // CustomSlideOrder stays null

        string json = JsonSerializer.Serialize(song, SongJsonOptions);
        Assert.DoesNotContain("customSlideOrder", json);

        // A pre-T9 song.json has no customSlideOrder key at all.
        const string legacyJson = "{\"title\":\"Legacy\",\"sections\":[{\"name\":\"Verse 1\",\"text\":\"Line\"}]}";
        var legacy = JsonSerializer.Deserialize<Song>(legacyJson, SongJsonOptions);
        Assert.NotNull(legacy);
        Assert.Null(legacy!.CustomSlideOrder);
        // …and resolves to the original section order.
        Assert.Equal(new string?[] { "Verse 1" },
            Names(SongSlideOrderResolver.ResolveUnits(legacy)));
    }

    // ─── Pipeline: PresenterEngine.GenerateSlidesForSong ────────────────

    private static PresenterEngine NewEngine()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        return new PresenterEngine(manager, new PresenterSettings());
    }

    [Fact]
    public void GenerateSlides_WithCustomOrder_FollowsOrder_WithRepeatAndBlankSlide()
    {
        var engine = NewEngine();
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>
        {
            SongSlideOrderEntry.ForSection("Chorus"),
            SongSlideOrderEntry.ForSection("Verse 1"),
            SongSlideOrderEntry.BlankSlide(),
            SongSlideOrderEntry.ForSection("Chorus")
        };

        var slides = engine.GenerateSlidesForSong(song);

        // One block per section text → one slide per section occurrence + 1 blank.
        Assert.Equal(4, slides.Count);
        Assert.Equal("Chorus", slides[0].SectionName);
        Assert.Equal("Verse 1", slides[1].SectionName);
        Assert.Equal(SlideType.Blank, slides[2].Type);
        Assert.True(string.IsNullOrEmpty(slides[2].Content.Text));
        Assert.Equal("", slides[2].SectionName);
        Assert.Equal("Chorus", slides[3].SectionName);
        Assert.Equal(new[] { 0, 1, 2, 3 }, slides.Select(s => s.Order).ToArray());
        Assert.Equal(SlideType.Lyrics, slides[0].Type);
        Assert.Equal(SlideType.Lyrics, slides[3].Type);
    }

    [Fact]
    public void GenerateSlides_NoCustomOrder_KeepsOriginalSectionOrder()
    {
        var engine = NewEngine();
        var song = ThreeSectionSong();

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(new[] { "Verse 1", "Chorus", "Verse 2" },
            slides.Select(s => s.SectionName).ToArray());
        Assert.All(slides, s => Assert.Equal(SlideType.Lyrics, s.Type));
    }

    [Fact]
    public void GenerateSlides_CustomOrder_MissingSection_GeneratesOnlyExisting()
    {
        var engine = NewEngine();
        var song = ThreeSectionSong();
        song.CustomSlideOrder = new List<SongSlideOrderEntry>
        {
            SongSlideOrderEntry.ForSection("Bridge"), // deleted since the order was set
            SongSlideOrderEntry.ForSection("Verse 2")
        };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Equal(new[] { "Verse 2" }, slides.Select(s => s.SectionName).ToArray());
    }
}
