using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// The all-sections center pane previews every service item's slides without
/// disturbing the live slide. It leans on the public, PURE generator
/// <see cref="PresenterEngine.GenerateSlidesForSong"/>, which must return a fresh
/// slide list that matches the live generation AND must not mutate engine state.
/// </summary>
public class GenerateSlidesForSongTests
{
    private static PresenterEngine NewEngine()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        return new PresenterEngine(manager, new PresenterSettings());
    }

    private static Song TwoSectionSong()
    {
        return new Song
        {
            Title = "Two Section Song",
            OriginalKey = "C",
            Sections = new List<Section>
            {
                // Two blocks (separated by a blank line) in the verse.
                new Section { Name = "Verse 1", Text = "Line one\nLine two\n\nLine three\nLine four" },
                new Section { Name = "Chorus 1", Text = "Chorus line one\nChorus line two" }
            }
        };
    }

    [Fact]
    public void GenerateSlidesForSong_ReturnsSlidesForEachBlock_WithSectionNames()
    {
        var engine = NewEngine();
        var song = TwoSectionSong();

        var slides = engine.GenerateSlidesForSong(song);

        // Verse 1 has two blocks, Chorus 1 has one block => 3 slides.
        Assert.Equal(3, slides.Count);
        Assert.Equal("Verse 1", slides[0].SectionName);
        Assert.Equal("Verse 1", slides[1].SectionName);
        Assert.Equal("Chorus 1", slides[2].SectionName);
        Assert.All(slides, s => Assert.Equal(SlideType.Lyrics, s.Type));
        Assert.All(slides, s => Assert.False(string.IsNullOrWhiteSpace(s.Content.Text)));
    }

    [Fact]
    public void GenerateSlidesForSong_DoesNotMutateEngineState()
    {
        var engine = NewEngine();
        var song = TwoSectionSong();

        // Fresh engine with no current item has no live slides.
        Assert.Equal(0, engine.TotalSlides);

        var slides = engine.GenerateSlidesForSong(song);

        // Purity: the generator returned slides but left live state untouched.
        Assert.True(slides.Count > 0);
        Assert.Equal(0, engine.TotalSlides);
        Assert.Null(engine.CurrentSlide);
    }

    [Fact]
    public void GenerateSlidesForSong_EmptySong_ReturnsEmptyList_NoMutation()
    {
        var engine = NewEngine();
        var song = new Song { Title = "Empty", OriginalKey = "C" };

        var slides = engine.GenerateSlidesForSong(song);

        Assert.Empty(slides);
        Assert.Equal(0, engine.TotalSlides);
    }
}
