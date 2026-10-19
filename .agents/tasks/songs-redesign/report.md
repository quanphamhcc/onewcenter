# Completion Report — Songs tab redesign (ProPresenter-style table)

## Summary
Redesigned the Songs tab (`SongLibraryPanel`) into a dark, ProPresenter-style table built on
`ListView` + `GridView`, reusing the Services/Home table styles (`OwcDarkListView` /
`OwcDarkGridViewColumnHeader`) for visual consistency. Code-only; no data files, Core models,
storage, or OperatorWindow wiring were changed. All existing events/handlers remain wired.

## New layout, element by element
Outer: root `UserControl` with the merged `OwcDesignSystem.xaml` ResourceDictionary and the
`Border Style="{StaticResource OwcPanelBorder}"` kept as-is. Inside, a `DockPanel`:

1. **Header** — `TextBlock` "Song Library" (kept, OWC branding; no "Presenter" logo).
2. **Top toolbar** (`Grid`, docked top):
   - Left `StackPanel`: a non-interactive muted **"Filters"** label; the existing
     **`KeyFilterComboBox`** (All Keys… list, `OwcDarkComboBox`); the existing
     **`ArtistFilterComboBox`** (All Artists, `OwcDarkComboBox`); the existing **`SortAZButton`**
     ("A-Z") and **`SortDateButton`** ("Date"); and the existing **`SearchTextBox`** widened to 260px
     (`OwcTextBox`). These are the original named controls kept inline — the brief permits "a cleanly
     styled row of the existing combos" — so their handlers bind unchanged with no re-parenting risk.
   - Right: prominent blue **`ImportChordProButton`** relabeled **"+ Add Song"**
     (`OwcBlueButton`), right-aligned.
3. **Empty state** — `TextBlock x:Name="EmptyStateText"` (kept; toggled by `UpdateEmptyState()`).
4. **Table** — `ListView x:Name="SongListBox" Style="{StaticResource OwcDarkListView}"` with a
   `GridView` (`ColumnHeaderContainerStyle="{StaticResource OwcDarkGridViewColumnHeader}"`). The
   ListView keeps the `SongListBox` name so `SelectedSong`, the constructor's `ItemsSource = Songs`,
   and `UpdateEmptyState()` all work unchanged (ListView is a ListBox/Selector subclass). Columns:
   1. `""` (36) — unbound placeholder **CheckBox** (select affordance, purely visual).
   2. **"Song"** (240) — `StackPanel`: bold `Title` + muted `Artist` second line (blank if none).
   3. **"Key"** (60) — real `{Binding Key}`.
   4. **"Last modified"** (120) — `{Binding LastModifiedDisplay}`, muted.
   5. **"Last modified by"** (120) — `{Binding LastModifiedBy}`, muted.
   6. `""` (70) — blue per-row **"+ Add"** button (`OwcBlueButton`, `Tag="{Binding}"`).
   7. `""` (44) — **⋮** overflow button (`OwcButtonBase`) with a `ContextMenu` offering
      **Edit Metadata…** and **Edit Slides…**.

## Control → event mapping
| Capability | Control (x:Name / cell) | Handler | Panel event → OperatorWindow handler |
|---|---|---|---|
| Song selection | `SongListBox` (ListView) | `SongList_SelectionChanged` | `SongSelected` → `OnSongSelected` |
| Search | `SearchTextBox` | `SearchTextBox_TextChanged` → `ApplyFilters` | (internal) |
| Filter by key | `KeyFilterComboBox` | `Filter_Changed` | (internal) |
| Filter by artist | `ArtistFilterComboBox` | `Filter_Changed` | (internal) |
| Sort A-Z | `SortAZButton` | `SortAZ_Click` | (internal) |
| Sort by date | `SortDateButton` | `SortDate_Click` | (internal) |
| **+ Add Song** (toolbar) | `ImportChordProButton` | `ImportChordPro_Click` | `ImportChordProRequested` → `OnImportChordProRequested` |
| Per-row **+ Add** | row button | `AddToService_Click` (reads `Tag`, no selection change) | `AddSongToServiceRequested` → `OnAddSongToServiceRequested` |
| ⋮ → **Edit Metadata…** | menu item | `EditMetadataMenu_Click` (selects row from `Tag`, then raises) | `EditMetadataRequested` → `OnEditMetadataRequested` (reads `SelectedSong`) |
| ⋮ → **Edit Slides…** | menu item | `EditSlidesMenu_Click` (selects row from `Tag`, then raises) | `EditSlidesRequested` → `OnEditSlidesRequested` (reads `SelectedSong`) |
| Host reads selection | `SelectedSong` getter | — | used by Edit Metadata/Slides handlers |
| Host refresh | `RefreshSongs()` / `Songs` | — | called after import/metadata/slides edits |

### What "+ Add Song" is wired to
"+ Add Song" is the former `ImportChordProButton`, restyled to `OwcBlueButton` and relabeled. Its
`Click` is still `ImportChordPro_Click`, which raises `ImportChordProRequested`. OperatorWindow's
`OnImportChordProRequested` runs the existing ChordPro file picker + `ImportChordProAsync(path)` and
then `RefreshSongs()`. So "+ Add Song" triggers the existing ChordPro import.

### Why the ⋮ menu items select the row first
OperatorWindow's `OnEditMetadataRequested` / `OnEditSlidesRequested` read
`_songLibraryPanel.SelectedSong`. The menu items therefore set `SongListBox.SelectedItem` from the
MenuItem's `Tag` (the row's `SongListItem`) before raising the event, so the edit always targets the
row whose ⋮ was clicked. The original `EditMetadata_Click` / `EditSlides_Click` methods are retained.

## Placeholders (exactly what is displayed)
- **Key** — real value, `{Binding Key}`.
- **Song** — real `Title` (bold) + real `Artist` on a muted second line; blank second line when
  `Artist` is empty. (The pipeline carries `Artist` only; no `Composer` reaches `SongListItem`.)
- **Last modified** — derived from the song folder's real filesystem timestamp:
  `Directory.GetLastWriteTime(Id)` formatted `yyyy-MM-dd`; falls back to the `song.json` file time;
  shows `"—"` if the path is missing or any exception occurs. Read-only filesystem access — no write
  to `song.json`, no model change. (`UpdatedAt` is `DateTime.UtcNow` at map time, not persisted, so it
  is deliberately not used here.)
- **Last modified by** — static placeholder string **"Local"** (the app has no per-user authorship).
- **Checkbox** — unbound placeholder `CheckBox` per row (no backing field, purely visual).

These display-only strings live on the Presenter display model `SongListItem`
(`LastModifiedDisplay`, `LastModifiedBy`) only.

## OperatorWindow contract (host compiles)
All five subscribed events are still declared and raised by the panel:
`SongSelected`, `ImportChordProRequested`, `EditMetadataRequested`, `EditSlidesRequested`,
`AddSongToServiceRequested`. Public surface (`SongLibraryService` setter, `SelectedSong`,
`RefreshSongs()`, `Songs`) is unchanged. `OperatorWindow.xaml.cs` was NOT edited. The full-solution
build (which compiles OperatorWindow against this panel) succeeds — proof the host still compiles.

## No data/model changes
- No edits to `OperatorWindow.xaml(.cs)` wiring.
- No edits to Core models (`SongLibraryItem`, `Song`), `SongLibraryService`, or storage.
- No edits to `app.db`, `Songs/*`, or `ServiceOrders/*.json`.
- New fields `LastModifiedDisplay` / `LastModifiedBy` are display-only on the Presenter `SongListItem`.
- `using System.IO;` added to the panel code-behind for the timestamp read.

## Build & test results
- **Build**: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded, 0 Error(s),
  4 Warning(s)** (the 4 are pre-existing: `MediaLibraryPanel._currentView` CS0414,
  `ChordChartWindow` CS8604, `WaveformEditorWindow._inReviewMode` CS0414).
- **Tests**: `dotnet test` (worktree) → **Passed! Failed: 0, Passed: 624, Skipped: 0,
  Total: 624** (WorshipPlayer.Tests.dll, net8.0). No regressions.

## Click-test checklist (manual STA smoke — code-verified wiring; no layout unit tests added)
- [ ] Songs tab renders as a dark table with columns: ☐ | Song | Key | Last modified | Last modified by | + Add | ⋮.
- [ ] Empty state text shows when there are no songs; the table shows when songs exist.
- [ ] Typing in Search fires `SearchTextBox_TextChanged` → `ApplyFilters`.
- [ ] All Keys / All Artists combos fire `Filter_Changed`.
- [ ] A-Z and Date buttons re-sort the list.
- [ ] "+ Add Song" (blue, top-right) opens the ChordPro import and refreshes the list.
- [ ] Per-row "+ Add" adds that row's song to the service WITHOUT changing list selection.
- [ ] Clicking a row raises `SongSelected` (loads the song in the presenter engine).
- [ ] ⋮ → "Edit Metadata…" selects that row and opens the metadata editor.
- [ ] ⋮ → "Edit Slides…" selects that row and opens the slide editor.
