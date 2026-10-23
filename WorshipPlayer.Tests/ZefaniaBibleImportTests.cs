using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services.Bible;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for dot2-T11: the real Zefania XML Bible import — ZefaniaBibleXmlParser
/// plus BibleDatabaseService.ImportTranslationAsync writing into a real temp
/// SQLite file. Uses the genuine fixture excerpt of the New Vietnamese Bible
/// (2002) module VIETNVB (Genesis 1-2, Psalm 23-24 = 72 verses), copied to the
/// test output as TestData/kinh-thanh-mau-zefania.xml.
/// </summary>
public class ZefaniaBibleImportTests : IDisposable
{
    private const string FixtureTranslationId = "VIETNVB";

    private readonly string _tempDbPath;
    private readonly BibleDatabaseService _service;
    private readonly string _fixturePath;

    public ZefaniaBibleImportTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"BibleImportTests_{Guid.NewGuid():N}.db");
        _service = new BibleDatabaseService(_tempDbPath);
        // Resolve via the test assembly's own location (not AppContext.BaseDirectory,
        // which points at the xUnit console runner's folder when tests run there).
        var assemblyDir = Path.GetDirectoryName(typeof(ZefaniaBibleImportTests).Assembly.Location)!;
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
    public void Parser_RealFixture_ReadsMetadataAndStructure()
    {
        Assert.True(File.Exists(_fixturePath), $"Fixture missing: {_fixturePath}");

        var doc = new ZefaniaBibleXmlParser().Parse(_fixturePath);

        Assert.Equal("New Vietnamese Bible (2002)", doc.BibleName);
        Assert.Equal("VIETNVB", doc.Identifier);
        Assert.Equal("VIE", doc.Language);

        Assert.Equal(2, doc.Books.Count);
        Assert.Equal(1, doc.Books[0].BookId);   // Genesis
        Assert.Equal(19, doc.Books[1].BookId);  // Psalms
        Assert.Equal(72, doc.TotalVerseCount);
    }

    [Fact]
    public async Task Import_RealFixture_CreatesTranslationWithFileMetadata()
    {
        await _service.InitializeAsync();

        var imported = await _service.ImportTranslationAsync(_fixturePath);

        Assert.Equal(FixtureTranslationId, imported.Id);
        Assert.Equal("New Vietnamese Bible (2002)", imported.Name);
        Assert.Equal("VIE", imported.Language);

        var translations = await _service.GetTranslationsAsync();
        Assert.Contains(translations, t => t.Id == FixtureTranslationId);
    }

    [Fact]
    public async Task Import_RealFixture_LoadsExactVerseCountsPerChapter()
    {
        await _service.InitializeAsync();
        await _service.ImportTranslationAsync(_fixturePath);

        Assert.Equal(31, (await GetWholeChapterAsync(bookId: 1, chapter: 1)).Count);   // Genesis 1
        Assert.Equal(25, (await GetWholeChapterAsync(bookId: 1, chapter: 2)).Count);   // Genesis 2
        Assert.Equal(6, (await GetWholeChapterAsync(bookId: 19, chapter: 23)).Count); // Psalm 23
        Assert.Equal(10, (await GetWholeChapterAsync(bookId: 19, chapter: 24)).Count); // Psalm 24
    }

    [Fact]
    public async Task Import_RealFixture_VerseTextMatchesFixtureExactly()
    {
        await _service.InitializeAsync();
        await _service.ImportTranslationAsync(_fixturePath);

        var genesis11 = await _service.GetVerseAsync(1, 1, 1, FixtureTranslationId);
        Assert.NotNull(genesis11);
        Assert.Equal("Ban đầu, Đức Chúa Trời sáng tạo trời đất.", genesis11!.Text);

        var psalm231 = await _service.GetVerseAsync(19, 23, 1, FixtureTranslationId);
        Assert.NotNull(psalm231);
        Assert.Equal("CHÚA là Đấng chăn giữ tôi,Tôi sẽ không thiếu thốn gì.", psalm231!.Text);
    }

    [Fact]
    public async Task Import_RealFixture_SearchFindsImportedVietnameseText()
    {
        await _service.InitializeAsync();
        await _service.ImportTranslationAsync(_fixturePath);

        var results = await _service.SearchAsync("sáng tạo", FixtureTranslationId);

        Assert.NotEmpty(results);
        Assert.Contains(results, v => v.BookId == 1 && v.Chapter == 1 && v.Verse == 1);
    }

    [Fact]
    public async Task Import_Twice_IsIdempotent_NoDuplicatedVerses()
    {
        await _service.InitializeAsync();

        await _service.ImportTranslationAsync(_fixturePath);
        await _service.ImportTranslationAsync(_fixturePath);

        Assert.Equal(31, (await GetWholeChapterAsync(bookId: 1, chapter: 1)).Count);
        Assert.Equal(72, await CountVersesAsync(FixtureTranslationId));

        var translations = await _service.GetTranslationsAsync();
        Assert.Single(translations, t => t.Id == FixtureTranslationId);
    }

    [Fact]
    public async Task Import_NonZefaniaXml_ThrowsAndLeavesNoTranslationRecord()
    {
        await _service.InitializeAsync();
        var bogusPath = Path.Combine(Path.GetTempPath(), $"NotABible_{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(bogusPath, "<?xml version=\"1.0\"?><catalog><book id=\"1\"/></catalog>");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.ImportTranslationAsync(bogusPath));
            Assert.Contains("Zefania", ex.Message);

            // Rollback clean: only the seeded translations remain, no verses at all.
            var translations = await _service.GetTranslationsAsync();
            Assert.Equal(2, translations.Count);
            Assert.DoesNotContain(translations, t => t.Id.Contains("NOTABIBLE"));
            Assert.Equal(0, await CountAllVersesAsync());
        }
        finally
        {
            if (File.Exists(bogusPath)) File.Delete(bogusPath);
        }
    }

    [Fact]
    public async Task Import_MalformedXml_ThrowsAndLeavesNoTranslationRecord()
    {
        await _service.InitializeAsync();
        var bogusPath = Path.Combine(Path.GetTempPath(), $"Broken_{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(bogusPath, "<XMLBIBLE><BIBLEBOOK bnumber=\"1\">");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.ImportTranslationAsync(bogusPath));

            var translations = await _service.GetTranslationsAsync();
            Assert.Equal(2, translations.Count);
        }
        finally
        {
            if (File.Exists(bogusPath)) File.Delete(bogusPath);
        }
    }

    [Fact]
    public async Task Import_MissingFile_ThrowsFileNotFound()
    {
        await _service.InitializeAsync();
        var missing = Path.Combine(Path.GetTempPath(), $"Missing_{Guid.NewGuid():N}.xml");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.ImportTranslationAsync(missing));
    }

    [Fact]
    public void Parser_BookNumberOutsideCanon_Throws()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <XMLBIBLE biblename="Bogus">
              <BIBLEBOOK bnumber="99" bname="Nope" bsname="No">
                <CHAPTER cnumber="1"><VERS vnumber="1">Text.</VERS></CHAPTER>
              </BIBLEBOOK>
            </XMLBIBLE>
            """;
        var path = Path.Combine(Path.GetTempPath(), $"OutOfCanon_{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);

        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => new ZefaniaBibleXmlParser().Parse(path));
            Assert.Contains("99", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Import_NoIdentifierInFile_FallsBackToFileName()
    {
        await _service.InitializeAsync();

        // Minimal valid Zefania file without INFORMATION/identifier: the
        // translation id falls back to the file name, as the old stub did.
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <XMLBIBLE biblename="Mini Test Bible">
              <BIBLEBOOK bnumber="1" bname="Genesis" bsname="Gen">
                <CHAPTER cnumber="1">
                  <VERS vnumber="1">Câu một có dấu tiếng Việt.</VERS>
                  <VERS vnumber="2">Second verse.</VERS>
                </CHAPTER>
              </BIBLEBOOK>
            </XMLBIBLE>
            """;
        var path = Path.Combine(Path.GetTempPath(), "MiniTest.xml");
        await File.WriteAllTextAsync(path, xml);

        try
        {
            var imported = await _service.ImportTranslationAsync(path);

            Assert.Equal("MINITEST", imported.Id);
            Assert.Equal("Mini Test Bible", imported.Name);

            var verses = await GetWholeChapterAsync(bookId: 1, chapter: 1, translationId: "MINITEST");
            Assert.Equal(2, verses.Count);
            Assert.Equal("Câu một có dấu tiếng Việt.", verses[0].Text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private async Task<List<BibleVerse>> GetWholeChapterAsync(int bookId, int chapter, string translationId = FixtureTranslationId)
    {
        var reference = new ScriptureReference
        {
            BookId = bookId,
            Chapter = chapter,
            StartVerse = 0,
            EndVerse = null,
            TranslationId = translationId
        };
        return await _service.GetVersesAsync(reference);
    }

    private async Task<int> CountVersesAsync(string translationId)
    {
        using var connection = new SqliteConnection($"Data Source={_tempDbPath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Verses WHERE TranslationId = @id";
        cmd.Parameters.AddWithValue("@id", translationId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountAllVersesAsync()
    {
        using var connection = new SqliteConnection($"Data Source={_tempDbPath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Verses";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
