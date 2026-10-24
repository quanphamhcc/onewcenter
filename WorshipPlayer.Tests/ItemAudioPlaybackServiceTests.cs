using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Đợt 2 T17 (R9): ItemAudioPlaybackService sequencing with a fake player
/// (NAudio is Windows-only; the fake drives the same IItemAudioPlayer contract).
/// </summary>
public class ItemAudioPlaybackServiceTests
{
    private sealed class FakePlayer : IItemAudioPlayer
    {
        public readonly List<string> PlayedFiles = new();
        public int StopCalls;
        public readonly HashSet<string> FailPaths = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler? PlaybackEnded;

        public void Play(string filePath)
        {
            if (FailPaths.Contains(filePath))
                throw new FileNotFoundException("not found", filePath);
            PlayedFiles.Add(filePath);
        }

        public void Stop() => StopCalls++;

        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);

        public void Dispose() { }
    }

    private static ItemAudioPlaybackService NewService(FakePlayer fake)
        => new ItemAudioPlaybackService(() => fake);

    [Fact]
    public void PlayForItem_PlaysFilesInOrder_AndStopsAtEnd_WhenLoopOff()
    {
        var fake = new FakePlayer();
        using var service = NewService(fake);

        service.PlayForItem(new ResolvedItemAudio(new List<string> { "a.mp3", "b.mp3" }, Loop: false));

        Assert.Equal(new List<string> { "a.mp3" }, fake.PlayedFiles);
        Assert.True(service.IsPlaying);

        fake.RaiseEnded(); // a.mp3 ended naturally
        Assert.Equal(new List<string> { "a.mp3", "b.mp3" }, fake.PlayedFiles);
        Assert.True(service.IsPlaying);

        fake.RaiseEnded(); // b.mp3 ended naturally
        Assert.False(service.IsPlaying);
        Assert.True(fake.StopCalls >= 1);
        Assert.Equal(2, fake.PlayedFiles.Count); // nothing more played
    }

    [Fact]
    public void PlayForItem_RestartsFromFirstFile_WhenLoopOn()
    {
        var fake = new FakePlayer();
        using var service = NewService(fake);

        service.PlayForItem(new ResolvedItemAudio(new List<string> { "a.mp3", "b.mp3" }, Loop: true));

        fake.RaiseEnded(); // a ended -> b
        fake.RaiseEnded(); // b ended -> loop -> a again

        Assert.Equal(new List<string> { "a.mp3", "b.mp3", "a.mp3" }, fake.PlayedFiles);
        Assert.True(service.IsPlaying);
    }

    [Fact]
    public void PlayForItem_SkipsMissingFiles_AndLogs_WithoutCrashing()
    {
        var fake = new FakePlayer();
        fake.FailPaths.Add("gone.mp3");
        var logs = new List<string>();
        using var service = NewService(fake);
        service.LogMessage += (_, m) => logs.Add(m);

        service.PlayForItem(new ResolvedItemAudio(new List<string> { "gone.mp3", "ok.mp3" }, Loop: false));

        Assert.Equal(new List<string> { "ok.mp3" }, fake.PlayedFiles);
        Assert.NotEmpty(logs);
        Assert.Contains(logs, m => m.Contains("gone.mp3"));
        Assert.True(service.IsPlaying);
    }

    [Fact]
    public void PlayForItem_AllFilesMissing_StopsCleanly()
    {
        var fake = new FakePlayer();
        fake.FailPaths.Add("gone.mp3");
        using var service = NewService(fake);

        service.PlayForItem(new ResolvedItemAudio(new List<string> { "gone.mp3" }, Loop: true));

        Assert.Empty(fake.PlayedFiles);
        Assert.False(service.IsPlaying);
    }

    [Fact]
    public void PlayForItem_SecondCall_StopsFirstQueue_SingleStream()
    {
        var fake = new FakePlayer();
        using var service = NewService(fake);

        service.PlayForItem(new ResolvedItemAudio(new List<string> { "a.mp3", "b.mp3" }, Loop: true));
        service.PlayForItem(new ResolvedItemAudio(new List<string> { "c.mp3" }, Loop: false));

        // Old stream stopped before the new one started: "c.mp3" is the last play,
        // and only one Stop sequence happened between them.
        Assert.Equal("c.mp3", fake.PlayedFiles[^1]);
        Assert.True(fake.StopCalls >= 1);

        // The old queue must be gone: ending "c.mp3" stops everything, no "a"/"b" replay.
        fake.RaiseEnded();
        Assert.False(service.IsPlaying);
        Assert.DoesNotContain("a.mp3", fake.PlayedFiles.GetRange(2, fake.PlayedFiles.Count - 2));
    }

    [Fact]
    public void PlayForItem_EmptyList_StopsPrevious_AndDoesNothing()
    {
        var fake = new FakePlayer();
        using var service = NewService(fake);

        service.PlayForItem(new ResolvedItemAudio(new List<string> { "a.mp3" }, Loop: false));
        int playsAfterFirst = fake.PlayedFiles.Count;

        service.PlayForItem(new ResolvedItemAudio(new List<string>(), Loop: false));

        Assert.Equal(playsAfterFirst, fake.PlayedFiles.Count);
        Assert.False(service.IsPlaying);
        Assert.True(fake.StopCalls >= 1);
    }

    [Fact]
    public void Stop_HaltsPlayback_AndClearsQueue()
    {
        var fake = new FakePlayer();
        using var service = NewService(fake);

        service.PlayForItem(new ResolvedItemAudio(new List<string> { "a.mp3", "b.mp3" }, Loop: true));
        service.Stop();

        Assert.False(service.IsPlaying);
        fake.RaiseEnded(); // stray event after stop must not resume anything
        Assert.Equal(1, fake.PlayedFiles.Count);
    }

    [Fact]
    public void Dispose_StopsPlayback()
    {
        var fake = new FakePlayer();
        var service = NewService(fake);
        service.PlayForItem(new ResolvedItemAudio(new List<string> { "a.mp3" }, Loop: true));
        service.Dispose();
        Assert.True(fake.StopCalls >= 1);
    }
}
