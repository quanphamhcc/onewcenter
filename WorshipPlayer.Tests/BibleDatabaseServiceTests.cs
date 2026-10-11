using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services.Bible;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for BibleDatabaseService using in-memory SQLite.
/// Tests schema creation, querying verses, and search functionality.
/// </summary>
public class BibleDatabaseServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly BibleDatabaseService _service;

    public BibleDatabaseServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"BibleTests_{Guid.NewGuid():N}.db");
        _service = new BibleDatabaseService(_tempDbPath);
    }

    public void Dispose()
    {
        // SQLite uses connection pooling by default, which keeps a file handle
        // open after the service's connections are disposed. Clear the pools so
        // the temp .db file can be deleted without an IOException.
        SqliteConnection.ClearAllPools();

        if (File.Exists(_tempDbPath))
        {
            try
            {
                File.Delete(_tempDbPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if the OS still
                // holds a transient lock.
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesSchema()
    {
        await _service.InitializeAsync();

        // Verify the database file was created
        Assert.True(File.Exists(_tempDbPath));

        // Verify tables exist by querying
        using var connection = new SqliteConnection($"Data Source={_tempDbPath}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync();

        var tables = new List<string>();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        Assert.Contains("Translations", tables);
        Assert.Contains("Books", tables);
        Assert.Contains("Verses", tables);
    }

    [Fact]
    public async Task InitializeAsync_SeedsTranslations()
    {
        await _service.InitializeAsync();

        var translations = await _service.GetTranslationsAsync();

        Assert.True(translations.Count >= 2);
        Assert.Contains(translations, t => t.Id == "KJV");
        Assert.Contains(translations, t => t.Id == "WEB");
    }

    [Fact]
    public async Task InitializeAsync_SeedsBooks()
    {
        await _service.InitializeAsync();

        var books = await _service.GetBooksAsync("KJV");

        Assert.Equal(66, books.Count);
        Assert.Equal("Genesis", books[0].Name);
        Assert.Equal("Revelation", books[65].Name);
    }

    [Fact]
    public async Task GetBooksAsync_InvalidTranslation_ReturnsEmpty()
    {
        await _service.InitializeAsync();

        var books = await _service.GetBooksAsync("INVALID");

        Assert.Empty(books);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        await _service.InitializeAsync();

        var results = await _service.SearchAsync("", "KJV");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        await _service.InitializeAsync();

        var results = await _service.SearchAsync("   ", "KJV");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WithVerses_FindsResults()
    {
        await _service.InitializeAsync();
        await InsertTestVerseAsync("For God so loved the world", 43, 3, 16, "KJV");

        var results = await _service.SearchAsync("loved the world", "KJV");

        Assert.Single(results);
        Assert.Contains("loved the world", results[0].Text);
    }

    [Fact]
    public async Task GetVerseAsync_ExistingVerse_ReturnsVerse()
    {
        await _service.InitializeAsync();
        await InsertTestVerseAsync("In the beginning God created the heavens and the earth.", 1, 1, 1, "KJV");

        var verse = await _service.GetVerseAsync(1, 1, 1, "KJV");

        Assert.NotNull(verse);
        Assert.Equal(1, verse!.BookId);
        Assert.Equal(1, verse.Chapter);
        Assert.Equal(1, verse.Verse);
        Assert.Contains("In the beginning", verse.Text);
    }

    [Fact]
    public async Task GetVerseAsync_NonExistent_ReturnsNull()
    {
        await _service.InitializeAsync();

        var verse = await _service.GetVerseAsync(1, 1, 1, "KJV");

        Assert.Null(verse);
    }

    [Fact]
    public async Task GetVersesAsync_VerseRange_ReturnsMultiple()
    {
        await _service.InitializeAsync();
        await InsertTestVerseAsync("Verse one text", 43, 3, 16, "KJV");
        await InsertTestVerseAsync("Verse two text", 43, 3, 17, "KJV");
        await InsertTestVerseAsync("Verse three text", 43, 3, 18, "KJV");

        var reference = new ScriptureReference
        {
            BookId = 43,
            Chapter = 3,
            StartVerse = 16,
            EndVerse = 18,
            TranslationId = "KJV"
        };

        var verses = await _service.GetVersesAsync(reference);

        Assert.Equal(3, verses.Count);
    }

    [Fact]
    public async Task GetVersesAsync_SingleVerse_ReturnsSingle()
    {
        await _service.InitializeAsync();
        await InsertTestVerseAsync("For God so loved the world", 43, 3, 16, "KJV");

        var reference = new ScriptureReference
        {
            BookId = 43,
            Chapter = 3,
            StartVerse = 16,
            TranslationId = "KJV"
        };

        var verses = await _service.GetVersesAsync(reference);

        Assert.Single(verses);
        Assert.Contains("loved the world", verses[0].Text);
    }

    [Fact]
    public async Task GetVersesAsync_WholeChapter_ReturnsAll()
    {
        await _service.InitializeAsync();
        await InsertTestVerseAsync("Chapter verse 1", 19, 23, 1, "KJV");
        await InsertTestVerseAsync("Chapter verse 2", 19, 23, 2, "KJV");
        await InsertTestVerseAsync("Chapter verse 3", 19, 23, 3, "KJV");

        var reference = new ScriptureReference
        {
            BookId = 19,
            Chapter = 23,
            StartVerse = 0,
            EndVerse = null,
            TranslationId = "KJV"
        };

        var verses = await _service.GetVersesAsync(reference);

        Assert.Equal(3, verses.Count);
    }

    [Fact]
    public async Task ParseReferenceAsync_ValidReference_ReturnsParsed()
    {
        await _service.InitializeAsync();

        var result = await _service.ParseReferenceAsync("John 3:16", "KJV");

        Assert.NotNull(result);
        Assert.Equal("John", result!.BookName);
        Assert.Equal(3, result.Chapter);
        Assert.Equal(16, result.StartVerse);
    }

    [Fact]
    public async Task ParseReferenceAsync_InvalidReference_ReturnsNull()
    {
        await _service.InitializeAsync();

        var result = await _service.ParseReferenceAsync("InvalidRef", "KJV");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetTranslationsAsync_ReturnsKJVAsDefault()
    {
        await _service.InitializeAsync();

        var translations = await _service.GetTranslationsAsync();
        var kjv = translations.First(t => t.Id == "KJV");

        Assert.True(kjv.IsDefault);
        Assert.Equal("King James Version", kjv.Name);
        Assert.Equal("en", kjv.Language);
    }

    private async Task InsertTestVerseAsync(string text, int bookId, int chapter, int verse, string translationId)
    {
        using var connection = new SqliteConnection($"Data Source={_tempDbPath}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Verses (BookId, Chapter, Verse, Text, TranslationId)
            VALUES (@bookId, @chapter, @verse, @text, @translationId)";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        cmd.Parameters.AddWithValue("@chapter", chapter);
        cmd.Parameters.AddWithValue("@verse", verse);
        cmd.Parameters.AddWithValue("@text", text);
        cmd.Parameters.AddWithValue("@translationId", translationId);
        await cmd.ExecuteNonQueryAsync();
    }
}
