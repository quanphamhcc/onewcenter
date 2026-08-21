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
    public async Task<BibleTranslation> ImportTranslationAsync(string xmlFilePath)
    {
        if (string.IsNullOrWhiteSpace(xmlFilePath))
            throw new ArgumentException("XML file path is required.", nameof(xmlFilePath));

        if (!File.Exists(xmlFilePath))
            throw new FileNotFoundException("Bible XML file not found.", xmlFilePath);

        // Read XML and determine translation ID from the filename
        var fileName = Path.GetFileNameWithoutExtension(xmlFilePath);
        var translationId = fileName.ToUpperInvariant();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Insert translation record
        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT OR IGNORE INTO Translations (Id, Name, Abbreviation, Language, IsDefault)
            VALUES (@id, @name, @abbreviation, 'en', 0)";
        insertCmd.Parameters.AddWithValue("@id", translationId);
        insertCmd.Parameters.AddWithValue("@name", translationId);
        insertCmd.Parameters.AddWithValue("@abbreviation", translationId);
        await insertCmd.ExecuteNonQueryAsync();

        return new BibleTranslation
        {
            Id = translationId,
            Name = translationId,
            Abbreviation = translationId,
            Language = "en",
            IsDefault = false,
            DatabasePath = _databasePath
        };
    }
}
