using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// F12: SnapshotStageData returns a fresh, internally consistent StageDisplayData
/// built under the state lock. CurrentText/SectionName correspond to the current
/// index, and SongTitle/Key/OriginalKey reflect the loaded song.
/// </summary>
public class PresenterEngineSnapshotTests
{
    private static PresenterEngine EngineWithLoadedSong()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Snapshot Song",
            Slides = new List<Slide>
            {
                new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "v1a" } },
                new Slide { SectionName = "VERSE 1", Content = new SlideContent { Text = "v1b" } },
                new Slide { SectionName = "CHORUS 1", Content = new SlideContent { Text = "c1a" } },
                new Slide { SectionName = "CHORUS 1", Content = new SlideContent { Text = "c1b" } }
            }
        };

        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test", DateTime.Now.ToString("yyyy-MM-dd"));
        // Engine BEFORE AddItem so its ItemChanged handler loads the item slides
        // (AddItem fires ItemChanged only for the first item).
        var engine = new PresenterEngine(manager, new PresenterSettings());
        manager.AddItem(item);
        engine.OnSongLoaded(new Song { Title = "Snapshot Song", OriginalKey = "G" });
        return engine;
    }

    private static void AssertConsistent(PresenterEngine engine)
    {
        var snap = engine.SnapshotStageData();

        Assert.Equal("Snapshot Song", snap.SongTitle);
        Assert.Equal("G", snap.OriginalKey);
        Assert.Equal("G", snap.Key);
        Assert.Equal(engine.CurrentSlideIndex, snap.CurrentSlideIndex);
        Assert.Equal(engine.TotalSlides, snap.TotalSlides);

        var current = engine.CurrentSlide;
        Assert.Equal(current?.Content?.Text ?? "", snap.CurrentText);
        Assert.Equal(current?.SectionName ?? "", snap.SectionName);
    }

    [Fact]
    public void SnapshotStageData_StaysConsistentAcrossSectionChangeLoopAndPosition()
    {
        var engine = EngineWithLoadedSong();

        // Section change -> first VERSE 1 slide (index 0).
        engine.OnSectionChanged("VERSE 1");
        AssertConsistent(engine);
        int afterVerse = engine.CurrentSlideIndex;

        // Section change -> first CHORUS 1 slide (index 2); monotonic within move.
        engine.OnSectionChanged("CHORUS 1");
        AssertConsistent(engine);
        Assert.True(engine.CurrentSlideIndex >= afterVerse);
        int firstChorus = engine.CurrentSlideIndex;

        // Advance within the chorus (position-style update), index must be monotonic.
        engine.GoToSlide(firstChorus + 1);
        AssertConsistent(engine);
        Assert.True(engine.CurrentSlideIndex >= firstChorus);

        // Loop back to the first CHORUS 1 slide (reset).
        engine.OnSectionLooped("CHORUS 1");
        AssertConsistent(engine);
        Assert.Equal(firstChorus, engine.CurrentSlideIndex);
    }

    [Fact]
    public void SnapshotStageData_ReflectsCurrentTextAndSection()
    {
        var engine = EngineWithLoadedSong();

        engine.GoToSlide(2); // first CHORUS 1 slide
        var snap = engine.SnapshotStageData();

        Assert.Equal("c1a", snap.CurrentText);
        Assert.Equal("CHORUS 1", snap.SectionName);
        Assert.Equal("c1b", snap.NextText);
    }
}
