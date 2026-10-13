# Verification — Chord-above-lyric fix

Iteration: FIRST (no `code-review.json` existed when this ran).

## What was implemented (report parts a–d)

- **(a) Shared Core builder.** Added `ChordProParserService.BuildChordOverLyric(string text)` and a
  `ChordLyricLine` model (`ChordLine`, `LyricLine`, `CarriedOver`) in
  `WorshipPlayer.Core/Services/ChordProParserService.cs`. It replicates
  `ChordChartWindow.BuildFromInlineChords`/`BuildChordLine` exactly: split on `\n`; skip
  blank/whitespace lines (matches the reference `continue`); per line call `ExtractChordPositions` +
  `StripChords`; fill `char[Math.Max(lyric.Length + 10, 80)]` with spaces; write each chord at
  `Math.Clamp(offset, 0, len - chord.Length - 1)`; `TrimEnd()`; carry the last chord onto chordless
  lines as a dim `(chord)` with `CarriedOver = true`. Pure (no WPF types).
- **(b) Operator STAGE preview.** `OperatorWindow.xaml` — replaced the single proportional
  `NextSlidePreview` `TextBlock` with a monospace host: an `ItemsControl x:Name="StagePreviewItems"`
  whose item template is a green monospace chord row (`Consolas, Courier New`) above a white monospace
  lyric row, plus a `StagePreviewSection` header TextBlock and a `StagePreviewPlaceholder` for standby.
  `OperatorWindow.xaml.cs UpdatePreviews()` now calls
  `_engine.GetStageText(current, _engine.PitchShiftSemitones)` (applies transpose + live pitch shift —
  fixes the pitch-shift-ignored bug), then `BuildChordOverLyric`, and binds the result.
- **(c) Congregation OutputWindow.** `OutputWindow.xaml` — added a hidden
  `ItemsControl` (`SlideAChords`/`SlideBChords`) inside each existing `Viewbox` (so Viewbox
  auto-scaling is preserved), green monospace chord row above a white bold lyric row.
  `OutputWindow.xaml.cs LoadSlideIntoBuffer` now takes the chord host; the `ShowChords == false`
  branch is unchanged (StripChords → themed proportional `TextBlock`, chord host collapsed); the
  `ShowChords == true` branch transposes first, then `BuildChordOverLyric`, binds the host, shows the
  host and collapses the text block. `ShowSlide` and `SetPitchShift` pass the matching host.
- **(d) Transposition order.** Transpose/pitch-shift happens on the inline `[chord]` text first
  (`GetStageText` / `Transpose`), then `BuildChordOverLyric` — in both WPF surfaces.
- **Optional consistency follow-up (done, low-risk).** `ChordChartWindow.BuildFromInlineChords` now
  delegates to the Core `BuildChordOverLyric` for its strings while keeping its own TextBlock
  creation/coloring (dim `(chord)` via `CarriedOver`, collapsed empty chord rows). Visible output
  unchanged. The web `StageDisplayHtml` was not touched.

## Tests added

`WorshipPlayer.Tests/ChordOverLyricTests.cs` (5 facts):
1. Chord sits above the correct char offset (`B` at col 0, `E` above "bless").
2. Chordless line carries over the dim `(B)` with `CarriedOver == true`.
3. Transpose(+2)-then-build keeps lyric row + chord columns identical (chord text changes B→C#, E→F#).
4. Empty / whitespace-only input returns an empty list.
5. No-chord text degrades gracefully (empty chord rows, lyrics intact).

## Commands run and results

- Core-only build (during development):
  `dotnet build WorshipPlayer.Core/WorshipPlayer.Core.csproj --nologo` → Build succeeded, 0 errors.
- New tests only (during development):
  `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj --filter "FullyQualifiedName~ChordOverLyric"`
  → total 5, failed 0, succeeded 5.
- Full solution build:
  `dotnet build WorshipBacktrackPlayer.sln --nologo` → **Build succeeded, 0 errors, 4 warnings** —
  exactly the 4 pre-existing warnings, no new ones:
  - CS0414 `MediaLibraryPanel._currentView`
  - CS8604 `ChordChartWindow.BuildChordLine` (`lyricText`)
  - CS0414 `WaveformEditorWindow._inReviewMode`
  (CS8604 + the two CS0414 are the documented pre-existing set.)
- Full test suite:
  `dotnet test WorshipPlayer.Tests --nologo` → **total: 437, failed: 0, succeeded: 437, skipped: 0**
  (432 pre-existing + 5 new). The console line `Error loading service order: 't' is an invalid start
  of a property name` is expected log output from a pre-existing negative/parse test, not a failure.

## Manual check (not automated — WPF UI)

Not executed in this headless environment. Expected behavior after the change: STAGE preview and the
congregation output with ShowChords ON render a green monospace chord line ABOVE the lyric line (no
inline `[..]`); the congregation ShowChords-OFF path is visually unchanged (StripChords + themed font).
