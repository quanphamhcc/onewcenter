using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// F4 consumer test: PresenterEngine.OnSectionLooped must reset to the FIRST slide
/// of the named section (re-show), not advance. An unmatched name is a no-op.
/// </summary>
public class PresenterEngineLoopResetTests
{
    private static PresenterEngine EngineWithSections()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            Title = "Loop Song",
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
        // Construct the engine BEFORE adding the item so its ItemChanged handler
        // runs and loads the item slides (AddItem only fires ItemChanged once, for
        // the first item).
        var engine = new PresenterEngine(manager, new PresenterSettings());
        manager.AddItem(item);
        return engine;
    }

    [Fact]
    public void OnSectionLooped_ResetsToFirstSlideOfSection()
    {
        var engine = EngineWithSections();

        // Advance into CHORUS 1 and move within it (to the second chorus slide, index 3).
        engine.GoToSlide(3);
        Assert.Equal(3, engine.CurrentSlideIndex);

        engine.OnSectionLooped("CHORUS 1");

        // Reset to the FIRST CHORUS 1 slide (index 2), not advanced.
        Assert.Equal(2, engine.CurrentSlideIndex);
    }

    [Fact]
    public void OnSectionLooped_UnmatchedName_IsNoOp_AndDoesNotThrow()
    {
        var engine = EngineWithSections();
        engine.GoToSlide(3);
        int before = engine.CurrentSlideIndex;

        var ex = Record.Exception(() => engine.OnSectionLooped("BRIDGE"));

        Assert.Null(ex);
        Assert.Equal(before, engine.CurrentSlideIndex);
    }
}
