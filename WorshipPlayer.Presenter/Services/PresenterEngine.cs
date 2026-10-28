using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// The core presentation engine — manages slide state, transitions, and output.
/// 
/// This is the "brain" of the Presenter module. It:
/// 1. Tracks the current slide within the current service item
/// 2. Responds to navigation commands (next/prev slide, next/prev item)
/// 3. Responds to auto-advance triggers (MIDI cues, timestamps)
/// 4. Fires events that the output windows listen to for rendering
/// 5. Provides dual rendering: congregation (no chords) and stage (chords transposed)
/// 6. Supports Nashville numbers and capo display
/// 
/// The PresenterEngine does NOT handle rendering — it only manages state.
/// Rendering is handled by the OutputWindow and StageDisplayWindow.
/// </summary>
public class PresenterEngine : IDisposable
{
    private readonly ServiceOrderManager _serviceOrderManager;
    private readonly PresenterSettings _settings;
    private readonly ChordProParserService _chordParser;

    // Guards the mutable slide/song state so background threads (audio render
    // thread raising SectionChanged/PositionChanged) can snapshot it without
    // torn reads. All writers take this lock; SlideChanged is raised outside it.
    private readonly object _stateLock = new();

    private List<Slide> _currentSlides = new();
    private int _currentSlideIndex = -1;

    // Issue #5 (27/10): last audio position seen by OnPlaybackPositionUpdate.
    // The audio clock owns slide advancement when timestamp advance is on;
    // section events are gated against it (see SectionEventAgreesWithPosition).
    // A stale value (previous song, before the first update of the new song)
    // can only make the gate ignore one section event; the position matcher
    // corrects on the next update, so no reset on slide load is needed.
    private double? _lastPositionMs;
    // A section event may legitimately land a few hundred ms before its
    // slide's timestamp (event dispatch + position report granularity).
    // Anything further out means the event disagrees with the audio clock
    // and must not move the display — the matcher advances at the right time.
    private const double SectionEventGraceMs = 250;
    private bool _isLive = false;
    private bool _isBlacked = false;

    // Section-aware state
    private Song? _currentSong;
    private int _pitchShiftSemitones = 0;
    private double _tempoPercent = 100.0;
    private bool _isManualAdvanceMode = false;

    // Per-item auto-advance (roadmap C4). One-shot timer, (re)armed whenever a new item
    // becomes current and only when that item is configured with the Timer condition.
    private System.Timers.Timer? _autoAdvanceTimer;

    // Đợt 2 T17 (R9): per-item attached audio. Exactly one stream at a time —
    // switching items stops the old item's audio before the new item's starts.
    // Settable so tests can inject a fake-player service on Linux (NAudio is
    // Windows-only).
    public ItemAudioPlaybackService ItemAudio { get; set; } = new();

    // Đợt 2 T5 (R3.4): countdown reporting for the armed Timer auto-advance, so the
    // operator UI can show the remaining time at the top of the service list. The
    // ticker only REPORTS — the one-shot timer above still performs the advance.
    // Pure remaining-time math lives in Core's AutoAdvanceCountdown.
    private System.Timers.Timer? _autoAdvanceCountdownTicker;
    private string? _autoAdvanceCountdownItemId;
    private DateTimeOffset _autoAdvanceCountdownStartedAtUtc;
    private int _autoAdvanceCountdownDelayMs;

    // ─── Public State ──────────────────────────────────────────────
    public ServiceItem? CurrentItem => _serviceOrderManager.CurrentItem;
    public Slide? CurrentSlide => _currentSlideIndex >= 0 && _currentSlideIndex < _currentSlides.Count
        ? _currentSlides[_currentSlideIndex]
        : null;
    public Slide? NextSlide => _currentSlideIndex + 1 < _currentSlides.Count
        ? _currentSlides[_currentSlideIndex + 1]
        : null;
    public Slide? PreviousSlide => _currentSlideIndex > 0
        ? _currentSlides[_currentSlideIndex - 1]
        : null;
    public int CurrentSlideIndex => _currentSlideIndex;
    public int TotalSlides => _currentSlides.Count;
    public bool IsLive => _isLive;
    public bool IsBlacked => _isBlacked;
    public List<Slide> CurrentSlides => _currentSlides;
    public int CurrentItemIndex => _serviceOrderManager.CurrentItemIndex;
    public ServiceOrder? CurrentServiceOrder => _serviceOrderManager.CurrentServiceOrder;
    public PresenterSettings Settings => _settings;

    // ─── Events ────────────────────────────────────────────────────
    /// <summary>
    /// Fired when the current slide changes (for output window rendering).
    /// </summary>
    public event EventHandler<SlideChangedEventArgs>? SlideChanged;

    /// <summary>
    /// Fired when the service item changes (new song, scripture, etc.).
    /// </summary>
    public event EventHandler<ServiceItemChangedEventArgs>? ItemChanged;

    /// <summary>
    /// Fired when the per-item auto-advance countdown starts, ticks (about four
    /// times per second), or ends (đợt 2 T5 / R3.4). The args carry the item id and
    /// the remaining milliseconds; <see cref="AutoAdvanceCountdownEventArgs.IsActive"/>
    /// is false when no countdown is running anymore (item changed, item became
    /// ineligible, or the countdown ran out).
    /// </summary>
    public event EventHandler<AutoAdvanceCountdownEventArgs>? AutoAdvanceCountdownChanged;

    /// <summary>
    /// Fired when live/blank state changes.
    /// </summary>
    public event EventHandler<DisplayStateChangedEventArgs>? DisplayStateChanged;

    /// <summary>
    /// Fired when pitch shift changes (so displays can re-render with new transposition).
    /// </summary>
    public event EventHandler<PitchShiftChangedEventArgs>? PitchShiftChanged;

    /// <summary>
    /// Fired when tempo changes.
    /// </summary>
    public event EventHandler<TempoChangedEventArgs>? TempoChanged;

    /// <summary>
    /// Fired when a song is loaded into the presenter.
    /// </summary>
    public event EventHandler<SongLoadedEventArgs>? SongLoaded;

    public PresenterEngine(ServiceOrderManager serviceOrderManager, PresenterSettings settings)
        : this(serviceOrderManager, settings, new ChordProParserService())
    {
    }

    public PresenterEngine(ServiceOrderManager serviceOrderManager, PresenterSettings settings, ChordProParserService chordParser)
    {
        _serviceOrderManager = serviceOrderManager;
        _settings = settings;
        _chordParser = chordParser;

        // Listen to service order navigation
        _serviceOrderManager.ItemChanged += OnServiceItemChanged;
    }

    // ─── Slide Navigation ──────────────────────────────────────────

    /// <summary>
    /// Advance to the next slide. If at the end of current item's slides,
    /// optionally advance to the next service item.
    /// </summary>
    public bool NextSlideAction()
    {
        if (_currentSlides.Count == 0) return false;

        if (_currentSlideIndex + 1 < _currentSlides.Count)
        {
            // Next slide within current item
            SetSlideIndex(_currentSlideIndex + 1);
            return true;
        }
        else
        {
            // End of slides for this item — advance to next service item
            if (_settings.AutoAdvanceOnSongEnd)
            {
                return _serviceOrderManager.NextItem();
            }
            return false;
        }
    }

    /// <summary>
    /// Go to the previous slide. If at the beginning, go to previous item's last slide.
    /// </summary>
    public bool PreviousSlideAction()
    {
        if (_currentSlideIndex > 0)
        {
            SetSlideIndex(_currentSlideIndex - 1);
            return true;
        }
        else
        {
            // At beginning — go to previous item
            if (_serviceOrderManager.PreviousItem())
            {
                // Jump to last slide of previous item
                if (_currentSlides.Count > 0)
                    SetSlideIndex(_currentSlides.Count - 1);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Jump to a specific slide index within the current item.
    /// </summary>
    public void GoToSlide(int index)
    {
        if (index >= 0 && index < _currentSlides.Count)
        {
            SetSlideIndex(index);
        }
    }

    /// <summary>
    /// Advance to the next service item.
    /// </summary>
    public bool NextItemAction()
    {
        return _serviceOrderManager.NextItem();
    }

    /// <summary>
    /// Go back to the previous service item.
    /// </summary>
    public bool PreviousItemAction()
    {
        return _serviceOrderManager.PreviousItem();
    }

    /// <summary>
    /// Jump to a specific service item by index.
    /// </summary>
    public void GoToItem(int itemIndex)
    {
        _serviceOrderManager.GoToItem(itemIndex);
    }

    // ─── Display State ─────────────────────────────────────────────

    /// <summary>
    /// Go live — start displaying the current slide on output.
    /// </summary>
    public void GoLive()
    {
        _isLive = true;
        _isBlacked = false;
        DisplayStateChanged?.Invoke(this, new DisplayStateChangedEventArgs
        {
            IsLive = true,
            IsBlacked = false
        });
    }

    /// <summary>
    /// Black out the output display (shows nothing or logo).
    /// </summary>
    public void GoBlack()
    {
        _isBlacked = true;
        DisplayStateChanged?.Invoke(this, new DisplayStateChangedEventArgs
        {
            IsLive = _isLive,
            IsBlacked = true
        });
    }

    /// <summary>
    /// Unblack — resume showing the current slide.
    /// </summary>
    public void Unblack()
    {
        _isBlacked = false;
        DisplayStateChanged?.Invoke(this, new DisplayStateChangedEventArgs
        {
            IsLive = _isLive,
            IsBlacked = false
        });
    }

    /// <summary>
    /// Show logo slide (church logo on black background).
    /// </summary>
    public void ShowLogo()
    {
        _isBlacked = false;
        // Fire a special slide change with a "logo" slide
        SlideChanged?.Invoke(this, new SlideChangedEventArgs
        {
            PreviousSlide = CurrentSlide,
            NewSlide = null, // null = show logo
            Transition = SlideTransition.Fade,
            TransitionDurationMs = 500,
            IsLogo = true
        });
    }

    // ─── Auto-Advance Integration ──────────────────────────────────

    /// <summary>
    /// Called by MidiPresenterSync when a MIDI cue is received from the backtrack player.
    /// Advances the slide based on the cue action.
    /// </summary>
    public void OnMidiCueReceived(MidiCueAction action, int slideNumber = 0)
    {
        if (!_settings.AutoAdvanceFromMidi) return;

        switch (action)
        {
            case MidiCueAction.NextSlide:
                NextSlideAction();
                break;
            case MidiCueAction.PrevSlide:
                PreviousSlideAction();
                break;
            case MidiCueAction.NextItem:
                NextItemAction();
                break;
            case MidiCueAction.TriggerSlide:
                if (slideNumber > 0 && slideNumber <= _currentSlides.Count)
                    GoToSlide(slideNumber - 1); // 1-based to 0-based
                break;
        }
    }

    /// <summary>
    /// Called periodically during backtrack playback to check timestamp-based auto-advance.
    /// </summary>
    public void OnPlaybackPositionUpdate(double positionMs)
    {
        if (_currentSlides.Count == 0) { WorshipPlayer.Core.Services.DiagLog.Write($"PosUpdate pos={positionMs:0} SKIP (0 slides)"); return; }

        // Issue #5 (27/10): remember the audio clock for OnSectionChanged's
        // gate, even when timestamp advance is off (the gate itself only
        // applies when the setting is on, so this never changes off-mode
        // behaviour).
        _lastPositionMs = positionMs;

        if (!_settings.AutoAdvanceFromTimestamp) { WorshipPlayer.Core.Services.DiagLog.Write($"PosUpdate pos={positionMs:0} SKIP (AutoAdvanceFromTimestamp OFF)"); return; }

        // Find the slide that should be showing at this timestamp
        int chosen = -1;
        for (int i = _currentSlides.Count - 1; i >= 0; i--)
        {
            var slide = _currentSlides[i];
            if (slide.TimestampMs.HasValue && slide.TimestampMs.Value > 0 && positionMs >= slide.TimestampMs.Value)
            {
                chosen = i;
                if (i != _currentSlideIndex)
                {
                    WorshipPlayer.Core.Services.DiagLog.Write($"PosUpdate pos={positionMs:0} -> ADVANCE slide {_currentSlideIndex} -> {i} (ts={slide.TimestampMs}) section='{slide.SectionName}'");
                    SetSlideIndex(i);
                }
                break;
            }
        }
        if (chosen == -1 && _currentSlideIndex != 0
            && _currentSlides.Any(s => s.TimestampMs.HasValue && s.TimestampMs.Value > 0))
        {
            // Issue #5 (27/10): pos is before the first timestamped slide —
            // song start, or a stale index carried from a previous item/tap
            // (field log: curIdx=13 at pos=1765 stayed wrong until pos passed
            // the first timestamp). Slide 0 is the correct "not yet reached"
            // display; previously the matcher left any stale index in place.
            SetSlideIndex(0);
        }
        // Throttled 'stuck' trace: log roughly once/sec when the chosen slide is NOT past the
        // current one (i.e. nothing advances), to reveal a stalled/out-of-order timestamp.
        long bucket = (long)(positionMs / 1000);
        if (bucket != _diagLastPosBucket)
        {
            _diagLastPosBucket = bucket;
            var cur = (_currentSlideIndex >= 0 && _currentSlideIndex < _currentSlides.Count) ? _currentSlides[_currentSlideIndex] : null;
            WorshipPlayer.Core.Services.DiagLog.Write($"PosUpdate pos={positionMs:0} curIdx={_currentSlideIndex} curTs={cur?.TimestampMs} curSection='{cur?.SectionName}' chosenIdx={chosen} totalSlides={_currentSlides.Count}");
        }
    }
    private long _diagLastPosBucket = -1;

    /// <summary>
    /// Called when backtrack reports a section change. Finds the first slide
    /// for that section and jumps to it.
    /// </summary>
    public void OnSectionChanged(string sectionName)
    {
        WorshipPlayer.Core.Services.DiagLog.Write($"OnSectionChanged('{sectionName}') curIdx={_currentSlideIndex} totalSlides={_currentSlides.Count}");
        if (string.IsNullOrEmpty(sectionName)) return;

        // Find the first slide matching this section
        bool matched = false;
        for (int i = 0; i < _currentSlides.Count; i++)
        {
            if (_currentSlides[i].SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                WorshipPlayer.Core.Services.DiagLog.Write($"OnSectionChanged matched section '{sectionName}' at slide {i} (curIdx={_currentSlideIndex})");
                if (i != _currentSlideIndex && SectionEventAgreesWithPosition(i))
                    SetSlideIndex(i);
                break;
            }
        }
        if (!matched)
            WorshipPlayer.Core.Services.DiagLog.Write($"OnSectionChanged NO slide matches section '{sectionName}' — section names in list: " + string.Join(" | ", System.Linq.Enumerable.Select(_currentSlides, s => s.SectionName)));
    }

    /// <summary>
    /// Issue #5 (27/10): does a backtrack section event agree with the audio
    /// clock? A section event fires when audio crosses the section's chart
    /// boundary, but the section's first slide is timestamped at its first
    /// sung word — up to several seconds LATER (field log: PRE-CHORUS 1 event
    /// at pos 43941, slide ts 47322 → 3.4s early jump, then the position
    /// matcher yanked it back → visible flicker, repeated 1→0→1, 2→1→2, ...).
    /// When timestamp advance is active and the audio position is known, only
    /// honour the event if its slide agrees with the clock within grace; the
    /// position matcher advances at the right timestamp anyway. Falls back to
    /// the old behaviour (always honour) when the matcher is off, no position
    /// is known yet, or the target slide is untimestamped. As a side effect
    /// this also stops a bare repeated-section event (e.g. second CHORUS)
    /// from jumping back to the FIRST chorus while the matcher is active.
    /// Loop replays arrive via OnSectionLooped, not here, and are unaffected.
    /// </summary>
    private bool SectionEventAgreesWithPosition(int targetIndex)
    {
        if (!_settings.AutoAdvanceFromTimestamp) return true;
        if (!_lastPositionMs.HasValue) return true;
        var ts = _currentSlides[targetIndex].TimestampMs;
        if (!ts.HasValue || ts.Value <= 0) return true;
        return Math.Abs(ts.Value - _lastPositionMs.Value) <= SectionEventGraceMs;
    }

    // ─── Song/Section-Aware Methods ───────────────────────────────

    /// <summary>
    /// Load a song into the presenter engine. Generates slides from sections
    /// and assigns timing from ChartSlides.
    /// </summary>
    public void LoadSong(Song song)
    {
        // F2/F3: ServiceItem slides are the single source of truth. If the current
        // item already carries pre-baked slides, LoadSong must NOT regenerate them
        // nor fire SlideChanged — OnServiceItemChanged already loaded + emitted them.
        // GenerateSlidesFromSong is a fallback only (library/ChordPro songs with
        // sections[].text and no item slides).
        bool itemHasSlides = _serviceOrderManager.CurrentItem?.Slides?.Count > 0;

        // Snapshot the slides the operator is currently looking at BEFORE any
        // regeneration, so a late async backtrack load of the SAME song does not
        // snap a tapped slide back to slide 0 (field report 2026-10-24: thumbnail
        // taps kept landing back on INTRO / blank). GenerateSlidesForSong is pure
        // and deterministic, so slides already loaded via LoadItemSlides for this
        // song compare equal section-by-section and text-by-text.
        List<(string Section, string Text)> previousSnapshot;
        int previousIndex;
        lock (_stateLock)
        {
            _currentSong = song;
            previousIndex = _currentSlideIndex;
            previousSnapshot = _currentSlides
                .Select(s => (s.SectionName ?? "", s.Content?.Text ?? ""))
                .ToList();
        }
        _pitchShiftSemitones = 0;
        _tempoPercent = 100.0;

        bool regeneratedSameSlides = false;
        if (!itemHasSlides)
        {
            // Fallback: generate slides from the song's sections[].text.
            // GenerateSlidesFromSong resets _currentSlides/_currentSlideIndex under lock.
            GenerateSlidesFromSong(song);

            if (previousSnapshot.Count > 0 && previousIndex >= 0
                && previousSnapshot.Count == _currentSlides.Count
                && previousSnapshot.Zip(_currentSlides, (prev, cur) =>
                    prev.Section == (cur.SectionName ?? "")
                    && prev.Text == (cur.Content?.Text ?? "")).All(same => same))
            {
                // Same song, same slides as the operator already has (e.g. loaded via
                // LoadItemSlides on a thumbnail tap): keep the tapped slide instead
                // of resetting to slide 0, and emit nothing — the current slide is
                // already displayed.
                regeneratedSameSlides = true;
                lock (_stateLock)
                {
                    _currentSlideIndex = Math.Min(previousIndex, _currentSlides.Count - 1);
                }
            }
        }

        SongLoaded?.Invoke(this, new SongLoadedEventArgs { Song = song });

        // Fire slide changed ONLY in the fallback case (slide 0). When the item
        // already had slides, OnServiceItemChanged owns the SlideChanged emit.
        // When regeneration reproduced the slides already shown, nothing is fired.
        if (!itemHasSlides && !regeneratedSameSlides && _currentSlides.Count > 0 && _currentSlideIndex >= 0)
        {
            SlideChanged?.Invoke(this, new SlideChangedEventArgs
            {
                PreviousSlide = null,
                NewSlide = _currentSlides[_currentSlideIndex],
                Transition = SlideTransition.Fade,
                TransitionDurationMs = 500,
                IsLogo = false
            });
        }
    }

    /// <summary>
    /// Load a standalone song (e.g. picked from the Songs tab) unconditionally.
    /// Unlike <see cref="LoadSong"/>, this ignores the current ServiceItem's pre-baked
    /// slides and ALWAYS regenerates slides from the song's sections[].text, then emits.
    /// This gives the Songs-tab path its own entry point without touching the F2/F3
    /// "ServiceItem slides are the source of truth" behavior of the service-order flow.
    /// </summary>
    public void LoadStandaloneSong(Song song)
    {
        lock (_stateLock)
        {
            _currentSong = song;
        }
        _pitchShiftSemitones = 0;
        _tempoPercent = 100.0;

        // Always generate from the song's sections[].text — no itemHasSlides guard.
        // GenerateSlidesFromSong resets _currentSlides/_currentSlideIndex under lock.
        GenerateSlidesFromSong(song);

        SongLoaded?.Invoke(this, new SongLoadedEventArgs { Song = song });

        if (_currentSlides.Count > 0 && _currentSlideIndex >= 0)
        {
            SlideChanged?.Invoke(this, new SlideChangedEventArgs
            {
                PreviousSlide = null,
                NewSlide = _currentSlides[_currentSlideIndex],
                Transition = SlideTransition.Fade,
                TransitionDurationMs = 500,
                IsLogo = false
            });
        }
    }

    /// <summary>
    /// Load an externally generated set of slides (e.g. scripture slides produced by
    /// <see cref="ScriptureSlideGenerator"/> from the Scripture tab) directly into the
    /// engine and emit the first one. Mirrors <see cref="LoadStandaloneSong"/>: it sets
    /// the slide list + index under <c>_stateLock</c> and fires a single SlideChanged for
    /// slide 0 so the existing OnSlideChanged → OutputWindow.ShowSlide path renders it.
    /// Does not touch song/service-item state beyond the slide list.
    /// </summary>
    public void LoadScriptureSlides(List<Slide> slides)
    {
        // Đợt 2 T17 (R9): quick present takes the output away from the current
        // service item, so the item's attached audio stops. Re-selecting the item
        // later re-fires ItemChanged and restarts its audio from the top.
        ItemAudio.Stop();

        Slide? firstSlide;
        lock (_stateLock)
        {
            _currentSlides = slides ?? new List<Slide>();
            _currentSlideIndex = _currentSlides.Count > 0 ? 0 : -1;
            firstSlide = _currentSlideIndex >= 0 ? _currentSlides[0] : null;
        }

        if (firstSlide != null)
        {
            SlideChanged?.Invoke(this, new SlideChangedEventArgs
            {
                PreviousSlide = null,
                NewSlide = firstSlide,
                Transition = SlideTransition.Fade,
                TransitionDurationMs = 500,
                IsLogo = false
            });
        }
    }

    /// <summary>
    /// Replace the live slide list WITHOUT jumping to slide 0 (field feature
    /// 2026-10-24: editing a section's lyrics while its item is live). Unlike
    /// <see cref="LoadItemSlides"/>, the caller picks which slide stays on screen
    /// via <paramref name="preferredIndex"/> (clamped); exactly one SlideChanged
    /// fires for that slide, so the AUDIENCE preview updates in place instead of
    /// flashing through slide 0. An empty list clears the index and fires nothing.
    /// </summary>
    public void ReplaceSlidesPreservingIndex(List<Slide> slides, int preferredIndex)
    {
        Slide? previousSlide;
        Slide? newSlide;
        lock (_stateLock)
        {
            previousSlide = _currentSlideIndex >= 0 && _currentSlideIndex < _currentSlides.Count
                ? _currentSlides[_currentSlideIndex]
                : null;
            _currentSlides = slides ?? new List<Slide>();
            _currentSlideIndex = _currentSlides.Count > 0
                ? Math.Clamp(preferredIndex, 0, _currentSlides.Count - 1)
                : -1;
            newSlide = _currentSlideIndex >= 0 ? _currentSlides[_currentSlideIndex] : null;
        }

        if (newSlide != null)
        {
            SlideChanged?.Invoke(this, new SlideChangedEventArgs
            {
                PreviousSlide = previousSlide,
                NewSlide = newSlide,
                Transition = SlideTransition.Fade,
                TransitionDurationMs = 500,
                IsLogo = false
            });
        }
    }

    /// <summary>
    /// Load slides already resolved for the CURRENT service item (e.g. the operator
    /// center pane's GetSlidesForItem output for a library-linked Song or a live-linked
    /// Slides item whose ServiceItem.Slides are empty) directly into the engine and
    /// emit the first one. Unlike <see cref="LoadScriptureSlides"/> it does NOT stop
    /// item audio: it is called right after GoToItem, which already stopped the
    /// previous item's audio and started the new item's. This is what makes tapping a
    /// thumbnail of a non-baked item work: without it, GoToSlide runs against an
    /// empty slide list and is a no-op (field report 2026-10-24).
    /// </summary>
    public void LoadItemSlides(List<Slide> slides)
    {
        Slide? firstSlide;
        lock (_stateLock)
        {
            _currentSlides = slides ?? new List<Slide>();
            _currentSlideIndex = _currentSlides.Count > 0 ? 0 : -1;
            firstSlide = _currentSlideIndex >= 0 ? _currentSlides[0] : null;
        }

        if (firstSlide != null)
        {
            SlideChanged?.Invoke(this, new SlideChangedEventArgs
            {
                PreviousSlide = null,
                NewSlide = firstSlide,
                Transition = SlideTransition.Fade,
                TransitionDurationMs = 500,
                IsLogo = false
            });
        }
    }

    /// <summary>
    /// Generate slides from a song's sections[].text field.
    /// Uses ChordChartData timing when available for auto-advance.
    /// This is the bridge between the backtrack's song data and the presenter's slide system.
    /// </summary>
    private void GenerateSlidesFromSong(Song song)
    {
        var generated = GenerateSlidesForSong(song);

        // Publish the generated slides under the state lock; set to first slide if any.
        lock (_stateLock)
        {
            _currentSlides = generated;
            _currentSlideIndex = generated.Count > 0 ? 0 : -1;
        }
        DiagDumpSlides("GenerateSlidesFromSong");
    }

    /// <summary>
    /// Pure slide generator: builds a fresh slide list from a song's sections[].text
    /// WITHOUT mutating any engine state (no <c>_stateLock</c>, no writes to
    /// <c>_currentSlides</c>/<c>_currentSlideIndex</c>). Shares the exact block/timestamp
    /// logic used by the live path (<see cref="GenerateSlidesFromSong"/>) so sections
    /// rendered for non-live items match the live rendering byte-for-byte. Used by the
    /// all-sections center pane to preview every service item without disturbing the live
    /// slide/go-live/backtrack flow.
    /// </summary>
    public List<Slide> GenerateSlidesForSong(Song song)
    {
        var generated = new List<Slide>();

        if (song.Sections == null)
        {
            return generated;
        }

        int order = 0;

        // Use ChordChartData slide timing if available
        var chartSlides = song.ChordChart?.Slides;

        // Worship songs repeat section names (every "CHORUS" in the song).
        // Count occurrences in generation order so chart matching below can
        // pair the Nth section with the Nth chart entry of that name.
        var sectionOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Field fix (Issue #5, 28/10): per-section spans for chart anchoring
        // (applied after generation, before the safety net below).
        var sectionSpans = new List<(int start, int end, double loopInMs, bool hasEmbeddedTime)>();

        // Walk the song's slide-generation units (đợt 2 T9 / R6.1): without a custom
        // slide order this is exactly the original section order; with one, sections
        // repeat/skip per the order and Blank units become one blank slide each.
        foreach (var unit in SongSlideOrderResolver.ResolveUnits(song))
        {
            if (unit.IsBlank)
            {
                generated.Add(new Slide
                {
                    Type = SlideType.Blank,
                    Content = new SlideContent(),
                    SectionName = "",
                    Order = order++,
                    Transition = SlideTransition.Fade,
                    TransitionDurationMs = 500
                });
                continue;
            }

            var section = unit.Section!;
            string sectionText = section.Text;
            if (string.IsNullOrWhiteSpace(sectionText)) continue;

            // This section's occurrence index (0-based) among sections
            // sharing its name — counted only for sections that actually
            // produce slides, so empty sections consume no chart entries.
            int sectionOccurrence = sectionOccurrences.TryGetValue(section.Name, out var seen)
                ? seen : 0;
            sectionOccurrences[section.Name] = sectionOccurrence + 1;

            // Chart-anchoring span for this section (recorded after its
            // second pass below).
            int sectionStartIndex = generated.Count;
            bool sectionHasEmbeddedTime = false;

            // Split section text into slide blocks (separated by empty lines)
            var blocks = _chordParser.ExtractSlideBlocks(sectionText);

            // Field feature 2026-10-24: a block typed as consecutive lines (no blank
            // line between them) used to project as ONE slide — a 4-line chorus on
            // one screen in tiny text. Now each block is chunked into groups of
            // linesPerSlide lines (the section's override, else the global setting,
            // clamped 2–4): 2 lines on screen, then the next 2, and so on. Chunks
            // never cross a section boundary — each section's blocks are chunked
            // independently, in section order — so when a chorus ends, the next
            // slide is the next section's first lines. A blank line still starts a
            // new block (and therefore a new slide) as before.
            //
            // Auto-advance (music playing => slides jump by themselves) runs on
            // OnPlaybackPositionUpdate, which only sees slides WITH a timestamp —
            // so chunks must carry timestamps too, or the music would stall on a
            // block's first chunk. A chunk's time is interpolated evenly inside its
            // block's window: the block's start (embedded {M:SS.mmm} timestamp, else
            // its ChordChart start) to the block's end (ChordChart end, else the
            // next timed slide's start, else the section's loop-out). Chunks of an
            // untimed block stay untimed (section-change / MIDI / manual advance
            // still work for them, exactly as before chunking).
            int linesPerSlide = Math.Clamp(
                section.LinesPerSlideOverride ?? _settings.LinesPerSlide, 2, 4);

            // ChordChart SlideIndex counts BLOCKS split by empty lines within the
            // section — NOT 2-line chunks. Matching by chunk index would hand a
            // block's second chunk the NEXT block's start time.
            int blockIndexInSection = 0;
            var chunkMeta = new List<(int slideIndex, double startMs, double? endMs, int chunkIndex, int chunkCount, double sectionLoopOutMs)>();
            foreach (var (timestampMs, text) in blocks)
            {
                // An embedded {M:SS.mmm} time is explicit user intent: the
                // section keeps its times and is excluded from chart anchoring.
                if (timestampMs >= 0) sectionHasEmbeddedTime = true;

                // Field fix 2026-10-25: match the Nth section occurrence to
                // the Nth chart entry with this name + block index. Matching
                // by name alone (FirstOrDefault) handed EVERY "CHORUS" the
                // first chorus's times, so the timeline went non-monotonic
                // and the position path jumped to the last chorus and stuck
                // there. No chart entry for this occurrence => untimed:
                // never reuse an earlier occurrence's time.
                var matchingChartSlide = chartSlides?
                    .Where(cs => cs.SectionName.Equals(section.Name, StringComparison.OrdinalIgnoreCase) &&
                                 cs.SlideIndex == blockIndexInSection)
                    .Skip(sectionOccurrence)
                    .FirstOrDefault();

                double? blockStart = timestampMs >= 0
                    ? timestampMs
                    : matchingChartSlide?.StartMs;
                double? blockEnd = matchingChartSlide != null && matchingChartSlide.EndMs > 0
                    ? matchingChartSlide.EndMs
                    : null;

                var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                int chunkCount = (lines.Length + linesPerSlide - 1) / linesPerSlide;
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    string chunkText = string.Join("\n",
                        lines.Skip(chunkIndex * linesPerSlide).Take(linesPerSlide));

                    generated.Add(new Slide
                    {
                        Type = SlideType.Lyrics,
                        Content = new SlideContent
                        {
                            Text = chunkText,
                            Subtitle = section.Name
                        },
                        SectionName = section.Name,
                        Order = order++,
                        // First chunk: the block's start. Later chunks: interpolated
                        // in the second pass below (their end boundary — the next
                        // timed slide — is only known once generation finishes).
                        TimestampMs = chunkIndex == 0 ? blockStart : null,
                        Transition = SlideTransition.Fade,
                        TransitionDurationMs = 500
                    });

                    if (blockStart.HasValue)
                    {
                        chunkMeta.Add((generated.Count - 1, blockStart.Value, blockEnd,
                            chunkIndex, chunkCount, section.LoopOutMs));
                    }
                }

                blockIndexInSection++;
            }

            // Second pass: interpolate timestamps for chunks after the first in
            // each timed block, evenly across [blockStart, blockEnd).
            foreach (var meta in chunkMeta)
            {
                if (meta.chunkIndex == 0) continue;

                double? end = meta.endMs;
                if (end == null || end <= meta.startMs)
                {
                    // Next timed slide after this chunk: a later block's first
                    // chunk in this same section (later sections are not
                    // generated yet at this point; their boundary is covered by
                    // the section loop-out fallback below).
                    for (int i = meta.slideIndex + 1; i < generated.Count; i++)
                    {
                        var t = generated[i].TimestampMs;
                        if (t.HasValue && t.Value > meta.startMs)
                        {
                            end = t.Value;
                            break;
                        }
                    }
                }
                if ((end == null || end <= meta.startMs) &&
                    meta.sectionLoopOutMs > meta.startMs)
                {
                    end = meta.sectionLoopOutMs;
                }

                if (end.HasValue && end.Value > meta.startMs)
                {
                    generated[meta.slideIndex].TimestampMs =
                        meta.startMs + meta.chunkIndex * (end.Value - meta.startMs) / meta.chunkCount;
                }
            }

            sectionSpans.Add((sectionStartIndex, generated.Count, section.LoopInMs, sectionHasEmbeddedTime));
        }

        // Chart anchoring (Issue #5, 28/10): the chord chart shows a section
        // from its chart boundary (LoopInMs, bar-derived), but slide
        // timestamps came from the first SUNG WORD of each block — up to
        // ~10s later (BRIDGE 1: chart 146448, first sung word 156678). In
        // that gap the chord chart had moved on while the audience still
        // showed the previous section, and the position matcher yanked any
        // early section jump back (field log 27/10: 10->9, 11->9 around
        // pos 143k-149k). So anchor each section's slides to its chart
        // start: shift the whole section's timestamps uniformly so its
        // first slide lands on LoopInMs. Uniform shifting preserves
        // intra-section rhythm; sections with an embedded manual timestamp
        // keep their times (explicit user intent). The clamp keeps the
        // timeline monotonic even for repeated sections (same LoopInMs
        // twice) — the safety net below stays as backstop. With anchoring,
        // mixer section events (which fire at LoopInMs) and the matcher
        // agree, and the display no longer depends on section events that
        // stop firing after PRE-CHORUS 2 in the field logs.
        double prevTimedMs = double.MinValue;
        foreach (var (start, end, loopInMs, hasEmbeddedTime) in sectionSpans)
        {
            double? lastTs = null;
            for (int i = end - 1; i >= start; i--)
            {
                var ts = generated[i].TimestampMs;
                if (ts.HasValue) { lastTs = ts.Value; break; }
            }

            int first = -1;
            double firstTs = 0;
            for (int i = start; i < end; i++)
            {
                var ts = generated[i].TimestampMs;
                if (ts.HasValue) { first = i; firstTs = ts.Value; break; }
            }

            if (first >= 0 && !hasEmbeddedTime && loopInMs > 0)
            {
                double target = Math.Max(loopInMs, prevTimedMs + 1);
                double? secondTs = null;
                for (int i = first + 1; i < end; i++)
                {
                    var ts = generated[i].TimestampMs;
                    if (ts.HasValue) { secondTs = ts.Value; break; }
                }

                // Apply only when the section's own order is already
                // increasing (otherwise leave it for the safety net) and
                // the shift is material.
                if ((!secondTs.HasValue || secondTs.Value > firstTs) && Math.Abs(firstTs - target) >= 1)
                {
                    double shift = firstTs - target;
                    for (int i = start; i < end; i++)
                    {
                        var ts = generated[i].TimestampMs;
                        if (ts.HasValue)
                            generated[i].TimestampMs = ts.Value - shift;
                    }
                    if (lastTs.HasValue) lastTs = lastTs.Value - shift;
                }
            }

            if (lastTs.HasValue) prevTimedMs = lastTs.Value;
        }

        // Safety net (field fix 2026-10-25): the position-update path assumes
        // timestamps increase with slide order. Any timestamp that steps back
        // — a repeated section reusing an earlier time, stale chart data,
        // embedded times on a repeated section — would make it jump to the
        // last slide carrying that old time and stick there. Drop such
        // timestamps instead; those slides still advance by section change,
        // MIDI, or tap, exactly like untimed blocks.
        double lastTimedMs = double.MinValue;
        foreach (var slide in generated)
        {
            if (slide.TimestampMs.HasValue)
            {
                if (slide.TimestampMs.Value <= lastTimedMs)
                    slide.TimestampMs = null;
                else
                    lastTimedMs = slide.TimestampMs.Value;
            }
        }

        return generated;
    }

    /// <summary>
    /// Alias/entry point for LoadSong.
    /// </summary>
    public void OnSongLoaded(Song song)
    {
        LoadSong(song);
    }

    /// <summary>
    /// Called when the backtrack reports a section has looped.
    /// Re-shows the first slide for that section.
    /// </summary>
    public void OnSectionLooped(string sectionName)
    {
        if (string.IsNullOrEmpty(sectionName)) return;

        // Find the first slide matching this section
        for (int i = 0; i < _currentSlides.Count; i++)
        {
            if (_currentSlides[i].SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
            {
                SetSlideIndex(i);
                return;
            }
        }
    }

    /// <summary>
    /// Called when pitch shift changes (backtrack real-time transposition).
    /// Updates internal state and fires event for displays to re-render.
    /// </summary>
    public void OnPitchShiftChanged(int semitones)
    {
        _pitchShiftSemitones = semitones;
        PitchShiftChanged?.Invoke(this, new PitchShiftChangedEventArgs { Semitones = semitones });
    }

    /// <summary>
    /// Called when tempo changes.
    /// </summary>
    public void OnTempoChanged(double percent)
    {
        _tempoPercent = percent;
        TempoChanged?.Invoke(this, new TempoChangedEventArgs { TempoPercent = percent });
    }

    /// <summary>
    /// Called when the backtrack song ends. Advances to next service item.
    /// </summary>
    public void OnSongEnded()
    {
        if (_settings.AutoAdvanceOnSongEnd)
        {
            _serviceOrderManager.NextItem();
        }
    }

    // ─── Dual Rendering Pipeline ───────────────────────────────────

    /// <summary>
    /// Get text for the congregation display (chords stripped).
    /// </summary>
    public string GetCongregationText(Slide slide)
    {
        if (slide?.Content?.Text == null) return "";
        return _chordParser.StripChords(slide.Content.Text);
    }

    /// <summary>
    /// Get text for the stage display (chords kept, transposed by given semitones).
    /// </summary>
    public string GetStageText(Slide slide, int transposeSemitones)
    {
        if (slide?.Content?.Text == null) return "";

        if (transposeSemitones == 0)
            return slide.Content.Text;

        return _chordParser.Transpose(slide.Content.Text, transposeSemitones);
    }

    /// <summary>
    /// Get text with Nashville number notation for the given key.
    /// </summary>
    public string GetNashvilleText(Slide slide, string key)
    {
        if (slide?.Content?.Text == null) return "";
        if (string.IsNullOrEmpty(key)) return slide.Content.Text;

        return _chordParser.ConvertToNashvilleNumbers(slide.Content.Text, key);
    }

    /// <summary>
    /// Get text with chords transposed for capo display.
    /// Shows what chord shapes to play with the given capo fret.
    /// </summary>
    public string GetCapoText(Slide slide, int capoFret)
    {
        if (slide?.Content?.Text == null) return "";
        if (capoFret == 0) return slide.Content.Text;

        return _chordParser.CapoTranspose(slide.Content.Text, capoFret);
    }

    // ─── Thread-Safe Snapshot ──────────────────────────────────────

    /// <summary>
    /// Build a fresh, internally consistent <see cref="StageDisplayData"/> from the
    /// current engine state, read entirely under <c>_stateLock</c>. This exists so the
    /// audio render thread (which raises SectionChanged/PositionChanged) can read the
    /// slide/song state without torn reads (F12). Every dereference is null-guarded
    /// (slide, Content and song may be null) and the returned instance is not mutated
    /// after the lock is released.
    /// </summary>
    public StageDisplayData SnapshotStageData()
    {
        lock (_stateLock)
        {
            var slides = _currentSlides;
            int index = _currentSlideIndex;
            int total = slides?.Count ?? 0;

            Slide? current = slides != null && index >= 0 && index < slides.Count
                ? slides[index]
                : null;
            Slide? next = slides != null && index + 1 >= 0 && index + 1 < slides.Count
                ? slides[index + 1]
                : null;

            string currentText = current?.Content?.Text ?? "";
            string nextText = next?.Content?.Text ?? "";
            string sectionName = current?.SectionName ?? "";

            var song = _currentSong;
            string songTitle = song?.Title ?? "";
            string originalKey = song?.OriginalKey ?? "";

            return new StageDisplayData
            {
                CurrentText = currentText,
                NextText = nextText,
                SectionName = sectionName,
                SongTitle = songTitle,
                Key = originalKey,
                OriginalKey = originalKey,
                IsLive = _isLive,
                IsBlacked = _isBlacked,
                CurrentSlideIndex = index,
                TotalSlides = total
            };
        }
    }

    // ─── State Accessors ───────────────────────────────────────────

    /// <summary>
    /// The currently loaded song (null if no song loaded).
    /// </summary>
    public Song? CurrentSong => _currentSong;

    /// <summary>
    /// Current pitch shift in semitones.
    /// </summary>
    public int PitchShiftSemitones => _pitchShiftSemitones;

    /// <summary>
    /// Current tempo as percentage of original.
    /// </summary>
    public double TempoPercent => _tempoPercent;

    /// <summary>
    /// Whether the engine is in manual advance mode (no backtrack).
    /// </summary>
    public bool IsManualAdvanceMode => _isManualAdvanceMode;

    /// <summary>
    /// Id of the item whose auto-advance countdown is currently running, or null
    /// when no countdown is active (đợt 2 T5 / R3.4).
    /// </summary>
    public string? AutoAdvanceCountdownItemId => _autoAdvanceCountdownItemId;

    /// <summary>
    /// Milliseconds remaining on the running auto-advance countdown, computed live
    /// from its start timestamp; 0 when no countdown is active or it has finished
    /// (đợt 2 T5 / R3.4).
    /// </summary>
    public long AutoAdvanceCountdownRemainingMs => _autoAdvanceCountdownItemId == null
        ? 0
        : AutoAdvanceCountdown.RemainingMs(
            _autoAdvanceCountdownDelayMs, _autoAdvanceCountdownStartedAtUtc, DateTimeOffset.UtcNow);

    // ─── Internal ──────────────────────────────────────────────────

    private void OnServiceItemChanged(object? sender, ServiceItemChangedEventArgs e)
    {
        // Đợt 2 T17 (R9): item audio — stop the previous item's audio immediately,
        // then start the new item's attached audio. R9.4: a Song item with
        // UseBacktrack = true never plays its attached audio (no overlap with the
        // backtrack). This is the single choke point for every current-item change
        // (manual click, auto-advance, service reload), so no path can leak audio.
        ItemAudio.Stop();
        if (e.NewItem != null && ServiceItemAudio.ShouldPlayAttachedAudio(e.NewItem))
        {
            var audio = ServiceItemAudio.ResolvePlayback(e.NewItem);
            if (audio.Files.Count > 0)
                ItemAudio.PlayForItem(audio);
        }

        // Per-item auto-advance: cancel any pending timer from the previous item, then
        // re-arm only if the NEW item opts in via the Timer condition. Items with no
        // settings or Manual/AudioEnd/VideoEnd never start a timer, so they behave
        // exactly as before (acceptance criterion 4).
        RestartAutoAdvanceTimer(e.NewItem);

        // Load slides for the new service item
        if (e.NewItem != null)
        {
            Slide? firstSlide;
            lock (_stateLock)
            {
                // Đợt 2 T10 / R6.2: the item's OWN slide order (when set) is applied
                // on top of its baked slides — priority T10 > T9 > original (R6.3).
                // A null order makes Apply an identity, so pre-T10 items load
                // byte-for-byte as before.
                _currentSlides = ServiceItemSlideOrder.Apply(e.NewItem.Slides, e.NewItem.SlideOrder);
                _currentSlideIndex = _currentSlides.Count > 0 ? 0 : -1;
                firstSlide = _currentSlideIndex >= 0 ? _currentSlides[0] : null;
            }

            // Determine if this is a manual advance song (no backtrack)
            _isManualAdvanceMode = !e.NewItem.UseBacktrack;

            ItemChanged?.Invoke(this, e);

            if (firstSlide != null)
            {
                SlideChanged?.Invoke(this, new SlideChangedEventArgs
                {
                    PreviousSlide = null,
                    NewSlide = firstSlide,
                    Transition = SlideTransition.Fade,
                    TransitionDurationMs = 500,
                    IsLogo = false
                });
            }
        }
        DiagDumpSlides("OnServiceItemChanged item='" + (e.NewItem?.Title ?? "null") + "'");
    }

    // TEMP DIAG: dump the current live slide list with timestamps so Muse can see missing /
    // out-of-order timestamps that stall OnPlaybackPositionUpdate (Issue #5 lyric hang).
    private void DiagDumpSlides(string where)
    {
        try
        {
            WorshipPlayer.Core.Services.DiagLog.Write($"--- SLIDES loaded ({where}) count={_currentSlides.Count} curIdx={_currentSlideIndex} ---");
            for (int i = 0; i < _currentSlides.Count; i++)
            {
                var s = _currentSlides[i];
                string txt = (s.Content?.Text ?? "").Replace("\n", " / ");
                if (txt.Length > 45) txt = txt.Substring(0, 45) + "...";
                WorshipPlayer.Core.Services.DiagLog.Write($"  [{i,2}] ts={(s.TimestampMs.HasValue ? s.TimestampMs.Value.ToString("0") : "null"),8} section='{s.SectionName}' text='{txt}'");
            }
        }
        catch { }
    }

    private void SetSlideIndex(int newIndex)
    {
        Slide? previousSlide;
        Slide? newSlide;
        lock (_stateLock)
        {
            previousSlide = _currentSlideIndex >= 0 && _currentSlideIndex < _currentSlides.Count
                ? _currentSlides[_currentSlideIndex]
                : null;
            _currentSlideIndex = newIndex;
            newSlide = _currentSlideIndex >= 0 && _currentSlideIndex < _currentSlides.Count
                ? _currentSlides[_currentSlideIndex]
                : null;
        }

        var transition = newSlide?.Transition ?? SlideTransition.Fade;
        var duration = newSlide?.TransitionDurationMs ?? 500;

        SlideChanged?.Invoke(this, new SlideChangedEventArgs
        {
            PreviousSlide = previousSlide,
            NewSlide = newSlide,
            Transition = transition,
            TransitionDurationMs = duration,
            IsLogo = false
        });
    }

    /// <summary>
    /// Stop/dispose any pending auto-advance timer and, if the given item is configured
    /// with the Timer "After" condition, start a fresh one-shot timer for its delay.
    /// </summary>
    private void RestartAutoAdvanceTimer(ServiceItem? newItem)
    {
        if (_autoAdvanceTimer != null)
        {
            _autoAdvanceTimer.Stop();
            _autoAdvanceTimer.Dispose();
            _autoAdvanceTimer = null;
        }

        // Đợt 2 T5 (R3.4): this method runs on EVERY current-item change (including
        // manual navigation), so cancelling the previous item's countdown here is
        // what makes the on-screen countdown disappear/restart exactly on change.
        CancelAutoAdvanceCountdown();

        var settings = newItem?.AutoAdvanceSettings;

        // Đợt 2 T3: lyrics/scripture items never auto-advance — don't even arm the
        // timer. The pure resolver enforces the same rule at fire time.
        if (!AutoAdvanceResolver.IsEligible(newItem))
            return;

        if (settings == null || settings.AutoAdvanceAfterCondition != AutoAdvanceAfterCondition.Timer)
            return;

        // Guard against a non-positive delay (treat as default) so the timer is valid.
        // AutoAdvanceCountdown.EffectiveDelayMs is that same default rule, shared with
        // the countdown display so the two can never disagree (đợt 2 T5).
        double delay = AutoAdvanceCountdown.EffectiveDelayMs(settings.AutoAdvanceDelayMs);

        var timer = new System.Timers.Timer(delay) { AutoReset = false };
        timer.Elapsed += (_, _) => FireAutoAdvance(AutoAdvanceTrigger.Timer);
        _autoAdvanceTimer = timer;
        timer.Start();

        // Đợt 2 T5 (R3.4): start reporting the countdown for this item. The ticker
        // only publishes remaining-time updates; the one-shot timer above remains
        // the single mechanism that actually advances.
        _autoAdvanceCountdownItemId = newItem?.Id;
        _autoAdvanceCountdownDelayMs = settings.AutoAdvanceDelayMs;
        _autoAdvanceCountdownStartedAtUtc = DateTimeOffset.UtcNow;
        var ticker = new System.Timers.Timer(250) { AutoReset = true };
        ticker.Elapsed += OnAutoAdvanceCountdownTick;
        _autoAdvanceCountdownTicker = ticker;
        ticker.Start();
        RaiseAutoAdvanceCountdownChanged();
    }

    /// <summary>
    /// Countdown ticker (đợt 2 T5): publish the latest remaining time, or end the
    /// countdown when it has run out. The one-shot auto-advance timer fires at the
    /// same moment and normally moves to the next item — which cancels this
    /// countdown via <see cref="RestartAutoAdvanceTimer"/> — but when the advance
    /// decision is None (e.g. last item in the service) nothing else would clear
    /// the display, so the ticker closes the countdown itself here.
    /// </summary>
    private void OnAutoAdvanceCountdownTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_autoAdvanceCountdownItemId == null)
            return;

        if (AutoAdvanceCountdown.IsFinished(
                _autoAdvanceCountdownDelayMs, _autoAdvanceCountdownStartedAtUtc, DateTimeOffset.UtcNow))
        {
            CancelAutoAdvanceCountdown();
            return;
        }

        RaiseAutoAdvanceCountdownChanged();
    }

    /// <summary>
    /// Stop countdown reporting and notify listeners that no countdown is active.
    /// Safe to call when no countdown is running (no event is raised then).
    /// </summary>
    private void CancelAutoAdvanceCountdown()
    {
        if (_autoAdvanceCountdownTicker != null)
        {
            _autoAdvanceCountdownTicker.Stop();
            _autoAdvanceCountdownTicker.Dispose();
            _autoAdvanceCountdownTicker = null;
        }

        if (_autoAdvanceCountdownItemId != null)
        {
            _autoAdvanceCountdownItemId = null;
            RaiseAutoAdvanceCountdownChanged();
        }
    }

    private void RaiseAutoAdvanceCountdownChanged()
    {
        AutoAdvanceCountdownChanged?.Invoke(this, new AutoAdvanceCountdownEventArgs
        {
            ItemId = _autoAdvanceCountdownItemId,
            RemainingMs = AutoAdvanceCountdownRemainingMs,
            IsActive = _autoAdvanceCountdownItemId != null
        });
    }

    /// <summary>
    /// Entry point for the video-ended auto-advance trigger (đợt 2 T4 / R3): the
    /// output layer calls this when the current item's video (per-item background
    /// video or full-screen video) plays to its end. Only an item whose configured
    /// condition is VideoEnd advances — the pure resolver enforces the match, so an
    /// item without a running video, or with any other condition, never moves here.
    /// </summary>
    public void NotifyCurrentItemVideoEnded()
    {
        FireAutoAdvance(AutoAdvanceTrigger.VideoEnd);
    }

    /// <summary>
    /// Invoked when a per-item auto-advance trigger fires (timer elapsed, or the
    /// current item's video ended). Asks the pure <see cref="AutoAdvanceResolver"/>
    /// what to do for the current item under that trigger, then executes the
    /// decision via the manager (which raises ItemChanged for the UI to render).
    /// </summary>
    private void FireAutoAdvance(AutoAdvanceTrigger trigger)
    {
        var order = _serviceOrderManager.CurrentServiceOrder;
        var currentId = _serviceOrderManager.CurrentItem?.Id;
        if (order == null || currentId == null)
            return;

        var decision = AutoAdvanceResolver.Resolve(order, currentId, trigger);
        switch (decision.Action)
        {
            case AutoAdvanceAction.NextItem:
                _serviceOrderManager.NextItem();
                break;

            case AutoAdvanceAction.GoToItem:
                int index = order.Items.FindIndex(i => i.Id == decision.TargetItemId);
                if (index >= 0)
                    _serviceOrderManager.GoToItem(index);
                break;

            case AutoAdvanceAction.None:
            default:
                // Do nothing.
                break;
        }
    }

    public void Dispose()
    {
        _serviceOrderManager.ItemChanged -= OnServiceItemChanged;

        if (_autoAdvanceTimer != null)
        {
            _autoAdvanceTimer.Stop();
            _autoAdvanceTimer.Dispose();
            _autoAdvanceTimer = null;
        }

        if (_autoAdvanceCountdownTicker != null)
        {
            _autoAdvanceCountdownTicker.Stop();
            _autoAdvanceCountdownTicker.Dispose();
            _autoAdvanceCountdownTicker = null;
        }
        _autoAdvanceCountdownItemId = null;

        // Đợt 2 T17 (R9): release the item-audio stream with the engine.
        ItemAudio.Dispose();
    }
}

/// <summary>
/// Event args for auto-advance countdown updates (đợt 2 T5 / R3.4).
/// </summary>
public class AutoAdvanceCountdownEventArgs : EventArgs
{
    /// <summary>Id of the item counting down; null when no countdown is active.</summary>
    public string? ItemId { get; set; }

    /// <summary>Milliseconds remaining until the auto-advance fires; 0 when inactive or finished.</summary>
    public long RemainingMs { get; set; }

    /// <summary>True while a countdown is running.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Event args for slide changes — used by output windows for rendering.
/// </summary>
public class SlideChangedEventArgs : EventArgs
{
    public Slide? PreviousSlide { get; set; }
    public Slide? NewSlide { get; set; }
    public SlideTransition Transition { get; set; }
    public int TransitionDurationMs { get; set; }
    public bool IsLogo { get; set; }
}

/// <summary>
/// Event args for display state changes (live/black).
/// </summary>
public class DisplayStateChangedEventArgs : EventArgs
{
    public bool IsLive { get; set; }
    public bool IsBlacked { get; set; }
}

/// <summary>
/// Event args for pitch shift changes.
/// </summary>
public class PitchShiftChangedEventArgs : EventArgs
{
    public int Semitones { get; set; }
}

/// <summary>
/// Event args for tempo changes.
/// </summary>
public class TempoChangedEventArgs : EventArgs
{
    public double TempoPercent { get; set; }
}

/// <summary>
/// Event args for song loaded events.
/// </summary>
public class SongLoadedEventArgs : EventArgs
{
    public Song Song { get; set; } = null!;
}
