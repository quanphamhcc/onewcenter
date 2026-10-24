using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// F2/F3: ServiceItem slides are the single source of truth. LoadSong must keep
/// pre-baked item slides and fire no duplicate SlideChanged; GenerateSlidesFromSong
/// is a fallback only (item has no slides).
/// </summary>
public class PresenterEngineSlideSourceTests
{
    // Build an engine whose current item carries the given slides. The engine is
    // constructed BEFORE AddItem so its ItemChanged handler loads the item slides
    // and fires the single slide-0 emit (AddItem only fires ItemChanged for the
    // first item). Callers subscribe their SlideChanged counter AFTER this returns,
    // so that initial emit is not counted.
    private static PresenterEngine EngineWithCurrentItem(ServiceItem item)
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        var engine = new PresenterEngine(manager, new PresenterSettings());
        manager.AddItem(item);
        return engine;
    }

    private static ServiceItem SongItemWithSlides()
    {
        return new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Pre-baked Song",
            Slides = new List<Slide>
            {
                new Slide
                {
                    Type = SlideType.Lyrics,
                    SectionName = "VERSE 1",
                    Content = new SlideContent { Text = "[C]Verse one line" }
                },
                new Slide
                {
                    Type = SlideType.Lyrics,
                    SectionName = "CHORUS 1",
                    Content = new SlideContent { Text = "[G]Chorus one line" }
                }
            }
        };
    }

    [Fact]
    public void LoadSong_ItemHasSlides_KeepsItemSlides_AndFiresNoExtraSlideChanged()
    {
        var item = SongItemWithSlides();
        var engine = EngineWithCurrentItem(item);

        int slideChangedCount = 0;
        engine.SlideChanged += (_, _) => slideChangedCount++;

        // A song whose own sections are empty — if LoadSong wrongly regenerated,
        // the item slides would be replaced with an empty set.
        var emptyTextSong = new Song { Title = "Pre-baked Song", OriginalKey = "C" };

        engine.OnSongLoaded(emptyTextSong);

        // Item slides preserved (count + section names), NOT regenerated to empty.
        Assert.Equal(2, engine.CurrentSlides.Count);
        Assert.Equal("VERSE 1", engine.CurrentSlides[0].SectionName);
        Assert.Equal("CHORUS 1", engine.CurrentSlides[1].SectionName);

        // LoadSong fired NO additional SlideChanged beyond the OnServiceItemChanged
        // slide-0 emit that happened before we subscribed. So the counter stays 0.
        Assert.Equal(0, slideChangedCount);
    }

    [Fact]
    public void LoadSong_ItemHasNoSlides_FallsBackToGeneration_AndFiresOnce()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Fallback Song",
            Slides = new List<Slide>() // no pre-baked slides
        };
        var engine = EngineWithCurrentItem(item);

        int slideChangedCount = 0;
        engine.SlideChanged += (_, _) => slideChangedCount++;

        var song = new Song
        {
            Title = "Fallback Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "Verse 1", Text = "Amazing grace how sweet the sound" }
            }
        };

        engine.OnSongLoaded(song);

        Assert.True(engine.CurrentSlides.Count > 0);
        Assert.Equal("Verse 1", engine.CurrentSlides[0].SectionName, ignoreCase: true);

        // Fallback fired SlideChanged exactly once (slide 0).
        Assert.Equal(1, slideChangedCount);
    }

    [Fact]
    public void LoadStandaloneSong_ItemHasSlides_StillGeneratesSongSlides_AndFiresSlideChanged()
    {
        // Current item carries pre-baked slides — LoadSong would be gated by the
        // F2/F3 guard. LoadStandaloneSong must ignore that and generate the song's
        // own slides anyway (the Songs-tab path).
        var item = SongItemWithSlides();
        var engine = EngineWithCurrentItem(item);

        int slideChangedCount = 0;
        engine.SlideChanged += (_, _) => slideChangedCount++;

        var song = new Song
        {
            Title = "The Blessing",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "Verse 1", Text = "[B]The Lord bless you and keep you" }
            }
        };

        engine.LoadStandaloneSong(song);

        // The standalone song's slides were generated, replacing the pre-baked item slides.
        Assert.True(engine.TotalSlides > 0);
        Assert.Equal("Verse 1", engine.CurrentSlides[0].SectionName, ignoreCase: true);

        // SlideChanged fired for the standalone load (slide 0).
        Assert.Equal(1, slideChangedCount);
    }

    // Field report 2026-10-24 (video): tapping/clicking a slide thumbnail — mouse
    // AND touch — never moved the live slide; the AUDIENCE preview stayed on slide 1
    // (INTRO, blank). Root cause: the thumbnail click always re-ran GoToItem, which
    // reloads engine slides from ServiceItem.Slides — empty for library-linked /
    // setlist-added songs — so GoToSlide was a no-op and the later async backtrack
    // LoadSong snapped back to slide 0. The fix's engine half is covered here:
    // LoadItemSlides gives the engine the section's resolved slides, GoToSlide then
    // lands on the tapped slide, and a late LoadSong of the SAME song must preserve
    // that slide instead of resetting to 0.
    [Fact]
    public void LoadItemSlides_ThenGoToSlide_ThenSameSongLoad_KeepsTappedSlide()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Added Song",
            Slides = new List<Slide>() // added via Songs tab / setlist: no baked slides
        };
        var engine = EngineWithCurrentItem(item);

        var song = new Song
        {
            Title = "Added Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                new Section { Name = "Verse 1", Text = "First line here" },
                new Section { Name = "Chorus", Text = "Chorus line here" },
                new Section { Name = "Bridge", Text = "Bridge line here" }
            }
        };

        // Before any load the engine has no slides, so a tap alone is a no-op
        // (this is the old failure mode, documented, not the fix).
        engine.GoToSlide(1);
        Assert.Equal(-1, engine.CurrentSlideIndex);

        // The thumbnail-click fix: load the section's resolved slides, then go to
        // the tapped slide (index 1 = Chorus).
        var resolved = engine.GenerateSlidesForSong(song);
        Assert.Equal(3, resolved.Count);
        engine.LoadItemSlides(resolved);
        engine.GoToSlide(1);
        Assert.Equal(1, engine.CurrentSlideIndex);
        Assert.Equal("Chorus", engine.CurrentSlides[1].SectionName, ignoreCase: true);

        // The late async backtrack load of the same song must NOT snap back to 0
        // and must NOT re-emit a slide change for a slide already displayed.
        int slideChangedCount = 0;
        engine.SlideChanged += (_, _) => slideChangedCount++;
        engine.OnSongLoaded(song);

        Assert.Equal(1, engine.CurrentSlideIndex);
        Assert.Equal("Chorus", engine.CurrentSlide?.SectionName, ignoreCase: true);
        Assert.Equal(0, slideChangedCount);
    }

    [Fact]
    public void LoadSong_DifferentSong_StillResetsToSlide0()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "First Song",
            Slides = new List<Slide>()
        };
        var engine = EngineWithCurrentItem(item);

        var first = new Song
        {
            Title = "First Song",
            Sections = new List<Section>
            {
                new Section { Name = "Verse 1", Text = "First verse" },
                new Section { Name = "Chorus", Text = "First chorus" }
            }
        };
        engine.LoadItemSlides(engine.GenerateSlidesForSong(first));
        engine.GoToSlide(1);
        Assert.Equal(1, engine.CurrentSlideIndex);

        int slideChangedCount = 0;
        engine.SlideChanged += (_, _) => slideChangedCount++;

        var second = new Song
        {
            Title = "Second Song",
            Sections = new List<Section>
            {
                new Section { Name = "Verse 1", Text = "Second verse" }
            }
        };
        engine.OnSongLoaded(second);

        Assert.Equal(0, engine.CurrentSlideIndex);
        Assert.Equal(1, engine.TotalSlides);
        Assert.Equal(1, slideChangedCount);
    }
}
