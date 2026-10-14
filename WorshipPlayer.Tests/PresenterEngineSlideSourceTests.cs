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
}
