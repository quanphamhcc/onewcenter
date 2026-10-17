using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the pure full-screen video routing decision (roadmap 7.2).
/// These exercise VideoRouteResolver.Resolve directly with in-memory ServiceItem fixtures and no
/// WPF/LibVLC — the resolver is UI-agnostic by design.
///
/// NOTE: the actual VideoView rendering/playback is NOT unit-testable headless (it requires the
/// native libvlc core + a visible window). Only this routing DECISION is covered here; on-screen
/// playback is verified manually (see report.md).
/// </summary>
public class VideoRouteResolverTests
{
    private static ServiceItem VideoItem(string path) =>
        new() { Title = "Welcome", Type = ServiceItemType.Video, MediaPath = path };

    [Fact]
    public void NullItem_DoesNotPlay()
    {
        var decision = VideoRouteResolver.Resolve(null);

        Assert.False(decision.PlayVideo);
        Assert.Equal("", decision.VideoPath);
        Assert.False(decision.Loop);
    }

    [Fact]
    public void VideoItemWithPath_PlaysOnce_PathEchoed()
    {
        var decision = VideoRouteResolver.Resolve(VideoItem(@"C:\media\welcome.mp4"));

        Assert.True(decision.PlayVideo);
        Assert.Equal(@"C:\media\welcome.mp4", decision.VideoPath);
        Assert.False(decision.Loop); // full-screen service videos play once; looping is the theme path
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void VideoItemWithEmptyOrWhitespacePath_DoesNotPlay(string path)
    {
        var decision = VideoRouteResolver.Resolve(VideoItem(path));

        Assert.False(decision.PlayVideo);
        Assert.Equal("", decision.VideoPath);
    }

    [Theory]
    [InlineData(ServiceItemType.Song)]
    [InlineData(ServiceItemType.Scripture)]
    [InlineData(ServiceItemType.Announcement)]
    [InlineData(ServiceItemType.Sermon)]
    [InlineData(ServiceItemType.Image)]
    [InlineData(ServiceItemType.Prayer)]
    public void NonVideoItem_DoesNotPlay_EvenWithMediaPath(ServiceItemType type)
    {
        var item = new ServiceItem { Title = "X", Type = type, MediaPath = @"C:\media\clip.mp4" };

        var decision = VideoRouteResolver.Resolve(item);

        Assert.False(decision.PlayVideo);
        Assert.Equal("", decision.VideoPath);
    }
}
