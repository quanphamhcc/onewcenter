# Completion Report — ChordPro Import Wiring (roadmap D5)

Branch: `feature/chordpro-import`
Worktree: `.worktrees\chordpro-import` (main checkout untouched)

## What was implemented

Wired the orphaned ChordPro import UI into a reachable Songs-area entry point. No new storage
format, no new parser construction threaded through `SetServices` — the existing
`SongLibraryService.ImportChordProAsync(path)` already owns parser + `song.json` persistence.

### Design decision (confirms the plan, deviates from the task's injection hypothesis)
The task hypothesized the parser service must be constructed/injected (TemplateService pattern).
Reading the source showed that is unnecessary:
- `SongLibraryService` constructs its own `ChordProParserService` (`WorshipPlayer.Core\Services\SongLibraryService.cs:16,21`).
- `ChordProImportDialog.ParserService` is never read; the dialog only parses lossily for its own
  preview (`ChordProImportDialog.xaml.cs:14-20,121-162`).
- The real import path `ISongLibraryService.ImportChordProAsync` is fully implemented
  (`SongLibraryService.cs:115-160`): parse → map directives → create `Song` → copy sections
  (inline `[chord]` text preserved) → `SongManager.SaveSongAsync` (`song.json`) → `Songs.Add`.

So the entry point calls `ImportChordProAsync(path)` for the authoritative import (preserves
chords + sections) and uses `ChordProImportDialog` purely as the file-picker + preview + confirm UX.
This mirrors the existing `MediaLibraryPanel.ImportRequested` → `OperatorWindow` host pattern,
keeping the panel service-agnostic.

### Files changed
1. `WorshipPlayer.Presenter\Views\SongLibrary\SongLibraryPanel.xaml`
   — added `ImportChordProButton` ("Import ChordPro…") under the header, styled `OwcButtonBase`.
2. `WorshipPlayer.Presenter\Views\SongLibrary\SongLibraryPanel.xaml.cs`
   — added `public event EventHandler? ImportChordProRequested;` (line 34) and
     `ImportChordPro_Click` (line 84) that raises it. Panel references no service.
3. `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`
   — subscribed `_songLibraryPanel.ImportChordProRequested += OnImportChordProRequested;` inside
     the single-run `if (_songLibraryPanel == null)` guard of `SwitchToTab("Songs")` (line 1624).
   — added `OnImportChordProRequested` (line 1686): guards service, shows `ChordProImportDialog`
     (Owner = this), reads the confirmed path from `dialog.FilePathTextBox.Text`, validates it, and
     fire-and-forgets `ImportChordProFromFileAsync` (line 1704), which awaits
     `ImportChordProAsync`, calls `RefreshSongs()`, and surfaces errors via `ShowToolError`.
4. `WorshipPlayer.Presenter\Views\SongLibrary\ChordProImportDialog.xaml.cs`
   — extended the OpenFileDialog `Filter` to include `*.pro;*.crd` so the picker matches the
     acceptance list (`.chopro,.cho,.pro,.crd,.txt`). One-line change; nothing else in the dialog touched.
5. `WorshipPlayer.Tests\ChordProImportTests.cs` (new)
   — end-to-end test proving import-and-persist through the service.

## Full wired-path trace (file:line, every link constructed — no orphaned `new`)

1. Button click — `SongLibraryPanel.xaml` `ImportChordProButton` `Click="ImportChordPro_Click"`
   → `SongLibraryPanel.xaml.cs:84` `ImportChordPro_Click` → raises `ImportChordProRequested` (`:86`).
   The panel instance is constructed at `OperatorWindow.xaml.cs:1621` (`new SongLibraryPanel()`).
2. Subscription — `OperatorWindow.xaml.cs:1624` `_songLibraryPanel.ImportChordProRequested += OnImportChordProRequested;`
   (inside the `if (_songLibraryPanel == null)` guard → wired exactly once).
3. Host handler — `OperatorWindow.xaml.cs:1686` `OnImportChordProRequested`:
   `new ChordProImportDialog { Owner = this }` (`:1691`) → `ShowDialog()`.
4. OpenFileDialog — inside the dialog, `Browse_Click` shows `OpenFileDialog` filtered to
   `*.chopro;*.chordpro;*.cho;*.pro;*.crd` + `*.txt` (`ChordProImportDialog.xaml.cs:39`) and sets
   `FilePathTextBox.Text` (`:52`).
5. Confirm — on `Import_Click` the dialog sets `Confirmed = true; DialogResult = true`
   (`ChordProImportDialog.xaml.cs:92-93`). Host reads `dialog.FilePathTextBox.Text` and checks
   `File.Exists` (`OperatorWindow.xaml.cs:1694-1700`).
6. Async import — `OperatorWindow.xaml.cs:1699` `_ = ImportChordProFromFileAsync(path);` →
   `:1704` awaits `_songLibraryService.ImportChordProAsync(path)`.
7. Parser — `SongLibraryService.ImportChordProAsync` (`SongLibraryService.cs:115`) calls
   `_chordProParser.Parse(content)` (`:121`); `_chordProParser` constructed in the service ctor
   (`SongLibraryService.cs:21`). `ChordProParserService.Parse` is at `ChordProParserService.cs:92`.
8. Song created — directives mapped title/artist/key (`SongLibraryService.cs:125-127`), `new Song`
   (`:137`), parsed sections copied with inline `[chord]` text (`:148-154`).
9. Persist — `_songManager.SaveSongAsync(song)` (`SongLibraryService.cs:155`) writes
   `<FolderPath>\song.json` (`SongManager.cs:142-157`); then `_songManager.Songs.Add(song)` (`:156`).
   `_songManager` is the same instance that backs the library list.
10. Refresh — `OperatorWindow.xaml.cs:1711` `_songLibraryPanel?.RefreshSongs()` →
    `SongLibraryPanel.RefreshSongs` (`SongLibraryPanel.xaml.cs:48`) → `LoadSongsAsync` →
    `_songLibraryService.GetAllAsync()` repopulates `Songs`, so the imported song appears in the list.
11. Errors — any exception → `ShowToolError("Import ChordPro", ex.Message)`
    (`OperatorWindow.xaml.cs:1714`; helper at `:1887`).

`_songLibraryService` is set via `OperatorWindow.SetServices(...)` (`OperatorWindow.xaml.cs:488`),
so the service is the real app-constructed instance, not an orphaned `new`.

## Verification

### Build
`dotnet build WorshipBacktrackPlayer.sln -c Debug` in the worktree:
**Build succeeded with 4 warning(s), 0 errors** (exit 0). The 4 warnings are pre-existing and
unrelated to this change:
- `MediaLibraryPanel.xaml.cs(15,20)` CS0414 `_currentView` unused
- `ChordChartWindow.xaml.cs(235,51)` CS8604 possible null arg
- `WaveformEditorWindow.xaml.cs(1775,18)` CS0414 `_inReviewMode` unused
(The MediaLibraryPanel CS0414 is reported twice — once from the WPF temp-assembly pass and once
from the final Presenter compile; it is the same single pre-existing warning.)

### Tests
`dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug`:
**total: 480, failed: 0, succeeded: 480, skipped: 0** (exit 0).
Includes the existing ChordPro parser tests (CapoTranspose, ChordOverLyric, NashvilleNumber,
MusicianViewRenderer, PresenterEngineRendering) and the new `ChordProImportTests` which asserts:
returned `SongLibraryItem` title/artist/key match directives; `SongManager.Songs` contains the new
song; `song.json` is written to disk; sections are non-empty and retain inline `[chord]` text; the
persisted file round-trips to a `Song`. The console line "Error loading service order: 't' is an
invalid start of a property name" is benign output from an unrelated pre-existing test and does not
constitute a failure (0 failed).

### Not verified (stated honestly)
Interactive WPF UI was NOT launched (no desktop session in this environment). Visual confirmation
of the button rendering, the dialog appearing, and the song showing in the list after a real click
was NOT performed. Correctness is established via (a) the clean build, (b) 480 passing tests
including the end-to-end import-and-persist test, and (c) the file:line wired-path trace above
confirming every link is constructed and reachable from the Songs tab.
