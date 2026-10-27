using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Issue #5 (27/10, field log Christ Is Enough): the position matcher and the
/// backtrack section events fought each other, producing visible flicker.
/// A section event fires when audio crosses the section's chart boundary, but
/// the section's first slide is timestamped at its first sung word — up to
/// several seconds later — so the event jumped early and the matcher yanked
/// it back (log: 1→0→1, 2→1→2, 3→2→3, ... and 15→13 repeatedly at 4:00).
/// When timestamp advance is active and the audio position is known, a
/// section event must agree with the audio clock within grace, otherwise the
/// matcher owns advancement. Also: a position update before the first slide
/// timestamp must reset a stale index to slide 0 instead of leaving it
/// (field log: curIdx=13 at pos=1765 stayed wrong until pos passed 25531).
/// </summary>
public class PresenterEngineSectionEventGateTests
{
    private static PresenterEngine NewEngine()
    {
        var manager = new ServiceOrderManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        manager.CreateNew("Test Service", DateTime.Now.ToString("yyyy-MM-dd"));
        return new PresenterEngine(manager, new PresenterSettings());
    }

    private static List<Slide> Slides(params (string Section, double? Ts)[] defs)
    {
        var list = new List<Slide>();
        foreach (var (section, ts) in defs)
            list.Add(new Slide
            {
                SectionName = section,
                Content = new SlideContent { Text = section },
                TimestampMs = ts
            });
        return list;
    }

    [Fact]
    public void SectionEvent_EarlyVsAudioClock_IsIgnored_MatcherAdvancesAtTimestamp()
    {
        // PRE-CHORUS 1 case from the field log: event at pos 43941, slide ts
        // 47322 (3.4s early). The event must not move the display; the
        // matcher advances when pos actually reaches the timestamp.
        var engine = NewEngine();
        engine.LoadItemSlides(Slides(("INTRO", 25531), ("VERSE 1", 26692), ("PRE-CHORUS 1", 47322)));

        engine.OnPlaybackPositionUpdate(26692);
        Assert.Equal(1, engine.CurrentSlideIndex);

        // Section event arrives ~3.4s before the slide's timestamp.
        engine.OnPlaybackPositionUpdate(43941);
        engine.OnSectionChanged("PRE-CHORUS 1");
        Assert.Equal(1, engine.CurrentSlideIndex);

        engine.OnPlaybackPositionUpdate(47322);
        Assert.Equal(2, engine.CurrentSlideIndex);
    }

    [Fact]
    public void SectionEvent_OnTimeVsAudioClock_IsHonoured()
    {
        // Event within grace (100ms) of its slide's timestamp: allowed.
        var engine = NewEngine();
        engine.LoadItemSlides(Slides(("INTRO", 1000), ("VERSE 1", 10000), ("PRE-CHORUS 1", 20000)));

        engine.OnPlaybackPositionUpdate(19900);
        Assert.Equal(1, engine.CurrentSlideIndex);

        engine.OnSectionChanged("PRE-CHORUS 1");
        Assert.Equal(2, engine.CurrentSlideIndex);
    }

    [Fact]
    public void SectionEvent_RepeatedSectionName_DoesNotJumpBackToFirstOccurrence()
    {
        // Bare repeated-section event while the matcher is active: the first
        // CHORUS slide's timestamp is far behind the audio clock, so the
        // event must not yank the display back to the first chorus.
        var engine = NewEngine();
        engine.LoadItemSlides(Slides(("CHORUS", 10000), ("VERSE", 20000), ("CHORUS", 30000)));

        engine.OnPlaybackPositionUpdate(29000);
        Assert.Equal(1, engine.CurrentSlideIndex);

        engine.OnSectionChanged("CHORUS");
        Assert.Equal(1, engine.CurrentSlideIndex);

        engine.OnPlaybackPositionUpdate(30000);
        Assert.Equal(2, engine.CurrentSlideIndex);
    }

    [Fact]
    public void SectionEvent_NoPositionKnown_IsHonoured()
    {
        // Fallback: no position update has arrived yet (e.g. MIDI-cue-driven
        // show) — the old behaviour stands.
        var engine = NewEngine();
        engine.LoadItemSlides(Slides(("VERSE 1", 1000), ("CHORUS 1", 10000)));

        engine.OnSectionChanged("CHORUS 1");
        Assert.Equal(1, engine.CurrentSlideIndex);
    }

    [Fact]
    public void SectionEvent_UntimestampedTarget_IsHonoured()
    {
        // Target slide has no timestamp: nothing to compare against, so the
        // event stands (covers untimestamped songs).
        var engine = NewEngine();
        engine.LoadItemSlides(Slides(("VERSE 1", 1000), ("CHORUS 1", null)));

        engine.OnPlaybackPositionUpdate(5000);
        Assert.Equal(0, engine.CurrentSlideIndex);

        engine.OnSectionChanged("CHORUS 1");
        Assert.Equal(1, engine.CurrentSlideIndex);
    }

    [Fact]
    public void PositionUpdate_BeforeFirstTimestamp_ResetsStaleIndexToZero()
    {
        // Field log run 1: curIdx=13 at pos=1765 (before the first slide ts
        // 25531) stayed wrong. A position update before the first timestamp
        // must show slide 0.
        var engine = NewEngine();
        engine.LoadItemSlides(Slides(("INTRO", 25531), ("VERSE 1", 26692), ("PRE-CHORUS 1", 47322)));

        engine.GoToSlide(2);
        Assert.Equal(2, engine.CurrentSlideIndex);

        engine.OnPlaybackPositionUpdate(1765);
        Assert.Equal(0, engine.CurrentSlideIndex);

        engine.OnPlaybackPositionUpdate(26692);
        Assert.Equal(1, engine.CurrentSlideIndex);
    }

    [Fact]
    public void PositionUpdate_BeforeFirstTimestamp_UntimedSong_LeavesIndexAlone()
    {
        // No slide carries a timestamp (untimed song): the matcher has no
        // clock to enforce, so a manual selection must not be reset.
        var engine = NewEngine();
        engine.LoadItemSlides(Slides(("VERSE 1", null), ("CHORUS 1", null), ("VERSE 2", null)));

        engine.GoToSlide(2);
        engine.OnPlaybackPositionUpdate(1765);
        Assert.Equal(2, engine.CurrentSlideIndex);
    }
}
