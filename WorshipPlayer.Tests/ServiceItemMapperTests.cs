using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for <see cref="ServiceItemMapper"/> — the pure Song/Scripture ->
/// ServiceItem mapping behind the service-item picker. Cross-platform (net8.0, no WPF).
/// </summary>
public class ServiceItemMapperTests
{
    [Fact]
    public void ApplySong_SetsTypeSongFolderAndTitle()
    {
        var item = new ServiceItem { Title = "placeholder" };
        var song = new Song
        {
            Title = "Amazing Grace",
            Artist = "John Newton",
            FolderPath = Path.Combine(Path.GetTempPath(), "Songs", "AmazingGrace")
        };

        ServiceItemMapper.ApplySong(item, song);

        Assert.Equal(ServiceItemType.Song, item.Type);
        Assert.Equal(song.FolderPath, item.SongFolder);
        Assert.Equal("Amazing Grace", item.Title);
    }

    [Fact]
    public void ApplySong_BlankSongTitle_KeepsExistingTitle()
    {
        var item = new ServiceItem { Title = "Existing Name" };
        var song = new Song
        {
            Title = "   ",
            FolderPath = Path.Combine(Path.GetTempPath(), "Songs", "NoTitle")
        };

        ServiceItemMapper.ApplySong(item, song);

        Assert.Equal(song.FolderPath, item.SongFolder);
        Assert.Equal("Existing Name", item.Title);
    }

    [Fact]
    public void ApplySong_NullFolderPath_StoresEmptyString()
    {
        var item = new ServiceItem();
        var song = new Song { Title = "No Folder", FolderPath = null! };

        ServiceItemMapper.ApplySong(item, song);

        Assert.Equal("", item.SongFolder);
        Assert.Equal("No Folder", item.Title);
    }

    [Fact]
    public void ApplyScripture_SetsTypeReferenceAndTitle()
    {
        var item = new ServiceItem { Title = "placeholder" };

        ServiceItemMapper.ApplyScripture(item, "  John 3:16  ");

        Assert.Equal(ServiceItemType.Scripture, item.Type);
        Assert.Equal("John 3:16", item.ScriptureReference);
        Assert.Equal("John 3:16", item.Title);
    }

    [Fact]
    public void ApplyScripture_BlankReference_LeavesTitleUnchanged()
    {
        var item = new ServiceItem { Title = "Reading" };

        ServiceItemMapper.ApplyScripture(item, "   ");

        Assert.Equal(ServiceItemType.Scripture, item.Type);
        Assert.Equal("", item.ScriptureReference);
        Assert.Equal("Reading", item.Title);
    }

    [Fact]
    public void BuildTitle_Song_PickedSongTitleWins()
    {
        var song = new Song { Title = "10,000 Reasons" };

        var title = ServiceItemMapper.BuildTitle(
            ServiceItemType.Song, "typed override", song, null);

        Assert.Equal("10,000 Reasons", title);
    }

    [Fact]
    public void BuildTitle_Song_NoPickedSong_FallsBackToTypedName()
    {
        var title = ServiceItemMapper.BuildTitle(
            ServiceItemType.Song, "  My Song  ", null, null);

        Assert.Equal("My Song", title);
    }

    [Fact]
    public void BuildTitle_Scripture_ReferenceWins()
    {
        var title = ServiceItemMapper.BuildTitle(
            ServiceItemType.Scripture, "typed override", null, "  Psalm 23  ");

        Assert.Equal("Psalm 23", title);
    }

    [Fact]
    public void BuildTitle_TypedName_UsedWhenNoSongOrReference()
    {
        var title = ServiceItemMapper.BuildTitle(
            ServiceItemType.Announcement, "  Welcome  ", null, null);

        Assert.Equal("Welcome", title);
    }

    [Fact]
    public void BuildTitle_Fallback_WhenEverythingBlank()
    {
        var title = ServiceItemMapper.BuildTitle(
            ServiceItemType.Sermon, "   ", null, null);

        Assert.Equal("Sermon Item", title);
    }

    [Fact]
    public void BuildTitle_Song_BlankSongTitle_FallsBackToTypedName()
    {
        var song = new Song { Title = "  " };

        var title = ServiceItemMapper.BuildTitle(
            ServiceItemType.Song, "Typed Fallback", song, null);

        Assert.Equal("Typed Fallback", title);
    }
}
