using Xunit;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for CapoTranspose in ChordProParserService.
/// CapoTranspose transposes chords DOWN by capoFret semitones.
/// This shows what chord shapes to play with the capo.
/// </summary>
public class CapoTransposeTests
{
    private readonly ChordProParserService _parser = new();

    [Fact]
    public void CapoTranspose_Capo2_GBecomesF()
    {
        // With capo on 2, play F shape to sound G
        string result = _parser.CapoTranspose("[G]text", 2);
        Assert.Equal("[F]text", result);
    }

    [Fact]
    public void CapoTranspose_Capo2_ABecomesG()
    {
        // With capo on 2, play G shape to sound A
        string result = _parser.CapoTranspose("[A]text", 2);
        Assert.Equal("[G]text", result);
    }

    [Fact]
    public void CapoTranspose_Capo0_NoChange()
    {
        string result = _parser.CapoTranspose("[C]text", 0);
        Assert.Equal("[C]text", result);
    }

    [Fact]
    public void CapoTranspose_Capo1_TransposesDown1()
    {
        // G down 1 = F#
        string result = _parser.CapoTranspose("[G]text", 1);
        Assert.Equal("[F#]text", result);
    }

    [Fact]
    public void CapoTranspose_Capo3_GBecomesE()
    {
        // G down 3 = E
        string result = _parser.CapoTranspose("[G]text", 3);
        Assert.Equal("[E]text", result);
    }

    [Fact]
    public void CapoTranspose_Capo4_GBecomesDSharp()
    {
        // G down 4 = D# (Eb)
        string result = _parser.CapoTranspose("[G]text", 4);
        Assert.Equal("[D#]text", result);
    }

    [Fact]
    public void CapoTranspose_Capo5_GBecomesD()
    {
        // G down 5 = D
        string result = _parser.CapoTranspose("[G]text", 5);
        Assert.Equal("[D]text", result);
    }

    [Fact]
    public void CapoTranspose_Capo7_GBecomesC()
    {
        // G down 7 = C
        string result = _parser.CapoTranspose("[G]text", 7);
        Assert.Equal("[C]text", result);
    }

    [Fact]
    public void CapoTranspose_MinorChord()
    {
        // Am down 2 = Gm
        string result = _parser.CapoTranspose("[Am]text", 2);
        Assert.Equal("[Gm]text", result);
    }

    [Fact]
    public void CapoTranspose_SeventhChord()
    {
        // D7 down 2 = C7
        string result = _parser.CapoTranspose("[D7]text", 2);
        Assert.Equal("[C7]text", result);
    }

    [Fact]
    public void CapoTranspose_SlashChord()
    {
        // G/B down 2 = F/A
        string result = _parser.CapoTranspose("[G/B]text", 2);
        Assert.Equal("[F/A]text", result);
    }

    [Fact]
    public void CapoTranspose_MultipleChords()
    {
        string result = _parser.CapoTranspose("[G]How [C]great [D]is", 2);
        Assert.Equal("[F]How [A#]great [C]is", result);
    }

    [Fact]
    public void CapoTranspose_PreservesNC()
    {
        string result = _parser.CapoTranspose("[N.C.]text", 3);
        Assert.Equal("[N.C.]text", result);
    }

    [Fact]
    public void CapoTranspose_EmptyText()
    {
        string result = _parser.CapoTranspose("", 2);
        Assert.Equal("", result);
    }

    [Fact]
    public void CapoTranspose_NullText()
    {
        string result = _parser.CapoTranspose(null!, 2);
        Assert.Null(result);
    }

    [Fact]
    public void CapoTranspose_SusChord()
    {
        // Dsus4 down 2 = Csus4
        string result = _parser.CapoTranspose("[Dsus4]text", 2);
        Assert.Equal("[Csus4]text", result);
    }

    [Fact]
    public void CapoTranspose_Capo2_MultiLine()
    {
        string input = "[G]Line one\n[C]Line [D]two";
        string result = _parser.CapoTranspose(input, 2);
        Assert.Equal("[F]Line one\n[A#]Line [C]two", result);
    }
}
