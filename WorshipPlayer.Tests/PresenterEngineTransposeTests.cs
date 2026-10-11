using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// F5: GetStageText transposes chords by the given semitones; +2 takes [C] to [D],
/// and 0 returns the text unchanged.
/// </summary>
public class PresenterEngineTransposeTests
{
    private static PresenterEngine NewEngine()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        return new PresenterEngine(manager, new PresenterSettings());
    }

    [Fact]
    public void GetStageText_TransposesUpATone()
    {
        var engine = NewEngine();
        var slide = new Slide { Content = new SlideContent { Text = "[C]Amazing grace" } };

        var result = engine.GetStageText(slide, 2);

        Assert.Contains("[D]", result);
    }

    [Fact]
    public void GetStageText_ZeroSemitones_ReturnsOriginalUnchanged()
    {
        var engine = NewEngine();
        var original = "[C]Amazing grace";
        var slide = new Slide { Content = new SlideContent { Text = original } };

        var result = engine.GetStageText(slide, 0);

        Assert.Equal(original, result);
    }
}
