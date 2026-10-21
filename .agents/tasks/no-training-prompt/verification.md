# Verification — Remove blocking AI-Training-Data startup prompt

Branch: `feature/no-training-prompt`
Worktree: `.worktrees/no-training-prompt`

## Change summary

Root cause: the training-data folder defaulted to the hardcoded missing drive `E:\temp\OWClogs`,
and `MainWindow_Loaded` opened a blocking `OpenFolderDialog` whenever that folder was missing,
re-baking the same missing path on cancel — so the prompt reappeared on every launch.

Files changed:
- `WorshipPlayer.Core/Services/TrainingDataPaths.cs` (new) — pure helper. `DefaultFolder()` returns
  `Documents\OWC\TrainingData`; `IsUsable(path)` is false for null/empty/whitespace or a
  non-existent directory (covers the stale missing-drive setting).
- `WorshipPlayer.Core/Models/AppSettings.cs:49` — `TrainingDataFolder` default changed from
  `@"E:\temp\OWClogs"` to `TrainingDataPaths.DefaultFolder()`; added `using WorshipPlayer.Core.Services;`.
- `WorshipPlayer.UI/Views/MainWindow.xaml.cs:63-69` — removed the blocking `OpenFolderDialog` and the
  `E:\temp\OWClogs` fallback. Now: if `!TrainingDataPaths.IsUsable(_settings.TrainingDataFolder)`,
  set to `TrainingDataPaths.DefaultFolder()` and `await _settingsService.SaveAsync()`. No dialog.
  The following `Directory.CreateDirectory(...)` and `new TrainingDataService(...)` lines are unchanged,
  so the AI-training feature still gets a valid, created folder.
- `WorshipPlayer.Tests/TrainingDataPathsTests.cs` (new) — unit tests for the helper.

New default path: `Environment.GetFolderPath(SpecialFolder.MyDocuments)\OWC\TrainingData`.

## Build

Command (from worktree root):
`dotnet build WorshipBacktrackPlayer.sln -c Debug`

Result: **Build succeeded with 6 warning(s), 0 errors** (Exit Code 0). All 6 warnings are
pre-existing (OperatorWindow CS4014, MediaLibraryPanel CS0414, ChordChartWindow CS8604,
WaveformEditorWindow CS0414) and unrelated to this change.

## Test

Command (from worktree root):
`dotnet test WorshipBacktrackPlayer.sln -c Debug`

Result: **total: 782, failed: 0, succeeded: 782, skipped: 0** (Exit Code 0). Includes the 3 new
`TrainingDataPathsTests` facts/theory cases. The `Error loading service order: 't' is an invalid
start of a property name` console line is expected output from a test exercising malformed-JSON
handling; it is not a test failure.
