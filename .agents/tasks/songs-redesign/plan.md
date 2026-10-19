# Implementation Plan — Songs tab redesign (ProPresenter-style table)

Scope: redesign `SongLibraryPanel.xaml` + `.xaml.cs` into a dark, table-style song library (ListView+GridView) matching the Services-tab reference, while preserving every existing event, handler, public member, and the OperatorWindow event contract. Shared style additions in `OwcDesignSystem.xaml` are allowed but kept minimal. Do NOT change OperatorWindow's event contract, Core models, storage, or data files.

All paths below are under the worktree root:
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\songs-redesign\`

---

## Design decisions (made here, grounded in the code)

1. **Table control = `ListView` + `GridView`**, reusing the Services-tab pattern (`WorshipPlayer.Presenter/Views/ServiceBuilder/ServicesListView.xaml`): `Style="{StaticResource OwcDarkListView}"` on the ListView and `ColumnHeaderContainerStyle="{StaticResource OwcDarkGridViewColumnHeader}"` on the GridView. Rationale: those keyed styles already exist in the worktree `OwcDesignSystem.xaml` (verified) and give the exact dark/rounded theme; chosen over a hand-rolled `Grid` template so headers, hover, and selection match the rest of the app with zero new styling.

2. **Keep the ListView named `SongListBox`** (its existing `x:Name`). Rationale: `SelectedSong => SongListBox?.SelectedItem as SongListItem` and the constructor's `SongListBox.ItemsSource = Songs` / `UpdateEmptyState()` reference that exact name. A `ListView` IS a `Selector`/`ListBox` subclass, so `SelectedItem`, `SelectionChanged`, `ItemsSource`, and `Visibility` all keep working unchanged. Keeping the name avoids touching `SelectedSong`, `UpdateEmptyState`, and the constructor.

3. **"Filters ▾" = a styled row of the EXISTING combos** (All Keys / All Artists) plus the A-Z/Date sort buttons, kept inline in the toolbar (the brief allows "a cleanly styled row of the existing combos"). Rationale: the existing `Filter_Changed` and sort handlers are wired by `x:Name` (`KeyFilterComboBox`, `ArtistFilterComboBox`, `SortAZButton`, `SortDateButton`); keeping them as real controls preserves behavior with no code-behind change. A popup is not required and would add a container that risks detaching the named controls. Label the group with a non-interactive "Filters" `TextBlock` for the reference look.

4. **"+ Add Song" (toolbar, blue, right-aligned) → `ImportChordProRequested`.** It reuses the existing `ImportChordPro_Click` handler (wired in OperatorWindow to the ChordPro import + `RefreshSongs()`). Rationale: the brief says the prominent blue toolbar button is wired to the existing ChordPro import. The old `ImportChordProButton` is repurposed into this blue CTA (same handler, restyled to `OwcBlueButton`, relabeled "+ Add Song"). State in code comment that it imports a song.

5. **Per-row blue "+ Add" → `AddSongToServiceRequested`** via the existing `AddToService_Click`, reading `((FrameworkElement)sender).Tag` bound to the row `SongListItem`. Rationale: unchanged mechanism; adding does not change selection.

6. **Per-row ⋮ overflow menu** reusing the Services `Overflow_Click` + `ContextMenu` pattern, with items **Edit Metadata…** and **Edit Slides…**. Rationale: OperatorWindow's `OnEditMetadataRequested`/`OnEditSlidesRequested` read `_songLibraryPanel.SelectedSong`, so the menu MUST set list selection to its row before raising the event (see item 4 of the edits). The old top `EditMetadataButton`/`EditSlidesButton` are removed from the toolbar and their capability moves into this per-row menu; their handlers `EditMetadata_Click`/`EditSlides_Click` are REUSED by the menu items (events unchanged).

7. **Columns:** `☐` (checkbox placeholder) | **Song** (bold `Title` + muted second line `Artist`) | **Key** | **Last modified** | **Last modified by** | `+ Add` | `⋮`.

## Placeholder decisions (stated, no data/model changes)

- **Key** — real: `{Binding Key}`.
- **Song** — real `Title` (bold) + `Artist` on muted second line; if `Artist` is empty the line renders blank (acceptable per brief). Note: the pipeline carries `Artist` only (no `Composer` reaches `SongListItem`); display `Artist`.
- **Last modified** — derive from the song folder's real filesystem timestamp. `SongListItem.Id` = `song.FolderPath` (confirmed in `SongLibraryService.MapToLibraryItem`). Compute `LastModifiedDisplay` in `LoadSongsAsync` using `Directory.GetLastWriteTime(Id)` (fallback to the `song.json` file time if the dir time is unavailable), formatted short (e.g. `yyyy-MM-dd`); on any exception or missing path show `"—"`. This reads the filesystem only — it does NOT write song.json or change models. (The source `UpdatedAt` is `DateTime.UtcNow` at map time, i.e. not persisted, so it is deliberately not used for this column.)
- **Last modified by** — static placeholder `"Local"`.
- **Checkbox** — placeholder affordance: an unbound `CheckBox` per row (no backing field, not wired); purely visual.
- **⋮** — real menu (Edit Metadata…/Edit Slides…), not a placeholder.

These placeholder strings live in the display model / code-behind only; `song.json` and Core models are untouched.

---

## Ordered steps

- [ ] 1. Extend the display model `SongListItem` (in `SongLibraryPanel.xaml.cs`) with two read-only-ish display fields: `string LastModifiedDisplay { get; set; } = "—";` and `string LastModifiedBy { get; set; } = "Local";`. Do NOT touch the Core `SongLibraryItem` model or any `*.cs` under `WorshipPlayer.Core`.
      Files: `WorshipPlayer.Presenter/Views/SongLibrary/SongLibraryPanel.xaml.cs`
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj -c Debug` succeeds.

- [ ] 2. In `LoadSongsAsync` populate the new fields when mapping each item: set `LastModifiedBy = "Local"`, and compute `LastModifiedDisplay` from the folder timestamp — `try { var p = item.Id; LastModifiedDisplay = Directory.Exists(p) ? Directory.GetLastWriteTime(p).ToString("yyyy-MM-dd") : (File.Exists(Path.Combine(p, "song.json")) ? File.GetLastWriteTime(Path.Combine(p, "song.json")).ToString("yyyy-MM-dd") : "—"); } catch { LastModifiedDisplay = "—"; }`. Add `using System.IO;` if not present. Leave the rest of the method (Id/Title/Artist/Key/DateAdded, exception logging, `UpdateEmptyState()`) unchanged.
      Files: `WorshipPlayer.Presenter/Views/SongLibrary/SongLibraryPanel.xaml.cs`
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj -c Debug` succeeds.

- [ ] 3. Add two small code-behind handlers to `SongLibraryPanel.xaml.cs` (append near the existing handlers):
      (a) `Overflow_Click(object sender, RoutedEventArgs e)` — mirrors `ServicesListView.Overflow_Click`: `if (sender is Button btn && btn.ContextMenu != null) { btn.ContextMenu.PlacementTarget = btn; btn.ContextMenu.IsOpen = true; }`.
      (b) `RowMenu_SelectRow(object sender, RoutedEventArgs e)` helper OR fold the selection into the menu-item click path: because OperatorWindow's Edit handlers read `SelectedSong`, the ⋮ MenuItems for Edit Metadata/Edit Slides must FIRST set `SongListBox.SelectedItem = ((FrameworkElement)sender).Tag` (the row's `SongListItem`), THEN call the existing `EditMetadata_Click`/`EditSlides_Click` bodies. Implement two thin wrappers `EditMetadataMenu_Click`/`EditSlidesMenu_Click` that do `if (((FrameworkElement)sender).Tag is SongListItem it) SongListBox.SelectedItem = it;` then `EditMetadataRequested?.Invoke(this, EventArgs.Empty);` / `EditSlidesRequested?.Invoke(this, EventArgs.Empty);`. Keep the original `EditMetadata_Click`/`EditSlides_Click` methods (still used as the invoke bodies or call them directly). Do not remove `ImportChordPro_Click`, `AddToService_Click`, `SearchTextBox_TextChanged`, `Filter_Changed`, `SortAZ_Click`, `SortDate_Click`, `SongList_SelectionChanged`.
      Files: `WorshipPlayer.Presenter/Views/SongLibrary/SongLibraryPanel.xaml.cs`
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj -c Debug` succeeds.

- [ ] 4. Rewrite the XAML body of `SongLibraryPanel.xaml` into the table layout. Keep the root `UserControl` + merged `OwcDesignSystem.xaml` ResourceDictionary and the outer `Border Style="{StaticResource OwcPanelBorder}"` exactly. Inside, replace the DockPanel contents with:
      - **Header** `TextBlock` "Song Library" (keep).
      - **Toolbar row** (`DockPanel.Dock="Top"`, horizontal): a muted "Filters" `TextBlock` label; the EXISTING `KeyFilterComboBox` (All Keys list, `Style=OwcDarkComboBox`, `SelectionChanged="Filter_Changed"`) and `ArtistFilterComboBox` (All Artists, same); the EXISTING `SortAZButton` (`Click="SortAZ_Click"`) and `SortDateButton` (`Click="SortDate_Click"`); the EXISTING `SearchTextBox` (`Style=OwcTextBox`, `TextChanged="SearchTextBox_TextChanged"`), given a wide width; and a right-aligned blue `ImportChordProButton` relabeled `Content="+ Add Song"`, `Style="{StaticResource OwcBlueButton}"`, `Click="ImportChordPro_Click"`. Preserve ALL these `x:Name`s and handler names verbatim.
      - **Empty state** `TextBlock x:Name="EmptyStateText"` (keep; `UpdateEmptyState` toggles it).
      - **Table**: `ListView x:Name="SongListBox" Style="{StaticResource OwcDarkListView}" SelectionChanged="SongList_SelectionChanged" Visibility="Collapsed"` with `ListView.View` = `GridView ColumnHeaderContainerStyle="{StaticResource OwcDarkGridViewColumnHeader}"` and columns:
        1. `""` width ~36 — `CheckBox` (unbound placeholder), `VerticalAlignment=Center`.
        2. `"Song"` width ~240 — `StackPanel`: `TextBlock {Binding Title}` bold primary; `TextBlock {Binding Artist}` muted secondary.
        3. `"Key"` width ~60 — `TextBlock {Binding Key}`.
        4. `"Last modified"` width ~120 — `TextBlock {Binding LastModifiedDisplay}` muted.
        5. `"Last modified by"` width ~120 — `TextBlock {Binding LastModifiedBy}` muted.
        6. `""` width ~70 — blue `Button Content="+ Add" Style="{StaticResource OwcBlueButton}" Tag="{Binding}" Click="AddToService_Click"`.
        7. `""` width ~44 — `Button Content="⋮" (&#x22EE;) Style="{StaticResource OwcButtonBase}" Click="Overflow_Click"` with a `Button.ContextMenu`/`ContextMenu` containing `MenuItem Header="Edit Metadata…" Click="EditMetadataMenu_Click" Tag="{Binding}"` and `MenuItem Header="Edit Slides…" Click="EditSlidesMenu_Click" Tag="{Binding}"`.
      Remove the old top button row (`EditMetadataButton`/`EditSlidesButton` and the standalone import button styling) and the old `ListBox`+`DataTemplate`; the `ScrollViewer` wrapper is no longer needed (ListView scrolls itself) — if kept, it must not steal the `SongListBox` name. Note: moving `SearchTextBox`/combos into the toolbar does not change their names or handlers, so `ApplyFilters`, `Filter_Changed`, and `SearchTextBox_TextChanged` stay valid.
      Files: `WorshipPlayer.Presenter/Views/SongLibrary/SongLibraryPanel.xaml`
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj -c Debug` succeeds (XAML compiles; all `StaticResource` keys resolve; all `Click`/`TextChanged`/`SelectionChanged` names bind to existing handlers).

- [ ] 5. (If any color/width constant is reused and not available) add at most one small keyed style to `OwcDesignSystem.xaml` — only if step 4 needs it. Default expectation: NO new style is required (OwcDarkListView, OwcDarkGridViewColumnHeader, OwcBlueButton, OwcButtonBase, OwcDarkComboBox, OwcTextBox, OwcPanelBorder all already exist). Skip this step if unused.
      Files: `WorshipPlayer.Presenter/Themes/OwcDesignSystem.xaml`
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj -c Debug` succeeds.

- [ ] 6. Full-solution build to confirm OperatorWindow's wiring still compiles against the unchanged public surface (`SongLibraryService` setter, `SelectedSong`, `RefreshSongs()`, and the five events). Do NOT edit `OperatorWindow.xaml.cs`.
      Files: none (verification only).
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` succeeds with 0 errors.

- [ ] 7. Manual STA smoke check (run the app, open Songs tab) to confirm: list renders as a table; search filters; key/artist combos and A-Z/Date sort respond; "+ Add Song" opens ChordPro import; per-row "+ Add" adds to service without changing selection; ⋮ → Edit Metadata…/Edit Slides… open the respective editors for that row; empty state shows when no songs.
      Files: none (manual verification).
      Verify: `dotnet run --project WorshipPlayer.UI` (or launch the built exe); exercise the Songs tab per the checklist. If a headless/STA run is not possible in this environment, state that and rely on steps 4 and 6 builds.

---

## Control → event mapping (proves every capability is preserved)

| Capability | Control (x:Name) | Event/handler | Raises panel event → OperatorWindow handler |
|---|---|---|---|
| Search | `SearchTextBox` | `SearchTextBox_TextChanged` → `ApplyFilters` | (internal) |
| Filter by key | `KeyFilterComboBox` | `Filter_Changed` | (internal) |
| Filter by artist | `ArtistFilterComboBox` | `Filter_Changed` | (internal) |
| Sort A-Z | `SortAZButton` | `SortAZ_Click` | (internal) |
| Sort by date | `SortDateButton` | `SortDate_Click` | (internal) |
| Import ChordPro / "+ Add Song" | `ImportChordProButton` | `ImportChordPro_Click` | `ImportChordProRequested` → `OnImportChordProRequested` |
| Edit Metadata | ⋮ MenuItem | `EditMetadataMenu_Click` (selects row, then invokes) | `EditMetadataRequested` → `OnEditMetadataRequested` (reads `SelectedSong`) |
| Edit Slides | ⋮ MenuItem | `EditSlidesMenu_Click` (selects row, then invokes) | `EditSlidesRequested` → `OnEditSlidesRequested` (reads `SelectedSong`) |
| Add to service | per-row `+ Add` | `AddToService_Click` (reads `Tag`) | `AddSongToServiceRequested` → `OnAddSongToServiceRequested` |
| Select song | `SongListBox` (ListView) | `SongList_SelectionChanged` | `SongSelected` → `OnSongSelected` |
| Host reads selection | `SelectedSong` getter (`SongListBox.SelectedItem`) | — | used by `OnEditMetadataRequested`/`OnEditSlidesRequested` |
| Host refresh | `RefreshSongs()` / `Songs` | — | called after import/metadata/slides edits |

OperatorWindow subscribes to exactly: `SongSelected`, `ImportChordProRequested`, `EditMetadataRequested`, `EditSlidesRequested`, `AddSongToServiceRequested` (OperatorWindow.xaml.cs ~lines 2734-2738). All five remain raised; none removed. `SongLibraryService` setter, `SelectedSong`, and `RefreshSongs()` keep their signatures.

## Guardrails / do-not-touch

- No changes to `OperatorWindow.xaml(.cs)` event wiring, no removed events.
- No changes to Core models (`SongLibraryItem`, `Song`), `SongLibraryService`, storage, or any `song.json` / data files.
- New display-only fields (`LastModifiedDisplay`, `LastModifiedBy`) live on the Presenter `SongListItem` display model only.
- Preserve every existing `x:Name` and handler method name listed above; keep the ListView named `SongListBox`.

## Gaps / assumptions

- The app has no real "last modified by" user concept, so `"Local"` is a clear placeholder (brief-approved).
- Folder timestamp is a reasonable real signal for "Last modified"; if `Id` is not a valid path at runtime the code falls back to `"—"`.
- "Filters ▾" is realized as an inline styled combo row (brief-permitted) rather than a popup, to avoid re-parenting the named filter controls and risking handler/selection regressions.
