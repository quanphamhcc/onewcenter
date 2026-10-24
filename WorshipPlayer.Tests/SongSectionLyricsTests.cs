using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Field feature 2026-10-24: editing ONE section's lyrics (e.g. CHORUS) from a
/// slide thumbnail. The pure helper must replace exactly that section's text and
/// leave everything else — other sections, loop points, song metadata — alone.
/// </summary>
public class SongSectionLyricsTests
{
    private static Song TwoSectionSong() => new()
    {
        Title = "Test Song",
        Sections = new List<Section>
        {
            new Section { Name = "VERSE 1", Text = "Old verse words", LoopInMs = 0, LoopOutMs = 1000 },
            new Section { Name = "CHORUS", Text = "Old chorus words", LoopInMs = 1000, LoopOutMs = 2000 }
        }
    };

    [Fact]
    public void TrySetSectionText_ReplacesOnlyThatSection()
    {
        var song = TwoSectionSong();

        Assert.True(SongSectionLyrics.TrySetSectionText(song, "CHORUS", "New chorus words\nSecond line"));

        Assert.Equal("New chorus words\nSecond line", song.Sections[1].Text);
        Assert.Equal("Old verse words", song.Sections[0].Text);
        Assert.Equal(1000, song.Sections[1].LoopInMs);
        Assert.Equal(2000, song.Sections[1].LoopOutMs);
    }

    [Fact]
    public void TrySetSectionText_MatchesNameCaseInsensitively()
    {
        var song = TwoSectionSong();

        Assert.True(SongSectionLyrics.TrySetSectionText(song, "chorus", "New words"));
        Assert.Equal("New words", song.Sections[1].Text);
    }

    [Fact]
    public void TrySetSectionText_MissingSection_ReturnsFalse_AndChangesNothing()
    {
        var song = TwoSectionSong();

        Assert.False(SongSectionLyrics.TrySetSectionText(song, "BRIDGE", "New words"));

        Assert.Equal("Old verse words", song.Sections[0].Text);
        Assert.Equal("Old chorus words", song.Sections[1].Text);
    }

    [Fact]
    public void TrySetSectionText_NullSong_ReturnsFalse()
    {
        Assert.False(SongSectionLyrics.TrySetSectionText(null, "CHORUS", "New words"));
    }

    [Fact]
    public void TrySetSectionText_NormalizesLineEndings()
    {
        var song = TwoSectionSong();

        Assert.True(SongSectionLyrics.TrySetSectionText(song, "CHORUS", "Line one\r\nLine two\rLine three"));
        Assert.Equal("Line one\nLine two\nLine three", song.Sections[1].Text);
    }

    [Fact]
    public void FindSection_ReturnsNull_ForBlankName()
    {
        Assert.Null(SongSectionLyrics.FindSection(TwoSectionSong(), ""));
        Assert.Null(SongSectionLyrics.FindSection(TwoSectionSong(), null));
    }
}
