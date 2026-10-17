using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for ScriptureSlideGenerator.
/// Tests splitting passages into slides, configurable lines per slide, and reference display.
/// </summary>
public class ScriptureSlideGeneratorTests
{
    private readonly ScriptureSlideGenerator _generator = new();

    [Fact]
    public void GenerateSlides_EmptyVerses_ReturnsEmpty()
    {
        var reference = CreateTestReference();
        var slides = _generator.GenerateSlides(new List<BibleVerse>(), reference);

        Assert.Empty(slides);
    }

    [Fact]
    public void GenerateSlides_SingleVerse_ProducesOneSlide()
    {
        var verses = new List<BibleVerse>
        {
            CreateVerse(1, "For God so loved the world.")
        };
        var reference = CreateTestReference();

        var slides = _generator.GenerateSlides(verses, reference);

        Assert.Single(slides);
        Assert.Contains("For God so loved the world.", slides[0].Content.Text);
    }

    [Fact]
    public void GenerateSlides_ManyVerses_SplitsAcrossSlides()
    {
        var verses = new List<BibleVerse>();
        for (int i = 1; i <= 10; i++)
        {
            verses.Add(CreateVerse(i, $"This is the text of verse {i} which is a moderate length sentence."));
        }
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            LinesPerSlide = 2,
            MaxCharsPerLine = 80
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        Assert.True(slides.Count > 1, "Multiple verses should split into multiple slides");
    }

    [Fact]
    public void GenerateSlides_DefaultLinesPerSlide_Is4()
    {
        var options = new ScriptureSlideGenerator.SlideGenerationOptions();
        Assert.Equal(4, options.LinesPerSlide);
    }

    [Fact]
    public void GenerateSlides_ConfigurableLinesPerSlide_2Lines()
    {
        var verses = CreateMultipleVerses(6, "Short verse text.");
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            LinesPerSlide = 2,
            MaxCharsPerLine = 100
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        // With 6 short verses at 2 lines per slide, should produce 3 slides
        Assert.Equal(3, slides.Count);
    }

    [Fact]
    public void GenerateSlides_ConfigurableLinesPerSlide_6Lines()
    {
        var verses = CreateMultipleVerses(6, "Short verse text.");
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            LinesPerSlide = 6,
            MaxCharsPerLine = 100
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        // With 6 short verses at 6 lines per slide, should be 1 slide
        Assert.Single(slides);
    }

    [Fact]
    public void GenerateSlides_ShowVerseNumbers_IncludesNumbers()
    {
        var verses = new List<BibleVerse>
        {
            CreateVerse(16, "For God so loved the world.")
        };
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowVerseNumbers = true
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        Assert.Contains("16", slides[0].Content.Text);
    }

    [Fact]
    public void GenerateSlides_HideVerseNumbers_ExcludesNumbers()
    {
        var verses = new List<BibleVerse>
        {
            CreateVerse(16, "For God so loved the world.")
        };
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowVerseNumbers = false
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        Assert.Equal("For God so loved the world.", slides[0].Content.Text);
    }

    [Fact]
    public void GenerateSlides_ShowReference_IncludesInSubtitle()
    {
        var verses = new List<BibleVerse>
        {
            CreateVerse(16, "For God so loved the world.")
        };
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowReference = true,
            ShowTranslationName = false
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        Assert.Contains("John", slides[0].Content.Subtitle);
    }

    [Fact]
    public void GenerateSlides_ShowTranslation_IncludesInSubtitle()
    {
        var verses = new List<BibleVerse>
        {
            CreateVerse(16, "For God so loved the world.")
        };
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowReference = true,
            ShowTranslationName = true
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        Assert.Contains("KJV", slides[0].Content.Subtitle);
    }

    [Fact]
    public void GenerateSlides_HideReferenceAndTranslation_EmptySubtitle()
    {
        var verses = new List<BibleVerse>
        {
            CreateVerse(16, "For God so loved the world.")
        };
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowReference = false,
            ShowTranslationName = false
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        Assert.Equal("", slides[0].Content.Subtitle);
    }

    [Fact]
    public void GenerateSlides_SlidesAreTypeScripture()
    {
        var verses = new List<BibleVerse>
        {
            CreateVerse(1, "Test verse text.")
        };
        var reference = CreateTestReference();

        var slides = _generator.GenerateSlides(verses, reference);

        Assert.All(slides, s => Assert.Equal(SlideType.Scripture, s.Type));
    }

    [Fact]
    public void GenerateSlides_SlidesHaveCorrectOrder()
    {
        var verses = CreateMultipleVerses(8, "Short verse.");
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            LinesPerSlide = 2,
            MaxCharsPerLine = 100
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        for (int i = 0; i < slides.Count; i++)
        {
            Assert.Equal(i, slides[i].Order);
        }
    }

    [Fact]
    public void GenerateSlidesFromText_EmptyText_ReturnsEmpty()
    {
        var slides = _generator.GenerateSlidesFromText("", "John 3:16", "KJV");
        Assert.Empty(slides);
    }

    [Fact]
    public void GenerateSlidesFromText_WhitespaceText_ReturnsEmpty()
    {
        var slides = _generator.GenerateSlidesFromText("   ", "John 3:16", "KJV");
        Assert.Empty(slides);
    }

    [Fact]
    public void GenerateSlidesFromText_MultipleLines_SplitsByLinesPerSlide()
    {
        var text = "Line one\nLine two\nLine three\nLine four\nLine five\nLine six";
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            LinesPerSlide = 2
        };

        var slides = _generator.GenerateSlidesFromText(text, "Test Ref", "KJV", options);

        Assert.Equal(3, slides.Count);
    }

    [Fact]
    public void GenerateSlidesFromText_IncludesReferenceInSubtitle()
    {
        var text = "Some scripture text";
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowReference = true,
            ShowTranslationName = true
        };

        var slides = _generator.GenerateSlidesFromText(text, "Psalm 23:1", "NIV", options);

        Assert.Single(slides);
        Assert.Contains("Psalm 23:1", slides[0].Content.Subtitle);
        Assert.Contains("NIV", slides[0].Content.Subtitle);
    }

    [Fact]
    public void GenerateSlidesFromText_OnlyReference_NoTranslation()
    {
        var text = "Some text";
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowReference = true,
            ShowTranslationName = false
        };

        var slides = _generator.GenerateSlidesFromText(text, "Gen 1:1", "KJV", options);

        Assert.Equal("Gen 1:1", slides[0].Content.Subtitle);
    }

    // ─── E3: long-passage auto-split (exact slide count) ───────────────

    [Fact]
    public void GenerateSlides_LongPassage_AutoSplitsIntoExactSlideCount()
    {
        // 12 short verses, each estimating to exactly 1 line (text + verse number
        // well under MaxCharsPerLine). At LinesPerSlide = 2 the generator packs 2
        // verses per slide → exactly 6 slides.
        var verses = CreateMultipleVerses(12, "Short verse.");
        var reference = CreateTestReference();
        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            LinesPerSlide = 2,
            MaxCharsPerLine = 80
        };

        var slides = _generator.GenerateSlides(verses, reference, options);

        Assert.Equal(6, slides.Count);
    }

    // ─── E5: ShowReference toggle changes slide content ────────────────

    [Fact]
    public void GenerateSlides_ShowReferenceToggle_ChangesSubtitle()
    {
        var verses = CreateMultipleVerses(3, "Some verse text here.");
        var reference = CreateTestReference();

        var withReference = _generator.GenerateSlides(verses, reference,
            new ScriptureSlideGenerator.SlideGenerationOptions
            {
                ShowReference = true,
                ShowTranslationName = false
            });
        var withoutReference = _generator.GenerateSlides(verses, reference,
            new ScriptureSlideGenerator.SlideGenerationOptions
            {
                ShowReference = false,
                ShowTranslationName = false
            });

        // Reference present when true, absent when false → content differs.
        Assert.Contains(reference.ToReferenceString(), withReference[0].Content.Subtitle);
        Assert.DoesNotContain(reference.ToReferenceString(), withoutReference[0].Content.Subtitle);
        Assert.NotEqual(withReference[0].Content.Subtitle, withoutReference[0].Content.Subtitle);
    }

    // ─── E5: ShowTranslationName toggle changes slide content ──────────

    [Fact]
    public void GenerateSlides_ShowTranslationNameToggle_ChangesSubtitle()
    {
        var verses = CreateMultipleVerses(3, "Some verse text here.");
        var reference = CreateTestReference(); // TranslationId = "KJV"

        var withTranslation = _generator.GenerateSlides(verses, reference,
            new ScriptureSlideGenerator.SlideGenerationOptions
            {
                ShowReference = false,
                ShowTranslationName = true
            });
        var withoutTranslation = _generator.GenerateSlides(verses, reference,
            new ScriptureSlideGenerator.SlideGenerationOptions
            {
                ShowReference = false,
                ShowTranslationName = false
            });

        // Translation id present when true, absent when false → content differs.
        Assert.Contains("KJV", withTranslation[0].Content.Subtitle);
        Assert.DoesNotContain("KJV", withoutTranslation[0].Content.Subtitle);
        Assert.NotEqual(withTranslation[0].Content.Subtitle, withoutTranslation[0].Content.Subtitle);
    }

    private static ScriptureReference CreateTestReference()
    {
        return new ScriptureReference
        {
            BookName = "John",
            BookId = 43,
            Chapter = 3,
            StartVerse = 16,
            TranslationId = "KJV"
        };
    }

    private static BibleVerse CreateVerse(int verseNum, string text)
    {
        return new BibleVerse
        {
            Id = verseNum,
            BookId = 43,
            BookName = "John",
            Chapter = 3,
            Verse = verseNum,
            Text = text,
            TranslationId = "KJV"
        };
    }

    private static List<BibleVerse> CreateMultipleVerses(int count, string text)
    {
        var verses = new List<BibleVerse>();
        for (int i = 1; i <= count; i++)
        {
            verses.Add(CreateVerse(i, text));
        }
        return verses;
    }
}
