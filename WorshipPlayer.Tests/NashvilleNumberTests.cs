using Xunit;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the Nashville number conversion in ChordProParserService.
/// </summary>
public class NashvilleNumberTests
{
    private readonly ChordProParserService _parser = new();

    [Fact]
    public void ConvertToNashville_KeyOfC_BasicChords()
    {
        string input = "[C]Amazing [Am]grace";
        string result = _parser.ConvertToNashvilleNumbers(input, "C");
        Assert.Equal("[1]Amazing [6m]grace", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfC_AllNaturalDegrees()
    {
        // C=1, D=2, E=3, F=4, G=5, A=6, B=7
        Assert.Equal("[1]", _parser.ConvertToNashvilleNumbers("[C]", "C"));
        Assert.Equal("[2]", _parser.ConvertToNashvilleNumbers("[D]", "C"));
        Assert.Equal("[3]", _parser.ConvertToNashvilleNumbers("[E]", "C"));
        Assert.Equal("[4]", _parser.ConvertToNashvilleNumbers("[F]", "C"));
        Assert.Equal("[5]", _parser.ConvertToNashvilleNumbers("[G]", "C"));
        Assert.Equal("[6]", _parser.ConvertToNashvilleNumbers("[A]", "C"));
        Assert.Equal("[7]", _parser.ConvertToNashvilleNumbers("[B]", "C"));
    }

    [Fact]
    public void ConvertToNashville_KeyOfG_CommonProgression()
    {
        // In key of G: G=1, C=4, D=5, Em=6m, Am=2m
        string input = "[G]How great [C]is our [D]God";
        string result = _parser.ConvertToNashvilleNumbers(input, "G");
        Assert.Equal("[1]How great [4]is our [5]God", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfG_MinorChords()
    {
        string input = "[Em]Sing with [Am]me";
        string result = _parser.ConvertToNashvilleNumbers(input, "G");
        Assert.Equal("[6m]Sing with [2m]me", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfD_SeventhChords()
    {
        // In key of D: D=1, G=4, A=5, A7=5-7
        string input = "[D]Lord [G]I [A7]come";
        string result = _parser.ConvertToNashvilleNumbers(input, "D");
        Assert.Equal("[1]Lord [4]I [5-7]come", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfA_FlatDegrees()
    {
        // In key of A: Bb = b2
        string input = "[Bb]text";
        string result = _parser.ConvertToNashvilleNumbers(input, "A");
        Assert.Equal("[b2]text", result);
    }

    [Fact]
    public void ConvertToNashville_PreservesNC()
    {
        string input = "[N.C.]no chord here";
        string result = _parser.ConvertToNashvilleNumbers(input, "G");
        Assert.Equal("[N.C.]no chord here", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfE_SusChords()
    {
        // In key of E: Asus = 4-sus, Bsus4 = 5-sus4
        string input = "[Asus]wait [Bsus4]here";
        string result = _parser.ConvertToNashvilleNumbers(input, "E");
        Assert.Equal("[4-sus]wait [5-sus4]here", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfC_Maj7()
    {
        string input = "[Cmaj7]text";
        string result = _parser.ConvertToNashvilleNumbers(input, "C");
        Assert.Equal("[1-maj7]text", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfC_Dim()
    {
        string input = "[Bdim]text";
        string result = _parser.ConvertToNashvilleNumbers(input, "C");
        Assert.Equal("[7-dim]text", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfC_Aug()
    {
        string input = "[Eaug]text";
        string result = _parser.ConvertToNashvilleNumbers(input, "C");
        Assert.Equal("[3-aug]text", result);
    }

    [Fact]
    public void ConvertToNashville_SlashChord()
    {
        // In key of C: G/B = 5/7
        string input = "[G/B]text";
        string result = _parser.ConvertToNashvilleNumbers(input, "C");
        Assert.Equal("[5/7]text", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfF_SharpNotes()
    {
        // In key of F: F=1, Bb=4, C=5
        string input = "[F]start [Bb]middle [C]end";
        string result = _parser.ConvertToNashvilleNumbers(input, "F");
        Assert.Equal("[1]start [4]middle [5]end", result);
    }

    [Fact]
    public void ConvertToNashville_EmptyText_ReturnsEmpty()
    {
        string result = _parser.ConvertToNashvilleNumbers("", "C");
        Assert.Equal("", result);
    }

    [Fact]
    public void ConvertToNashville_NullText_ReturnsNull()
    {
        string result = _parser.ConvertToNashvilleNumbers(null!, "C");
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToNashville_EmptyKey_ReturnsOriginal()
    {
        string input = "[C]test";
        string result = _parser.ConvertToNashvilleNumbers(input, "");
        Assert.Equal("[C]test", result);
    }

    [Fact]
    public void ConvertToNashville_MultiLine()
    {
        string input = "[G]Line one\n[C]Line [D]two";
        string result = _parser.ConvertToNashvilleNumbers(input, "G");
        Assert.Equal("[1]Line one\n[4]Line [5]two", result);
    }

    [Fact]
    public void ConvertToNashville_KeyOfBb()
    {
        // In key of Bb: Bb=1, Eb=4, F=5
        string input = "[Bb]one [Eb]four [F]five";
        string result = _parser.ConvertToNashvilleNumbers(input, "Bb");
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    [Fact]
    public void ConvertToNashville_MinorSeventh()
    {
        // Am7 in key of C = 6m-7
        string input = "[Am7]text";
        string result = _parser.ConvertToNashvilleNumbers(input, "C");
        Assert.Equal("[6m-7]text", result);
    }
}
