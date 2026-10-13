# Chord-above-lyric bug — WPF STAGE preview & congregation output

## Summary (answer first)

The WPF STAGE preview and the congregation OutputWindow render slide text as a
**single plain `TextBlock` with the raw inline `[chord]` string assigned directly to
`TextBlock.Text`**. Nothing on the WPF side converts inline `[chord]` tokens into a
two-line chord-over-lyric layout. The only correct implementations live in
(a) the web stage display JS (`StageDisplayHtml.formatTextWithChords`) and
(b) the old backtrack UI (`ChordChartWindow.BuildFromInlineChords` / `BuildChordLine`).
Both are driven by the same Core primitive, `ChordProParserService.ExtractChordPositions`,
which already returns each chord's character offset in the stripped lyric. The WPF
presenter surfaces simply never call it.

**Root cause:** the STAGE preview assigns `current.Content.Text` verbatim
(`OperatorWindow.xaml.cs:819`), and the OutputWindow assigns the raw/transposed text
verbatim (`OutputWindow.xaml.cs` `LoadSlideIntoBuffer`) — in both cases the `[..]`
brackets stay inline and the font is proportional (`Segoe UI`), so even if brackets were
stripped there is no column alignment.

**Minimal fix:** add one shared chord-over-lyric builder in Core (reusing
`ExtractChordPositions` + the exact char-array spacing from `ChordChartWindow.BuildChordLine`),
and have the Operator STAGE preview and OutputWindow render its output into a monospace
two-line-per-lyric-line layout. Transposition stays where it already is (apply
`Transpose`/capo/Nashville to the inline text first, then build the chord-over-lyric view),
so pitch shift keeps working unchanged.

---

## Evidence

### 1. Operator STAGE preview — exact control + value (still has `[..]` inline)

- XAML: the STAGE panel's text control is a single `TextBlock x:Name="NextSlidePreview"`
  with a proportional font and `TextWrapping="Wrap"` — not monospace.
  `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` (Stage Preview border, `NextSlidePreview`).
- Code-behind that fills it, `OperatorWindow.xaml.cs` `UpdatePreviews()`:
  ```csharp
  // STAGE preview: Show what MUSICIANS actually see (current slide WITH chords, always)
  string stageText = current?.Content.Text ?? "(standby)";      // line 819 — RAW, has [chords]
  if (!string.IsNullOrEmpty(current?.SectionName))
      stageText = $"{current.SectionName}\n{stageText}";
  NextSlidePreview.Text = stageText;                            // line 824
  ```
  `current.Content.Text` is the stored inline-`[chord]` string, so the preview literally
  shows `[B] The Lord bless you[E] ...`. (The AUDIENCE preview just above calls
  `_previewChordParser.StripChords(...)` when `CongregationShowChords` is off, which is why
  the audience box looks clean while STAGE shows brackets.)

### 2. Congregation OutputWindow — same raw assignment

- `WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs` `LoadSlideIntoBuffer(...)`:
  ```csharp
  if (_showChords)
      displayText = _pitchSemitones == 0 ? slideText
                                         : _chordParser.Transpose(slideText, _pitchSemitones);
  else
      displayText = _chordParser.StripChords(slideText);
  textBlock.Text = displayText;   // single TextBlock, inline [chords] when _showChords
  ```
  When `ShowChords` is off it strips correctly (current/intended congregation behavior).
  When `ShowChords` is on, chords are shown **inline**, not chord-over-lyric.
- The slide `TextBlock`s (`SlideAText`/`SlideBText` in `OutputWindow.xaml`) use
  `FontFamily="Segoe UI"` + bold inside a `Viewbox` — proportional, so there is no basis
  for column alignment even if parsed.

### 3. The engine produces inline text, by design

- `PresenterEngine.GetStageText(slide, transposeSemitones)` returns
  `slide.Content.Text` (or `_chordParser.Transpose(text, n)`) — i.e. inline `[chord]` text,
  transposition only. `GetCongregationText` returns `StripChords(...)`. Neither produces a
  two-line layout. (`PresenterEngine.cs`, Dual Rendering Pipeline region.)
- Note: the STAGE preview in `UpdatePreviews()` does **not** even call `GetStageText`; it
  reads `current.Content.Text` directly, so it also ignores the live pitch shift.

### 4. The correct algorithm already exists twice — both reuse one Core primitive

- Core primitive (the shared truth):
  `ChordProParserService.ExtractChordPositions(string line)` →
  `List<(int charOffset, string chord)>` where `charOffset` is the position **in the
  stripped lyric**. Paired with `StripChords(line)`.
  (`WorshipPlayer.Core/Services/ChordProParserService.cs`, `ExtractChordPositions`.)
- Reference WPF implementation (canonical): `ChordChartWindow.BuildFromInlineChords(text)`
  and `BuildChordLine(...)` in `WorshipPlayer.UI/Views/ChordChartWindow.xaml.cs`:
  per line it calls `ExtractChordPositions` + `StripChords`, fills a
  `char[Math.Max(lyric.Length+10, 80)]` with spaces, writes each chord at its clamped
  offset, `TrimEnd()`s it, and stacks a green monospace chord `TextBlock` **above** a white
  monospace lyric `TextBlock` (both `Consolas, Courier New, monospace`). It also carries the
  last chord over to chordless lines as a dim `(chord)`.
- Web implementation (already shipped, must stay consistent):
  `StageDisplayHtml.formatTextWithChords` does the identical char-array algorithm in JS and
  its own comment says it replicates `ChordChartWindow.BuildFromInlineChords`. Renders
  `.chord-row` (monospace, `white-space: pre`) above `.lyric-row`.

### 5. No shared WPF two-line builder today

- `MusicianViewRenderer` only transforms chord *notation* (pitch/capo/Nashville via
  `Transpose` / `CapoTranspose` / `ConvertToNashvilleNumbers`) and still returns inline
  `[chord]` text. It does not build chord-over-lyric. So the alignment logic is duplicated
  only in `ChordChartWindow` (WPF) and `StageDisplayHtml` (JS); the presenter STAGE preview
  and OutputWindow have no equivalent.

---

## Conclusions

1. **Render path, STAGE preview:** `PresenterEngine.SlideChanged` → `OperatorWindow.UpdatePreviews()`
   → `NextSlidePreview.Text = current.Content.Text` (inline `[chord]`, proportional font). Bug.
2. **Render path, congregation:** `PresenterEngine.SlideChanged` → `OutputWindow.ShowSlide` →
   `LoadSlideIntoBuffer` → `SlideAText/SlideBText.Text = raw-or-transposed text`. Inline when
   `ShowChords` is on; correctly stripped when off.
3. The chord-over-lyric algorithm is **already specified** in Core-adjacent code
   (`ExtractChordPositions`) and implemented in `ChordChartWindow` + the web JS. The WPF
   presenter simply does not reuse it.

## Recommended minimal fix

**(a) Add one shared builder in Core (single source of truth).**
Add a method to `WorshipPlayer.Core/Services/ChordProParserService.cs`, e.g.
`BuildChordOverLyric(string text)` returning a small model such as
`List<ChordLyricLine>` where each line has `string ChordLine` and `string LyricLine`
(and optionally a `bool CarriedOver` flag for the dim `(chord)` case). Internally it must
do **exactly** what `ChordChartWindow.BuildFromInlineChords`/`BuildChordLine` do:
split on `\n`, per line call `ExtractChordPositions` + `StripChords`, fill a
`char[Math.Max(lyric.Length + 10, 80)]`, write each chord at `Math.Clamp(offset, 0,
len - chord.Length - 1)`, `TrimEnd()`. Putting it in Core keeps it unit-testable (the
`WorshipPlayer.Tests` project already references Core and compiles Presenter *services*,
not WPF views) and lets the web, ChordChartWindow, and the presenter all converge on it.

**(b) Operator STAGE preview** (`OperatorWindow.xaml.cs` `UpdatePreviews`): replace the
single `NextSlidePreview` `TextBlock` with a monospace host (e.g. an `ItemsControl` /
`StackPanel`) and, for the stage text, call `GetStageText(current, _engine.PitchShiftSemitones)`
first (so transposition + live pitch shift apply), then feed the result to
`BuildChordOverLyric` and render each `ChordLyricLine` as a green monospace chord row above a
white monospace lyric row. Using `GetStageText` here also fixes the incidental bug that the
preview currently ignores pitch shift.

**(c) Congregation OutputWindow** (`OutputWindow.xaml.cs` `LoadSlideIntoBuffer` +
`OutputWindow.xaml`): keep the `ShowChords == false` branch exactly as-is (strip chords —
unchanged congregation behavior). For the `ShowChords == true` branch, after the existing
`Transpose(slideText, _pitchSemitones)` step, pass the transposed inline text through
`BuildChordOverLyric` and render it into the slide buffer as monospace chord-row/lyric-row
pairs instead of assigning the raw string to a single `TextBlock`. This requires the slide
content area to use a monospace font for the chord path (chord-over-lyric alignment is only
correct in a fixed-width font); the lyric-only (chords-off) path can keep the themed
proportional font.

**(d) Transposition correctness.** Order is: transpose/capo/Nashville on the inline
`[chord]` text FIRST (existing `GetStageText` / `MusicianViewRenderer` / `Transpose`), THEN
`BuildChordOverLyric`. Because `ExtractChordPositions` keys off the stripped-lyric offset
and transposition does not change lyric characters, chord alignment is preserved and pitch
shift / capo / Nashville all continue to work. This is the same ordering the web path and
`ChordChartWindow.BuildFromInlineChords` already use (transpose, then build).

**Consistency note / optional follow-up:** once `BuildChordOverLyric` exists in Core,
`ChordChartWindow.BuildFromInlineChords` can be refactored to call it, and the web
`formatTextWithChords` continues to mirror the same algorithm — giving one canonical
chord-over-lyric definition across all four surfaces (web stage, ChordChartWindow, Operator
STAGE preview, congregation output).

### Suggested verification
- Add xUnit tests in `WorshipPlayer.Tests` for `ChordProParserService.BuildChordOverLyric`
  (chord above correct syllable; carried-over chord on chordless line; transpose-then-build
  keeps alignment), then run `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`.
- Build the WPF app (`dotnet build WorshipPlayer.Presenter`) and manually confirm the STAGE
  preview and (with ShowChords on) the output show chords on their own line above the lyric.
