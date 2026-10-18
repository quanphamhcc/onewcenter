# Report — Service Item Song Picker

## What was implemented

Added a Song/Scripture picker to the Add/Edit Service Item dialog so the service-order
sidebar shows the real song title ("Amazing Grace") or scripture reference ("John 3:16")
instead of the "Song Item" placeholder, and the item is linked to real content.

### Link field
**The ServiceItem field that links the song is `ServiceItem.SongFolder`** (set to the
chosen `Song.FolderPath`). This is the field the playback chain reads (PresenterIntegration
→ MainWindow song resolution) and matches the existing `ServiceOrderManager.CreateFromSetlist`
precedent. Scripture links via `ServiceItem.ScriptureReference`.

### Files changed / added
- **Added** `WorshipPlayer.Core/Services/ServiceItemMapper.cs` — pure, UI-free mapping:
  - `ApplySong(ServiceItem, Song)` → sets `Type=Song`, `SongFolder=song.FolderPath`, `Title=song.Title` (keeps existing Title if song Title blank).
  - `ApplyScripture(ServiceItem, string)` → sets `Type=Scripture`, `ScriptureReference=trimmed`, `Title=trimmed` (blank reference leaves Title).
  - `BuildTitle(type, typedName, pickedSong, scriptureReference)` → single source of truth for Title (song Title > scripture ref > typed name > `"{Type} Item"` fallback).
- **Added** `WorshipPlayer.Tests/ServiceItemMapperTests.cs` — 11 xUnit `[Fact]` tests.
- **Changed** `WorshipPlayer.Presenter/Views/ServiceBuilder/AddServiceItemDialog.xaml` — added `SongPickerPanel` (editable, text-search `ComboBox` showing "Title — Artist") and `ScripturePanel` (reference `TextBox`), plus `SelectionChanged` handlers on the type combo and song combo.
- **Changed** `WorshipPlayer.Presenter/Views/ServiceBuilder/AddServiceItemDialog.xaml.cs` — `SongLibrary` property (property-injection, mirrors `ServiceOrderManager`); populate picker; show/hide by type (`UpdatePickerVisibility`); auto-fill name on song select; edit-mode pre-select via `SongPathResolver.Resolve` / pre-fill scripture; add + edit paths route Title and links through `ServiceItemMapper`.
- **Changed** `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — pass `_songManager.Songs` into the dialog at all three construction sites (`OnNewServiceRequested`, `NewService_Click`, `EditSelectedServiceItem`).

## Wired-path trace (file:line, after edits)

### Item Type = Song (add)
1. Dialog opens, type defaults to Song → `UpdatePickerVisibility()` shows `SongPickerPanel`
   (`AddServiceItemDialog.xaml.cs`, `UpdatePickerVisibility`).
2. Picker is populated from the injected library in `AddServiceItemDialog_Loaded`:
   `SongPickerComboBox.ItemsSource = SongLibrary.Select(s => new SongPickerItem(s))`.
   `SongLibrary` is set from `_songManager.Songs` at `OperatorWindow.xaml.cs:410`
   (OnNewServiceRequested), `~833` (NewService_Click), and in the `EditSelectedServiceItem`
   initializer (`~1438`).
3. Selecting a song → `SongPickerComboBox_SelectionChanged` auto-fills `ItemTitleTextBox.Text`.
4. `AddItem_Click` add path: `title = ServiceItemMapper.BuildTitle(Song, typed, selectedSong, …)`
   → `new ServiceItem { Type, Title }` → `ServiceItemMapper.ApplySong(serviceItem, selectedSong)`
   sets `SongFolder = song.FolderPath` + Title → `ServiceOrderManager.AddItem(serviceItem)` →
   `await ServiceOrderManager.SaveAsync()`.
5. Sidebar: `OperatorWindow.MakeItemRow` sets the row's `Title = item.Title` (OperatorWindow.xaml.cs:1356) → shows "Amazing Grace".
6. Playback: `PresenterIntegration.OnServiceItemChanged` fires `SongLoadRequested(e.NewItem.SongFolder)` (WorshipPlayer.UI/Services/PresenterIntegration.cs ~414-422) →
   `MainWindow.PresenterIntegration_SongLoadRequested` resolves via `FolderPath.EndsWith / Path.GetFileName` (WorshipPlayer.UI/Views/MainWindow.xaml.cs ~625-630). Full `FolderPath` satisfies both.

### Item Type = Scripture (add)
1. Selecting Scripture → `ItemTypeComboBox_SelectionChanged` → `UpdatePickerVisibility()` shows `ScripturePanel`.
2. Reference typed (e.g. "John 3:16") → `AddItem_Click`: `BuildTitle(Scripture, …, scriptureRef)` returns the trimmed reference → `ApplyScripture(serviceItem, scriptureRef)` sets `ScriptureReference` + Title → AddItem → SaveAsync.
3. Sidebar shows the reference as the Title.

### Edit mode pre-selection
- `AddServiceItemDialog_Loaded`: for a Song item, `SongPathResolver.Resolve(SongLibrary, ItemToEdit.SongFolder, ItemToEdit.Title)` finds the linked song and selects it in the combo; for a Scripture item, `ScriptureReferenceTextBox.Text = ItemToEdit.ScriptureReference`.
- `AddItem_Click` edit path: Title re-computed via `BuildTitle`, link re-applied via `ApplySong`/`ApplyScripture`, then `ItemEdited` → OperatorWindow saves (`SaveServiceOrderAsync`).

### Backward compatibility
No storage-format change. Old Song items with a stored `Title` and empty `SongFolder` still load; `BuildTitle` falls back to the stored/typed name and `SongPathResolver.Resolve` falls back to Title match. Persistence stays through `ServiceOrderManager.SaveAsync` with the same JSON fields.

## Build & test results

- `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded. 0 Errors, 4 Warnings** (all pre-existing and unrelated: 2× `MediaLibraryPanel._currentView` CS0414, 1× `ChordChartWindow` CS8604, 1× `WaveformEditorWindow._inReviewMode` CS0414).
- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` → **Passed! Failed: 0, Passed: 620, Skipped: 0, Total: 620**. That is the prior 609 + 11 new `ServiceItemMapperTests`.

## Verified vs. not verified

**Verified (automated):**
- Solution builds with 0 errors.
- `ServiceItemMapper` mapping logic verified by 11 unit tests: chosen Song → Title + `SongFolder`; blank song Title keeps existing Title; null FolderPath → empty string; Scripture reference → Title + `ScriptureReference`; blank reference leaves Title; `BuildTitle` precedence for Song / Scripture / typed name / fallback.
- All prior tests still green.
- Source trace confirmed by reading the files above (link field = `SongFolder`, sidebar reads `Title`, SaveAsync persists).

**NOT verified (requires manual visual testing — WPF dialog interaction cannot be unit-tested headless):**
- The Song picker actually listing songs and type-to-search filtering in the running app.
- Selecting a song visibly updating the Item name box.
- Picker fields showing/hiding live as the Item Type changes.
- Edit-mode combo visibly pre-selecting the linked song and the scripture box pre-filling.
- End-to-end sidebar refresh after Add/Edit and audio loading from `SongFolder` during playback.

These need a manual run of the Presenter: add a Song item, confirm the picker lists library songs, pick one, confirm the name auto-fills and the sidebar shows the real title; repeat for Scripture.
