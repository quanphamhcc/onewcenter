# Completion Report — D2 Song Slide WYSIWYG Editor

Branch: `feature/song-slide-editor` (worktree `.worktrees/song-slide-editor`)
Iteration: FIRST (no `review.json` present at start).

The feature lets the operator open a selected song from the **Songs** tab, pick a
slide (= section), edit its lyric text, and optionally set a per-slide background
image and template. On Save the edits persist to `song.json` via the existing
`SongManager.SaveSongAsync` path, preserving chords, timestamps, section structure,
MIDI cues, and metadata.

---

## Full file:line path trace (end to end)

1. **Entry point** — `SongLibraryPanel.xaml` adds an `Edit Slides…` button
   (`WorshipPlayer.Presenter/Views/SongLibrary/SongLibraryPanel.xaml:36-41`) whose
   `Click="EditSlides_Click"` handler
   (`SongLibraryPanel.xaml.cs:112-115`) raises `EditSlidesRequested`
   (declared `SongLibraryPanel.xaml.cs:48`). The panel stays service-agnostic,
   mirroring `EditMetadataRequested`.

2. **Subscription** — `OperatorWindow.SwitchToTab("Songs")` subscribes
   `_songLibraryPanel.EditSlidesRequested += OnEditSlidesRequested`
   (`OperatorWindow.xaml.cs:1627`, next to the metadata subscription).

3. **Handler** — `OperatorWindow.OnEditSlidesRequested`
   (`OperatorWindow.xaml.cs:1786-1818`): guards `_songLibraryService`/`_songManager`,
   reads `_songLibraryPanel.SelectedSong`, resolves the in-memory `Song` via
   `SongPathResolver.Resolve(_songManager.Songs, selected.Id, selected.Title)`
   (same robust resolution as `OnSongSelected`/`OnEditMetadataRequested`), guards
   empty-section songs, then calls the async `EditSongSlidesAsync`.

4. **Picker build + modal** — `OperatorWindow.EditSongSlidesAsync`
   (`OperatorWindow.xaml.cs:1820-1860`): builds the background image list from the
   existing `_mediaLibraryService.GetByTypeAsync(MediaLibraryType.Image)` (null-safe,
   empty if the service is null) and the template list from the existing
   `_templateService.GetAllAsync()` (null-safe). Opens
   `new SongSlideEditorWindow(song.Sections, imagePaths, templates) { Owner = this }`
   as a modal.

5. **Editor** — `SongSlideEditorWindow` (`.xaml` hosts `local:SongSlideEditor`;
   `.xaml.cs:19-30` calls `Editor.LoadSong(...)` and bridges `SaveRequested`→
   `Result = sections` + `DialogResult = true`, `CancelRequested`→`DialogResult = false`,
   mirroring `SongMetadataEditorWindow`). `SongSlideEditor.LoadSong`
   (`SongSlideEditor.xaml.cs:40-66`) binds the section list, lyric `TextBox`, a
   Background picker (`(None)` + image paths) and a Template picker (`(Default)` +
   templates). Selecting a section loads its Text/BackgroundImage/TemplateId;
   editing writes straight back onto that `Section` instance
   (`SectionList_SelectionChanged` + the `*_Changed`/`Lyrics_TextChanged` handlers,
   `SongSlideEditor.xaml.cs:72-120`).

6. **Persist** — on `DialogResult == true`, `EditSongSlidesAsync` calls
   `_songLibraryService.UpdateSlidesAsync(songId, window.Result)`
   (`OperatorWindow.xaml.cs:1853`). `SongLibraryService.UpdateSlidesAsync`
   (`SongLibraryService.cs:104-128`) resolves the `Song` (same `FolderPath`/folder-name
   match as `UpdateMetadataAsync`) and copies ONLY `Text`, `BackgroundImage`, and
   `TemplateId` (matched by index) onto the stored sections — loop points, bars,
   color, vocal pickup, lighting, and `CanLoop` are untouched; `ChordChart`,
   `MidiCues`, `BeatPositionsMs`, tracks, and metadata are never read. Then
   `await _songManager.SaveSongAsync(song)`.

7. **song.json** — `SongManager.SaveSongAsync` backs up (5-version) and re-serializes
   the whole `Song` with `DefaultIgnoreCondition.WhenWritingNull`, so unset
   `BackgroundImage`/`TemplateId` are omitted and all other fields survive verbatim.

8. **Refresh** — `EditSongSlidesAsync` calls `_songLibraryPanel?.RefreshSongs()`
   (`OperatorWindow.xaml.cs:1854`) to refresh the list.

**No orphaned `new`:** `SongSlideEditor` is now constructed exclusively by
`SongSlideEditorWindow.xaml` (`<local:SongSlideEditor x:Name="Editor"/>`), which is
constructed by `OperatorWindow.EditSongSlidesAsync`, which is reached from the panel
button → event → subscription. The previously-orphaned control is now on a live path.

---

## Additive model change (backward-compatible)

`WorshipPlayer.Core/Models/Song.cs`, class `Section`: added two nullable string
properties — `BackgroundImage` (`[JsonPropertyName("backgroundImage")]`) and
`TemplateId` (`[JsonPropertyName("templateId")]`). Both default to null; combined
with `SongManager`'s `DefaultIgnoreCondition.WhenWritingNull`, older `song.json`
files without these fields still deserialize (fields stay null) and re-serialize
without noise. No new file format, no version bump. A dedicated unit test
(`SectionDeserialization_WithoutNewFields_IsBackwardCompatible`) proves legacy JSON
loads with both fields null.

---

## Built-vs-wired breakdown (honest)

**Built new (did not exist):**
- `Section.BackgroundImage` / `Section.TemplateId` (additive storage).
- `SongSlideEditor` rebuilt from a single flat-string + hardcoded-combo stub into a
  section-list editor (XAML + code-behind fully rewritten).
- `SongSlideEditorWindow` modal host (new `.xaml` + `.xaml.cs`).
- `SongLibraryPanel` `Edit Slides…` button + `EditSlidesRequested` event +
  `EditSlides_Click`.
- `OperatorWindow.OnEditSlidesRequested` + `EditSongSlidesAsync` + the subscription.
- `ISongLibraryService.UpdateSlidesAsync` + its `SongLibraryService` implementation.
- Two unit tests.

**Reused read-only (not duplicated):**
- `SongManager.SaveSongAsync` + JSON options (persistence/backup).
- `ITemplateService.GetAllAsync` (template choices).
- `IMediaLibraryService.GetByTypeAsync(MediaLibraryType.Image)` (background images).
- `SongPathResolver.Resolve` (robust song resolution).
- `SongMetadataEditorWindow` pattern (modal host structure).
- `ShowToolError` (error surfacing).

**Deliberately out of scope (noted in plan):** honoring the per-section
background/template at *display* time (`LyricSlideGenerator` still reads only
`Section.Text`). D2 is storage + editor only; display is a follow-up.

---

## Build & test results (exact)

Build: `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo`
→ **Build succeeded with 4 warning(s) in 3.0s. Exit code 0. 0 errors.**
The 4 warnings are pre-existing (MediaLibraryPanel CS0414, ChordChartWindow CS8604,
WaveformEditorWindow CS0414) — unchanged from baseline; none in the files I touched.

Test: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug --nologo`
→ **Passed! Failed: 0, Passed: 483, Skipped: 0, Total: 483. Exit code 0.**
Baseline was ~481; +2 from this change:
- `UpdateSlidesAsync_PersistsLyricEdit_PreservesChordsTimestampsMetadata`
- `SectionDeserialization_WithoutNewFields_IsBackwardCompatible`

The round-trip test arranges a song with metadata, inline `[chords]`/`{timestamps}`,
a non-null `ChordChart`, and a `MidiCue`; edits slide-1 text + sets background/template
via `UpdateSlidesAsync`; reloads via a fresh `SongManager`; and asserts the edit
survived while the unedited section's `[...]`/`{...}` markers, loop/bar structure,
`ChordChart`, `MidiCues`, and all metadata are intact.

---

## What was and was NOT verified

**Verified:** clean solution build (0 errors, 4 pre-existing warnings); full test
suite green (483/483); the end-to-end wired path by file:line (above) with no
orphaned `new`; persistence round-trip and backward-compat via automated tests.

**NOT verifiable headless (WPF desktop):** this is a WPF app and no headless UI
harness is available here, so I did **not** run the GUI or visually confirm rendering.
Specifically unverified by execution:
- Visual appearance/layout of the `SongSlideEditor` (section list | editor | preview)
  and the `SongSlideEditorWindow` chrome.
- Live text preview updates and combo-box population at runtime (logic is covered by
  the wired-path trace but not by a rendered UI run).
- The background/template pickers reflecting real media/template libraries at runtime
  (depends on services populated in a live composition).
Correctness for these rests on the clean XAML+C# build, the passing persistence tests,
and the traced wiring — not on a visual run.
