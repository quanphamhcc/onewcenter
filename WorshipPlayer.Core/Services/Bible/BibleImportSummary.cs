using WorshipPlayer.Core.Models.Bible;

namespace WorshipPlayer.Core.Services.Bible;

/// <summary>
/// Builds the user-facing summary line reported after a Bible XML import
/// (dot2-T12 / R7.3): the translation's name and code plus how many books and
/// verses were actually loaded, so the Settings UI never reports a bare
/// "imported" without concrete numbers. Pure formatting — no I/O.
/// </summary>
public static class BibleImportSummary
{
    /// <summary>
    /// Format the success message for a completed import, e.g.
    /// "Imported 'New Vietnamese Bible (2002)' (VIETNVB): 2 books, 72 verses."
    /// </summary>
    public static string Format(BibleTranslation translation, int bookCount, int verseCount)
    {
        ArgumentNullException.ThrowIfNull(translation);

        var bookWord = bookCount == 1 ? "book" : "books";
        var verseWord = verseCount == 1 ? "verse" : "verses";
        return $"Imported '{translation.Name}' ({translation.Abbreviation}): " +
               $"{bookCount} {bookWord}, {verseCount} {verseWord}.";
    }
}
