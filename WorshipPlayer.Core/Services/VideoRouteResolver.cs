using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure, UI-agnostic decision logic for routing a service item to full-screen video playback
/// (roadmap 7.2). Given the newly-activated <see cref="ServiceItem"/>, it decides whether the
/// output window should switch into video mode and with which file path and loop flag.
///
/// It never touches WPF, LibVLC, or the output window — the operator/UI layer owns deciding WHEN
/// to call <see cref="Resolve"/> (on item change) and HOW to execute the returned decision
/// (OutputWindow.ShowVideo / StopVideo). Keeping the decision here makes it unit-testable headless;
/// the actual rendering cannot be.
///
/// Motion-background looping videos go through the theme/background path, NOT this resolver, so a
/// full-screen service video plays once (<c>Loop = false</c>).
/// </summary>
public static class VideoRouteResolver
{
    /// <summary>
    /// Decide whether the given item routes to full-screen video playback.
    /// </summary>
    /// <param name="item">The newly-activated service item (may be null → no play).</param>
    public static VideoRouteDecision Resolve(ServiceItem? item)
    {
        if (item == null
            || item.Type != ServiceItemType.Video
            || string.IsNullOrWhiteSpace(item.MediaPath))
        {
            return VideoRouteDecision.NoPlay;
        }

        return new VideoRouteDecision(PlayVideo: true, VideoPath: item.MediaPath, Loop: false);
    }

    /// <summary>
    /// The outcome of a resolve: whether to play, the file path, and whether to loop.
    /// </summary>
    public readonly record struct VideoRouteDecision(bool PlayVideo, string VideoPath, bool Loop)
    {
        /// <summary>Convenience "do not play video" result (empty path, no loop).</summary>
        public static VideoRouteDecision NoPlay => new(false, "", false);
    }
}
