using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Đợt 2 T17 (R9): PresenterEngine starts/stops item audio on every current-item
/// change (manual GoToItem, auto-advance path, quick present). The engine's
/// ItemAudio service is swapped for a fake-player one (NAudio is Windows-only).
/// </summary>
public class PresenterEngineItemAudioTests
{
    private sealed class FakePlayer : IItemAudioPlayer
    {
        public readonly List<string> PlayedFiles = new();
        public int StopCalls;
        public event EventHandler? PlaybackEnded;
        public void Play(string filePath) => PlayedFiles.Add(filePath);
        public void Stop() => StopCalls++;
        public void Dispose() { }
        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private sealed record EngineHarness(ServiceOrderManager Manager, PresenterEngine Engine, FakePlayer Fake);

    private static EngineHarness NewEngineWithFakeAudio()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test", DateTime.Now.ToString("yyyy-MM-dd"));
        var engine = new PresenterEngine(manager, new PresenterSettings());
        var fake = new FakePlayer();
        engine.ItemAudio = new ItemAudioPlaybackService(() => fake);
        return new EngineHarness(manager, engine, fake);
    }

    private static ServiceItem Announcement(string title, params string[] files) => new()
    {
        Type = ServiceItemType.Announcement,
        Title = title,
        AudioAttachments = new List<string>(files),
        Slides = new List<Slide> { new() { Content = new SlideContent { Text = title } } },
    };

    private static ServiceItem BacktrackSong(string title, params string[] files) => new()
    {
        Type = ServiceItemType.Song,
        Title = title,
        UseBacktrack = true,
        AudioAttachments = new List<string>(files),
        Slides = new List<Slide> { new() { Content = new SlideContent { Text = title } } },
    };

    [Fact]
    public void GoToItem_StartsNewItemAudio_AndStopsOldItemAudio()
    {
        var harness = NewEngineWithFakeAudio();
        var (manager, engine, fake) = harness;
        manager.AddItem(Announcement("Welcome", "a.mp3", "b.mp3"));
        manager.AddItem(Announcement("Offering", "c.mp3"));

        // First item becomes current via AddItem -> its audio starts.
        Assert.Equal(new List<string> { "a.mp3" }, fake.PlayedFiles);

        int stopsBefore = fake.StopCalls;
        manager.GoToItem(1);

        // Old audio stopped first, then the new item's audio started.
        Assert.True(fake.StopCalls > stopsBefore);
        Assert.Equal("c.mp3", fake.PlayedFiles[^1]);
    }

    [Fact]
    public void GoToItem_SongWithBacktrack_DoesNotPlayAttachedAudio_R94()
    {
        var harness = NewEngineWithFakeAudio();
        var (manager, engine, fake) = harness;
        manager.AddItem(Announcement("Welcome", "a.mp3"));
        manager.AddItem(BacktrackSong("Amazing Grace", "bg.mp3"));

        int playsBefore = fake.PlayedFiles.Count;
        int stopsBefore = fake.StopCalls;
        manager.GoToItem(1);

        // Old item's audio stopped, but the backtrack song's attached audio
        // must NOT start (R9.4: never overlap the backtrack).
        Assert.True(fake.StopCalls > stopsBefore);
        Assert.Equal(playsBefore, fake.PlayedFiles.Count);
    }

    [Fact]
    public void LoadScriptureSlides_QuickPresent_StopsItemAudio()
    {
        var harness = NewEngineWithFakeAudio();
        var (manager, engine, fake) = harness;
        manager.AddItem(Announcement("Welcome", "a.mp3"));

        Assert.NotEmpty(fake.PlayedFiles);
        int stopsBefore = fake.StopCalls;

        engine.LoadScriptureSlides(new List<Slide>
        {
            new() { Type = SlideType.Scripture, Content = new SlideContent { Text = "v1" } }
        });

        Assert.True(fake.StopCalls > stopsBefore);
        Assert.True(fake.PlayedFiles.Count == 1); // no new audio started
    }

    [Fact]
    public void GoToItem_ItemWithoutAudio_OnlyStopsOldAudio()
    {
        var harness = NewEngineWithFakeAudio();
        var (manager, engine, fake) = harness;
        manager.AddItem(Announcement("Welcome", "a.mp3"));
        manager.AddItem(Announcement("Silent"));

        int playsBefore = fake.PlayedFiles.Count;
        manager.GoToItem(1);

        Assert.Equal(playsBefore, fake.PlayedFiles.Count);
        Assert.False(engine.ItemAudio.IsPlaying);
    }
}
