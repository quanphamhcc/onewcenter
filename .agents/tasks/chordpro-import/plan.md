# Implementation Plan — ChordPro Import Wiring (roadmap D5)

Worktree (all paths absolute, all edits here — never the main checkout):
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\chordpro-import`

Solution: `WorshipBacktrackPlayer.sln` (net8.0-windows, WPF). `dotnet` CLI confirmed available (10.0.400).

---

## Findings from reading the actual source (file:line cited)

### ChordProParserService public API (WorshipPlayer.Core\Services\ChordProParserService.cs)
- `class ChordProParserService` in namespace `WorshipPlayer.Core.Services` (ChordProParserService.cs:4,24). Parameterless (default) ctor.
- Nested result types:
  - `class ParseResult { List<ParsedSection> Sections; Dictionary<string,string> Directives; string FullText; }` (ChordProParserService.cs:66-80).
  - `class ParsedSection { string Name; string Text; }` — `Text` is inline `[chord]` format (ChordProParserService.cs:82-86).
- Key public method: `ParseResult Parse(string input)` — auto-detects UltimateGuitar / ChordPro / plain, extracts `{directives}` (lowercased keys), returns sections with inline-chord text (ChordProParserService.cs:92-152). Directive keys are lowercased (ChordProParserService.cs:108).
- Other public methods (not needed for this wiring, listed for completeness): `IsChordLine`, `LooksLikeChord`, `IsSectionLabel`, `Transpose`, `StripChords`, `StripChordsAndTimestamps`, `ExtractSlideBlocks`, `SetSlideTimestamps`, `ParseTimecodeToMs`, `ExtractChordPositions`, `ConvertToNashvilleNumbers`, `CapoTranspose`, `BuildChordOverLyric`.
- Tests exercise the parser with xUnit `[Fact]` across `WorshipPlayer.Tests\CapoTransposeTests.cs`, `ChordOverLyricTests.cs`, `NashvilleNumberTests.cs`, `MusicianViewRendererTests.cs`, `PresenterEngineRenderingTests.cs` (each `new ChordProParserService()`). The parser is complete and tested — do NOT modify it.

### CONFLICTS with the authoritative context — verified against source
1. **"the parser service is not constructed in the app" — FALSE.** `SongLibraryService` constructs its own `ChordProParserService` in its ctor (SongLibraryService.cs:17 field, :22 `new ChordProParserService()`). No app-level parser construction or injection is required.
2. **"the dialog needs a parser injected" — FALSE.** `ChordProImportDialog` exposes a `ParserService` property (ChordProImportDialog.xaml.cs:14-20) that is **never read**. The dialog parses with its own private `ExtractDirective`/`StripChords` helpers (ChordProImportDialog.xaml.cs:121-162), which strip chords and drop sections — lossy. The dialog is only a file-picker + preview + confirm UI; its outputs are `ImportedTitle/ImportedArtist/ImportedKey/ParsedLyrics/Confirmed` (ChordProImportDialog.xaml.cs:22-27).
3. **The real import path already exists.** `ISongLibraryService.ImportChordProAsync(string filePath)` (ISongLibraryService.cs:60) is fully implemented in `SongLibraryService.ImportChordProAsync` (SongLibraryService.cs:116-166): reads the file, `_chordProParser.Parse(content)`, maps directives title/artist/key, creates a `Song`, copies parsed sections (name + inline-chord text) into `Song.Sections`, calls `_songManager.SaveSongAsync(song)` (writes `song.json`), and `_songManager.Songs.Add(song)`. Returns a `SongLibraryItem`.

**Design decision (recorded):** The entry point will call `SongLibraryService.ImportChordProAsync(path)` for the actual import, NOT the dialog's lossy `ParsedLyrics`. Rationale: it preserves chords and sections, reuses the existing parser + `song.json` persistence (SongManager.SaveSongAsync, SongManager.cs:139-154), and satisfies "do not invent a new storage format." The `TemplateService` injection pattern is therefore NOT applicable — nothing new needs constructing or threading through `SetServices`. The dialog is used purely for preview/confirm UX.

### Persistence (WorshipPlayer.Core\Services\SongManager.cs)
- `SaveSongAsync(Song song)` requires `song.FolderPath` set, writes `<FolderPath>\song.json` with auto-backup (SongManager.cs:139-154). `ImportChordProAsync` already sets `FolderPath` (SongLibraryService.cs:135-143) and creates the directory.
- `SongManager.Songs` is the in-memory list the library reads from (SongManager.cs:16).

### Songs panel wiring (WorshipPlayer.Presenter\Views\SongLibrary\SongLibraryPanel.xaml[.cs])
- `SongLibraryPanel` is a `UserControl` with `ISongLibraryService? SongLibraryService` property whose setter calls `RefreshSongs()` (SongLibraryPanel.xaml.cs:14-25). `RefreshSongs()` → `LoadSongsAsync()` → `_songLibraryService.GetAllAsync()` repopulates the `Songs` ObservableCollection (SongLibraryPanel.xaml.cs:39-75). **`RefreshSongs()` is public — call it after import to refresh the list.**
- Current XAML has a header, search box, filters, sort buttons, and the list (SongLibraryPanel.xaml:19-116). There is **no** import button yet.
- Instantiated lazily in `OperatorWindow.SwitchToTab("Songs")` (OperatorWindow.xaml.cs:1618-1628): `new SongLibraryPanel()`, sets `SongLibraryService`, subscribes `SongSelected += OnSongSelected`, assigns to `SongsPanelContent.Content`.

### Pattern to mirror: MediaLibraryPanel import (already in the codebase)
- `MediaLibraryPanel` raises an `ImportRequested` event; `OperatorWindow` subscribes it to `OnMediaImportRequested` when the panel is created (OperatorWindow.xaml.cs:1644-1651). `OnMediaImportRequested` shows an `OpenFileDialog` and calls an async import on the service (OperatorWindow.xaml.cs:1688-1719). **This event-to-host pattern is the template for ChordPro import** — it keeps the panel service-agnostic and puts the dialog/host logic in `OperatorWindow`, exactly like Media.

---

## Ordered implementation items

- [ ] 1. Add an "Import ChordPro…" button and an `ImportChordProRequested` event to `SongLibraryPanel`.
      In the XAML, add a `Button` (e.g. under the header `TextBlock`, DockPanel.Dock="Top", styled `OwcButtonBase` to match existing buttons at SongLibraryPanel.xaml:70-78) named `ImportChordProButton` with `Click="ImportChordPro_Click"`. In the code-behind add `public event EventHandler? ImportChordProRequested;` and `private void ImportChordPro_Click(object sender, RoutedEventArgs e) => ImportChordProRequested?.Invoke(this, EventArgs.Empty);`. Mirror the `MediaLibraryPanel.ImportRequested` pattern (OperatorWindow.xaml.cs:1644-1651). Do not reference any service from the panel.
      Files: `WorshipPlayer.Presenter\Views\SongLibrary\SongLibraryPanel.xaml`, `WorshipPlayer.Presenter\Views\SongLibrary\SongLibraryPanel.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` from the worktree root — 0 errors (warnings may remain at the 4 pre-existing count).

- [ ] 2. Subscribe the new event when the Songs panel is created, mirroring the Media panel.
      In `OperatorWindow.SwitchToTab`'s `case "Songs"` block (OperatorWindow.xaml.cs:1618-1628), after the existing `_songLibraryPanel.SongSelected += OnSongSelected;`, add `_songLibraryPanel.ImportChordProRequested += OnImportChordProRequested;` inside the same `if (_songLibraryPanel == null)` guard so it is wired exactly once.
      Files: `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

- [ ] 3. Implement the host handler `OnImportChordProRequested` in `OperatorWindow` that shows the dialog, imports via the existing service, and refreshes the list.
      Add near `OnMediaImportRequested` (OperatorWindow.xaml.cs:~1688). Logic: guard `if (_songLibraryService == null) return;`. Construct `var dialog = new WorshipPlayer.Presenter.Views.SongLibrary.ChordProImportDialog { Owner = this };`. The dialog's own `Browse_Click` already shows an `OpenFileDialog` (ChordProImportDialog.xaml.cs:35-57) and populates `FilePathTextBox`; read the chosen path back from `dialog.FilePathTextBox.Text` after `ShowDialog()`. If `dialog.ShowDialog() == true && dialog.Confirmed` and the path is a non-empty existing file, call the existing import. Because `ImportChordProAsync` is async and the handler is a WPF event, implement the actual work in a private `async Task ImportChordProFromFileAsync(string path)` and invoke it fire-and-forget (`_ = ImportChordProFromFileAsync(path);`) exactly as `OnMediaImportRequested` does `_ = ImportMediaFilesAsync(...)` (OperatorWindow.xaml.cs:1718). In that async method: `try { await _songLibraryService.ImportChordProAsync(path); _songLibraryPanel?.RefreshSongs(); } catch (Exception ex) { ShowToolError("Import ChordPro", ex.Message); }` — reuse the existing `ShowToolError` helper (OperatorWindow.xaml.cs:1889-1896) and the public `RefreshSongs()` (SongLibraryPanel.xaml.cs:39).
      Note on the dialog's OpenFileDialog filter: it currently lists `*.chopro;*.chordpro;*.cho` + `*.txt` (ChordProImportDialog.xaml.cs:42). The task asks for `.chopro,.cho,.pro,.crd,.txt`. Update that `Filter` string in `Browse_Click` to include `*.pro;*.crd` so the picker matches the acceptance list. This is the one small edit to the dialog file; keep it minimal.
      Files: `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`, `WorshipPlayer.Presenter\Views\SongLibrary\ChordProImportDialog.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

- [ ] 4. Add a focused unit test that proves the import-and-persist path works end to end through the service.
      Add `WorshipPlayer.Tests\ChordProImportTests.cs` (xUnit `[Fact]`, matching the existing test style, e.g. ChordOverLyricTests.cs). Arrange a temp songs dir, build `new SongManager(tempSongsDir, tempSetlistsDir)`, `new SongUsageService(tempUsagePath)`, `new SongLibraryService(songManager, usageService)`; seed one existing song folder so `ImportChordProAsync`'s folder-derivation (SongLibraryService.cs:135-137) has a base dir; write a small `.chopro` file with `{title:...}{artist:...}{key:...}` and a couple `[chord]` lines; call `await service.ImportChordProAsync(choproPath)`. Assert: returned `SongLibraryItem.Title`/`Artist`/`Key` match the directives; `songManager.Songs` now contains the new song; the song's folder has a `song.json` on disk; the saved `Song.Sections` are non-empty and retain inline `[chord]` text. Clean up temp dirs in a `finally`/`IDisposable`.
      Files: `WorshipPlayer.Tests\ChordProImportTests.cs`
      Verify: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` — the new ChordPro import test(s) pass and no existing tests regress.

- [ ] 5. Final full-solution verification.
      Build and run the whole test suite to confirm nothing regressed and the clean-build invariant holds.
      Files: none (verification only).
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors (warning count unchanged from the pre-existing 4); then `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` → all tests green. Manual smoke (optional, note in findings if not run — WPF requires a desktop session): launch the app, open the Songs tab, click "Import ChordPro…", pick a `.chopro`, confirm, and verify the song appears in the library list.

---

## Scope guardrails (from the task)
- Changes limited to: SongLibraryPanel (button + event), OperatorWindow (one subscription + one handler), ChordProImportDialog (filter string only), and one new test file. Do NOT touch templates/scripture/media features, the parser, or `SongLibraryService.ImportChordProAsync` itself.
- Reuse existing persistence (`SongManager.SaveSongAsync` via `SongLibraryService.ImportChordProAsync`). No new storage format.
- Keep the build clean (0 errors). The 4 pre-existing warnings are acceptable.

## Open assumptions (reasonable, non-blocking)
- The dialog is used as preview/confirm only; the authoritative import uses `ImportChordProAsync(path)` not the dialog's `ParsedLyrics`. If a reviewer insists the dialog's parsed output must be used verbatim, that would lose chords/sections and contradict "reuse the parser" — flagged here as the intended, better-justified choice.
- Reading the chosen file path back from `dialog.FilePathTextBox.Text` is acceptable because the dialog already validates the pick in `Browse_Click`. If preferred, item 3 may instead expose a public `SelectedFilePath` property on the dialog returning `FilePathTextBox.Text`; functionally equivalent, implementer's choice.
