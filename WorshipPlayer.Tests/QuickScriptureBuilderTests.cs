using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Bible;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// dot2-T13 / R7.3: the pure <see cref="QuickScriptureBuilder"/> behind Quick
/// Scripture. A valid passage yields the right slides/content (splitting is
/// delegated to the ONE ScriptureSlideGenerator); a translation with no verse
/// data yields an empty slide list with the distinguishable
/// <see cref="QuickScriptureStatus.NoVerses"/> status — never an ambiguous
/// empty success. The last two tests run the full lookup path (parse → fetch
/// → build) against a real temp SQLite bible.db seeded by
/// BibleDatabaseService: KJV seed data covers John 3, WEB is registered but
/// ships no verse data.
/// </summary>
public class QuickScriptureBuilderTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly BibleDatabaseService _service;

    public QuickScriptureBuilderTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"QuickScriptureBuilderTests_{Guid.NewGuid():N}.db");
        _service = new BibleDatabaseService(_tempDbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); }
            catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private static ScriptureReference Reference(int startVerse, int? endVerse = null, string translation = "KJV")
    {
        return new ScriptureReference
        {
            BookName = "John",
            BookId = 43,
            Chapter = 3,
            StartVerse = startVerse,
            EndVerse = endVerse,
            TranslationId = translation
        };
    }

    private static BibleVerse Verse(int verse, string text)
    {
        return new BibleVerse
        {
            BookId = 43,
            BookName = "John",
            Chapter = 3,
            Verse = verse,
            Text = text,
            TranslationId = "KJV"
        };
    }

    [Fact]
    public void Build_ValidPassage_ReturnsSuccessWithExpectedSlide()
    {
        var verses = new List<BibleVerse>
        {
            Verse(16, "For God so loved the world"),
            Verse(17, "For God sent not his Son")
        };

        var result = QuickScriptureBuilder.Build(Reference(16), verses);

        Assert.True(result.IsSuccess);
        Assert.Equal(QuickScriptureStatus.Success, result.Status);
        var slide = Assert.Single(result.Slides);
        Assert.Equal(SlideType.Scripture, slide.Type);
        Assert.Equal(0, slide.Order);
        Assert.Equal("16 For God so loved the world\n17 For God sent not his Son", slide.Content.Text);
        Assert.Equal("John 3:16 (KJV)", slide.Content.Subtitle);
    }

    [Fact]
    public void Build_LongPassage_SplitsIntoMultipleSlidesInOrder()
    {
        var verses = new List<BibleVerse>
        {
            Verse(16, "First verse text."),
            Verse(17, "Second verse text."),
            Verse(18, "Third verse text.")
        };
        var options = new ScriptureSlideGenerator.SlideGenerationOptions { LinesPerSlide = 1 };

        var result = QuickScriptureBuilder.Build(Reference(16, 18), verses, options);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Slides.Count);
        Assert.Equal("16 First verse text.", result.Slides[0].Content.Text);
        Assert.Equal("17 Second verse text.", result.Slides[1].Content.Text);
        Assert.Equal("18 Third verse text.", result.Slides[2].Content.Text);
        Assert.Equal(0, result.Slides[0].Order);
        Assert.Equal(1, result.Slides[1].Order);
        Assert.Equal(2, result.Slides[2].Order);
        Assert.Equal("John 3:16-18 (KJV)", result.Slides[0].Content.Subtitle);
    }

    [Fact]
    public void Build_NullVerses_ReturnsNoVersesWithEmptySlides()
    {
        var result = QuickScriptureBuilder.Build(Reference(16), null);

        Assert.Equal(QuickScriptureStatus.NoVerses, result.Status);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Slides);
        Assert.Empty(result.Slides);
    }

    [Fact]
    public void Build_EmptyVerses_ReturnsNoVersesWithEmptySlides()
    {
        var result = QuickScriptureBuilder.Build(Reference(16), new List<BibleVerse>());

        Assert.Equal(QuickScriptureStatus.NoVerses, result.Status);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Slides);
        Assert.Empty(result.Slides);
    }

    [Fact]
    public void Build_NullReference_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => QuickScriptureBuilder.Build(null!, new List<BibleVerse>()));
    }

    [Fact]
    public async Task Build_FromSeededKjv_John316_SucceedsWithRealText()
    {
        await _service.InitializeAsync();
        // InitializeAsync seeds schema/translations/books only; the KJV verse
        // text is seeded separately (as in BibleDatabaseServiceTests).
        await _service.SeedKjvVersesAsync();

        var reference = await _service.ParseReferenceAsync("John 3:16", "KJV");
        Assert.NotNull(reference);
        var verses = await _service.GetVersesAsync(reference);

        var result = QuickScriptureBuilder.Build(reference, verses);

        Assert.True(result.IsSuccess);
        var slide = Assert.Single(result.Slides);
        Assert.Contains("For God so loved the world", slide.Content.Text);
        Assert.Equal("John 3:16 (KJV)", slide.Content.Subtitle);
    }

    [Fact]
    public async Task Build_FromTranslationWithoutData_ReturnsNoVerses()
    {
        await _service.InitializeAsync();

        // WEB is a registered translation, but the seed ships no verse data
        // for it — the R7 edge case: a distinguishable no-data outcome.
        var reference = await _service.ParseReferenceAsync("John 3:16", "WEB");
        Assert.NotNull(reference);
        var verses = await _service.GetVersesAsync(reference);
        Assert.Empty(verses);

        var result = QuickScriptureBuilder.Build(reference, verses);

        Assert.Equal(QuickScriptureStatus.NoVerses, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Slides);
    }
}
