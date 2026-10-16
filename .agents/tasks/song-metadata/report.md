# Completion Report — D4 Song Metadata Editor (wiring)

Branch: `feature/song-metadata` (committed, left in the worktree — not merged).

## Summary

Connected the previously ORPHANED `SongMetadataEditor` UserControl into the reachable Songs
area. A user can now click **Edit Metadata…** on the Song Library panel, edit a selected song's
title / artist / key / BPM / CCLI number / tags, and have the changes persisted to `song.json`
via the existing `SongLibraryService.UpdateMetadataAsync` → `SongManager.SaveSongAsync` path.
Sections/lyrics/chords are preserved because the save re-serializes the whole in-memory `Song`.

Implemented from scratch (no `review.json` present → first iteration), following `plan.md`.

## Changes

1. **`WorshipPlayer.Core/Models/Song.cs`** — added additive `Ccli` metadata field
   (`[JsonPropertyName("ccli")] public string Ccli { get; set; } = ""`). Matches the existing
   `System.Text.Json` convention; no new storage format. Old `song.json` files deserialize to `""`.

2. **`WorshipPlayer.Core/Services/SongLibraryService.cs`**
   - `UpdateMetadataAsync` now also persists `song.Ccli = songItem.CcliNumber ?? ""` and
     `song.Themes = string.Join(", ", songItem.Tags)`. Sections/Tracks/ChordChart untouched.
   - `MapToLibraryItem` now surfaces `CcliNumber = song.Ccli` and `Tags` (split from `Themes`)
     so the editor can be pre-filled.

3. **`WorshipPlayer.Presenter/Views/SongLibrary/SongLibraryPanel.xaml` / `.xaml.cs`**
   - New "Edit Metadata…" button next to the existing "Import ChordPro…" button (same style).
   - New `EditMetadataRequested` event + `EditMetadata_Click` handler (mirrors ChordPro).
   - New `SelectedSong` accessor so the host reads the selection without owning service logic.

4. **`WorshipPlayer.Presenter/Views/SongLibrary/SongMetadataEditorWindow.xaml` / `.xaml.cs`** (new)
   - Thin modal `Window` hosting the `SongMetadataEditor` UserControl. Bridges the editor's
     `SaveRequested`/`CancelRequested` to `DialogResult` and exposes `Result` (the `SongMetadata`),
     mirroring how `ChordProImportDialog` is consumed.

5. **`WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`**
   - Subscribes `_songLibraryPanel.EditMetadataRequested += OnEditMetadataRequested`.
   - `OnEditMetadataRequested`: guards on services, reads `SelectedSong`, resolves the `Song`
     via `SongPathResolver.Resolve`, opens `SongMetadataEditorWindow`, maps the result to a
     `SongLibraryItem`, and fire-and-forgets `UpdateSongMetadataAsync` which calls
     `UpdateMetadataAsync` then `RefreshSongs()` (try/catch → `ShowToolError`).

6. **`WorshipPlayer.Tests/SongLibraryServiceTests.cs`** — new `[Fact]`
   `UpdateMetadataAsync_PersistsMetadata_PreservesSections`.

## Wired-path trace (file:line)

1. Entry point — "Edit Metadata…" button:
   `WorshipPlayer.Presenter/Views/SongLibrary/SongLibraryPanel.xaml` (button `EditMetadataButton`,
   `Click="EditMetadata_Click"`).
2. Click handler raises event:
   `SongLibraryPanel.xaml.cs:102` `EditMetadata_Click` → `EditMetadataRequested?.Invoke` (:104).
   Event declared at `SongLibraryPanel.xaml.cs:41`; selection accessor `SelectedSong` at :47.
3. Host subscribes:
   `OperatorWindow.xaml.cs:1625` `_songLibraryPanel.EditMetadataRequested += OnEditMetadataRequested`.
4. Host handler:
   `OperatorWindow.xaml.cs:1723` `OnEditMetadataRequested` — reads `SelectedSong`, resolves the
   `Song` via `SongPathResolver.Resolve`, opens the editor window, maps `SongMetadata` →
   `SongLibraryItem`, calls `_ = UpdateSongMetadataAsync(item)` (:1768).
5. Editor host window:
   `SongMetadataEditorWindow.xaml.cs` ctor → `Editor.LoadMetadata(...)`; `OnSaveRequested` stores
   `Result` and sets `DialogResult = true`.
6. Persist:
   `OperatorWindow.xaml.cs:1772` `UpdateSongMetadataAsync` → `SongLibraryService.UpdateMetadataAsync`
   (`SongLibraryService.cs`, resolves song, sets Title/Artist/OriginalKey/Bpm/Ccli/Themes) →
   `SongManager.SaveSongAsync(song)` → writes `song.json` (`SongManager.cs:143`, serializes whole Song).
7. List refresh:
   `OperatorWindow.xaml.cs` `UpdateSongMetadataAsync` → `_songLibraryPanel?.RefreshSongs()`
   (`SongLibraryPanel.xaml.cs:52`).

No orphaned `new SongMetadataEditor(...)` remains — the only construction is inside
`SongMetadataEditorWindow.xaml` (`<local:SongMetadataEditor x:Name="Editor"/>`), which is reached
through the wired path above.

## Verification

- **Build**: `dotnet build WorshipBacktrackPlayer.sln` → `Build succeeded. 4 Warning(s) 0 Error(s)`.
  The 4 warnings are pre-existing and unrelated to this change.
- **Tests**: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` →
  `Passed! - Failed: 0, Passed: 481, Skipped: 0, Total: 481`. (Was 480 pre-existing; +1 new.)
  Focused run of the new test alone also passes:
  `--filter FullyQualifiedName~UpdateMetadataAsync_PersistsMetadata_PreservesSections` →
  `Passed! - Failed: 0, Passed: 1`.
  The new test writes a song.json with a section, updates metadata via the service save path,
  reloads with a fresh `SongManager.LoadAllSongsAsync()`, and asserts Title/Artist/Key/Bpm/Ccli/
  Themes changed while the single section's Name and lyrics Text are preserved.

### What was NOT verified
- **No interactive/visual UI run.** This is a WPF desktop app and a headless UI launch was not
  performed. Correctness of the UI path is argued via (a) the clean build (which compiles the new
  XAML window + UserControl composition and all event wiring), (b) the passing test suite incl. the
  new round-trip test, and (c) the precise file:line wired-path trace above. I did NOT visually
  confirm the button renders or the dialog opens on screen.

## Scope
Strictly metadata-editor wiring + minimal persistence (additive `Song.Ccli`). No changes to
templates, chordpro import logic, scripture, media, or unrelated code.
