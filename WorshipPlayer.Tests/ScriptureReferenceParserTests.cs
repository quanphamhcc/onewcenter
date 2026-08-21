using Xunit;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services.Bible;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for ScriptureReferenceParser.
/// Tests various reference formats, abbreviations, and edge cases.
/// </summary>
public class ScriptureReferenceParserTests
{
    private readonly ScriptureReferenceParser _parser = new();

    [Fact]
    public void Parse_SingleVerse_John316()
    {
        var result = _parser.Parse("John 3:16");

        Assert.NotNull(result);
        Assert.Equal("John", result!.BookName);
        Assert.Equal(43, result.BookId);
        Assert.Equal(3, result.Chapter);
        Assert.Equal(16, result.StartVerse);
        Assert.Null(result.EndVerse);
        Assert.Equal("KJV", result.TranslationId);
    }

    [Fact]
    public void Parse_VerseRange_John316To18()
    {
        var result = _parser.Parse("John 3:16-18");

        Assert.NotNull(result);
        Assert.Equal("John", result!.BookName);
        Assert.Equal(3, result.Chapter);
        Assert.Equal(16, result.StartVerse);
        Assert.Equal(18, result.EndVerse);
    }

    [Fact]
    public void Parse_NumberedBook_1Corinthians()
    {
        var result = _parser.Parse("1 Corinthians 13:4-7");

        Assert.NotNull(result);
        Assert.Equal("1 Corinthians", result!.BookName);
        Assert.Equal(46, result.BookId);
        Assert.Equal(13, result.Chapter);
        Assert.Equal(4, result.StartVerse);
        Assert.Equal(7, result.EndVerse);
    }

    [Fact]
    public void Parse_WholeChapter_Psalm23()
    {
        var result = _parser.Parse("Psalm 23");

        Assert.NotNull(result);
        Assert.Equal("Psalms", result!.BookName);
        Assert.Equal(19, result.BookId);
        Assert.Equal(23, result.Chapter);
        Assert.Equal(1, result.StartVerse);
        Assert.Null(result.EndVerse);
    }

    [Fact]
    public void Parse_FirstBook_Genesis11()
    {
        var result = _parser.Parse("Genesis 1:1");

        Assert.NotNull(result);
        Assert.Equal("Genesis", result!.BookName);
        Assert.Equal(1, result.BookId);
        Assert.Equal(1, result.Chapter);
        Assert.Equal(1, result.StartVerse);
    }

    [Fact]
    public void Parse_Abbreviation_Gen1To3()
    {
        var result = _parser.Parse("Gen 1:1-3");

        Assert.NotNull(result);
        Assert.Equal("Genesis", result!.BookName);
        Assert.Equal(1, result.BookId);
        Assert.Equal(1, result.Chapter);
        Assert.Equal(1, result.StartVerse);
        Assert.Equal(3, result.EndVerse);
    }

    [Fact]
    public void Parse_CustomTranslation_WEB()
    {
        var result = _parser.Parse("Romans 8:28", "WEB");

        Assert.NotNull(result);
        Assert.Equal("Romans", result!.BookName);
        Assert.Equal("WEB", result.TranslationId);
    }

    [Fact]
    public void Parse_Revelation_LastBook()
    {
        var result = _parser.Parse("Revelation 22:21");

        Assert.NotNull(result);
        Assert.Equal("Revelation", result!.BookName);
        Assert.Equal(66, result.BookId);
        Assert.Equal(22, result.Chapter);
        Assert.Equal(21, result.StartVerse);
    }

    [Fact]
    public void Parse_2Peter_DoubleNumberPrefix()
    {
        var result = _parser.Parse("2 Peter 3:9");

        Assert.NotNull(result);
        Assert.Equal("2 Peter", result!.BookName);
        Assert.Equal(61, result.BookId);
        Assert.Equal(3, result.Chapter);
        Assert.Equal(9, result.StartVerse);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        var result = _parser.Parse("");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_NullString_ReturnsNull()
    {
        var result = _parser.Parse(null!);
        Assert.Null(result);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsNull()
    {
        var result = _parser.Parse("   ");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidReference_ReturnsNull()
    {
        var result = _parser.Parse("NotABook 1:1");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_NumbersOnly_ReturnsNull()
    {
        var result = _parser.Parse("123");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_GeneratesDisplayText()
    {
        var result = _parser.Parse("John 3:16-18");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.DisplayText));
        Assert.Contains("John", result.DisplayText);
    }

    [Theory]
    [InlineData("Matt 5:1", "Matthew", 40)]
    [InlineData("Mk 1:1", "Mark", 41)]
    [InlineData("Lk 2:1", "Luke", 42)]
    [InlineData("Rev 1:1", "Revelation", 66)]
    [InlineData("Heb 11:1", "Hebrews", 58)]
    public void Parse_Abbreviations_ResolveCorrectly(string input, string expectedBook, int expectedId)
    {
        var result = _parser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedBook, result!.BookName);
        Assert.Equal(expectedId, result.BookId);
    }

    [Fact]
    public void GetBookNames_Returns66Books()
    {
        var names = ScriptureReferenceParser.GetBookNames();

        Assert.Equal(66, names.Count);
        Assert.Equal("Genesis", names[0]);
        Assert.Equal("Revelation", names[65]);
    }

    [Fact]
    public void GetBookId_ValidName_ReturnsId()
    {
        var id = ScriptureReferenceParser.GetBookId("Genesis");
        Assert.Equal(1, id);
    }

    [Fact]
    public void GetBookId_InvalidName_ReturnsNull()
    {
        var id = ScriptureReferenceParser.GetBookId("NotARealBook");
        Assert.Null(id);
    }
}
