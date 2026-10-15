# Baseline — home-services-screens

## Branch
`main` (working directly on main, no worktree)

## Build
Command: `dotnet build WorshipBacktrackPlayer.sln --nologo`

Result: **Build succeeded** — 0 errors, 4 warnings (all pre-existing):

- CS0414 — `MediaLibraryPanel._currentView` is assigned but its value is never used
  (`WorshipPlayer.Presenter\Views\MediaLibrary\MediaLibraryPanel.xaml.cs:15`)
- CS8604 — Possible null reference argument for `lyricText` in `ChordChartWindow.BuildChordLine`
  (`WorshipPlayer.UI\Views\ChordChartWindow.xaml.cs:235`)
- CS0414 — `WaveformEditorWindow._inReviewMode` is assigned but its value is never used
  (`WorshipPlayer.UI\Views\WaveformEditorWindow.xaml.cs:1775`)

Note: CS0414 for `MediaLibraryPanel._currentView` appears twice in the log (once for the
`_wpftmp` temporary assembly pass and once for the final Presenter assembly), so the raw
log lists 4 warning lines mapping to 3 distinct pre-existing warnings.

## Tests
Command: `dotnet test WorshipPlayer.Tests --nologo`

Result: **total: 460, failed: 0, succeeded: 460, skipped: 0**

(The `Error loading service order:` line in the test output is expected console output
from an error-handling test case, not a test failure.)
