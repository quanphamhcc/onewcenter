# Auto-Timed Slides — Design

## Guiding principle: EXTEND the existing machinery, don't replace it

The investigation found most of this feature already exists. The design reuses it:

| Concern | Already exists | What we add |
|---|---|---|
| Per-slide advance time | `ChartSlide.StartMs` / runtime `Slide.TimestampMs` | fill from Whisper + lead |
| Auto-advance at playback | `PresenterEngine.OnPlaybackPositionUpdate` + `AudioEngine.PositionChanged` wiring | nothing (works) |
| Grouping lyrics → slides | blank-line split in `RecalculateSlideTimings` / `ExtractSlideBlocks` | add N-lines-per-slide chunking |
| Manual timing | **Tap Slide Timing** (SPACE taps) + `ChartSlide.ManualTiming` | keep as-is |
| Settings storage | `PresenterSettings` (SQL DataJson) | add `LinesPerSlide`, `SlideLeadMs` |
| Song persistence | SQL-authoritative SaveSong + json backup | slide groupings/times ride along |

## Data model changes (additive, backward-compatible)

### PresenterSettings (`WorshipPlayer.Core/Models/PresenterSettings.cs`)
- `int LinesPerSlide` (JSON `linesPerSlide`, default 4, clamped 2–4) — global grouping default.
- `int SlideLeadMs` (JSON `slideLeadMs`, default 500) — lead time subtracted from each auto time.

(Both stored in the existing `PresenterSettings` JSON row; missing → default.)

### Section (`WorshipPlayer.Core/Models/Song.cs`)
- `int? LinesPerSlideOverride` (JSON `linesPerSlideOverride`, default null) — per-section override of
  the global `LinesPerSlide`. Null = use global.

### ChartSlide (`WorshipPlayer.Core/Models/ChordChartData.cs`) — already sufficient
- Keep `SectionName`, `SlideIndex`, `StartMs`, `EndMs`, `ManualTiming`. Auto-timing writes `StartMs`
  with `ManualTiming=false`; tap/manual edits set `ManualTiming=true` (already the case).
- No schema change needed.

## New pure service: SlideGroupingService (`WorshipPlayer.Core/Services`)

Pure, unit-testable (no I/O, no audio). Responsible for turning a section's lyric text into slide
line-blocks per the configured rules.

```
record SlideBlock(int SlideIndex, IReadOnlyList<string> Lines, string Text);

List<SlideBlock> GroupSection(string sectionText, int linesPerSlide, bool honorBlankLineBreaks)
```

Rules:
1. Split `sectionText` into lines (newline = line, per confirmed Q2).
2. If the text contains **explicit blank-line breaks**, honor them as hard slide boundaries
   (preserves the existing authoring style), then within each block, if a block exceeds
   `linesPerSlide`, sub-split it into N-line slides.
3. If there are **no blank-line breaks**, chunk the whole section into groups of `linesPerSlide`
   lines.
4. Trailing remainder (e.g. 5 lines at 2/slide → 2,2,1) forms a final short slide.
5. Empty/whitespace lines between content are not counted as lyric lines.

This is the single source of grouping truth; `RecalculateSlideTimings` and the preview call it so
grouping is consistent everywhere.

## New service: SlideAutoTimingService (`WorshipPlayer.Core/Services`)

Pure where possible. Given a section's slide blocks and the Whisper word timestamps
(`Song.ChordChart` TimedWords / a words list), compute each slide's `StartMs`:

```
double? ComputeSlideStartMs(SlideBlock block, IReadOnlyList<TimedWord> words,
                            double sectionStartMs, double sectionEndMs,
                            int leadMs)
```

Algorithm (R4):
1. Take the slide's **first line**, extract its first 1–3 significant words (strip `[chords]`,
   punctuation, lowercase).
2. Find the earliest `TimedWord` within `[sectionStartMs, sectionEndMs]` whose word matches the
   slide's first significant word (exact, then prefix/substring for ≥3-char words).
3. Candidate time = matched word `StartMs` − `leadMs`.
4. Clamp: not < `sectionStartMs` (a slide can't start before its section) and not < 0.
5. Enforce monotonic non-decreasing times across a section's slides (a later slide can't be earlier
   than an earlier slide).
6. No confident match → return null (slide keeps section-start / manual time; never crash — R4.4).

Where the Whisper words come from: the Python analysis already produces word timestamps; they are
carried on `Song.ChordChart` (TimedWord via ChartLine.Words) when available. If words are absent
(no Whisper on this machine), auto-timing is skipped and the existing even-split / tap path is used.

## Integration points (WaveformEditorWindow)

Editing lives in the Waveform Editor (confirmed Q3). Changes:

1. **Grouping uses the setting:** `RecalculateSlideTimings` calls `SlideGroupingService.GroupSection`
   with `section.LinesPerSlideOverride ?? settings.LinesPerSlide` instead of only blank-line split.
2. **Auto-time button:** add an "Auto-Time Slides (vocal)" action that runs
   `SlideAutoTimingService` over the section's blocks using the song's Whisper words and writes
   `ChartSlide.StartMs` (ManualTiming=false, lead applied). Only overwrites non-manual slides (R3.3).
3. **Tap Slide Timing:** unchanged — remains the manual authoring path; sets `ManualTiming=true`.
4. **Per-section lines control:** a small numeric input (2–4, blank=global) bound to
   `section.LinesPerSlideOverride`, re-grouping on change.
5. The existing **Preview Chart** auto-advance already reflects `StartMs`, so previewing validates
   both auto and manual timing with no extra work.

## Settings UI (Presenter)

Add to the Presenter Settings window (`WorshipPlayer.Presenter/Views/Settings`):
- "Lines per slide" selector (2/3/4), bound to `PresenterSettings.LinesPerSlide`.
- "Slide lead time (ms)" numeric (default 500), bound to `PresenterSettings.SlideLeadMs`.
Saved through `PresenterSettingsService` (existing SQL JSON row).

## Playback (no change, Whisper-free)

At play time the existing chain advances slides by `Slide.TimestampMs`
(`AudioEngine.PositionChanged → MidiPresenterSync → PresenterEngine.OnPlaybackPositionUpdate`), gated
by `AutoAdvanceFromTimestamp`. Because `StartMs` persists in the song (SQL + json), a Whisper-free
machine plays the timed slides correctly (R5). The lead time is already baked into the stored
`StartMs`, so playback needs no lead logic.

## Persistence & portability (R6)

- `LinesPerSlideOverride` and `ChartSlide.StartMs` live on the Song → saved by the SQL-authoritative
  `SaveSongAsync` (+ song.json backup). They travel with the song.
- `LinesPerSlide` / `SlideLeadMs` live in `PresenterSettings` (per install). A copied song keeps its
  per-slide `StartMs` regardless of the target machine's global setting, so timing is portable.

## Testing strategy

- **SlideGroupingService** unit tests: N-lines chunking (2/3/4), blank-line breaks honored, oversize
  block sub-split, remainder slide, empty lines ignored, per-section override vs global.
- **SlideAutoTimingService** unit tests: first-word match → StartMs−lead; clamp to section start;
  monotonic ordering; no-match → null; lead never produces negative/pre-section times.
- **Round-trip**: a section with override + manual slide persists through SaveSong and reloads from
  the DB (extends the existing SQL-authoritative song tests).
- Keep all 786 existing tests green.

## Build order (incremental, each step builds + tests green)

1. Models: `PresenterSettings.LinesPerSlide`/`SlideLeadMs`, `Section.LinesPerSlideOverride` (+ tests).
2. `SlideGroupingService` + tests. Wire `RecalculateSlideTimings` to use it.
3. `SlideAutoTimingService` + tests. Add "Auto-Time Slides" action in the Waveform Editor.
4. Per-section lines control in the Waveform Editor.
5. Settings UI (lines-per-slide, lead ms).
6. End-to-end verify in the app on a real song; confirm playback advance; confirm Whisper-free
   playback by checking stored StartMs values.

## Out of scope (restated)
- Per-word karaoke highlight UI; section-detection changes; live-mic timing.
