# Implementation Plan — Chord-above-lyric fix (WPF STAGE preview + congregation output)

Source of truth: `.agents/tasks/chord-above-lyric-bug.md` (root-cause report, parts a–d). This plan
implements that minimal fix. Verified against the report's file:line evidence — chords DO render
inline today (confirmed by reading `OperatorWindow.xaml.cs UpdatePreviews` and
`OutputWindow.xaml.cs LoadSlideIntoBuffer`), the user's report is correct.

## Context discovered during exploration

- Build is green on base. Build the WPF app: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj`
  (net8.0-windows, builds clean except one pre-existing unrelated CS0414 warning in MediaLibraryPanel).
- Tests: xUnit (`xunit` 2.6.6). Test project is `WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`
  (net8.0), references `WorshipPlayer.Core` directly and `Compile Include`s selected Presenter
  *services* (not WPF views). Run: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`.
- Core primitive already present: `ChordProParserService.ExtractChordPositions(string line)` →
  `List<(int charOffset, string chord)>` (offset into stripped lyric), plus `StripChords` and
  `Transpose` (all in `WorshipPlayer.Core/Services/ChordProParserService.cs`).
- Reference algorithm to copy EXACTLY: `WorshipPlayer.UI/Views/ChordChartWindow.xaml.cs`
  `BuildFromInlineChords` + `BuildChordLine` — `char[Math.Max(lyric.Length + 10, 80)]` filled with
  spaces, each chord written at `Math.Clamp(charOffset, 0, len - chord.Length - 1)`, `TrimEnd()`,
  with the last chord carried onto chordless lines as a dim `(chord)`.
- STAGE preview control: single `TextBlock x:Name="NextSlidePreview"` (proportional, `TextWrapping=Wrap`)
  in `WorshipPlayer.Presenter/Views/OperatorWindow.xaml`. Filled by `UpdatePreviews()` at
  `NextSlidePreview.Text = stageText` with raw `current.Content.Text` (ignores pitch shift).
- Congregation controls: `SlideAText`/`SlideBText` single `TextBlock`s each inside a `Viewbox`
  (`WorshipPlayer.Presenter/Views/OutputWindow.xaml`, `FontFamily="Segoe UI"`), filled by
  `LoadSlideIntoBuffer`. `_showChords==false` strips (correct — keep as-is); `_showChords==true`
  assigns raw/transposed inline text (the bug).
- `PresenterEngine.GetStageText(Slide, int)` transposes inline text; `PresenterEngine.PitchShiftSemitones`
  exposes the live pitch shift. Both exist and are referenced by `OperatorWindow` via `_engine`.
- `ChordPosition` model (with `CharOffset`) already lives in `WorshipPlayer.Core/Models/ChordChartData.cs`
  (marked legacy) — the new model should NOT reuse it; add a dedicated `ChordLyricLine`.

## Loop stop contract (unchanged)

This plan does NOT restructure the workflow. The existing implement/review loop will execute it; its
stop condition still reads `.agents/tasks/chord-above-lyric-bug/code-review.json` and stops when
`verdict == APPROVED`. Nothing here changes that contract.

---

# Implementation Plan

- [ ] 1. Add the shared chord-over-lyric model + builder to Core (part a — single source of truth).
      Add a `ChordLyricLine` type (`string ChordLine`, `string LyricLine`, `bool CarriedOver`) and a
      public method `List<ChordLyricLine> BuildChordOverLyric(string text)` to `ChordProParserService`.
      It must replicate `ChordChartWindow.BuildFromInlineChords`/`BuildChordLine` EXACTLY: split `text`
      on `'\n'`; per line call `ExtractChordPositions` + `StripChords`; when the line has chords, fill
      `char[Math.Max(lyric.Length + 10, 80)]` with spaces, write each chord at
      `Math.Clamp(charOffset, 0, len - chord.Length - 1)`, `TrimEnd()` the chord row, set
      `CarriedOver=false`, and track the last chord written; when the line has no chords but a prior
      chord exists, emit `ChordLine = "(" + lastChord + ")"`, `CarriedOver=true`; `LyricLine` is always
      the stripped lyric. Decide the empty/whitespace-line handling to match the reference, which
      `continue`s (skips) blank lines — do the same so alignment and carry-over match. Keep the method
      pure (no WPF types) so Tests can call it.
      Files: `WorshipPlayer.Core/Services/ChordProParserService.cs` (add `BuildChordOverLyric` + nested
      or namespace-level `ChordLyricLine` class).
      Verify: `dotnet build WorshipPlayer.Core/WorshipPlayer.Core.csproj` succeeds. (Tests added in step 2.)

- [ ] 2. Add xUnit tests for `BuildChordOverLyric` (part a verification; required by the report's
      Verification section).
      Create `ChordOverLyricTests` with `private readonly ChordProParserService _parser = new();`
      following the existing `CapoTransposeTests` style. Cover at minimum: (1) a single chord lands
      above the correct syllable — e.g. `"[B]The Lord [E]bless you"` yields a lyric row
      `"The Lord bless you"` with `B` at column 0 and `E` at the column where "bless" starts (assert via
      `ChordLine[offset]` / `IndexOf`); (2) a chordless line after a chorded line produces
      `ChordLine == "(B)"` with `CarriedOver == true`; (3) transpose-then-build keeps alignment — run
      `Transpose(text, 2)` first, then `BuildChordOverLyric`, and assert the lyric row is unchanged and
      the chord column is identical to the untransposed build (chord text differs, column does not);
      (4) empty input yields an empty list.
      Files: `WorshipPlayer.Tests/ChordOverLyricTests.cs`.
      Verify: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj --filter "FullyQualifiedName~ChordOverLyric"`
      — all new tests pass. Also run the full suite once: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`.

- [ ] 3. Replace the STAGE preview control with a monospace chord-over-lyric host (part b, XAML half).
      In `OperatorWindow.xaml`, replace the single `TextBlock x:Name="NextSlidePreview"` inside the STAGE
      `Border`'s `Grid` with an `ItemsControl x:Name="StagePreviewItems"` (keep the same
      `HorizontalAlignment/VerticalAlignment/Margin`), whose `ItemTemplate` is a vertical `StackPanel`
      containing a GREEN monospace chord `TextBlock` (`FontFamily="Consolas, Courier New"`, bound to
      `ChordLine`) above a WHITE monospace lyric `TextBlock` (same font family, bound to `LyricLine`).
      Keep the "STAGE" label and the `StageConnectedIndicator` siblings untouched. The SectionName header
      is handled in code-behind (step 4) — either as the first item or a separate `TextBlock` above the
      `ItemsControl`; pick one and note it. Removing `NextSlidePreview` means any other reference to it
      must be updated — grep and fix (see step 4).
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml`.
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj` — succeeds once the
      code-behind (step 4) is updated; expect a transient build break until step 4 lands, so build after step 4.

- [ ] 4. Feed the STAGE preview from `GetStageText` + `BuildChordOverLyric` (part b + part d, code-behind).
      In `OperatorWindow.xaml.cs` `UpdatePreviews()`, replace the block that builds `stageText` from
      `current.Content.Text` and assigns `NextSlidePreview.Text`. New behavior: if `current` is null show
      the standby placeholder; otherwise get `string stageText = _engine.GetStageText(current, _engine.PitchShiftSemitones);`
      (this applies transposition + live pitch shift — fixes the incidental pitch-shift bug, part d order:
      transpose first), then `var lines = _previewChordParser.BuildChordOverLyric(stageText);` and set
      `StagePreviewItems.ItemsSource = lines;`. Render the `SectionName` header above the list (per the
      choice made in step 3), preserving today's behavior of showing the section name above the text.
      Grep the file for any remaining `NextSlidePreview` references and remove/retarget them.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`.
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj` succeeds with no new
      warnings/errors; `grep` for `NextSlidePreview` returns no matches in the Presenter project.

- [ ] 5. Add a monospace chord-over-lyric render path to the congregation OutputWindow (part c + part d).
      In `OutputWindow.xaml`, add to each slide buffer (`SlideA`, `SlideB`) a hidden monospace host for
      the chord path — e.g. an `ItemsControl x:Name="SlideAChords"`/`SlideBChords` inside the existing
      `Viewbox` (so the Viewbox auto-scaling is preserved), `Collapsed` by default, with the same green
      chord-row / themed-lyric-row `ItemTemplate` (chord row monospace `Consolas, Courier New`; lyric row
      uses the themed font so the chords-OFF look is untouched). Keep the existing `SlideAText`/`SlideBText`
      TextBlocks for the lyric-only (chords-OFF) path exactly as they are.
      In `OutputWindow.xaml.cs` `LoadSlideIntoBuffer(...)`, add the matching chord-host params and:
      - KEEP the `_showChords == false` branch EXACTLY as-is (`StripChords` → `textBlock.Text`), make the
        chord host `Collapsed` and the `textBlock` `Visible`.
      - For `_showChords == true`: after the existing `Transpose(slideText, _pitchSemitones)` step (part d
        order), call `_chordParser.BuildChordOverLyric(transposed)` and bind the result to the chord host
        (`ItemsSource`), set the chord host `Visible` and `textBlock` `Collapsed`. Apply the monospace font
        to the chord row and the themed font/color to the lyric row.
      Wire the two new hosts through `ShowSlide` and `SetPitchShift` (both call `LoadSlideIntoBuffer` with
      the A or B triple) so pitch re-render and buffer-swap keep working. Preserve header/footer handling.
      Files: `WorshipPlayer.Presenter/Views/OutputWindow.xaml`, `WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs`.
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj` succeeds; `dotnet test
      WorshipPlayer.Tests/WorshipPlayer.Tests.csproj` still passes (PresenterEngine is compiled into Tests;
      ensure no signature it exercises changed).

- [ ] 6. Optional consistency follow-up (do ONLY if low-risk and the build stays green): refactor
      `ChordChartWindow.BuildFromInlineChords` to delegate to the new Core `BuildChordOverLyric` so all
      surfaces share one definition. The Core builder returns strings only, so `BuildFromInlineChords`
      keeps its own `TextBlock` creation/coloring (incl. the dim `(chord)` styling via `CarriedOver`) but
      sources `ChordLine`/`LyricLine` from Core instead of its inline char-array loop. If this changes any
      visible output or risks regressions, SKIP it and leave a one-line note in the PR/commit body instead.
      Do NOT touch the web `StageDisplayHtml.formatTextWithChords` (already correct).
      Files (if done): `WorshipPlayer.UI/Views/ChordChartWindow.xaml.cs`.
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj` (or the solution) succeeds and
      `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj` passes; if skipped, no verification needed.

- [ ] 7. Final verification + commit (do NOT push).
      Build the solution (`dotnet build WorshipBacktrackPlayer.sln`) and run the full test suite
      (`dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`) — both green. Confirm manually (or note
      as a manual check) that the STAGE preview and the congregation output with ShowChords ON now render a
      green monospace chord line ABOVE the lyric line (no inline `[..]`), and that the congregation
      ShowChords-OFF path looks unchanged. Stage only the files touched above and commit with a message
      describing the chord-over-lyric fix; do not push.
      Files: none (git).
      Verify: `dotnet build WorshipBacktrackPlayer.sln` and `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`
      both succeed; `git log -1 --stat` shows the commit with only the intended files.

## Notes / assumptions

- The report says the dim carried-over chord renders as `(chord)`; `ChordLyricLine.CarriedOver` carries
  that flag so each WPF surface can apply the dim style while sharing one string builder in Core.
- Steps 3 and 4 together swap the STAGE control; the project will not build between them, so build/verify
  after step 4 (noted inline). This keeps the control replacement as one logical change across XAML + code-behind.
- No AGENTS.md / CONTRIBUTING / `.kiro/steering` files exist in the repo; build/test commands above were
  confirmed by actually running them on the base branch (both green).
