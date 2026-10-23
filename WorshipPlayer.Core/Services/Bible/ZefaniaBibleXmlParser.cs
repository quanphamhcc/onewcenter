using System.Xml;
using System.Xml.Linq;

namespace WorshipPlayer.Core.Services.Bible;

/// <summary>
/// In-memory representation of a parsed Zefania XML Bible file: translation
/// metadata plus every book/chapter/verse found in the file.
/// </summary>
public sealed class ZefaniaBibleDocument
{
    /// <summary>Value of the root element's <c>biblename</c> attribute, if present.</summary>
    public string? BibleName { get; init; }

    /// <summary>Value of INFORMATION/identifier, if present (e.g. "VIETNVB").</summary>
    public string? Identifier { get; init; }

    /// <summary>Value of INFORMATION/language, if present (e.g. "VIE").</summary>
    public string? Language { get; init; }

    /// <summary>Value of INFORMATION/title, if present.</summary>
    public string? Title { get; init; }

    public List<ZefaniaBook> Books { get; } = new();

    public int TotalVerseCount => Books.Sum(b => b.Chapters.Sum(c => c.Verses.Count));
}

/// <summary>One BIBLEBOOK element: a canonical book (1-66) with its chapters.</summary>
public sealed class ZefaniaBook
{
    /// <summary>Canonical book id 1-66, from the <c>bnumber</c> attribute.</summary>
    public int BookId { get; init; }

    /// <summary>Value of the <c>bname</c> attribute, if present.</summary>
    public string? Name { get; init; }

    /// <summary>Value of the <c>bsname</c> attribute, if present.</summary>
    public string? ShortName { get; init; }

    public List<ZefaniaChapter> Chapters { get; } = new();
}

/// <summary>One CHAPTER element with its verses.</summary>
public sealed class ZefaniaChapter
{
    public int ChapterNumber { get; init; }

    public List<ZefaniaVerse> Verses { get; } = new();
}

/// <summary>One VERS element.</summary>
public sealed class ZefaniaVerse
{
    public int VerseNumber { get; init; }

    public string Text { get; init; } = "";
}

/// <summary>
/// Pure parser for Zefania XML Bible files
/// (<c>&lt;XMLBIBLE biblename=...&gt;&lt;BIBLEBOOK bnumber/bname/bsname&gt;&lt;CHAPTER cnumber&gt;&lt;VERS vnumber&gt;text&lt;/VERS&gt;</c>).
/// Book numbers are validated against the canonical 66-book metadata in
/// <see cref="BibleSeedData.GetBookData"/> so the numbers can be used directly
/// as book ids by <see cref="BibleDatabaseService"/>.
/// Throws <see cref="InvalidDataException"/> with a descriptive message for any
/// file that is not a usable Zefania Bible (wrong root element, missing or
/// out-of-canon book numbers, no verses, malformed XML).
/// </summary>
public sealed class ZefaniaBibleXmlParser
{
    /// <summary>
    /// Parse a Zefania XML file from disk. The whole file is parsed and
    /// validated before returning, so callers can safely write the result to
    /// the database inside a single transaction.
    /// </summary>
    public ZefaniaBibleDocument Parse(string xmlFilePath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(xmlFilePath);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(xmlFilePath)}' is not well-formed XML: {ex.Message}", ex);
        }

        return Parse(doc, Path.GetFileName(xmlFilePath));
    }

    /// <summary>Parse and validate an already-loaded document.</summary>
    public ZefaniaBibleDocument Parse(XDocument doc, string sourceName = "XML")
    {
        var root = doc.Root;
        if (root == null || !IsElement(root, "XMLBIBLE"))
        {
            throw new InvalidDataException(
                $"'{sourceName}' is not a Zefania XML Bible file: the root element must be <XMLBIBLE>.");
        }

        var canonicalBookIds = new HashSet<int>(BibleSeedData.GetBookData().Select(b => b.Id));

        var document = new ZefaniaBibleDocument
        {
            BibleName = AttributeValue(root, "biblename"),
            Identifier = ChildText(root, "INFORMATION", "identifier"),
            Language = ChildText(root, "INFORMATION", "language"),
            Title = ChildText(root, "INFORMATION", "title")
        };

        foreach (var bookElement in root.Elements().Where(e => IsElement(e, "BIBLEBOOK")))
        {
            var book = ParseBook(bookElement, canonicalBookIds, sourceName);
            document.Books.Add(book);
        }

        if (document.Books.Count == 0)
        {
            throw new InvalidDataException(
                $"'{sourceName}' is not a usable Zefania XML Bible file: it contains no <BIBLEBOOK> elements.");
        }

        if (document.TotalVerseCount == 0)
        {
            throw new InvalidDataException(
                $"'{sourceName}' is not a usable Zefania XML Bible file: it contains no verses.");
        }

        return document;
    }

    private static ZefaniaBook ParseBook(XElement bookElement, HashSet<int> canonicalBookIds, string sourceName)
    {
        var numberText = AttributeValue(bookElement, "bnumber");
        if (numberText == null || !int.TryParse(numberText, out var bookId))
        {
            throw new InvalidDataException(
                $"'{sourceName}': a <BIBLEBOOK> element is missing a valid 'bnumber' attribute.");
        }

        if (!canonicalBookIds.Contains(bookId))
        {
            throw new InvalidDataException(
                $"'{sourceName}': book number {bookId} is outside the canonical 66-book range (1-66).");
        }

        var book = new ZefaniaBook
        {
            BookId = bookId,
            Name = AttributeValue(bookElement, "bname"),
            ShortName = AttributeValue(bookElement, "bsname")
        };

        foreach (var chapterElement in bookElement.Elements().Where(e => IsElement(e, "CHAPTER")))
        {
            var chapterNumberText = AttributeValue(chapterElement, "cnumber");
            if (chapterNumberText == null || !int.TryParse(chapterNumberText, out var chapterNumber))
            {
                throw new InvalidDataException(
                    $"'{sourceName}': book {bookId} has a <CHAPTER> element without a valid 'cnumber' attribute.");
            }

            var chapter = new ZefaniaChapter { ChapterNumber = chapterNumber };

            foreach (var verseElement in chapterElement.Elements().Where(e => IsElement(e, "VERS")))
            {
                var verseNumberText = AttributeValue(verseElement, "vnumber");
                if (verseNumberText == null || !int.TryParse(verseNumberText, out var verseNumber))
                {
                    throw new InvalidDataException(
                        $"'{sourceName}': book {bookId} chapter {chapterNumber} has a <VERS> element without a valid 'vnumber' attribute.");
                }

                chapter.Verses.Add(new ZefaniaVerse
                {
                    VerseNumber = verseNumber,
                    // XElement.Value concatenates the text of the element and any
                    // inline child elements (e.g. formatting tags inside a verse).
                    Text = verseElement.Value.Trim()
                });
            }

            book.Chapters.Add(chapter);
        }

        return book;
    }

    private static bool IsElement(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static string? AttributeValue(XElement element, string attributeName)
    {
        var attribute = element.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, attributeName, StringComparison.OrdinalIgnoreCase));
        var value = attribute?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? ChildText(XElement parent, string containerName, string childName)
    {
        var container = parent.Elements().FirstOrDefault(e => IsElement(e, containerName));
        var child = container?.Elements().FirstOrDefault(e => IsElement(e, childName));
        var value = child?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
