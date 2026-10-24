using WorshipPlayer.Core.Models.Bible;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// Provides access to Bible data from a local SQLite database.
/// Supports searching, verse retrieval, and translation management.
/// </summary>
public interface IBibleProvider
{
    /// <summary>
    /// Search for verses matching the given query text.
    /// </summary>
    /// <param name="query">Text to search for in verse content.</param>
    /// <param name="translationId">Translation to search in.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <returns>Matching verses.</returns>
    Task<List<BibleVerse>> SearchAsync(string query, string translationId, int maxResults = 50);

    /// <summary>
    /// Get a single verse by book, chapter, and verse number.
    /// </summary>
    Task<BibleVerse?> GetVerseAsync(int bookId, int chapter, int verse, string translationId);

    /// <summary>
    /// Get a range of verses (e.g., John 3:16-18).
    /// </summary>
    /// <param name="reference">Parsed scripture reference.</param>
    /// <returns>Ordered list of verses in the range.</returns>
    Task<List<BibleVerse>> GetVersesAsync(ScriptureReference reference);

    /// <summary>
    /// Get all available books for a given translation.
    /// </summary>
    Task<List<BibleBook>> GetBooksAsync(string translationId);

    /// <summary>
    /// Get all installed Bible translations.
    /// </summary>
    Task<List<BibleTranslation>> GetTranslationsAsync();

    /// <summary>
    /// Count the verses stored for a translation (0 when the translation is
    /// unknown or has no verse data). Used to report concrete import results.
    /// </summary>
    /// <param name="translationId">Translation to count verses for.</param>
    Task<int> GetVerseCountAsync(string translationId);

    /// <summary>
    /// Parse a scripture reference string (e.g., "John 3:16-18") into a ScriptureReference object.
    /// </summary>
    /// <param name="referenceText">The reference string to parse.</param>
    /// <param name="translationId">Default translation to use.</param>
    /// <returns>Parsed reference, or null if invalid.</returns>
    Task<ScriptureReference?> ParseReferenceAsync(string referenceText, string translationId);

    /// <summary>
    /// Import a Bible translation from an XML file into the local database.
    /// </summary>
    /// <param name="xmlFilePath">Path to the Bible XML file.</param>
    /// <returns>The created translation record.</returns>
    Task<BibleTranslation> ImportTranslationAsync(string xmlFilePath);
}
