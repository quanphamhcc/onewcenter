using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// E3: externally generated scripture slides reach the engine via LoadScriptureSlides,
/// which sets the slide list + index and fires a single SlideChanged for slide 0 so the
/// existing OnSlideChanged → OutputWindow.ShowSlide path renders them.
/// </summary>
public class PresenterEngineScriptureTests
{
    private static PresenterEngine NewEngine()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        return new PresenterEngine(manager, new PresenterSettings());
    }

    private static List<Slide> ThreeScriptureSlides()
    {
        return new List<Slide>
        {
            new Slide { Type = SlideType.Scripture, Order = 0, Content = new SlideContent { Text = "Verse one", Subtitle = "John 3:16 (KJV)" } },
            new Slide { Type = SlideType.Scripture, Order = 1, Content = new SlideContent { Text = "Verse two", Subtitle = "John 3:17 (KJV)" } },
            new Slide { Type = SlideType.Scripture, Order = 2, Content = new SlideContent { Text = "Verse three", Subtitle = "John 3:18 (KJV)" } }
        };
    }

    [Fact]
    public void LoadScriptureSlides_SetsSlidesSelectsFirst_AndFiresSlideChangedOnce()
    {
        var engine = NewEngine();

        int slideChangedCount = 0;
        Slide? firstEmitted = null;
        engine.SlideChanged += (_, e) =>
        {
            slideChangedCount++;
            firstEmitted = e.NewSlide;
        };

        var slides = ThreeScriptureSlides();
        engine.LoadScriptureSlides(slides);

        Assert.Equal(3, engine.TotalSlides);
        Assert.Equal(0, engine.CurrentSlideIndex);
        Assert.Same(slides[0], engine.CurrentSlide);
        Assert.Equal(1, slideChangedCount);
        Assert.Same(slides[0], firstEmitted);
    }

    [Fact]
    public void LoadScriptureSlides_EmptyList_SelectsNothing_AndFiresNoSlideChanged()
    {
        var engine = NewEngine();

        int slideChangedCount = 0;
        engine.SlideChanged += (_, _) => slideChangedCount++;

        engine.LoadScriptureSlides(new List<Slide>());

        Assert.Equal(0, engine.TotalSlides);
        Assert.Equal(-1, engine.CurrentSlideIndex);
        Assert.Null(engine.CurrentSlide);
        Assert.Equal(0, slideChangedCount);
    }
}
