# Output / Audience chord bug — root-cause investigation (READ-ONLY)

Repo: `owc-multitrack-feature-lyric-presenter` · branch `main` · HEAD `0b7a68b`
Scope: why the real congregation OutputWindow shows no chords when CHORDS is ON (Problem 1), and why the operator AUDIENCE preview shows raw inline `[chord]` text (Problem 2).

## Summary answer (read this first)

- **Problem 1 (no chords on the real projector output):** The OutputWindow render path is correct, but the live OutputWindow instance has `_showChords == false` at render time. The chord state lives in `PresenterSettings.CongregationShowChords` and is only pushed into an OutputWindow via `SetChordDisplay(...)` inside `ToggleChords_Click`. On GO LIVE (and on display reconfigure) the output window is **lazily created fresh** in `PresenterIntegration` with `_showChords` defaulting to `false`, and the current `CongregationShowChords` value is **never re-applied** to the new instance. The user's flow is exactly this: single-monitor, press GO LIVE → a new `OutputWindow` is built → chords default OFF → lyrics render via `StripChords`, no chords. The toggle only affects the output if it is pressed *after* the window already exists, and that effect is lost whenever the window is recreated.
- **Problem 2 (AUDIENCE preview shows `[B]...[E]...` brackets):** `UpdatePreviews()` strips chords for the AUDIENCE box **only when `CongregationShowChords` is false**. When the toggle is ON it assigns the raw `slide.Content.Text` (which still contains inline `[chord]` tokens) straight into the plain `CurrentSlidePreview` TextBlock — so you see literal brackets. It never calls `BuildChordOverLyric` for the audience box (unlike the STAGE box, which always does).
- **Design flag:** There is a genuine ambiguity. `CongregationShowChords` is a single flag that drives BOTH the congregation projector output AND the AUDIENCE preview. STAGE always shows chords independently. The orchestrator must decide whether the audience/congregation surface should ever show chords (worship convention = audience lyrics only, stage = chords) before the fix is finalized. See "Design clarification".

---

## Evidence

### A. The OutputWindow render path is correct in isolation
`WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs` `LoadSlideIntoBuffer(...)`:
- When `_showChords` is true it transposes, then `chordHost.ItemsSource = _chordParser.BuildChordOverLyric(transposed)`, sets `chordHost.Visibility = Visible` and hides the lyric `textBlock`.
- When `_showChords` is false it sets `chordHost` null+Collapsed, shows `textBlock`, and `textBlock.Text = _chordParser.StripChords(slideText)`.

The XAML chord host (`SlideAChords` / `SlideBChords` in `OutputWindow.xaml`) is a real `ItemsControl` inside the Viewbox with a chord-row-over-lyric-row DataTemplate. It is `Visibility="Collapsed"` by default and only revealed by the `_showChords==true` branch. **So the host is wired correctly; it is simply never reached because `_showChords` is false.**

### B. Where `_showChords` comes from — and the gap
`OutputWindow._showChords` defaults to `false` (field init, `OutputWindow.xaml.cs`). It is set only via:
- `public bool ShowChords { set; }` and
- `public void SetChordDisplay(bool show)` (`OutputWindow.xaml.cs:135`).

The only caller that pushes the live toggle is `OperatorWindow.ToggleChords_Click` (`OperatorWindow.xaml.cs:436`):
```
settings.CongregationShowChords = !settings.CongregationShowChords;
_outputWindow?.SetChordDisplay(settings.CongregationShowChords);
...
_outputWindow.ShowSlide(currentSlide, SlideTransition.Cut, 0);  // re-render
```
This works **only if `_outputWindow` already exists and is the same instance that stays live.**

The output window is created in `WorshipPlayer.UI/Services/PresenterIntegration.cs` in two places, **neither of which applies `CongregationShowChords`:**
- `ApplyDisplaySettings` (`PresenterIntegration.cs:438`): `_outputWindow = new OutputWindow();` then `PlaceOnDisplay` / `Show`.
- `HandleGoLiveSingleMonitor` (`PresenterIntegration.cs:486`): `_outputWindow = new OutputWindow();` then `GoFullScreen(topmost:false)`.

Both then call `_operatorWindow?.SetOutputWindow(_outputWindow)` — but `OperatorWindow.SetOutputWindow` (`OperatorWindow.xaml.cs:222`) just stores the reference:
```
public void SetOutputWindow(OutputWindow? outputWindow) { _outputWindow = outputWindow; }
```
No `SetChordDisplay`, no pitch re-sync. A freshly created `OutputWindow` therefore always starts with chords OFF.

`PresenterSettings.CongregationShowChords` (`WorshipPlayer.Core/Models/PresenterSettings.cs:103`) defaults to `false` and is the authoritative stored state; nothing seeds a new OutputWindow from it.

**Net:** the single-monitor GO LIVE path (the user's scenario) builds the output window with chords off every time, and even on dual-monitor, if the window is (re)created after the toggle was set, the state is lost. This is Problem 1's root cause: `_showChords` is `false` at render, so `LoadSlideIntoBuffer` runs the `StripChords` branch and no chord host is shown.

### C. AUDIENCE preview assigns raw text — Problem 2
`OperatorWindow.UpdatePreviews()` (`OperatorWindow.xaml.cs:802`):
```
string audienceText = current?.Content.Text ?? "(blank)";
if (_engine != null && !_engine.Settings.CongregationShowChords)
    audienceText = _previewChordParser.StripChords(audienceText);
CurrentSlidePreview.Text = audienceText;
```
When `CongregationShowChords` is **ON**, the `if` is skipped, so `audienceText` keeps the raw ChordPro inline tokens (`[B] The Lord bless you[E]...`). `CurrentSlidePreview` is a plain `TextBlock` (`OperatorWindow.xaml:37`) with no chord parsing, so the brackets render literally. That is exactly the screenshot.

Contrast the STAGE preview in the same method (`OperatorWindow.xaml.cs:830`), which always does `BuildChordOverLyric(GetStageText(...))` into `StagePreviewItems` and therefore renders chord-over-lyric correctly. The AUDIENCE box has no equivalent chord-over-lyric branch — its only two states are "stripped" (toggle off) or "raw with brackets" (toggle on).

### D. StripChords / parser confirmation
`ChordProParserService.StripChords` (`ChordProParserService.cs:604`) removes `[...]` tokens via `InlineChordRegex`. `BuildChordOverLyric` (`ChordProParserService.cs:930`) returns `List<ChordLyricLine>` (chord row + lyric row) consumed by the ItemsControls in both the STAGE preview and the OutputWindow chord host.

### E. There is also a dead/parallel toggle
`OperatorWindow.ChordToggle_Changed` (`OperatorWindow.xaml.cs:620`) reads `ChordToggleCheckBox.IsChecked` and calls `_outputWindow?.SetChordDisplay(...)` but does **not** update `settings.CongregationShowChords` or `UpdatePreviews()`. If the checkbox in the screenshot is wired to this handler rather than `ToggleChords_Click`, the stored setting and the previews never change, and the state is still lost on window recreation. The orchestrator/fixer should confirm which control the visible "CHORDS" checkbox is bound to and consolidate onto one source of truth.

---

## Key questions answered

1. **Value of `_showChords` at render when CHORDS checkbox is ON / what text is produced.** For the user's single-monitor GO LIVE, `_showChords` is `false` (new OutputWindow, never seeded from `CongregationShowChords`), so `LoadSlideIntoBuffer` runs the else branch: `textBlock.Text = StripChords(slideText)` and the chord host stays Collapsed → lyrics only, no chords. (`OutputWindow.xaml.cs` LoadSlideIntoBuffer; `PresenterIntegration.cs:486`.)
2. **Is the chord host in the visual tree / visible?** Yes, it is in the tree (`SlideAChords`/`SlideBChords`, `OutputWindow.xaml`). It is `Collapsed` by default and only shown by the `_showChords==true` branch, which is never reached. Not a layout/zero-size/visibility-bug in the host itself.
3. **What does the AUDIENCE preview assign and why brackets?** `CurrentSlidePreview.Text = raw Content.Text` when `CongregationShowChords` is ON (strip only happens when OFF); plain TextBlock → literal `[chord]` brackets. (`OperatorWindow.xaml.cs:802-816`, `OperatorWindow.xaml:37`.)
4. **How does the toggle propagate / is state lost on recreate?** Toggle → `settings.CongregationShowChords` + `_outputWindow?.SetChordDisplay(...)` in `ToggleChords_Click`. On single-monitor GO LIVE (`HandleGoLiveSingleMonitor`, `:486`) and on `ApplyDisplaySettings` (`:438`) a new OutputWindow is created and `SetOutputWindow` only stores the reference (`:222`) — the chord state is **lost** every time the window is (re)created. Yes, the state is lost.
5. **Current audience-vs-stage chord behavior (design enumeration):**
   - STAGE preview: always chord-over-lyric, independent of the toggle, transposed by pitch shift. (`OperatorWindow.xaml.cs:830`.)
   - AUDIENCE preview: toggle OFF → stripped lyrics; toggle ON → raw text with brackets (bug). (`:802-816`.)
   - Congregation OutputWindow: toggle OFF → stripped lyrics; toggle ON → chord-over-lyric — *if* the instance ever received `SetChordDisplay(true)` and was not recreated afterward. (`OutputWindow.xaml.cs` LoadSlideIntoBuffer.)
   - One flag (`CongregationShowChords`) governs both the audience preview and the congregation projector output.

---

## Design clarification to resolve before fixing (flag — do not assume)

The congregation surface uses the **same** `CongregationShowChords` flag for preview and projector. Standard worship convention is audience = lyrics only, stage = chords. But the user has the CHORDS toggle ON for the output and expects to see chords there. Two coherent options:

- **Option A — honor the toggle on the congregation output (matches current user intent):** congregation output and its AUDIENCE preview both show chord-over-lyric when `CongregationShowChords` is ON. Fix seeds the new OutputWindow from the setting and makes the AUDIENCE preview render chord-over-lyric instead of raw text.
- **Option B — audience is always lyrics-only; chords are a stage-only concept:** drop/ignore `CongregationShowChords` for the audience surface and never show chords on congregation output; keep STAGE as the only chord surface.

The orchestrator should pick A or B. The user's messages (CHORDS ON, "expect to see them on output") point to **A**; the recommended fix below assumes A but is written so each piece is independently applicable.

---

## Recommended minimal fix (assuming Option A)

1. **Seed every freshly created OutputWindow from the stored setting (fixes Problem 1 — the main one).**
   In `PresenterIntegration.cs`, immediately after each `_outputWindow = new OutputWindow();` (lines ~438 and ~486), call `_outputWindow.SetChordDisplay(_presenterSettings.CongregationShowChords);` and also push the current pitch shift. Equivalently (and more robustly), do this inside `OperatorWindow.SetOutputWindow` (`:222`) so any reference swap re-applies `_engine.Settings.CongregationShowChords` (and pitch) to the new instance. Preferring `SetOutputWindow` centralizes it in one place.

2. **Render chord-over-lyric in the AUDIENCE preview when the toggle is ON (fixes Problem 2).**
   The AUDIENCE box is currently a plain TextBlock. To show chord-over-lyric it needs an ItemsControl mirroring the STAGE one. Minimal approach: in `OperatorWindow.xaml` add a chord ItemsControl beside `CurrentSlidePreview` (reuse the STAGE DataTemplate), and in `UpdatePreviews()` branch: toggle OFF → set TextBlock to `StripChords(...)` and hide the ItemsControl; toggle ON → `BuildChordOverLyric(GetCongregationText-with-chords/GetStageText-style text)` into the ItemsControl and hide the TextBlock. If a smaller change is preferred and brackets are the only complaint, at minimum stop showing raw brackets (never leave inline `[chord]` text in a plain TextBlock).

3. **Consolidate the duplicate toggle (prevents regressions).**
   Ensure the visible CHORDS control is bound to `ToggleChords_Click` (which updates the setting + previews) and either remove or align `ChordToggle_Changed` (`:620`) so there is a single source of truth that always writes `CongregationShowChords` and calls `UpdatePreviews()`.

**Verification after fix:** build the Presenter/UI projects and run the existing ChordPro parser unit tests (the project's `dotnet test` for `WorshipPlayer.Core` / parser tests that cover `StripChords`/`BuildChordOverLyric`). Then manual check per the user's flow: single-monitor, CHORDS ON, GO LIVE → projector shows chord-over-lyric; AUDIENCE box shows chord-over-lyric (not brackets); toggle OFF → both show clean lyrics. Grep is not sufficient verification here because the defect is runtime state propagation, not a missing symbol.
