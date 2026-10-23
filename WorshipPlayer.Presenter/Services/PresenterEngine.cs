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
        if (!_settings.AutoAdvanceFromTimestamp) return;
        if (_currentSlides.Count == 0) return;

        // Find the slide that should be showing at this timestamp
        for (int i = _currentSlides.Count - 1; i >= 0; i--)
        {
            var slide = _currentSlides[i];
            if (slide.TimestampMs.HasValue && slide.TimestampMs.Value > 0 && positionMs >= slide.TimestampMs.Value)
            {
                if (i != _currentSlideIndex)
                {
                    SetSlideIndex(i);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Called when backtrack reports a section change. Finds the first slide
    /// for that section and jumps to it.
    /// </summary>
    public void OnSectionChanged(string sectionName)
    {
        if (string.IsNullOrEmpty(sectionName)) return;

        // Find the first slide matching this section
        for (int i = 0; i < _currentSlides.Count; i++)
        {
            if (_currentSlides[i].SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
            {
                if (i != _currentSlideIndex)
                    SetSlideIndex(i);
                break;
            }
        }
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

        lock (_stateLock)
        {
            _currentSong = song;
        }
        _pitchShiftSemitones = 0;
        _tempoPercent = 100.0;

        if (!itemHasSlides)
        {
            // Fallback: generate slides from the song's sections[].text.
            // GenerateSlidesFromSong resets _currentSlides/_currentSlideIndex under lock.
            GenerateSlidesFromSong(song);
        }

        SongLoaded?.Invoke(this, new SongLoadedEventArgs { Song = song });

        // Fire slide changed ONLY in the fallback case (slide 0). When the item
        // already had slides, OnServiceItemChanged owns the SlideChanged emit.
        if (!itemHasSlides && _currentSlides.Count > 0 && _currentSlideIndex >= 0)
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

        if (song.Sections == null || song.Sections.Count == 0)
        {
            return generated;
        }

        int order = 0;

        // Use ChordChartData slide timing if available
        var chartSlides = song.ChordChart?.Slides;

        foreach (var section in song.Sections)
        {
            string sectionText = section.Text;
            if (string.IsNullOrWhiteSpace(sectionText)) continue;

            // Split section text into slide blocks (separated by empty lines)
            var blocks = _chordParser.ExtractSlideBlocks(sectionText);

            int slideIndexInSection = 0;
            foreach (var (timestampMs, text) in blocks)
            {
                // Try to get timing from ChordChartData
                double? slideTimestamp = null;
                if (timestampMs >= 0)
                {
                    // Timestamp embedded in text: {M:SS.mmm}
                    slideTimestamp = timestampMs;
                }
                else if (chartSlides != null)
                {
                    // Find matching ChartSlide by section name + slide index
                    var matchingChartSlide = chartSlides.FirstOrDefault(cs =>
                        cs.SectionName.Equals(section.Name, StringComparison.OrdinalIgnoreCase) &&
                        cs.SlideIndex == slideIndexInSection);

                    if (matchingChartSlide != null)
                    {
                        slideTimestamp = matchingChartSlide.StartMs;
                    }
                }

                generated.Add(new Slide
                {
                    Type = SlideType.Lyrics,
                    Content = new SlideContent
                    {
                        Text = text,
                        Subtitle = section.Name
                    },
                    SectionName = section.Name,
                    Order = order++,
                    TimestampMs = slideTimestamp,
                    Transition = SlideTransition.Fade,
                    TransitionDurationMs = 500
                });

                slideIndexInSection++;
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
                _currentSlides = e.NewItem.Slides ?? new List<Slide>();
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
