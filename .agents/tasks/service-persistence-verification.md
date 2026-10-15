# Service Order Persistence — Verification Note

First iteration (no `service-persistence-review.json` existed). Implemented sections A–F
from `service-persistence-bug.md` per `service-persistence-plan.md`.

## Build
Command: `dotnet build WorshipBacktrackPlayer.sln --nologo`
Result: **Build succeeded, 0 errors, 4 warnings** — all pre-existing, no NEW warnings:
- CS0414 `MediaLibraryPanel._currentView` (assigned but never used)
- CS8604 `ChordChartWindow.BuildChordLine` (possible null arg `lyricText`)
- CS0414 `WaveformEditorWindow._inReviewMode` (assigned but never used)
- (the WPF temp-assembly pass re-emits the MediaLibraryPanel CS0414 once; same identity, not a new warning)

## Tests
Command: `dotnet test WorshipPlayer.Tests --nologo`
Result: **total: 450, failed: 0, succeeded: 450, skipped: 0** (447 pre-existing + 3 new).
New tests in `WorshipPlayer.Tests/ServiceOrderPersistenceTests.cs`:
- `AddItem_ThenSave_SurvivesRestart` — CreateNew + AddItem + SaveAsync writes a file; a
  fresh manager's LoadAsync returns the added item.
- `RemoveItem_ThenSave_SurvivesRestart` — RemoveItem + SaveAsync; reload reflects deletion.
- `EditItem_ThenSave_SurvivesRestart` — mutate Title + Type + SaveAsync; reload reflects it.
Each uses its own temp `ServiceOrders` dir, cleaned up in `Dispose`.
Note: the console line `Error loading service order: 't' is an invalid start of a property name`
is from the pre-existing `DefensiveLoadTests` (intentional malformed-JSON case), not a failure.

## Runtime launch (critical XAML-crash check)
Built Debug, then launched `WorshipPlayer.UI/bin/Debug/net8.0-windows/WorshipBacktrackPlayer.exe`
via `Start-Process -PassThru` and polled:
- exe exists: True
- After 12s: `HasExited=False` (process stayed alive — NO `(DependencyProperty.UnsetValue)
  is not a valid value for property 'BorderBrush'` crash and no XAML load exception; such a
  crash terminates the WPF process immediately on window load)
- `Responding=True` (UI message loop responsive)
- `CloseMainWindow()` closed it gracefully (`ClosedGracefully`) — confirms a real interactive
  main window rendered, i.e. the Services tab / SERVICE ORDER sidebar loaded.

The new sidebar `ListBox` uses the KEYED `OwcServiceListBox` style only (no implicit/keyless
style) with concrete brushes; no bare `BorderBrush` that could resolve to `UnsetValue`.

## Manual eyeball steps for the user
1. Add: open Presenter → SERVICE ORDER sidebar → `+ Add Item` → pick a type → Add Item.
   Fully close the app, reopen it → the item is STILL there (bug fixed; a
   `ServiceOrders/{yyyy-MM-dd}_New Service.json` file is written on add).
2. Edit: double-click an item (or select it and click `Edit Item`, or right-click → Edit),
   change the Type, Save → close/reopen → the change persists.
3. Delete: select an item, click `Remove Item` (or right-click → Remove) → close/reopen →
   the item stays removed.
