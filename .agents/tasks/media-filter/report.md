# Completion Report — Media Filter Panel (roadmap F3)

Branch: `feature/media-filter` (worktree). First iteration (no `review.json` present).

## What was implemented

Filter controls added to the reachable **Media** panel (`MediaLibraryPanel`), wired live to the
existing `MediaLibraryService` semantics via a new pure, testable Core helper. No new query layer,
no data-model/storage change, import path untouched (except the single call-site swap noted below).

### Controls (XAML)
Replaced the two non-functional Image/Video checkboxes with a Songs-tab-style filter row
(`MediaLibraryPanel.xaml`):
- `TypeFilterComboBox` (`OwcDarkComboBox`): **All Types / Image / Video / Audio** — covers every
  `MediaLibraryType` value in the model. "All Types" is the default/reset state.
- `TagFilterComboBox` (`OwcDarkComboBox`): **All Tags** default, populated at load from the distinct
  tags present in the library.
- `ClearFiltersButton` (`OwcButtonBase`, "Clear"): reset affordance.
- The existing `SearchTextBox` (name/tag search) is kept.

### Filtering logic
`WorshipPlayer.Core/Services/MediaLibraryFilter.cs` — pure static helper:
- `Apply(items, MediaLibraryType? type, string? tag, string? search)` composes **type AND tag AND
  search**, each null/empty dimension being a no-op (all-null = full list = reset). Semantics mirror the
  service exactly: type equality (`GetByTypeAsync`), case-insensitive exact-tag membership
  (`GetByTagAsync`), case-insensitive name-OR-tag substring (`SearchAsync`).
- `DistinctTags(items)` returns sorted, case-insensitive-distinct tags for the Tag dropdown.

### Why composed client-side
`MediaLibraryService` exposes only **single-dimension** queries (`GetByTypeAsync` OR `GetByTagAsync`
OR `SearchAsync`); there is no combined query. So combined type+tag+search is composed in-memory over
`GetAllAsync()` results in `MediaLibraryFilter`, reusing the same matching rules — no new query layer and
no duplicated per-dimension logic. This also makes the composition unit-testable in Core (the WPF panel
code-behind is not testable in `WorshipPlayer.Tests`, which can only reference `net8.0` Core).

### Panel wiring (`MediaLibraryPanel.xaml.cs`)
- Added `private readonly List<MediaItem> _allItems` as the unfiltered master list. `Items` (bound to
  `ListPanel`) is now always the filtered view.
- `LoadMediaAsync` fills `_allItems` from `GetAllAsync()`, rebuilds the Tag dropdown, then calls
  `ApplyFilters()`.
- `ApplyFilters()` reads selected type (null when "All Types"), selected tag (null when "All Tags"), and
  `SearchTextBox.Text`, calls `MediaLibraryFilter.Apply` over `_allItems`, and refills `Items`.
- `ClearFilters_Click` resets Type→All, Tag→All, clears the search box, and re-applies (returns to full list).
- `AddImportedItem(MediaItem)` appends an imported item to `_allItems`, rebuilds tag options, and
  re-applies filters so imports show live.
- `Search_TextChanged`/`Filter_Changed` stay guarded by `IsLoaded`.

### Import path (preserved)
`OperatorWindow.ImportMediaFilesAsync` previously added directly to `_mediaLibraryPanel.Items`. Because
`Items` is now the filtered view, that one call site was swapped to
`_mediaLibraryPanel.AddImportedItem(...)` so the master list stays authoritative. No change to the import
dialog, `OnMediaImportRequested`, or `MediaLibraryService.ImportAsync`.

## Wired-path trace (file:line)

Type filter changed:
- `MediaLibraryPanel.xaml:84-90` `TypeFilterComboBox` `SelectionChanged="Filter_Changed"`
- `MediaLibraryPanel.xaml.cs` `Filter_Changed` (guarded by `IsLoaded`) → `ApplyFilters()`
- `ApplyFilters()` reads type/tag/search → calls `MediaLibraryFilter.Apply(_allItems.Select(ToLibraryItem), type, tag, search)`
- `MediaLibraryFilter.Apply` (`WorshipPlayer.Core/Services/MediaLibraryFilter.cs`) applies type equality / tag membership / name-tag search
- `ApplyFilters()` clears and refills `Items` from the filtered ids → `ListPanel.ItemsSource = Items` updates live → `UpdateEmptyState()`

Tag filter changed: `TagFilterComboBox` `SelectionChanged="Filter_Changed"` → same path.
Name search: `SearchTextBox` `TextChanged="Search_TextChanged"` → `ApplyFilters()` → same path.
Clear: `ClearFiltersButton` `Click="ClearFilters_Click"` → resets selections + search → `ApplyFilters()` (all no-op → full list).
Load: `MediaLibraryService` setter → `RefreshMedia()` → `LoadMediaAsync()` → `GetAllAsync()` fills `_allItems`, `RebuildTagFilterOptions`, `ApplyFilters()`.
Import (preserved): `MediaLibraryPanel.ImportRequested` → `OperatorWindow.OnMediaImportRequested` → `MediaLibraryService.ImportAsync` → `MediaLibraryPanel.AddImportedItem` → `ApplyFilters()`.

No orphaned controls: both combos and the Clear button have handlers; the old Image/Video checkboxes
and their reliance on the stub `ApplyFilters()` are gone. The service's query semantics are the single
source of filtering behavior, reused via the helper.

## Verification

Run inside the worktree.

- `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded, 0 errors, 4 warning(s)**
  — identical to the recorded baseline (the only Media-related one is the pre-existing
  `MediaLibraryPanel._currentView` CS0414; the other three are in WorshipPlayer.UI). The XAML compiled.
- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` →
  **Passed! Failed: 0, Passed: 497, Skipped: 0, Total: 497** (483 baseline + 14 new
  `MediaLibraryFilterTests`: no-filters, type-only Image/Video/Audio, tag-only case-insensitive + no-match,
  search-by-name, search-by-tag, combined intersection + empty intersection, whitespace no-op, null items,
  and `DistinctTags` sorted/distinct/case-insensitive + null).

### Not verified
- **No headless UI/visual run was performed.** This is a WPF desktop app and a headless render of the
  panel is not possible in this environment. Correctness is established by the clean build (XAML compiles),
  the passing filter unit tests over the exact composition the panel invokes, and the file:line wired-path
  trace above — not by visual inspection.

## Scope confirmation
Changed only: `MediaLibraryFilter.cs` (new), `MediaLibraryFilterTests.cs` (new),
`MediaLibraryPanel.xaml`, `MediaLibraryPanel.xaml.cs`, and one call-site line in `OperatorWindow.xaml.cs`.
No import/song/template/scripture logic touched; model, service, and storage unchanged.
