using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// F9/F10 defensive: malformed service-order JSON loads to null without throwing,
/// and a song with null/empty Sections and Tracks drives the engine fallback path
/// and SnapshotStageData without an NRE.
/// </summary>
public class DefensiveLoadTests
{
    [Fact]
    public async Task LoadAsync_MalformedJson_ReturnsNull_DoesNotThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "broken.json");
        await File.WriteAllTextAsync(file, "{ this is not valid json ]]");

        try
        {
            var manager = new ServiceOrderManager(dir);
            ServiceOrder? result = null;
            var ex = await Record.ExceptionAsync(async () => result = await manager.LoadAsync(file));

            Assert.Null(ex);
            Assert.Null(result);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void SongWithNullSectionsAndTracks_FallbackLoad_And_Snapshot_DoNotThrow()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test", DateTime.Now.ToString("yyyy-MM-dd"));
        var engine = new PresenterEngine(manager, new PresenterSettings());
        // Song item carries no slides, so LoadSong takes the fallback generation path.
        manager.AddItem(new ServiceItem { Type = ServiceItemType.Song, Title = "Empty", Slides = new List<Slide>() });

        // Hand-edited JSON can null out collections that default to new(); simulate it.
        var song = new Song { Title = "Empty", OriginalKey = "C" };
        song.Sections = null!;
        song.Tracks = null!;

        var ex = Record.Exception(() =>
        {
            engine.OnSongLoaded(song);
            var snap = engine.SnapshotStageData();
            Assert.NotNull(snap);
        });

        Assert.Null(ex);
    }
}
