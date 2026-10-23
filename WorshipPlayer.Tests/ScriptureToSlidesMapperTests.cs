using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for dot2-T14 (Scripture to Slides): mapping scripture passages into
/// Slides-library presentations via the single ScriptureSlideGenerator.
/// </summary>
public class ScriptureToSlidesMapperTests
{
    private static ScriptureReference John316_17() => new()
    {
        BookName = "John",
        BookId = 43,
        Chapter = 3,
        StartVerse = 16,
        EndVerse = 17,
        TranslationId = "KJV"
    };

    private static List<BibleVerse> John316_17Verses() => new()
    {
        new BibleVerse { BookId = 43, BookName = "John", Chapter = 3, Verse = 16, TranslationId = "KJV", Text = "For God so loved the world." },
        new BibleVerse { BookId = 43, BookName = "John", Chapter = 3, Verse = 17, TranslationId = "KJV", Text = "He that believeth on him is not condemned." }
    };

    private static ScriptureReference Romans828_32() => new()
    {
        BookName = "Romans",
        BookId = 45,
        Chapter = 8,
        StartVerse = 28,
        EndVerse = 32,
        TranslationId = "KJV"
    };

    private static List<BibleVerse> Romans828_32Verses() => new()
    {
        new BibleVerse { BookId = 45, BookName = "Romans", Chapter = 8, Verse = 28, TranslationId = "KJV", Text = "All things work together for good." },
        new BibleVerse { BookId = 45, BookName = "Romans", Chapter = 8, Verse = 29, TranslationId = "KJV", Text = "Predestinate to be conformed to his image." },
        new BibleVerse { BookId = 45, BookName = "Romans", Chapter = 8, Verse = 30, TranslationId = "KJV", Text = "Whom he called, them he also justified." },
        new BibleVerse { BookId = 45, BookName = "Romans", Chapter = 8, Verse = 31, TranslationId = "KJV", Text = "If God be for us, who can be against us?" },
        new BibleVerse { BookId = 45, BookName = "Romans", Chapter = 8, Verse = 32, TranslationId = "KJV", Text = "Spared not his own Son, but delivered him up." }
    };

    [Fact]
    public void MapPassage_OnePassage_CorrectSlideCountAndFirstSlideCarriesReference()
    {
        // 2 short verses, 1 line each -> 1 slide (default 4 lines/slide).
        var slides = ScriptureToSlidesMapper.MapPassage(
            new ScripturePassage(John316_17(), John316_17Verses()));

        Assert.Single(slides);
        var slide = slides[0];
        Assert.Equal(0, slide.Order);
        Assert.Equal("John 3:16-17 (KJV)", slide.Subtitle);
        Assert.Contains("loved the world", slide.Text);
        Assert.Contains("not condemned", slide.Text);
    }

    [Fact]
    public void CreatePresentation_TwoNonContiguousPassages_OrderContinuesAndNoVerseMixing()
    {
        var presentation = ScriptureToSlidesMapper.CreatePresentation(
            "John 3 (KJV)",
            new[]
            {
                new ScripturePassage(John316_17(), John316_17Verses()),
                new ScripturePassage(Romans828_32(), Romans828_32Verses())
            });

        // Passage 1 -> 1 slide; passage 2 -> 5 lines -> 2 slides (4 + 1).
        Assert.Equal(3, presentation.Slides.Count);
        Assert.Equal(new[] { 0, 1, 2 }, presentation.Slides.Select(s => s.Order).ToArray());

        Assert.Equal("John 3:16-17 (KJV)", presentation.Slides[0].Subtitle);
        Assert.Equal("Romans 8:28-32 (KJV)", presentation.Slides[1].Subtitle);
        Assert.Equal("Romans 8:28-32 (KJV)", presentation.Slides[2].Subtitle);

        // No verse mixing: each slide group holds only its own passage's text.
        Assert.DoesNotContain("work together for good", presentation.Slides[0].Text);
        Assert.DoesNotContain("loved the world", presentation.Slides[1].Text);
        Assert.DoesNotContain("loved the world", presentation.Slides[2].Text);
        Assert.Contains("delivered him up", presentation.Slides[2].Text);
    }

    [Fact]
    public void AppendPassage_ToExistingPresentation_ContinuesOrderFromLastSlide()
    {
        var presentation = ScriptureToSlidesMapper.CreatePresentation(
            "Mixed",
            new[] { new ScripturePassage(John316_17(), John316_17Verses()) });

        ScriptureToSlidesMapper.AppendPassage(
            presentation,
            new ScripturePassage(Romans828_32(), Romans828_32Verses()));

        Assert.Equal(3, presentation.Slides.Count);
        Assert.Equal(new[] { 0, 1, 2 }, presentation.Slides.Select(s => s.Order).ToArray());
        Assert.Equal("Romans 8:28-32 (KJV)", presentation.Slides[2].Subtitle);
    }

    [Fact]
    public void MapPassage_EmptyVerses_ReturnsNoSlides()
    {
        Assert.Empty(ScriptureToSlidesMapper.MapPassage(
            new ScripturePassage(John316_17(), new List<BibleVerse>())));
        Assert.Empty(ScriptureToSlidesMapper.MapPassage(
            new ScripturePassage(John316_17(), null)));
    }

    [Fact]
    public void CreatePresentation_BlankName_FallsBackToDefaultName()
    {
        var presentation = ScriptureToSlidesMapper.CreatePresentation(
            "   ",
            new[] { new ScripturePassage(John316_17(), John316_17Verses()) });

        Assert.Equal("Scripture", presentation.Name);
        Assert.Single(presentation.Slides);
    }

    [Fact]
    public void RuntimeProjection_PreservesSubtitleSoSlidesReadIndependently()
    {
        var presentation = ScriptureToSlidesMapper.CreatePresentation(
            "John 3 (KJV)",
            new[] { new ScripturePassage(John316_17(), John316_17Verses()) });

        var runtime = SlidePresentationMapper.ToSlides(presentation);

        Assert.Single(runtime);
        Assert.Equal("John 3:16-17 (KJV)", runtime[0].Content.Subtitle);
        Assert.Contains("loved the world", runtime[0].Content.Text);
    }
}
