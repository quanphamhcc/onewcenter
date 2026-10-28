namespace WorshipPlayer.Core.Models;

/// <summary>
/// Pure selection rule for the chord-chart window's current page (Issue #5,
/// 28/10). The window polls the audio clock (+500ms pre-roll) and must show
/// the same section as the audience slides at every position, including
/// after backward seeks.
///
/// The audience slides already carry these very numbers: each audience
/// slide's timestamp is its chord block's StartMs (later chunks interpolated
/// inside the block), so the audience's last-start-wins matcher and this
/// rule agree on section everywhere — provided this rule also falls back
/// to last-start instead of holding a stale page.
///
/// Rule, in order:
/// 1. The first slide whose [StartMs, EndMs) contains currentMs (the
///    musician's chord page for this moment).
/// 2. Otherwise the last slide whose StartMs is at or before currentMs.
///    This is the fallback the old "keep the previous page" rule lacked:
///    after a backward seek into a gap between slides, holding the
///    previous page showed a STALE section (e.g. BRIDGE 1 while the music
///    was back at CHORUS), while the audience (last-start-wins) correctly
///    showed CHORUS. Forward play is unchanged: in a gap, the last slide
///    already started is exactly the page the old hold showed.
/// 3. Otherwise -1 (keep the current page): currentMs is before the first
///    slide, e.g. the song's opening seconds.
/// </summary>
public static class ChordChartSlideSelector
{
    public static int SelectSlideIndex(IReadOnlyList<ChartSlide> slides, double currentMs)
    {
        for (int i = 0; i < slides.Count; i++)
        {
            var slide = slides[i];
            if (currentMs >= slide.StartMs && currentMs < slide.EndMs)
                return i;
        }

        for (int i = slides.Count - 1; i >= 0; i--)
        {
            if (currentMs >= slides[i].StartMs)
                return i;
        }

        return -1;
    }
}
