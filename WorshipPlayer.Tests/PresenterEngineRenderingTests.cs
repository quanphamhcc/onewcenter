using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the dual rendering pipeline logic.
/// PresenterEngine.GetCongregationText delegates to ChordProParserService.StripChords.
/// PresenterEngine.GetStageText delegates to ChordProParserService.Transpose.
/// PresenterEngine.GetNashvilleText delegates to ChordProParserService.ConvertToNashvilleNumbers.
/// PresenterEngine.GetCapoText delegates to ChordProParserService.CapoTranspose.
/// 
/// These tests verify the underlying logic used by the engine's dual rendering pipeline.
/// </summary>
public class PresenterEngineRenderingTests
{
    private readonly ChordProParserService _parser = new();

    // ─── Congregation Display (Chords Stripped) ─────────────────────

    [Fact]
    public void CongregationDisplay_StripsAllChords()
    {
        string slideText = "[G]How great [C]is our [D]God";
        string result = _parser.StripChords(slideText);
        Assert.Equal("How great is our God", result);
    }

    [Fact]
    public void CongregationDisplay_NoChords_ReturnsUnchanged()
    {
        string slideText = "Just plain lyrics with no chords";
        string result = _parser.StripChords(slideText);
        Assert.Equal("Just plain lyrics with no chords", result);
    }

    [Fact]
    public void CongregationDisplay_EmptyText_ReturnsEmpty()
    {
        string result = _parser.StripChords("");
        Assert.Equal("", result);
    }

    [Fact]
    public void CongregationDisplay_MultiLine()
    {
        string slideText = "[G]Line one lyrics\n[C]Line two [D]lyrics";
        string result = _parser.StripChords(slideText);
        Assert.Equal("Line one lyrics\nLine two lyrics", result);
    }

    [Fact]
    public void CongregationDisplay_PreservesSpacing()
    {
        string slideText = "[B]    The Lord bless you  [E]and keep you";
        string result = _parser.StripChords(slideText);
        Assert.Equal("    The Lord bless you  and keep you", result);
    }

    // ─── Stage Display (Chords Transposed) ──────────────────────────

    [Fact]
    public void StageDisplay_ZeroTranspose_ReturnsOriginal()
    {
        string slideText = "[G]How great [C]is";
        string result = _parser.Transpose(slideText, 0);
        Assert.Equal("[G]How great [C]is", result);
    }

    [Fact]
    public void StageDisplay_TransposeUp2()
    {
        string slideText = "[G]How great [C]is";
        string result = _parser.Transpose(slideText, 2);
        // G + 2 = A, C + 2 = D
        Assert.Equal("[A]How great [D]is", result);
    }

    [Fact]
    public void StageDisplay_TransposeDown1()
    {
        string slideText = "[G]How great [C]is";
        string result = _parser.Transpose(slideText, -1);
        // G - 1 = F#, C - 1 = B
        Assert.Equal("[F#]How great [B]is", result);
    }

    [Fact]
    public void StageDisplay_TransposeUp5_KeepsChordFormat()
    {
        string slideText = "[Am]text [Dm]text";
        string result = _parser.Transpose(slideText, 5);
        // A + 5 = D, D + 5 = G
        Assert.Equal("[Dm]text [Gm]text", result);
    }

    [Fact]
    public void StageDisplay_TransposePreservesNC()
    {
        string slideText = "[N.C.]text [G]chord";
        string result = _parser.Transpose(slideText, 2);
        Assert.Equal("[N.C.]text [A]chord", result);
    }

    [Fact]
    public void StageDisplay_TransposeSlashChord()
    {
        string slideText = "[G/B]text";
        string result = _parser.Transpose(slideText, 2);
        // G+2=A, B+2=C#
        Assert.Equal("[A/C#]text", result);
    }

    [Fact]
    public void StageDisplay_TransposeWrapAround()
    {
        // B + 1 = C
        string slideText = "[B]text";
        string result = _parser.Transpose(slideText, 1);
        Assert.Equal("[C]text", result);
    }

    // ─── Nashville Display ──────────────────────────────────────────

    [Fact]
    public void NashvilleDisplay_BasicProgression()
    {
        string slideText = "[G]How great [C]is our [D]God";
        string result = _parser.ConvertToNashvilleNumbers(slideText, "G");
        Assert.Equal("[1]How great [4]is our [5]God", result);
    }

    [Fact]
    public void NashvilleDisplay_MinorChords()
    {
        string slideText = "[Em]verse [Am]line";
        string result = _parser.ConvertToNashvilleNumbers(slideText, "G");
        // Em=6m, Am=2m in G
        Assert.Equal("[6m]verse [2m]line", result);
    }

    // ─── Capo Display ───────────────────────────────────────────────

    [Fact]
    public void CapoDisplay_TransposesDown()
    {
        string slideText = "[G]How great [C]is our [D]God";
        string result = _parser.CapoTranspose(slideText, 2);
        // G-2=F, C-2=A#, D-2=C
        Assert.Equal("[F]How great [A#]is our [C]God", result);
    }

    [Fact]
    public void CapoDisplay_Capo0_NoChange()
    {
        string slideText = "[G]text";
        string result = _parser.CapoTranspose(slideText, 0);
        Assert.Equal("[G]text", result);
    }
}
