using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Core.Services.Bible;

/// <summary>
/// SQLite-based Bible data provider. Manages a local Bible database with schema
/// creation, verse querying, and scripture reference parsing.
/// </summary>
public class BibleDatabaseService : IBibleProvider
{
    private readonly string _databasePath;
    private readonly ScriptureReferenceParser _parser;
    private string _connectionString;

    public BibleDatabaseService(string databasePath)
    {
        _databasePath = databasePath;
        _parser = new ScriptureReferenceParser();
        _connectionString = $"Data Source={databasePath}";
    }

    /// <summary>
    /// Initialize the database: create schema and seed translation records.
    /// </summary>
    public async Task InitializeAsync()
    {
        var dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Create schema
        using var schemaCmd = connection.CreateCommand();
        schemaCmd.CommandText = BibleSeedData.GenerateSchemaSQL();
        await schemaCmd.ExecuteNonQueryAsync();

        // Seed translations
        using var seedCmd = connection.CreateCommand();
        seedCmd.CommandText = BibleSeedData.SeedTranslationsSQL();
        await seedCmd.ExecuteNonQueryAsync();

        // Seed books for each translation
        foreach (var translationId in new[] { "KJV", "WEB" })
        {
            using var booksCmd = connection.CreateCommand();
            booksCmd.CommandText = BibleSeedData.SeedBooksSQL(translationId);
            await booksCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Seed KJV verse text for testable lookup data (Genesis 1, Psalms 23, John 3, Romans 8).
    /// Called separately from InitializeAsync so tests can control their own data.
    /// </summary>
    public async Task SeedKjvVersesAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var versesCmd = connection.CreateCommand();
        versesCmd.CommandText = BibleSeedData.SeedKjvVersesSQL();
        await versesCmd.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task<List<BibleVerse>> SearchAsync(string query, string translationId, int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<BibleVerse>();

        var results = new List<BibleVerse>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT v.Id, v.BookId, v.Chapter, v.Verse, v.Text, v.TranslationId, b.Name as BookName
            FROM Verses v
            JOIN Books b ON v.BookId = b.Id AND v.TranslationId = b.TranslationId
            WHERE v.TranslationId = @translationId AND v.Text LIKE @query
            LIMIT @maxResults";
        cmd.Parameters.AddWithValue("@translationId", translationId);
        cmd.Parameters.AddWithValue("@query", $"%{query}%");
        cmd.Parameters.AddWithValue("@maxResults", maxResults);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new BibleVerse
            {
                Id = reader.GetInt32(0),
                BookId = reader.GetInt32(1),
                Chapter = reader.GetInt32(2),
                Verse = reader.GetInt32(3),
                Text = reader.GetString(4),
                TranslationId = reader.GetString(5),
                BookName = reader.GetString(6)
            });
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<BibleVerse?> GetVerseAsync(int bookId, int chapter, int verse, string translationId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT v.Id, v.BookId, v.Chapter, v.Verse, v.Text, v.TranslationId, b.Name as BookName
            FROM Verses v
            JOIN Books b ON v.BookId = b.Id AND v.TranslationId = b.TranslationId
            WHERE v.BookId = @bookId AND v.Chapter = @chapter AND v.Verse = @verse AND v.TranslationId = @translationId";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        cmd.Parameters.AddWithValue("@chapter", chapter);
        cmd.Parameters.AddWithValue("@verse", verse);
        cmd.Parameters.AddWithValue("@translationId", translationId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new BibleVerse
            {
                Id = reader.GetInt32(0),
                BookId = reader.GetInt32(1),
                Chapter = reader.GetInt32(2),
                Verse = reader.GetInt32(3),
                Text = reader.GetString(4),
                TranslationId = reader.GetString(5),
                BookName = reader.GetString(6)
            };
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<List<BibleVerse>> GetVersesAsync(ScriptureReference reference)
    {
        var results = new List<BibleVerse>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();

        if (reference.EndChapter != null && reference.EndChapter != reference.Chapter)
        {
            // Cross-chapter range
            cmd.CommandText = @"
                SELECT v.Id, v.BookId, v.Chapter, v.Verse, v.Text, v.TranslationId, b.Name as BookName
                FROM Verses v
                JOIN Books b ON v.BookId = b.Id AND v.TranslationId = b.TranslationId
                WHERE v.BookId = @bookId AND v.TranslationId = @translationId
                AND (
                    (v.Chapter = @startChapter AND v.Verse >= @startVerse)
                    OR (v.Chapter > @startChapter AND v.Chapter < @endChapter)
                    OR (v.Chapter = @endChapter AND v.Verse <= @endVerse)
                )
                ORDER BY v.Chapter, v.Verse";
            cmd.Parameters.AddWithValue("@bookId", reference.BookId);
            cmd.Parameters.AddWithValue("@translationId", reference.TranslationId);
            cmd.Parameters.AddWithValue("@startChapter", reference.Chapter);
            cmd.Parameters.AddWithValue("@startVerse", reference.StartVerse);
            cmd.Parameters.AddWithValue("@endChapter", reference.EndChapter);
            cmd.Parameters.AddWithValue("@endVerse", reference.EndVerse ?? 999);
        }
        else if (reference.EndVerse != null)
        {
            // Same-chapter verse range
            cmd.CommandText = @"
                SELECT v.Id, v.BookId, v.Chapter, v.Verse, v.Text, v.TranslationId, b.Name as BookName
                FROM Verses v
                JOIN Books b ON v.BookId = b.Id AND v.TranslationId = b.TranslationId
                WHERE v.BookId = @bookId AND v.Chapter = @chapter
                AND v.Verse >= @startVerse AND v.Verse <= @endVerse
                AND v.TranslationId = @translationId
                ORDER BY v.Verse";
            cmd.Parameters.AddWithValue("@bookId", reference.BookId);
            cmd.Parameters.AddWithValue("@chapter", reference.Chapter);
            cmd.Parameters.AddWithValue("@startVerse", reference.StartVerse);
            cmd.Parameters.AddWithValue("@endVerse", reference.EndVerse);
            cmd.Parameters.AddWithValue("@translationId", reference.TranslationId);
        }
        else if (reference.StartVerse > 0)
        {
            // Single verse
            cmd.CommandText = @"
                SELECT v.Id, v.BookId, v.Chapter, v.Verse, v.Text, v.TranslationId, b.Name as BookName
                FROM Verses v
                JOIN Books b ON v.BookId = b.Id AND v.TranslationId = b.TranslationId
                WHERE v.BookId = @bookId AND v.Chapter = @chapter AND v.Verse = @startVerse
                AND v.TranslationId = @translationId";
            cmd.Parameters.AddWithValue("@bookId", reference.BookId);
            cmd.Parameters.AddWithValue("@chapter", reference.Chapter);
            cmd.Parameters.AddWithValue("@startVerse", reference.StartVerse);
            cmd.Parameters.AddWithValue("@translationId", reference.TranslationId);
        }
        else
        {
            // Whole chapter
            cmd.CommandText = @"
                SELECT v.Id, v.BookId, v.Chapter, v.Verse, v.Text, v.TranslationId, b.Name as BookName
                FROM Verses v
                JOIN Books b ON v.BookId = b.Id AND v.TranslationId = b.TranslationId
                WHERE v.BookId = @bookId AND v.Chapter = @chapter
                AND v.TranslationId = @translationId
                ORDER BY v.Verse";
            cmd.Parameters.AddWithValue("@bookId", reference.BookId);
            cmd.Parameters.AddWithValue("@chapter", reference.Chapter);
            cmd.Parameters.AddWithValue("@translationId", reference.TranslationId);
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new BibleVerse
            {
                Id = reader.GetInt32(0),
                BookId = reader.GetInt32(1),
                Chapter = reader.GetInt32(2),
                Verse = reader.GetInt32(3),
                Text = reader.GetString(4),
                TranslationId = reader.GetString(5),
                BookName = reader.GetString(6)
            });
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<List<BibleBook>> GetBooksAsync(string translationId)
    {
        var results = new List<BibleBook>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Name, Abbreviation, Testament, ChapterCount, BookOrder
            FROM Books
            WHERE TranslationId = @translationId
            ORDER BY BookOrder";
        cmd.Parameters.AddWithValue("@translationId", translationId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new BibleBook
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Abbreviation = reader.GetString(2),
                Testament = Enum.TryParse<Testament>(reader.GetString(3), out var t) ? t : Testament.OldTestament,
                ChapterCount = reader.GetInt32(4),
                Order = reader.GetInt32(5)
            });
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<List<BibleTranslation>> GetTranslationsAsync()
    {
        var results = new List<BibleTranslation>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Abbreviation, Language, IsDefault FROM Translations";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new BibleTranslation
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Abbreviation = reader.GetString(2),
                Language = reader.GetString(3),
                IsDefault = reader.GetInt32(4) == 1,
                DatabasePath = _databasePath
            });
        }

        return results;
    }

    /// <inheritdoc />
    public Task<ScriptureReference?> ParseReferenceAsync(string referenceText, string translationId)
    {
        var reference = _parser.Parse(referenceText, translationId);
        return Task.FromResult(reference);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Parses the file with <see cref="ZefaniaBibleXmlParser"/> first (invalid
    /// files throw <see cref="InvalidDataException"/> before the database is
    /// touched), then writes the translation record, its book records and all
    /// verses into bible.db inside ONE transaction: any failure rolls back so
    /// no half-imported translation is left behind. Re-importing the same
    /// translation replaces its previous data instead of duplicating verses.
    /// </remarks>
    public async Task<BibleTranslation> ImportTranslationAsync(string xmlFilePath)
    {
        if (string.IsNullOrWhiteSpace(xmlFilePath))
            throw new ArgumentException("XML file path is required.", nameof(xmlFilePath));

        if (!File.Exists(xmlFilePath))
            throw new FileNotFoundException("Bible XML file not found.", xmlFilePath);

        // Parse + validate the whole file before opening the database.
        var document = new ZefaniaBibleXmlParser().Parse(xmlFilePath);

        // Translation identity: prefer the file's own metadata (INFORMATION /
        // biblename); fall back to the file name, as the previous stub did.
        var translationId = !string.IsNullOrWhiteSpace(document.Identifier)
            ? document.Identifier.Trim().ToUpperInvariant()
            : Path.GetFileNameWithoutExtension(xmlFilePath).ToUpperInvariant();
        var name = document.BibleName ?? document.Title ?? translationId;
        var language = !string.IsNullOrWhiteSpace(document.Language)
            ? document.Language.Trim()
            : "en";

        var canonicalBooks = BibleSeedData.GetBookData().ToDictionary(b => b.Id);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();
        try
        {
            // Re-import replaces this translation's previous data.
            using (var deleteVerses = CreateCommand(connection, transaction,
                "DELETE FROM Verses WHERE TranslationId = @id"))
            {
                deleteVerses.Parameters.AddWithValue("@id", translationId);
                await deleteVerses.ExecuteNonQueryAsync();
            }

            using (var deleteBooks = CreateCommand(connection, transaction,
                "DELETE FROM Books WHERE TranslationId = @id"))
            {
                deleteBooks.Parameters.AddWithValue("@id", translationId);
                await deleteBooks.ExecuteNonQueryAsync();
            }

            using (var deleteTranslation = CreateCommand(connection, transaction,
                "DELETE FROM Translations WHERE Id = @id"))
            {
                deleteTranslation.Parameters.AddWithValue("@id", translationId);
                await deleteTranslation.ExecuteNonQueryAsync();
            }

            using (var insertTranslation = CreateCommand(connection, transaction, @"
                INSERT INTO Translations (Id, Name, Abbreviation, Language, IsDefault)
                VALUES (@id, @name, @abbreviation, @language, 0)"))
            {
                insertTranslation.Parameters.AddWithValue("@id", translationId);
                insertTranslation.Parameters.AddWithValue("@name", name);
                insertTranslation.Parameters.AddWithValue("@abbreviation", translationId);
                insertTranslation.Parameters.AddWithValue("@language", language);
                await insertTranslation.ExecuteNonQueryAsync();
            }

            // Book rows for the books present in the file, using the canonical
            // 66-book metadata (name/abbreviation/testament/chapter count).
            using var insertBook = CreateCommand(connection, transaction, @"
                INSERT INTO Books (Id, Name, Abbreviation, Testament, ChapterCount, BookOrder, TranslationId)
                VALUES (@id, @name, @abbreviation, @testament, @chapterCount, @bookOrder, @translationId)");
            var bookIdParam = insertBook.Parameters.Add("@id", SqliteType.Integer);
            var bookNameParam = insertBook.Parameters.Add("@name", SqliteType.Text);
            var bookAbbrParam = insertBook.Parameters.Add("@abbreviation", SqliteType.Text);
            var bookTestamentParam = insertBook.Parameters.Add("@testament", SqliteType.Text);
            var bookChaptersParam = insertBook.Parameters.Add("@chapterCount", SqliteType.Integer);
            var bookOrderParam = insertBook.Parameters.Add("@bookOrder", SqliteType.Integer);
            var bookTranslationParam = insertBook.Parameters.Add("@translationId", SqliteType.Text);
            bookTranslationParam.Value = translationId;

            foreach (var book in document.Books)
            {
                var meta = canonicalBooks[book.BookId];
                bookIdParam.Value = meta.Id;
                bookNameParam.Value = meta.Name;
                bookAbbrParam.Value = meta.Abbreviation;
                bookTestamentParam.Value = meta.Testament;
                bookChaptersParam.Value = meta.ChapterCount;
                bookOrderParam.Value = meta.Id;
                await insertBook.ExecuteNonQueryAsync();
            }

            // All verses, parameterized bulk insert inside the same transaction.
            using var insertVerse = CreateCommand(connection, transaction, @"
                INSERT INTO Verses (BookId, Chapter, Verse, Text, TranslationId)
                VALUES (@bookId, @chapter, @verse, @text, @translationId)");
            var verseBookParam = insertVerse.Parameters.Add("@bookId", SqliteType.Integer);
            var verseChapterParam = insertVerse.Parameters.Add("@chapter", SqliteType.Integer);
            var verseNumberParam = insertVerse.Parameters.Add("@verse", SqliteType.Integer);
            var verseTextParam = insertVerse.Parameters.Add("@text", SqliteType.Text);
            var verseTranslationParam = insertVerse.Parameters.Add("@translationId", SqliteType.Text);
            verseTranslationParam.Value = translationId;

            foreach (var book in document.Books)
            {
                verseBookParam.Value = book.BookId;
                foreach (var chapter in book.Chapters)
                {
                    verseChapterParam.Value = chapter.ChapterNumber;
                    foreach (var verse in chapter.Verses)
                    {
                        verseNumberParam.Value = verse.VerseNumber;
                        verseTextParam.Value = verse.Text;
                        await insertVerse.ExecuteNonQueryAsync();
                    }
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return new BibleTranslation
        {
            Id = translationId,
            Name = name,
            Abbreviation = translationId,
            Language = language,
            IsDefault = false,
            DatabasePath = _databasePath
        };
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        return cmd;
    }
}
