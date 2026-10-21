# Auto-Timed Slides — Tasks

- [x] 1. Model fields (PresenterSettings.LinesPerSlide/SlideLeadMs, Section.LinesPerSlideOverride)
- [x] 2. SlideGroupingService (pure) + unit tests (11)
- [x] 3. SlideAutoTimingService (pure) + unit tests (11)
- [x] 4. Wire RecalculateSlideTimings to use SlideGroupingService
- [x] 5. Python pipeline honors --lines-per-slide + --slide-lead-ms (build_chord_chart)
- [x] 6. Per-section lines-per-slide combo in the Waveform Editor (+ model round-trip)
- [x] 7. Settings UI: lines-per-slide + lead ms (Defaults section)
- [x] 8. Full test suite green (812) + Python compiles + app runs

Notes:
- Playback auto-advance reuses the existing Slide.TimestampMs → PresenterEngine chain (no change).
- Timecodes persist in the song (SQL + json backup), so playback needs no Whisper.
- Still to wire (optional follow-up): feed PresenterSettings.LinesPerSlide/SlideLeadMs into the
  SectionEditorWindow.LinesPerSlide/SlideLeadMs properties at the call site that opens it, so the
  configured global actually reaches Python (currently defaults 4/500 unless the opener sets them).
