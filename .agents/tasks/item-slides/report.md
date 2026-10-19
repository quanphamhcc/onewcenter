# Completion Report — Sidebar Song-item click generates & displays lyric slides

## Task
Clicking a Song service-item in the left SERVICE ORDER sidebar must generate and display that
song's lyric slides in the center grid (previously it showed the empty state because the item's
`Slides[]` is empty and the linked library song was never resolved).

## Change made (single file, scoped)
`WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs` — inside
`ServiceOrderList_SelectionChanged`, between the existing `_serviceOrderManager?.GoToItem(index);`
and the trailing `RefreshSlideThumbnails();`, added a guarded on-demand song-load block that
mirrors the Songs-tab path (`OnSongSelected`).

No models, storage, data files, XAML, or other handlers were touched. No slides are persisted
back into the `ServiceItem`/JSON (generated on-demand). `OperatorWindow` remains
StaticResource-free (no XAML/resource added).

## Exact engine method used
`PresenterEngine.LoadStandaloneSong(Song song)` (WorshipPlayer.Presenter\Services\PresenterEngine.cs:381)
— same method the Songs tab uses. It regenerates slides from `song.Sections[].Text` synchronously
under `_stateLock` (identical sync shape to the Songs tab; no new async / no new heavy UI-thread work).

## file:line trace
Song-item click in SERVICE ORDER sidebar →
`ServiceOrderList_SelectionChanged` (OperatorWindow.xaml.cs:1429) →
`_serviceOrderManager?.GoToItem(index)` (OperatorWindow.xaml.cs:1445) →
empty-slides guard `display.Item.Type == ServiceItemType.Song && _engine != null && _songManager != null && _engine.TotalSlides == 0` (OperatorWindow.xaml.cs ~1448) →
`SongPathResolver.Resolve(_songManager.Songs, display.Item.SongFolder, display.Item.Title)` (OperatorWindow.xaml.cs ~1454) →
`_engine.LoadStandaloneSong(song)` (OperatorWindow.xaml.cs ~1458; PresenterEngine.cs:381) →
`RefreshSlideThumbnails()` (OperatorWindow.xaml.cs ~1470) renders the generated Verse/Chorus lyric thumbnails.

Guard conditions (verified):
- `ServiceItemType.Song` is the first enum value (ServiceOrder.cs:182) — Scripture/Media/other types untouched.
- `_engine`/`_songManager` null-guards mirror `OnSongSelected` (OperatorWindow.xaml.cs:2372).
- `_engine.TotalSlides == 0` (`TotalSlides => _currentSlides.Count`, PresenterEngine.cs:58) — only generate when the item has NO pre-baked slides, so pre-baked-slide Song items are a no-op.
- `SongFolder` is the correct link key (ServiceOrder.cs:88); `SongPathResolver.Resolve` matches normalized path → leaf folder name → title.

## Behavior preservation
- Non-Song items (Scripture/Media/etc.): guard fails on `Type`, path unchanged.
- Song items WITH pre-baked slides: guard fails on `TotalSlides == 0`, no regeneration.
- Backtrack-driven load: untouched (`LoadStandaloneSong` does not touch the audio engine).
- Resolve failure (`song == null`): logs to Debug and falls through to `RefreshSlideThumbnails()` → existing empty-state behavior, no crash.
- No heavy synchronous work added beyond what the Songs tab already does.

## Build result
`dotnet build WorshipBacktrackPlayer.sln -c Debug` →
**Build succeeded with 4 warning(s), 0 Error(s)** (pre-existing baseline warnings:
MediaLibraryPanel._currentView CS0414, ChordChartWindow CS8604, WaveformEditorWindow._inReviewMode CS0414).
No new warning introduced by the edit.

## Test result
`dotnet test WorshipBacktrackPlayer.sln -c Debug` →
**total: 621, failed: 0, succeeded: 621, skipped: 0.** No regressions.
(The "Error loading service order: 't' is an invalid start of a property name" console line is
expected output from a test exercising the malformed-JSON error path — not a failure.)

## Click-test checklist (manual, pending WPF launch)
- [ ] Open the Sunday service.
- [ ] Click the TheBlessing Song item in the SERVICE ORDER sidebar.
- [ ] Center grid shows Verse/Chorus lyric thumbnails instead of "Load a song in the backtrack to see slides here".
- [ ] Other items (Scripture, pre-baked Song) still work.
- [ ] App stays responsive (no "Not Responding").

Build + full test suite are green and the fix reuses the proven Songs-tab generation path; the
manual WPF click-test is the remaining runtime confirmation.
