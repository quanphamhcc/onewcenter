# Chord-over-lyric rendering fix for STAGE preview and congregation output

Moves the two-line chord-over-lyric layout out of the two surfaces that already had it (`ChordChartWindow`, web stage) and into a single Core primitive, then wires the WPF STAGE preview and the congregation `OutputWindow` (ShowChords ON) to it. Before this change both surfaces assigned raw inline `[chord]` text to a single proportional `TextBlock`, so musicians saw literal brackets with no column alignment and the preview also ignored live pitch shift. The fix adds `ChordProParserService.BuildChordOverLyric`, which reproduces the canonical `ChordChartWindow.BuildFromInlineChords`/`BuildChordLine` algorithm verbatim, and renders its output as a green monospace chord row above a white/themed monospace lyric row. Transposition is applied to the inline text first, then the layout is built, so pitch shift, capo, and Nashville alignment are preserved.

Watch for:
- The congregation chords-ON lyric row changed from centered proportional text to left-aligned monospace (confirmed). This is a new render path, not a regression of the chords-OFF path, but it is a visible layout shift worth a conscious sign-off.
- A stale generated `OperatorWindow.g.cs` under `obj/Release` still references the removed `NextSlidePreview` (confirmed, build artifact only — no source reference remains).

**Verdict**: APPROVED

## High-level view

The design consolidates one algorithm that previously lived in two places into Core as `BuildChordOverLyric`, returning plain `ChordLyricLine` strings (ChordLine, LyricLine, CarriedOver) with no WPF types so it is unit-testable. The implementation is a line-for-line copy of the reference `BuildFromInlineChords`/`BuildChordLine`: split on `\n`, skip whitespace lines, `ExtractChordPositions` + `StripChords` per line, `char[Math.Max(lyric.Length + 10, 80)]` filled with spaces, each chord at `Math.Clamp(offset, 0, len - chord.Length - 1)`, `TrimEnd`, and last-chord carry-over as a dim `(chord)`. I verified this against the pre-change source and it matches exactly, including that `lastChord` is only updated inside the chorded branch.

The STAGE preview now pulls text through `_engine.GetStageText(current, PitchShiftSemitones)` before building, which both renders chords above lyrics and fixes the incidental bug where the preview ignored live pitch shift. The SectionName header is preserved as a separate green header above the items control, and a standby placeholder covers the null/empty case.

The congregation `OutputWindow` keeps the ShowChords-OFF branch byte-for-byte (StripChords into the themed proportional TextBlock); the ShowChords-ON branch transposes first, then builds the layout into a new monospace `ItemsControl` nested inside the existing `Viewbox` so auto-scaling still applies. The chord host and the text block toggle visibility so only one path shows at a time.

`ChordChartWindow` was refactored to delegate its string building to the Core method while keeping its own TextBlock creation and dim-chord coloring, which removes the duplicated algorithm without changing its visible output. The web `StageDisplayHtml` was correctly left untouched.

<details>
<summary>Issues (2)</summary>

1. **Congregation chords-ON alignment change** — the chords-ON lyric row is left-aligned monospace rather than the centered proportional text used by the chords-OFF path; this is inherent to column-aligned chords but is a visible shift that should be consciously accepted.
2. **Stale generated artifact** — `obj/Release/.../OperatorWindow.g.cs` still names the removed `NextSlidePreview`; it is a build output and clears on rebuild, no source change needed.

</details>

<details>
<summary>Details</summary>

### Core builder reproduces the reference algorithm exactly

I diffed `BuildChordOverLyric` against the pre-change `ChordChartWindow.BuildFromInlineChords`/`BuildChordLine` from `HEAD~1`. The split on `'\n'`, the `string.IsNullOrWhiteSpace` skip, the `ExtractChordPositions` + `StripChords` pair, the `char[Math.Max(lyricText.Length + 10, 80)]` buffer, `Array.Fill(' ')`, the `Math.Clamp(charOffset, 0, lineLength - chord.Length - 1)` clamp, the inner `offset + i < lineLength` write guard, the `TrimEnd()`, and the carry-over `$"({lastChord})"` are identical. The one subtlety that matters — that `lastChord` is assigned only inside the chord-positions loop and not on chordless lines — is preserved, so carry-over behavior matches the reference across multi-line input. The empty/whitespace handling (`continue`) is the same, which is why the empty-input test correctly expects an empty list rather than a blank entry.

### STAGE preview: transpose-then-build via GetStageText

`UpdatePreviews()` now takes the null/engine-null branch to the standby placeholder, otherwise calls `GetStageText(current, _engine.PitchShiftSemitones)` and feeds the result into `BuildChordOverLyric`. `GetStageText` returns the inline text transposed by the live pitch offset (0 means unchanged), so the ordering is transpose-first, build-second as required, and the preview now tracks pitch shift that the old `current.Content.Text` read ignored. SectionName is surfaced through the dedicated `StagePreviewSection` header rather than being prepended into the text stream, which keeps the header out of the monospace alignment. The placeholder also reappears when `stageLines.Count == 0`, covering slides that strip to nothing.

### Congregation OutputWindow: OFF path untouched, ON path monospace

The ShowChords-OFF branch is unchanged in behavior: `StripChords(slideText)` into `SlideAText`/`SlideBText`, chord host collapsed, text block visible — the themed proportional congregation look is preserved. The ShowChords-ON branch transposes the inline text first (`Transpose(slideText, _pitchSemitones)`, or the raw text when the offset is 0) and then builds into the new `SlideAChords`/`SlideBChords` `ItemsControl`. Both hosts live inside the existing `Viewbox`, so the Viewbox scaling that fits slides to the output surface still applies to the chord layout. `ShowSlide` and `SetPitchShift` were both updated to pass the matching chord host for the active/inactive buffer, so buffer swaps and pitch re-renders stay consistent.

One real behavioral change on the chords-ON path: the lyric row is now `FontFamily="Consolas, Courier New"` left-aligned instead of the centered `Segoe UI` of the chords-OFF path. Left alignment is required for the chord columns to line up with syllables, so this is correct for the feature, but a congregation that previously saw centered lyrics with chords inline will now see left-aligned monospace. This is a design consequence of column alignment, not a defect; flagging it so it is an accepted change rather than a surprise.

### ChordChartWindow refactor keeps output, drops duplication

`BuildFromInlineChords` now iterates `parser.BuildChordOverLyric(text)` and uses `line.ChordLine`/`line.LyricLine`/`line.CarriedOver`, keeping its own TextBlock construction and the dim-brush styling for carried-over chords. The branch structure maps the old `chordPositions.Count > 0` / carried-over / collapsed cases onto `CarriedOver` and `!string.IsNullOrEmpty(line.ChordLine)` / else-collapsed, which is behavior-preserving: a chorded line yields a non-empty `ChordLine` and the normal (non-dim) block, a chordless-with-prior line yields `CarriedOver` and the dim block, and a chordless-no-prior line yields an empty `ChordLine` and the collapsed block. Transposition is still applied before the Core call, matching the old order.

### Tests

`ChordOverLyricTests` has five facts covering the behavior that matters: chord lands at the correct stripped-lyric offset (B at column 0, E above "bless"); a chordless line after a chorded one carries over `(B)` with `CarriedOver == true`; transpose-then-build keeps the lyric row and the chord start columns identical while the chord text changes (B→C#, E→F#); empty and whitespace-only input return an empty list; and no-chord text yields empty chord rows with lyrics intact. The transpose test is the strongest one — it asserts column stability independent of chord-text length, which is the core correctness property of the feature. The alignment is exercised through the public API with `IndexOf`/column math rather than hardcoded spacing, so it is robust to incidental buffer-length changes.

Not tested: the WPF rendering itself (ItemsControl binding, Viewbox scaling, visibility toggling between the OFF/ON hosts) is not covered — expected, since the test project compiles Core and Presenter services but not WPF views, and the verification notes this as a manual check. A reviewer relying on this fix should still eyeball the two surfaces once in the running app.

### Evidence review (build/test not re-run)

Per the task constraint I did not re-run the build or suite. The verification file and commit message both report `dotnet build WorshipBacktrackPlayer.sln` with 0 errors and exactly the 4 pre-existing warnings (CS0414 ×2, CS8604), and `dotnet test` at 437 passed / 0 failed (432 pre-existing + 5 new). The two records agree with each other and with the five tests present in the diff, and the warning set is itemized rather than summarized, so the evidence is specific and internally consistent. No articulable doubt remained that would justify a spot-check.

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Core/Services/ChordProParserService.cs` — added `BuildChordOverLyric` + `ChordLyricLine` model (the shared builder).
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` — replaced single `NextSlidePreview` TextBlock with monospace chord/lyric ItemsControl + section header + standby placeholder.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `UpdatePreviews` feeds STAGE preview from `GetStageText` then `BuildChordOverLyric`.
- `WorshipPlayer.Presenter/Views/OutputWindow.xaml` — added hidden monospace chord ItemsControl inside each Viewbox for the chords-ON path.
- `WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs` — `LoadSlideIntoBuffer` takes a chord host; OFF branch unchanged, ON branch transposes then builds.
- `WorshipPlayer.UI/Views/ChordChartWindow.xaml.cs` — delegates string building to the Core method, keeps own TextBlock styling.
- `WorshipPlayer.Tests/ChordOverLyricTests.cs` — 5 new xUnit facts for `BuildChordOverLyric`.

Full diff: `git diff HEAD~1 HEAD`.

</details>
