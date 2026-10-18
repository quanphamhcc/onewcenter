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

    /// <summary>
    /// Mirrors the Songs-tab "+ Add" handler's Core-side logic (BuildTitle -> new ServiceItem
    /// -> ApplySong -> AddItem) without WPF: the item appended to the current order must carry
    /// the song's Title and FolderPath so the sidebar label and playback link match the picker.
    /// No SaveAsync call, so no data files are touched.
    /// </summary>
    [Fact]
    public void AddSongToService_ProducesExpectedSidebarTitleAndSongFolder()
    {
        // Base path is required by the ctor, but this test never calls SaveAsync, so no file
        // is written (uses a unique temp path purely to satisfy the constructor).
        var manager = new ServiceOrderManager(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Sunday", "2024-01-07");

        var song = new Song
        {
            Title = "Amazing Grace",
            FolderPath = Path.Combine("Songs", "Amazing Grace")
        };

        var title = ServiceItemMapper.BuildTitle(ServiceItemType.Song, null, song, null);
        var item = new ServiceItem { Type = ServiceItemType.Song, Title = title };
        ServiceItemMapper.ApplySong(item, song);
        item.Title = title;

        manager.AddItem(item);

        var added = Assert.Single(manager.CurrentServiceOrder!.Items);
        Assert.Equal(ServiceItemType.Song, added.Type);
        Assert.Equal("Amazing Grace", added.Title);
        Assert.Equal(Path.Combine("Songs", "Amazing Grace"), added.SongFolder);
    }
}
