using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the đợt 2 T8 slide-label navigation logic
/// (<see cref="SlideLabelNavigation"/>): the pure mapping the Operator window uses both to
/// color/name its slide chips and to turn a label hotkey keystroke into a target slide.
/// Covers: hotkey lookup (case-insensitive, empty never matches), slide→label resolution by
/// explicit LabelId through the section bridge, the legacy name fallback (including when
/// the song's sections are not loaded), a deleted label resolving to no label, and the
/// first-slide jump target when several sections share one label.
/// </summary>
public class SlideLabelNavigationTests
{
    private static List<SlideLabelDefinition> Catalog() => new()
    {
        new() { Id = "verse-1", Name = "Verse 1", Color = "#4A90D9", Hotkey = "V" },
        new() { Id = "chorus",  Name = "Chorus",  Color = "#E4572E", Hotkey = "C" },
        new() { Id = "bridge",  Name = "Bridge",  Color = "#D6336C", Hotkey = "" },
    };

    private static Slide S(string sectionName) => new() { SectionName = sectionName };

    // ── FindLabelByHotkey ────────────────────────────────────────────────

    [Fact]
    public void FindLabelByHotkey_MatchesCaseInsensitivelyAndTrims()
    {
        Assert.Equal("chorus", SlideLabelNavigation.FindLabelByHotkey(Catalog(), "c")?.Id);
        Assert.Equal("verse-1", SlideLabelNavigation.FindLabelByHotkey(Catalog(), " V ")?.Id);
    }

    [Fact]
    public void FindLabelByHotkey_EmptyOrUnknown_ReturnsNull()
    {
        Assert.Null(SlideLabelNavigation.FindLabelByHotkey(Catalog(), ""));
        Assert.Null(SlideLabelNavigation.FindLabelByHotkey(Catalog(), "   "));
        Assert.Null(SlideLabelNavigation.FindLabelByHotkey(Catalog(), null));
        Assert.Null(SlideLabelNavigation.FindLabelByHotkey(Catalog(), "X"));
        Assert.Null(SlideLabelNavigation.FindLabelByHotkey(null, "C"));
    }

    // ── ResolveLabelForSlide ─────────────────────────────────────────────

    [Fact]
    public void ResolveLabelForSlide_ExplicitLabelId_WinsOverSectionName()
    {
        // Section is NAMED "Verse 1" but explicitly labelled Chorus: the LabelId must win.
        var sections = new List<Section> { new() { Name = "Verse 1", LabelId = "chorus" } };
        var label = SlideLabelNavigation.ResolveLabelForSlide(S("Verse 1"), sections, Catalog());
        Assert.Equal("chorus", label?.Id);
    }

    [Fact]
    public void ResolveLabelForSlide_LegacySection_FallsBackToNameMatch()
    {
        var sections = new List<Section> { new() { Name = "Chorus" } };
        var label = SlideLabelNavigation.ResolveLabelForSlide(S("chorus"), sections, Catalog());
        Assert.Equal("chorus", label?.Id);
    }

    [Fact]
    public void ResolveLabelForSlide_SectionsNotLoaded_StillResolvesByName()
    {
        var label = SlideLabelNavigation.ResolveLabelForSlide(S("Verse 1"), null, Catalog());
        Assert.Equal("verse-1", label?.Id);
    }

    [Fact]
    public void ResolveLabelForSlide_DeletedLabel_ResolvesToNoLabel()
    {
        var sections = new List<Section> { new() { Name = "Tag", LabelId = "deleted-id" } };
        Assert.Null(SlideLabelNavigation.ResolveLabelForSlide(S("Tag"), sections, Catalog()));
    }

    [Fact]
    public void ResolveLabelForSlide_UnlabelledOrBlank_ReturnsNull()
    {
        var sections = new List<Section> { new() { Name = "Intro" } };
        Assert.Null(SlideLabelNavigation.ResolveLabelForSlide(S("Intro"), sections, Catalog()));
        Assert.Null(SlideLabelNavigation.ResolveLabelForSlide(S(""), sections, Catalog()));
        Assert.Null(SlideLabelNavigation.ResolveLabelForSlide(null, sections, Catalog()));
    }

    // ── FindFirstSlideIndexByHotkey ──────────────────────────────────────

    [Fact]
    public void FindFirstSlideIndexByHotkey_ReturnsFirstSlideOfFirstMatchingSection()
    {
        var sections = new List<Section>
        {
            new() { Name = "Verse 1" },
            new() { Name = "Chorus" },
            new() { Name = "Verse 2" },
            new() { Name = "Chorus" }, // repeated section, same label
        };
        var slides = new List<Slide> { S("Verse 1"), S("Verse 1"), S("Chorus"), S("Chorus"), S("Verse 2"), S("Chorus") };

        // "C" → Chorus: first Chorus slide is index 2 (first matching section wins).
        Assert.Equal(2, SlideLabelNavigation.FindFirstSlideIndexByHotkey(slides, sections, Catalog(), "C"));
        Assert.Equal(0, SlideLabelNavigation.FindFirstSlideIndexByHotkey(slides, sections, Catalog(), "v"));
    }

    [Fact]
    public void FindFirstSlideIndexByHotkey_UsesExplicitLabelIdNotJustName()
    {
        var sections = new List<Section>
        {
            new() { Name = "Part A", LabelId = "chorus" },
            new() { Name = "Part B", LabelId = "verse-1" },
        };
        var slides = new List<Slide> { S("Part A"), S("Part B") };

        Assert.Equal(0, SlideLabelNavigation.FindFirstSlideIndexByHotkey(slides, sections, Catalog(), "C"));
        Assert.Equal(1, SlideLabelNavigation.FindFirstSlideIndexByHotkey(slides, sections, Catalog(), "V"));
    }

    [Fact]
    public void FindFirstSlideIndexByHotkey_NoMatch_ReturnsMinusOne()
    {
        var sections = new List<Section> { new() { Name = "Verse 1" } };
        var slides = new List<Slide> { S("Verse 1") };

        Assert.Equal(-1, SlideLabelNavigation.FindFirstSlideIndexByHotkey(slides, sections, Catalog(), "C")); // label exists, no slide carries it
        Assert.Equal(-1, SlideLabelNavigation.FindFirstSlideIndexByHotkey(slides, sections, Catalog(), "Z")); // no such hotkey
        Assert.Equal(-1, SlideLabelNavigation.FindFirstSlideIndexByHotkey(slides, sections, Catalog(), ""));  // empty hotkey
        Assert.Equal(-1, SlideLabelNavigation.FindFirstSlideIndexByHotkey(new List<Slide>(), sections, Catalog(), "V"));
        Assert.Equal(-1, SlideLabelNavigation.FindFirstSlideIndexByHotkey(null, sections, Catalog(), "V"));
    }
}
