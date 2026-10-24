# Verification — Slides editors: reliable touch + mouse input

## What changed (summary)

Finished the touch-vs-mouse input fix for the three Slides modal windows, extending the already-shipped `SlideServiceItemEditorWindow` Topmost+Activate fix (commit a0baaed) to the other two modals and adding comfortable touch targets to the short buttons in all three.

One consistent Topmost/activation strategy was applied uniformly: **per-dialog `Topmost = true` + `Activate()` on `Loaded`** (the lower-risk option already started). `OperatorWindow` / `OutputWindow` global state was NOT touched, so the live audience output is never cleared or moved.

Files changed:
- `WorshipPlayer.Presenter/Themes/OwcDesignSystem.xaml`
  - Added `MinHeight="34"` to the existing keyed `OwcCompactButton` style.
  - Added a new keyed `OwcTouchButton` style (`BasedOn="{StaticResource OwcButtonBase}"`, `MinHeight="36"`, `MinWidth="44"`).
  - Did NOT add a global/implicit `Button` style and did NOT modify `OwcButtonBase` (the file's own note documents a crash a global Button style caused).
- `WorshipPlayer.Presenter/Views/Slides/ListPickerDialog.xaml.cs` — `Topmost = true;` + `Loaded += (_, _) => Activate();` in the ctor.
- `WorshipPlayer.Presenter/Views/Slides/ListPickerDialog.xaml` — Cancel → `OwcTouchButton`; Cancel + Select given `MinHeight="36"`.
- `WorshipPlayer.Presenter/Views/Slides/SlideEditorWindow.xaml.cs` — `Topmost = true;` + `Loaded += (_, _) => Activate();` in the ctor.
- `WorshipPlayer.Presenter/Views/Slides/SlideEditorWindow.xaml` — Close → `OwcTouchButton`; Close + Save-and-close `MinHeight="36"`; `+` add-slide icon Height 28 → 32; Delete/Move up/Move down inherit the `OwcCompactButton` `MinHeight="34"` bump.
- `WorshipPlayer.Presenter/Views/Slides/SlideServiceItemEditorWindow.xaml` — hit-target bumps only (the existing Topmost+Activate code-behind was left intact):
  - Header Remove / Close / Save-and-close → `MinHeight="36"` (Close also → `OwcTouchButton`).
  - Rename `✎` → `MinWidth="40"` + `MinHeight="36"`; rename OK → `MinHeight="36"`; rename Cancel → `OwcTouchButton` + `MinHeight="36"`.
  - Audio `+` icon Height 28 → 32; Clear template → `OwcTouchButton`.
  - Edit slides / Select (Slides card), Select (template), `✕` remove-audio inherit the `OwcCompactButton` `MinHeight="34"` bump.

No model, live-link, or unrelated-window changes. No inline-brush hacks in the three editor windows (they keep merging `OwcDesignSystem.xaml` and using keyed OWC styles). No `OperatorWindow` `PanningMode`/`IsManipulationEnabled` scrollers touched. No DiagLog / file logging.

## Automated verification (ran; results below)

### Build
Command (run from the worktree root, incremental — no clean, no bin/obj delete, no app.db reset):

```
dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug --nologo
```

Result: **Build succeeded** — Exit Code 0. Exactly the 6 known pre-existing warnings, NO new warnings:
- CS4014 `OperatorWindow.xaml.cs(728,21)` — not awaited (pre-existing).
- CS0414 `MediaLibraryPanel.xaml.cs(16,20)` — `_currentView` unused (pre-existing).
- CS0414 `WaveformEditorWindow.xaml.cs(1802,18)` — `_inReviewMode` unused (pre-existing).
- CS8604 `ChordChartWindow.xaml.cs(235,51)` — possible null `lyricText` (pre-existing).
(The Presenter `_wpftmp` pass re-surfaces the CS4014 + CS0414 pair — same two warnings, not new.)

### Tests
Command (run from the worktree root):

```
dotnet test --nologo
```

Result: **total: 854, failed: 0, succeeded: 854, skipped: 0** — Exit Code 0. The single "Error loading service order: 't' is an invalid start of a property name" line in the output is an intentionally-tested malformed-JSON error path (test console output), not a failing test.

## UI / touch verification — what was and was NOT runtime-verified

This build environment is a headless/non-interactive server with **no touchscreen and no interactive WPF session available**, so I could NOT drive real taps/clicks against the running app. I am explicitly NOT claiming touch was runtime-verified. The automated gates above (build + 854 tests) passed. Below is the per-change reasoning for why each fix makes a tap reach the control, plus manual test steps for the user to confirm on the touchscreen+mouse rig.

### Why a tap will now reach each control (WPF touch→mouse promotion reasoning)

1. **Z-order (the dominant reported symptom "Close/Cancel very hard to press").** While Live, `OutputWindow` is `Topmost=true` full-screen. A non-Topmost modal opened over it can end up BEHIND it in the top-most band, so the operator's input lands on the output window instead of the button — the same root cause the shipped `SlideServiceItemEditorWindow` fix addressed for the mouse. Setting `Topmost = true` on `ListPickerDialog` and `SlideEditorWindow` (matching the shipped window) puts every Slides modal in the same top-most band as the output window and above it, and `Activate()` on `Loaded` brings it to the foreground. This applies to BOTH mouse and touch, because both are delivered to whichever window owns the pixel under the pointer/finger — fixing which window is on top fixes both input paths at once.

2. **Touch→mouse promotion is not blocked in these windows.** WPF raises touch as `Touch*`/`Stylus*` preview+bubbling events first; when those go unhandled it **promotes** the gesture to the equivalent mouse events, which is what fires `Button.Click`. A control only fails to receive the promoted click if something in the tree handles the touch/stylus event first (e.g. a `ScrollViewer` with `PanningMode` treating the tap as a pan, an `IsManipulationEnabled` container consuming it, or a handled `TouchDown`/`StylusDown`). I grepped all three Slides windows for `IsManipulationEnabled`, `PanningMode`, `StylusDown`, `TouchDown`, `ManipulationStarting`, `StylusSystemGesture`, `IsPressAndHoldEnabled` — **none exist**. So nothing in these windows swallows touch; an unhandled tap promotes to a mouse click and reaches the button exactly like a mouse click. (The only `PanningMode`/`IsManipulationEnabled` usages in the app are in `OperatorWindow`, which is out of scope and left untouched.)

3. **Hit-target size.** The buttons' clickable area is the `OwcButtonBase` ControlTemplate `Border` (which has a non-null `Background` brush and is therefore hit-testable). The reported-hard buttons were short (compact chips ~24-26px, rename `✎` MinWidth=30, icon buttons Height=28). A fingertip contact patch is ~9-11mm; on a short control the tap can land in the margin/gap around the Border and never hit it. Raising the effective height to 34-36px (`OwcCompactButton` MinHeight=34, `OwcTouchButton` MinHeight=36, icon buttons Height→32) enlarges the hit-test Border so the fingertip lands on it on the first try. This does not change the mouse path (a mouse click already hit the smaller target) and keeps the dark-theme layout — MinHeight only grows a control when its natural height is smaller, and the compact header/footer rows have room for 34-36px.

4. **ComboBox dropdowns (Source / Auto-advance After / When complete / Target).** The shared `OwcDarkComboBox` template opens via an `OwcComboBoxToggleButton` with `ClickMode="Press"` whose template `Border` has `Background="Transparent"` (Transparent IS hit-testable in WPF) covering the whole face, and the `ContentSite` presenter is `IsHitTestVisible="False"` so it never blocks the toggle. `ClickMode=Press` opens on the press edge, which is touch-friendly. Nothing handles touch/stylus on this path, so a tap promotes to a mouse click on the toggle and opens the dropdown; a tap on a `ComboBoxItem` (Padding 8,6) selects it. No code change was needed here and none was made — this style is shared app-wide and the task says to only add a scoped ComboBox fix if runtime shows an actual swallow.

### Manual touch test steps for the user (please run on the touchscreen+mouse rig)

Do each step first by **finger tap**, then repeat by **mouse click**. Every action should activate on the FIRST try.

Pre: Go Live so the audience `OutputWindow` is full-screen and Topmost.

1. Open a Slides service item → the editor opens ABOVE the live output.
   - Tap/click: Close, Save and close (enable it first by making an edit), Remove service item.
   - Tap the rename `✎`, then tap OK and (reopen) Cancel.
   - Tap Edit slides, Select (Slides card), Select + Clear (Stage template), `+` add audio, `✕` remove an audio row.
   - Tap the Source, After, When complete, and Target dropdowns; confirm each opens on first tap and an item selects on first tap.
2. From the service-item editor, open the presentation/template picker (`ListPickerDialog`) → it opens above the live output. Tap/click Cancel and Select; double-tap a row to commit.
3. From the service-item editor, open Edit slides (`SlideEditorWindow`) → it opens above the live output. Tap/click Close, Save and close, the `+` add-slide, Delete, Move up, Move down; tap into the slide text box and type.

If any single control still needs a second tap, note exactly which one and whether it was touch or mouse, and whether the live output blanked or moved — that would indicate a control-specific swallow to fix with a scoped `Stylus.IsPressAndHoldEnabled="False"` (or similar) on just that control.
