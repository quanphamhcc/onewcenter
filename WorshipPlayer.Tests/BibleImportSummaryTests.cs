using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services.Bible;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for dot2-T12 support logic: the pure <see cref="BibleImportSummary"/>
/// message the Settings → Bibles UI reports after an XML import, and
/// <see cref="BibleDatabaseService.GetVerseCountAsync"/> which supplies the
/// concrete verse count for that message (against a real temp SQLite file,
/// using the genuine VIETNVB fixture: 2 books, 72 verses).
/// </summary>
public class BibleImportSummaryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly BibleDatabaseService _service;
    private readonly string _fixturePath;

    public BibleImportSummaryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"BibleImportSummaryTests_{Guid.NewGuid():N}.db");
        _service = new BibleDatabaseService(_tempDbPath);
        // Resolve via the test assembly's own location (not AppContext.BaseDirectory,
        // which points at the xUnit console runner's folder when tests run there).
        var assemblyDir = Path.GetDirectoryName(typeof(BibleImportSummaryTests).Assembly.Location)!;
        _fixturePath = Path.Combine(assemblyDir, "TestData", "kinh-thanh-mau-zefania.xml");
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

    [Fact]
    public void Format_IncludesNameCodeAndCounts()
    {
        var translation = new BibleTranslation
        {
            Id = "VIETNVB",
            Name = "New Vietnamese Bible (2002)",
            Abbreviation = "VIETNVB",
            Language = "VIE"
        };

        var message = BibleImportSummary.Format(translation, bookCount: 2, verseCount: 72);

        Assert.Equal("Imported 'New Vietnamese Bible (2002)' (VIETNVB): 2 books, 72 verses.", message);
    }

    [Fact]
    public void Format_SingularBookAndVerse()
    {
        var translation = new BibleTranslation { Name = "Tiny", Abbreviation = "TNY" };

        var message = BibleImportSummary.Format(translation, bookCount: 1, verseCount: 1);

        Assert.Equal("Imported 'Tiny' (TNY): 1 book, 1 verse.", message);
    }

    [Fact]
    public void Format_NullTranslation_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BibleImportSummary.Format(null!, 1, 1));
    }

    [Fact]
    public async Task GetVerseCount_AfterImportingFixture_Returns72()
    {
        await _service.InitializeAsync();
        var imported = await _service.ImportTranslationAsync(_fixturePath);

        var count = await _service.GetVerseCountAsync(imported.Id);

        Assert.Equal(72, count);
    }

    [Fact]
    public async Task GetVerseCount_UnknownTranslation_ReturnsZero()
    {
        await _service.InitializeAsync();

        var count = await _service.GetVerseCountAsync("NOPE");

        Assert.Equal(0, count);
    }
}
