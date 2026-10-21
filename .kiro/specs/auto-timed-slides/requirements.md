# Auto-Timed Slides — Requirements

## Overview

Use the Demucs-separated **vocal stem** and Whisper **word-level timestamps** to automatically
split each song section's lyrics into slides and assign each slide a **playback timecode**, so that
during backtrack playback the Presenter advances slides automatically — synced to where the words are
actually sung.

The timecodes are computed **once** (on a machine with Whisper/Python) and **saved with the song**.
On any other computer (e.g. the Sunday playback PC) the slides advance from the saved timecodes with
**no Whisper, Python, GPU, or internet required** — the playback machine only needs the song data
(app.db + song.json backup) and the audio stems.

## Goals

1. Split a section's lyrics into slides using a **user-configurable lines-per-slide** setting (2–4),
   exposed in the Presenter settings.
2. Allow a **per-section override** of lines-per-slide when a particular section needs different
   grouping.
3. Allow **manual per-slide adjustment** — both the line grouping (where a slide breaks) and the
   advance timecode — when the auto split/timing lands awkwardly.
4. Auto-advance slides during playback from the saved timecodes (reusing the existing
   `Slide.TimestampMs` → `PresenterEngine.OnPlaybackPositionUpdate` chain).
5. Timecodes travel with the song so playback works on a Whisper-free machine.

## Non-goals (this iteration)

- Karaoke-style per-word highlighting (the `TimedWord` model exists but word-highlight UI is out of
  scope here; only slide-level advance is in scope).
- Changing the section-detection or lyric-assignment logic (covered by the separate VERSE-1-at-vocal
  work). This feature consumes whatever `Section.Text` and vocal word-timestamps already exist.
- Real-time (live-mic) timing capture. Timing is derived from the recorded vocal stem offline.

## Terminology

- **Section** — an existing `Song.Sections[]` entry (INTRO, VERSE 1, …) with `Text` (its lyrics).
- **Slide** — a group of 2–4 lyric lines shown together. Multiple slides per section (typically 3–4).
- **Line** — one line of lyric text.
- **Timecode / advance time** — the playback position (ms) at which a slide should appear.

## Requirements

### R1 — Global lines-per-slide setting
- **R1.1** A `LinesPerSlide` setting (integer, allowed 2–4, default 4) is added to `PresenterSettings`
  and persisted via `PresenterSettingsService` (app.db DataJson; additive/backward-compatible).
- **R1.2** A control in the Presenter Settings window lets the user choose 2, 3, or 4.
- **R1.3** Changing the global value re-chunks sections that do NOT have a per-section override and do
  NOT have manual slide breaks, on the next slide generation.

### R2 — Per-section override
- **R2.1** A `Section` may carry an optional `LinesPerSlide` override (null = use global).
- **R2.2** When set, that section is chunked at the override value instead of the global value.
- **R2.3** The override is additive to the Song model (JSON + SQL DataJson) and backward-compatible
  (missing = null = use global).

### R3 — Manual per-slide adjustment
- **R3.1** The user can manually set where a slide breaks within a section (override the automatic
  N-line grouping for that section), producing an explicit list of slides.
- **R3.2** The user can manually nudge/set a slide's advance timecode.
- **R3.3** Manual edits are marked (reuse `ChartSlide.ManualTiming`) and are NOT overwritten when
  Auto-Detect / re-timing is run again.

### R4 — Auto-timing from vocals (authoring, Whisper required)
- **R4.1** When the vocal word-timestamps are available (Whisper), each generated slide's advance
  time is the **start time of the first sung word of that slide's first line**.
- **R4.2** The computed time is stored in the existing `ChordChartData.ChartSlide.StartMs` (matched by
  `SectionName` + `SlideIndex`), which the existing generator copies into runtime `Slide.TimestampMs`.
- **R4.3** A **lead time** (configurable ms, **default 500**) is subtracted from each slide's advance
  time so the slide appears ~500 ms before the first word is sung (congregation reads ahead). Clamped
  so a slide's time never goes below 0 or before its section start.
- **R4.4** If no word matches a slide's first line confidently, that slide gets no auto time (null);
  it then advances manually or via the section's start. The process never crashes on a bad match.

### R5 — Auto-advance at playback (no Whisper needed)
- **R5.1** During playback, slides advance automatically from the saved `Slide.TimestampMs` via the
  existing `AudioEngine.PositionChanged → MidiPresenterSync → PresenterEngine.OnPlaybackPositionUpdate`
  chain, gated by `PresenterSettings.AutoAdvanceFromTimestamp`.
- **R5.2** No Python/Whisper/GPU is used at playback time — only the stored timecodes.
- **R5.3** The timecodes persist in the song's SQL row (DataJson) and song.json backup, so copying the
  song to another machine carries the slide timing with it.

### R6 — Persistence & portability
- **R6.1** Slide grouping overrides and timecodes are stored on the Song (ChordChart.Slides +
  Section overrides), saved through the existing SQL-authoritative SaveSong path (+ json backup).
- **R6.2** Re-running Auto-Detect preserves manual timings/groupings (R3.3) and re-computes only
  auto ones.

## Acceptance criteria (high level)

- With Whisper available, running the timing step on a song with section lyrics produces slides of the
  configured line count, each with a sensible advance time near where its first line is sung.
- Lowering/raising the global lines-per-slide re-chunks non-overridden sections.
- A per-section override changes only that section's chunking.
- A manual slide break / timecode survives a re-run of Auto-Detect.
- On a second machine with NO Whisper/Python, opening the song and playing it advances the slides at
  the saved times.

## Decisions (confirmed with user)

- **Q1 (lead time):** CONFIRMED — default **500 ms** lead (slides appear 500 ms before the first word).
  Configurable.
- **Q2 (line = ?):** CONFIRMED — a "line" is a newline-separated line in `Section.Text`.
- **Q3 (editing surface):** CONFIRMED — the **Waveform Editor** (WaveformEditorWindow). Slide timing
  is edited here, alongside the vocal waveform. It already hosts the Lyrics & Chords box, Preview
  Chart, and a "Tap Slide Timing" button, so slide markers + timecode nudging live in this window.
