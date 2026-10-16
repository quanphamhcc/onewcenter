# C4 Per-item Auto-Advance — Completion Report

Branch: `feature/auto-advance` (in worktree `.worktrees/auto-advance`)
Iteration: first (no `review.json` present at start)

## What was built

Wired the previously orphaned `ItemSettingsPanel` into a reachable modal and implemented
the auto-advance decision + trigger. Per-item settings now edit the Core
`ServiceItemSettings`, persist through the existing `ServiceOrderManager` save path, and
drive a real timer-based advance in `PresenterEngine` through a pure, unit-tested resolver.

## End-to-end wiring trace (file:line)

1. **Sidebar entry (two reachable entry points)** —
   `WorshipPlayer.Presenter/Views/OperatorWindow.xaml:253` context-menu item
   `"Item Settings…"` and `WorshipPlayer.Presenter/Views/OperatorWindow.xaml:279`
   side button `"Item Settings"`, both `Click="ItemSettings_Click"`.
2. **Event handler** — `OperatorWindow.xaml.cs` `ItemSettings_Click` →
   `OpenSelectedItemSettings()` (added right after `EditSelectedServiceItem`, ~line 1313).
   Mirrors the `EditSelectedServiceItem` precedent: reads
   `ServiceOrderList.SelectedItem as ServiceItemDisplay`, opens a modal, persists on OK.
3. **Modal host** — `WorshipPlayer.Presenter/Views/ServiceBuilder/ItemSettingsWindow.xaml(.cs)`
   hosts `ItemSettingsPanel`, calls `LoadSettings(item, allItems)`, and exposes the edited
   `ServiceItemSettings` as `Result` after `SettingsSaved` fires (then `DialogResult = true`).
4. **Panel** — `WorshipPlayer.Presenter/Views/ServiceBuilder/ItemSettingsPanel.xaml(.cs)`.
   `LoadSettings` reads `item.AutoAdvanceSettings` (null → defaults), preselects the Go-To /
   After combos via `ItemSettingsMapping`, fills the "Specific Item" target picker from the
   other items, and populates the delay. `Save_Click` builds a Core `ServiceItemSettings`
   and raises `SettingsSaved`. The panel's string combos ↔ Core enums are bridged by
   `internal static class ItemSettingsMapping` in the same file.
5. **Settings assigned + saved** — back in `OpenSelectedItemSettings()`:
   `display.Item.AutoAdvanceSettings = window.Result;` then
   `await SaveServiceOrderAsync();` → `ServiceOrderManager.SaveAsync()`
   (`WorshipPlayer.Core/Services/ServiceOrderManager.cs:84`). Serializer uses
   `DefaultIgnoreCondition = WhenWritingNull` (ctor, line ~43) so items with null settings
   write no `autoAdvanceSettings` key — old files round-trip unchanged.
6. **Engine trigger** — `WorshipPlayer.Presenter/Services/PresenterEngine.cs`:
   `OnServiceItemChanged` now calls `RestartAutoAdvanceTimer(e.NewItem)`. That method
   disposes any pending timer and, only if the new item's
   `AutoAdvanceAfterCondition == Timer`, starts a one-shot `System.Timers.Timer`
   (`AutoReset = false`) for `AutoAdvanceDelayMs`. On elapse it calls `FireAutoAdvance()`.
   The timer field is disposed in `Dispose()`.
7. **Resolver decision** — `FireAutoAdvance()` calls
   `AutoAdvanceResolver.Resolve(CurrentServiceOrder, CurrentItem.Id)`
   (`WorshipPlayer.Core/Services/AutoAdvanceResolver.cs`), a pure static method returning
   `Decision(AutoAdvanceAction Action, string? TargetItemId)`.
8. **Execution** — `FireAutoAdvance()` switches on the action:
   `NextItem` → `_serviceOrderManager.NextItem()`; `GoToItem` → translate `TargetItemId`
   to an index via `order.Items.FindIndex(i => i.Id == id)` and call
   `_serviceOrderManager.GoToItem(index)` (guarded `index >= 0`); `None` → no-op. The
   manager raises `ItemChanged`, which the UI already renders.

## Wired-vs-built: which 'After' trigger conditions are actually implemented

- **Manual** — implemented. Default; starts no timer, resolver returns `None`. Behavior
  identical to today.
- **Timer (panel "Time Delay")** — implemented and active. The engine arms a one-shot timer
  on item activation and the resolver + execution path are fully wired.
- **AudioEnd / VideoEnd (panel "End of Media")** — DEFERRED. The panel keeps the option
  visible, but no per-item media-ended signal reaches `PresenterEngine` for a service item
  today (the existing `OnSongEnded` is backtrack/song-scoped, not item-media-scoped). The
  engine starts no timer for these conditions and the resolver returns `None`, so selecting
  "End of Media" currently results in no auto-advance. Future work: raise an item-media-ended
  event from whatever plays item audio/video and route it to `FireAutoAdvance`.

Note on the Core enum value `AutoAdvanceGoTo.NextSlide` (the model default, not surfaced in
the panel): the resolver treats it the same as `NextItem` at the item-advance boundary, so
saved files using the default remain safe.

## Go To invalid-id fallback choice

`SpecificItem` with a `GoToItemId` that matches no item (or is null) **falls back to
advancing to the next item**, or returns `None` (stop) if the current item is already the
last one. No wraparound. This keeps a misconfigured target from silently stopping the
service while still never looping unexpectedly. Covered by
`TimerSpecificItem_MissingTarget_FallsBackToNextItem`,
`TimerSpecificItem_NullTarget_FallsBackToNextItem`, and
`TimerSpecificItem_InvalidTargetOnLastItem_ReturnsNone`.

## Default behavior unchanged

Items with no `AutoAdvanceSettings`, or `AutoAdvanceAfterCondition == Manual`
(and the deferred `AudioEnd`/`VideoEnd`), never start a timer and the resolver returns
`None`. Manual next/prev slide and next/prev item paths were not touched.

## Verification (run before finishing)

### Build
Command: `dotnet build .worktrees/auto-advance/WorshipBacktrackPlayer.sln -v minimal`
Result: **Build succeeded with 4 warning(s), 0 errors.** The 4 warnings are all
pre-existing and unrelated to this change:
- `MediaLibraryPanel.xaml.cs(16,20): CS0414` field `_currentView` assigned but never used
- `ChordChartWindow.xaml.cs(235,51): CS8604` possible null argument `lyricText`
- `WaveformEditorWindow.xaml.cs(1775,18): CS0414` field `_inReviewMode` assigned but never used
- (the `MediaLibraryPanel` CS0414 is reported once for the WPF temp-assembly pass and once
  for the main pass — same underlying warning)

### Tests
Command: `dotnet test .worktrees/auto-advance/WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -v minimal`
Result: **total: 513, failed: 0, succeeded: 513, skipped: 0.**
Baseline was 497, so +16 new tests (14 in `AutoAdvanceResolverTests` + 2 in
`AutoAdvanceSettingsPersistenceTests`), all green.
The console line `Error loading service order: 't' is an invalid start of a property name`
is expected output from a pre-existing negative-path test deserializing invalid JSON; it is
not a failure (0 failed).

New test files:
- `WorshipPlayer.Tests/AutoAdvanceResolverTests.cs` — covers: null settings → None;
  Manual → None; Timer+NextItem → NextItem; Timer+NextSlide → NextItem; SpecificItem valid →
  GoTo target; SpecificItem missing/null → NextItem fallback; SpecificItem invalid on last →
  None; NextItem on last → None (no wraparound); Loop → GoTo current id;
  AudioEnd/VideoEnd → None; unknown current id → None; null order → None.
- `WorshipPlayer.Tests/AutoAdvanceSettingsPersistenceTests.cs` — settings survive
  save+reload; item without settings omits the `autoAdvanceSettings` key and reloads as null.

## Headless limitation (explicit)

Only the DECISION logic (`AutoAdvanceResolver`) and persistence are unit-tested. The live
timer-driven advance during an actual service was **NOT exercised headless** — the
`System.Timers.Timer` arming lives in `PresenterEngine.OnServiceItemChanged`, which runs
inside the WPF Presenter module (`net8.0-windows`) that the `net8.0` test project cannot
reference, and no headless UI run was performed. The timer wiring was verified by code
review and compilation only; the resolver it invokes is fully unit-tested.

## Scope

Only `ItemSettingsPanel` wiring, the resolver + engine trigger, and persistence were
touched. Song editors, templates, media, and scripture were not modified.
