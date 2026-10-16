# Completion Report — Bulk Edit (roadmap C3)

**Branch:** `feature/bulk-edit` (in worktree `.worktrees/bulk-edit`, not merged, worktree left intact)
**Iteration:** first (no `review.json` present at start)

## What was built

C3 "Bulk edit" — multi-select service items and batch-apply background / transition / auto-advance
to all selected items, with per-field opt-in so untouched settings are never wiped. Implemented per
`plan.md`. The previously orphaned `BulkEditPanel` is now reachable through a modal host window
following the established editor-UserControl convention (`ItemSettingsWindow` → `ItemSettingsPanel`).

### Files added
- `WorshipPlayer.Core/Services/BulkEditService.cs` — pure, testable Core logic:
  - `BulkEditOptions` (opt-in flags + chosen values), plain data, reachable from the net8.0 test project.
  - `static void Apply(BulkEditOptions, IEnumerable<ServiceItem>)` — applies only opted-in fields.
  - `ParseBackground(string?)` / `ParseAfterCondition(string?)` — label→enum mapping shared with C4.
- `WorshipPlayer.Presenter/Views/ServiceBuilder/BulkEditWindow.xaml(.cs)` — modal host for `BulkEditPanel`,
  mirrors `ItemSettingsWindow`; exposes chosen `BulkEditOptions` as `Result` after Apply.
- `WorshipPlayer.Tests/BulkEditServiceTests.cs` — focused unit tests (see below).

### Files modified
- `WorshipPlayer.Presenter/Views/ServiceBuilder/BulkEditPanel.xaml.cs` — removed the untestable local
  `BulkEditOptions`; the panel now builds the Core `BulkEditOptions`, mapping combo text through
  `BulkEditService.ParseBackground` / `ParseAfterCondition`.
- `WorshipPlayer.Presenter/Views/ServiceBuilder/ItemSettingsPanel.xaml.cs` — `ItemSettingsMapping.ToAfter`
  now delegates to `BulkEditService.ParseAfterCondition` so C3 and C4 agree on the mapping.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` — added a "Bulk" `ToggleButton` in the sidebar
  header and a hidden "Apply to selected…" button in the bottom action stack.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `_bulkEditMode` flag; toggle handlers flip
  `ServiceOrderList.SelectionMode` between `Single`/`Extended`; `SelectionChanged` guarded so bulk mode
  does not drive single-item navigation; `ApplyToSelected_Click` collects the selection, opens the
  window, applies, persists, refreshes.

## Design notes / scope guards
- Pure operation lives in **Core**, not the WPF code-behind, because the net8.0 test project can only
  reference Core (Presenter is net8.0-windows).
- **Opt-in semantics:** each `Apply*` flag false ⇒ that property is left exactly as-is. Auto-advance
  lazily creates `ServiceItemSettings` and sets ONLY `AutoAdvanceAfterCondition`; GoTo / delay / fade /
  GoToItemId are preserved (requirement 3).
- Background "None"/empty/unmapped ⇒ `null` ⇒ no change (there is no `BackgroundType.None`).
- Only `ServiceItem.BackgroundType`, `ServiceItem.TransitionTimeMs`, and
  `ServiceItemSettings.AutoAdvanceAfterCondition` are mutated — all already serialize today, so no new
  storage format and backward-compatible (requirement 4).
- The panel's `DeleteRequested` (bulk delete) is intentionally NOT surfaced by the host; C3 is scoped to
  batch-apply only.
- Multi-select is an explicit toggle so default single-click navigation (C2/C4) is preserved. Group-header
  rows (`Item == null`) are filtered out of the selected set. C2 grouped rendering is untouched.

## Wired-path trace (file:line)
1. Toggle on — `OperatorWindow.xaml` `BulkEditToggle` (`ToggleButton`, Checked=`BulkEditToggle_Checked`).
2. `OperatorWindow.xaml.cs` `BulkEditToggle_Checked` → sets `_bulkEditMode = true`,
   `ServiceOrderList.SelectionMode = Extended`, reveals `ApplyToSelectedBtn`.
3. Multi-select — user ctrl/shift-clicks item rows; `ServiceOrderList_SelectionChanged` early-returns
   while `_bulkEditMode` (no navigation).
4. "Apply to selected…" — `OperatorWindow.xaml` `ApplyToSelectedBtn` (Click=`ApplyToSelected_Click`).
5. `OperatorWindow.xaml.cs` `ApplyToSelected_Click` → projects `ServiceOrderList.SelectedItems` to
   `ServiceItem`, drops group headers (`Item == null`), requires ≥2, constructs `BulkEditWindow(count)`,
   `ShowDialog()`.
6. `BulkEditWindow` ctor → `BulkEditPanel.SetSelectedCount`, subscribes `ApplyRequested`.
7. User ticks fields, clicks Apply → `BulkEditPanel.Apply_Click` builds Core `BulkEditOptions`
   (via `BulkEditService.ParseBackground` / `ParseAfterCondition`), raises `ApplyRequested`.
8. `BulkEditWindow.OnApplyRequested` → sets `Result`, `DialogResult = true`.
9. Back in `ApplyToSelected_Click` → `BulkEditService.Apply(window.Result, selectedItems)` mutates
   only opted-in fields on every selected item.
10. `await SaveServiceOrderAsync()` → `ServiceOrderManager.SaveAsync` persists to disk.
11. `RefreshServiceOrderList()` rebuilds the sidebar (respects `_suppressServiceOrderSelection`).

## Unit tests (`BulkEditServiceTests.cs`)
- (a) chosen field applied to EVERY item: background, transition, after-condition (incl. lazy settings creation).
- (b) unchosen fields untouched; auto-advance preserves GoTo/delay/fade/GoToItemId; opted-in-but-null background = no change.
- (c) no-op options over items changes nothing; empty selection does not throw.
- Mapping edge cases: `ParseBackground` ("None"/""/unknown/null → null; mapped labels → enum),
  `ParseAfterCondition` ("Time Delay"→Timer, "End of Media"→AudioEnd, else Manual).

## Verification (what was run and the result)
- `dotnet build WorshipBacktrackPlayer.sln -clp:ErrorsOnly` → **Build succeeded, 0 errors, 4 warnings**
  (the 4 pre-existing warnings: MediaLibraryPanel._currentView CS0414, ChordChartWindow CS8604,
  WaveformEditorWindow._inReviewMode CS0414). Exit code 0.
- `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj` → **total: 545, failed: 0, succeeded: 545,
  skipped: 0**. Prior baseline was 525; +20 from the new bulk-edit `[Fact]`/`[Theory]` cases. Exit code 0.
  (The console line "Error loading service order: 't' is an invalid start of a property name" is an
  expected log from a pre-existing negative-path test, not a failure.)

## What was NOT verified
- **No visual/headless UI run.** This is a WPF (net8.0-windows) desktop app; the test project is net8.0
  and cannot instantiate WPF controls. UI correctness (toggle flipping selection mode, dialog appearance)
  was not exercised at runtime. Correctness is established via: clean solution build, the passing Core
  batch-apply unit tests (where the real logic risk lives), and the wired-path file:line trace above.
  No visual verification is claimed.
