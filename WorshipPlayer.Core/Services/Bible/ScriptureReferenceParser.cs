using System.Text.RegularExpressions;
using WorshipPlayer.Core.Models.Bible;

namespace WorshipPlayer.Core.Services.Bible;

/// <summary>
/// Parses scripture reference strings (e.g., "John 3:16", "1 Corinthians 13:4-7", "Psalm 23")
/// into ScriptureReference objects.
/// </summary>
public class ScriptureReferenceParser
{
    // Maps abbreviated/alternate book names to canonical names
    private static readonly Dictionary<string, (string Name, int Id)> BookAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Old Testament
        { "Genesis", ("Genesis", 1) }, { "Gen", ("Genesis", 1) }, { "Ge", ("Genesis", 1) },
        { "Exodus", ("Exodus", 2) }, { "Exod", ("Exodus", 2) }, { "Ex", ("Exodus", 2) },
        { "Leviticus", ("Leviticus", 3) }, { "Lev", ("Leviticus", 3) }, { "Le", ("Leviticus", 3) },
        { "Numbers", ("Numbers", 4) }, { "Num", ("Numbers", 4) }, { "Nu", ("Numbers", 4) },
        { "Deuteronomy", ("Deuteronomy", 5) }, { "Deut", ("Deuteronomy", 5) }, { "Dt", ("Deuteronomy", 5) },
        { "Joshua", ("Joshua", 6) }, { "Josh", ("Joshua", 6) }, { "Jos", ("Joshua", 6) },
        { "Judges", ("Judges", 7) }, { "Judg", ("Judges", 7) }, { "Jdg", ("Judges", 7) },
        { "Ruth", ("Ruth", 8) }, { "Ru", ("Ruth", 8) },
        { "1 Samuel", ("1 Samuel", 9) }, { "1Samuel", ("1 Samuel", 9) }, { "1 Sam", ("1 Samuel", 9) }, { "1Sam", ("1 Samuel", 9) }, { "I Samuel", ("1 Samuel", 9) }, { "I Sam", ("1 Samuel", 9) },
        { "2 Samuel", ("2 Samuel", 10) }, { "2Samuel", ("2 Samuel", 10) }, { "2 Sam", ("2 Samuel", 10) }, { "2Sam", ("2 Samuel", 10) }, { "II Samuel", ("2 Samuel", 10) }, { "II Sam", ("2 Samuel", 10) },
        { "1 Kings", ("1 Kings", 11) }, { "1Kings", ("1 Kings", 11) }, { "1 Kgs", ("1 Kings", 11) }, { "1Kgs", ("1 Kings", 11) }, { "I Kings", ("1 Kings", 11) }, { "I Kgs", ("1 Kings", 11) },
        { "2 Kings", ("2 Kings", 12) }, { "2Kings", ("2 Kings", 12) }, { "2 Kgs", ("2 Kings", 12) }, { "2Kgs", ("2 Kings", 12) }, { "II Kings", ("2 Kings", 12) }, { "II Kgs", ("2 Kings", 12) },
        { "1 Chronicles", ("1 Chronicles", 13) }, { "1Chronicles", ("1 Chronicles", 13) }, { "1 Chr", ("1 Chronicles", 13) }, { "1Chr", ("1 Chronicles", 13) }, { "I Chronicles", ("1 Chronicles", 13) }, { "I Chr", ("1 Chronicles", 13) },
        { "2 Chronicles", ("2 Chronicles", 14) }, { "2Chronicles", ("2 Chronicles", 14) }, { "2 Chr", ("2 Chronicles", 14) }, { "2Chr", ("2 Chronicles", 14) }, { "II Chronicles", ("2 Chronicles", 14) }, { "II Chr", ("2 Chronicles", 14) },
        { "Ezra", ("Ezra", 15) }, { "Ezr", ("Ezra", 15) },
        { "Nehemiah", ("Nehemiah", 16) }, { "Neh", ("Nehemiah", 16) }, { "Ne", ("Nehemiah", 16) },
        { "Esther", ("Esther", 17) }, { "Est", ("Esther", 17) }, { "Es", ("Esther", 17) },
        { "Job", ("Job", 18) },
        { "Psalms", ("Psalms", 19) }, { "Psalm", ("Psalms", 19) }, { "Ps", ("Psalms", 19) }, { "Psa", ("Psalms", 19) },
        { "Proverbs", ("Proverbs", 20) }, { "Prov", ("Proverbs", 20) }, { "Pr", ("Proverbs", 20) },
        { "Ecclesiastes", ("Ecclesiastes", 21) }, { "Eccl", ("Ecclesiastes", 21) }, { "Ecc", ("Ecclesiastes", 21) }, { "Ec", ("Ecclesiastes", 21) },
        { "Song of Solomon", ("Song of Solomon", 22) }, { "Song", ("Song of Solomon", 22) }, { "SoS", ("Song of Solomon", 22) }, { "SS", ("Song of Solomon", 22) },
        { "Isaiah", ("Isaiah", 23) }, { "Isa", ("Isaiah", 23) }, { "Is", ("Isaiah", 23) },
        { "Jeremiah", ("Jeremiah", 24) }, { "Jer", ("Jeremiah", 24) }, { "Je", ("Jeremiah", 24) },
        { "Lamentations", ("Lamentations", 25) }, { "Lam", ("Lamentations", 25) }, { "La", ("Lamentations", 25) },
        { "Ezekiel", ("Ezekiel", 26) }, { "Ezek", ("Ezekiel", 26) }, { "Eze", ("Ezekiel", 26) },
        { "Daniel", ("Daniel", 27) }, { "Dan", ("Daniel", 27) }, { "Da", ("Daniel", 27) },
        { "Hosea", ("Hosea", 28) }, { "Hos", ("Hosea", 28) }, { "Ho", ("Hosea", 28) },
        { "Joel", ("Joel", 29) },
        { "Amos", ("Amos", 30) }, { "Am", ("Amos", 30) },
        { "Obadiah", ("Obadiah", 31) }, { "Obad", ("Obadiah", 31) }, { "Ob", ("Obadiah", 31) },
        { "Jonah", ("Jonah", 32) }, { "Jon", ("Jonah", 32) },
        { "Micah", ("Micah", 33) }, { "Mic", ("Micah", 33) },
        { "Nahum", ("Nahum", 34) }, { "Nah", ("Nahum", 34) }, { "Na", ("Nahum", 34) },
        { "Habakkuk", ("Habakkuk", 35) }, { "Hab", ("Habakkuk", 35) },
        { "Zephaniah", ("Zephaniah", 36) }, { "Zeph", ("Zephaniah", 36) }, { "Zep", ("Zephaniah", 36) },
        { "Haggai", ("Haggai", 37) }, { "Hag", ("Haggai", 37) }, { "Hg", ("Haggai", 37) },
        { "Zechariah", ("Zechariah", 38) }, { "Zech", ("Zechariah", 38) }, { "Zec", ("Zechariah", 38) },
        { "Malachi", ("Malachi", 39) }, { "Mal", ("Malachi", 39) },
        // New Testament
        { "Matthew", ("Matthew", 40) }, { "Matt", ("Matthew", 40) }, { "Mt", ("Matthew", 40) },
        { "Mark", ("Mark", 41) }, { "Mk", ("Mark", 41) },
        { "Luke", ("Luke", 42) }, { "Lk", ("Luke", 42) },
        { "John", ("John", 43) }, { "Jn", ("John", 43) }, { "Joh", ("John", 43) },
        { "Acts", ("Acts", 44) }, { "Act", ("Acts", 44) }, { "Ac", ("Acts", 44) },
        { "Romans", ("Romans", 45) }, { "Rom", ("Romans", 45) }, { "Ro", ("Romans", 45) },
        { "1 Corinthians", ("1 Corinthians", 46) }, { "1Corinthians", ("1 Corinthians", 46) }, { "1 Cor", ("1 Corinthians", 46) }, { "1Cor", ("1 Corinthians", 46) }, { "I Corinthians", ("1 Corinthians", 46) }, { "I Cor", ("1 Corinthians", 46) },
        { "2 Corinthians", ("2 Corinthians", 47) }, { "2Corinthians", ("2 Corinthians", 47) }, { "2 Cor", ("2 Corinthians", 47) }, { "2Cor", ("2 Corinthians", 47) }, { "II Corinthians", ("2 Corinthians", 47) }, { "II Cor", ("2 Corinthians", 47) },
        { "Galatians", ("Galatians", 48) }, { "Gal", ("Galatians", 48) }, { "Ga", ("Galatians", 48) },
        { "Ephesians", ("Ephesians", 49) }, { "Eph", ("Ephesians", 49) },
        { "Philippians", ("Philippians", 50) }, { "Phil", ("Philippians", 50) }, { "Php", ("Philippians", 50) },
        { "Colossians", ("Colossians", 51) }, { "Col", ("Colossians", 51) },
        { "1 Thessalonians", ("1 Thessalonians", 52) }, { "1Thessalonians", ("1 Thessalonians", 52) }, { "1 Thess", ("1 Thessalonians", 52) }, { "1Thess", ("1 Thessalonians", 52) }, { "I Thessalonians", ("1 Thessalonians", 52) }, { "I Thess", ("1 Thessalonians", 52) },
        { "2 Thessalonians", ("2 Thessalonians", 53) }, { "2Thessalonians", ("2 Thessalonians", 53) }, { "2 Thess", ("2 Thessalonians", 53) }, { "2Thess", ("2 Thessalonians", 53) }, { "II Thessalonians", ("2 Thessalonians", 53) }, { "II Thess", ("2 Thessalonians", 53) },
        { "1 Timothy", ("1 Timothy", 54) }, { "1Timothy", ("1 Timothy", 54) }, { "1 Tim", ("1 Timothy", 54) }, { "1Tim", ("1 Timothy", 54) }, { "I Timothy", ("1 Timothy", 54) }, { "I Tim", ("1 Timothy", 54) },
        { "2 Timothy", ("2 Timothy", 55) }, { "2Timothy", ("2 Timothy", 55) }, { "2 Tim", ("2 Timothy", 55) }, { "2Tim", ("2 Timothy", 55) }, { "II Timothy", ("2 Timothy", 55) }, { "II Tim", ("2 Timothy", 55) },
        { "Titus", ("Titus", 56) }, { "Tit", ("Titus", 56) },
        { "Philemon", ("Philemon", 57) }, { "Phlm", ("Philemon", 57) }, { "Phm", ("Philemon", 57) },
        { "Hebrews", ("Hebrews", 58) }, { "Heb", ("Hebrews", 58) },
        { "James", ("James", 59) }, { "Jas", ("James", 59) }, { "Jm", ("James", 59) },
        { "1 Peter", ("1 Peter", 60) }, { "1Peter", ("1 Peter", 60) }, { "1 Pet", ("1 Peter", 60) }, { "1Pet", ("1 Peter", 60) }, { "I Peter", ("1 Peter", 60) }, { "I Pet", ("1 Peter", 60) },
        { "2 Peter", ("2 Peter", 61) }, { "2Peter", ("2 Peter", 61) }, { "2 Pet", ("2 Peter", 61) }, { "2Pet", ("2 Peter", 61) }, { "II Peter", ("2 Peter", 61) }, { "II Pet", ("2 Peter", 61) },
        { "1 John", ("1 John", 62) }, { "1John", ("1 John", 62) }, { "1 Jn", ("1 John", 62) }, { "1Jn", ("1 John", 62) }, { "I John", ("1 John", 62) }, { "I Jn", ("1 John", 62) },
        { "2 John", ("2 John", 63) }, { "2John", ("2 John", 63) }, { "2 Jn", ("2 John", 63) }, { "2Jn", ("2 John", 63) }, { "II John", ("2 John", 63) }, { "II Jn", ("2 John", 63) },
        { "3 John", ("3 John", 64) }, { "3John", ("3 John", 64) }, { "3 Jn", ("3 John", 64) }, { "3Jn", ("3 John", 64) }, { "III John", ("3 John", 64) }, { "III Jn", ("3 John", 64) },
        { "Jude", ("Jude", 65) }, { "Jud", ("Jude", 65) },
        { "Revelation", ("Revelation", 66) }, { "Rev", ("Revelation", 66) }, { "Re", ("Revelation", 66) }
    };

    // Pattern: optional number prefix + book name + optional chapter:verse-verse
    private static readonly Regex ReferenceRegex = new(
        @"^((?:[123I]+\s*)?[A-Za-z]+(?:\s+[A-Za-z]+)*)\s*(\d+)?(?::(\d+))?(?:\s*[-–]\s*(?:(\d+):)?(\d+))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parse a scripture reference string into a ScriptureReference object.
    /// Handles formats like "John 3:16", "1 Corinthians 13:4-7", "Psalm 23", "Gen 1:1-3".
    /// </summary>
    /// <param name="referenceText">The reference string to parse.</param>
    /// <param name="translationId">Default translation to assign.</param>
    /// <returns>Parsed reference, or null if the input cannot be parsed.</returns>
    public ScriptureReference? Parse(string referenceText, string translationId = "KJV")
    {
        if (string.IsNullOrWhiteSpace(referenceText))
            return null;

        var text = referenceText.Trim();

        // Normalize Roman numeral prefixes: "I " -> "1 ", "II " -> "2 ", "III " -> "3 "
        text = Regex.Replace(text, @"^III\s+", "3 ");
        text = Regex.Replace(text, @"^II\s+", "2 ");
        text = Regex.Replace(text, @"^I\s+", "1 ");

        var match = ReferenceRegex.Match(text);
        if (!match.Success)
            return null;

        var bookPart = match.Groups[1].Value.Trim();
        var chapterStr = match.Groups[2].Value;
        var startVerseStr = match.Groups[3].Value;
        var endChapterStr = match.Groups[4].Value;
        var endVerseStr = match.Groups[5].Value;

        // Resolve book name
        if (!TryResolveBook(bookPart, out var bookName, out var bookId))
            return null;

        var reference = new ScriptureReference
        {
            BookName = bookName,
            BookId = bookId,
            TranslationId = translationId
        };

        // Parse chapter
        if (!string.IsNullOrEmpty(chapterStr))
        {
            reference.Chapter = int.Parse(chapterStr);
        }
        else
        {
            // No chapter specified - might be a whole-book reference or invalid
            return null;
        }

        // Parse start verse
        if (!string.IsNullOrEmpty(startVerseStr))
        {
            reference.StartVerse = int.Parse(startVerseStr);
        }
        else
        {
            // Chapter only (e.g., "Psalm 23") - whole chapter
            reference.StartVerse = 1;
            reference.EndVerse = null; // Will be filled from database
        }

        // Parse end verse/chapter
        if (!string.IsNullOrEmpty(endVerseStr))
        {
            if (!string.IsNullOrEmpty(endChapterStr))
            {
                reference.EndChapter = int.Parse(endChapterStr);
                reference.EndVerse = int.Parse(endVerseStr);
            }
            else
            {
                reference.EndVerse = int.Parse(endVerseStr);
            }
        }

        reference.DisplayText = reference.ToReferenceString();
        return reference;
    }

    /// <summary>
    /// Try to resolve a book name string to its canonical name and ID.
    /// </summary>
    private bool TryResolveBook(string input, out string bookName, out int bookId)
    {
        bookName = "";
        bookId = 0;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Direct lookup
        if (BookAliases.TryGetValue(input, out var result))
        {
            bookName = result.Name;
            bookId = result.Id;
            return true;
        }

        // Try without spaces (e.g., "1John" -> "1 John")
        var noSpace = Regex.Replace(input, @"^(\d)\s*", "$1 ");
        if (BookAliases.TryGetValue(noSpace, out result))
        {
            bookName = result.Name;
            bookId = result.Id;
            return true;
        }

        // Try prefix match (e.g., "Ge" for Genesis, but only if unambiguous)
        var matches = BookAliases
            .Where(kv => kv.Key.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value)
            .Distinct()
            .ToList();

        if (matches.Count == 1)
        {
            bookName = matches[0].Name;
            bookId = matches[0].Id;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get all known book names (canonical names only, no aliases).
    /// </summary>
    public static List<string> GetBookNames()
    {
        return BookAliases.Values
            .Select(v => v.Name)
            .Distinct()
            .OrderBy(n => BookAliases.First(kv => kv.Value.Name == n).Value.Id)
            .ToList();
    }

    /// <summary>
    /// Get the book ID for a given canonical book name.
    /// </summary>
    public static int? GetBookId(string bookName)
    {
        var entry = BookAliases.Values.FirstOrDefault(v =>
            v.Name.Equals(bookName, StringComparison.OrdinalIgnoreCase));
        return entry.Id > 0 ? entry.Id : null;
    }
}
