using Xunit;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for ChordProParserService.BuildChordOverLyric — the shared chord-over-lyric
/// builder used by the WPF STAGE preview and the congregation output.
/// </summary>
public class ChordOverLyricTests
{
    private readonly ChordProParserService _parser = new();

    [Fact]
    public void BuildChordOverLyric_ChordSitsAboveCorrectCharOffset()
    {
        // "[B]The Lord [E]bless you" strips to "The Lord bless you".
        // B lands at column 0; E lands where "bless" starts (offset 9).
        var lines = _parser.BuildChordOverLyric("[B]The Lord [E]bless you");

        var line = Assert.Single(lines);
        Assert.Equal("The Lord bless you", line.LyricLine);
        Assert.False(line.CarriedOver);

        int blessOffset = line.LyricLine.IndexOf("bless", System.StringComparison.Ordinal);
        Assert.Equal('B', line.ChordLine[0]);
        Assert.Equal("E", line.ChordLine.Substring(blessOffset, 1));
        // No chord between the two
        Assert.StartsWith("B", line.ChordLine.TrimStart());
    }

    [Fact]
    public void BuildChordOverLyric_ChordlessLineCarriesOverDimChord()
    {
        var lines = _parser.BuildChordOverLyric("[B]First line\nSecond line");

        Assert.Equal(2, lines.Count);
        Assert.False(lines[0].CarriedOver);

        // Second line has no chord — the last chord carries over as a dim "(chord)".
        Assert.Equal("(B)", lines[1].ChordLine);
        Assert.True(lines[1].CarriedOver);
        Assert.Equal("Second line", lines[1].LyricLine);
    }

    [Fact]
    public void BuildChordOverLyric_TransposeThenBuild_KeepsAlignment()
    {
        const string text = "[B]The Lord [E]bless you";

        var baseline = _parser.BuildChordOverLyric(text);
        var transposed = _parser.BuildChordOverLyric(_parser.Transpose(text, 2));

        var baseLine = Assert.Single(baseline);
        var transLine = Assert.Single(transposed);

        // Lyric row never changes with transposition.
        Assert.Equal(baseLine.LyricLine, transLine.LyricLine);

        // Chord columns are identical even though the chord text changes (B->C#, E->F#).
        int baseFirstCol = baseLine.ChordLine.Length - baseLine.ChordLine.TrimStart().Length;
        int transFirstCol = transLine.ChordLine.Length - transLine.ChordLine.TrimStart().Length;
        Assert.Equal(baseFirstCol, transFirstCol);

        int blessOffset = baseLine.LyricLine.IndexOf("bless", System.StringComparison.Ordinal);
        Assert.Equal("C#", transLine.ChordLine.Substring(0, 2));
        Assert.Equal("F#", transLine.ChordLine.Substring(blessOffset, 2));
    }

    [Fact]
    public void BuildChordOverLyric_EmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(_parser.BuildChordOverLyric(""));
        Assert.Empty(_parser.BuildChordOverLyric("   \n  \n"));
    }

    [Fact]
    public void BuildChordOverLyric_NoChordText_DegradesGracefully()
    {
        var lines = _parser.BuildChordOverLyric("Just some lyrics\nAnother line");

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal("", l.ChordLine));
        Assert.All(lines, l => Assert.False(l.CarriedOver));
        Assert.Equal("Just some lyrics", lines[0].LyricLine);
        Assert.Equal("Another line", lines[1].LyricLine);
    }
}
