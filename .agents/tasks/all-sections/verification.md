# Verification — All-Sections Center Pane (ProPresenter-style)

Branch: `feature/all-item-sections` (worktree `.worktrees/all-sections`)

## Build & Test results (exact)

- `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo`
  → **Build succeeded with 4 warning(s)** — **0 errors**. The 4 warnings are pre-existing and
  unrelated to this change:
  - `MediaLibraryPanel.xaml.cs(16,20): CS0414 _currentView assigned but never used`
  - `ChordChartWindow.xaml.cs(235,51): CS8604 possible null arg 'lyricText'`
  - `WaveformEditorWindow.xaml.cs(1775,18): CS0414 _inReviewMode assigned but never used`
  - (one duplicate of the MediaLibrary warning emitted during the WPF temp-assembly pass)

- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --nologo`
  → **total: 624, failed: 0, succeeded: 624, skipped: 0**. Previous baseline was 621; this change
  adds 3 tests in `GenerateSlidesForSongTests.cs`. No regressions. (The console line
  "Error loading service order: 't' is an invalid start..." is the expected log from
  `DefensiveLoadTests`, which asserts the loader survives malformed JSON — not a failure.)

## File:line trace of the feature

All paths relative to the worktree root.

### Pure, non-mutating slide generator (so sections can preview non-live items)
- `WorshipPlayer.Presenter/Services/PresenterEngine.cs`
  - `public List<Slide> GenerateSlidesForSong(Song song)` — fresh list, no `_stateLock`, no writes
    to `_currentSlides`/`_currentSlideIndex`. Holds the exact block/timestamp logic.
  - `private void GenerateSlidesFromSong(Song song)` — now calls `GenerateSlidesForSong` then
    publishes under `_stateLock` (live/`LoadStandaloneSong`/`LoadSong`/backtrack path unchanged).
- Unit tests: `WorshipPlayer.Tests/GenerateSlidesForSongTests.cs` — parity (slide/section counts),
  purity (`engine.TotalSlides` stays 0 after a call), and empty-song safety.

### RefreshSlideSections (the all-sections rebuild)
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` → `private void RefreshSlideSections()`.
  Clears `SlideSectionsPanel.Children` + `_sectionByItem`; null/empty order → shows `EmptyStateText`
  and returns; otherwise hides it, reads `thumbWidth` from `ZoomSlider`, then iterates
  `order.Items` in order building one section (`BuildSectionHeader` + `WrapPanel` of thumbnails)
  per item inside a per-item try/catch. Eager synchronous build; no async / no long sync loop on
  the UI thread.

### Section map (ServiceItem → section element)
- `OperatorWindow.xaml.cs` → `private readonly Dictionary<ServiceItem, FrameworkElement> _sectionByItem`.
  Cleared at the top of `RefreshSlideSections`; populated with `_sectionByItem[item] = sectionRoot;`
  (the section's root `StackPanel`) for each built section. Reference equality against the same
  `ServiceItem` instances the sidebar rows hold via `ServiceItemDisplay.Item`.

### Selection → scroll (not replace)
- `OperatorWindow.xaml.cs` → `ServiceOrderList_SelectionChanged` keeps the early-outs and
  `items.IndexOf` + `_serviceOrderManager?.GoToItem(index)` (engine/live/transport intact), then
  calls `ScrollToSection(display.Item)`. The `LoadStandaloneSong` block + old
  `RefreshSlideThumbnails()` call were removed; no section is cleared.
- `private void ScrollToSection(ServiceItem item)` — `_sectionByItem.TryGetValue` then
  `el.BringIntoView()` (UI thread, non-blocking). `GoToItem` fires `ItemChanged` →
  `RefreshSlideSections` (synchronous rebuild) before the scroll, so the map is fresh.

### Per-item slide acquisition
- `OperatorWindow.xaml.cs` → `private List<Slide> GetSlidesForItem(ServiceItem item)`. Song →
  pre-baked `item.Slides` if present else `SongPathResolver.Resolve(...)` +
  `_engine.GenerateSlidesForSong(song)`; other types → `item.Slides`. Returns empty on unresolved
  song / missing engine or song manager (guarded; caller renders a "could not load" note). Never
  persists generated slides.

### Live RED slide (live item only)
- `RefreshSlideSections` computes `isLiveSection = (itemIndex == _engine.CurrentItemIndex)` and
  `isActive = isLiveSection && slideIndex == _engine.CurrentSlideIndex`; non-live sections render
  every thumbnail neutral.
- `private void UpdateLiveSectionHighlights()` — locates the live item via
  `_engine.CurrentItemIndex` → `order.Items[liveIndex]` → `_sectionByItem[liveItem]`, finds that
  section's thumbnail `WrapPanel`, and reapplies RED/cyan/neutral border + `UpdateLabelChipLiveState`
  scoped to the live section only. Falls back to `RefreshSlideSections()` if the map is stale.
  Called from `OnSlideChanged` and `EditModeToggle_Click`.

### Thumbnail visual (reused exactly)
- `private Border BuildSlideThumbnail(Slide, int slideNumber, bool isActive, bool isSelected,
  double thumbWidth, Action<int> onClick)` — extracted verbatim from the old per-slide loop:
  `ResolveSlideBackground` + scrim, center lyric text, label chip (tagged `"labelChip"`, RED when
  live), number badge, RED live frame / cyan selected frame / neutral border.

### Zoom
- `ZoomSlider_ValueChanged` → `RefreshSlideSections()`; `thumbWidth = ZoomSlider.Value` is shared
  across ALL sections, so zoom resizes every thumbnail in every section.

### Empty-state
- `RefreshSlideSections`: `EmptyStateText.Visibility = Visible` + return when
  `_engine == null || CurrentServiceOrder == null || Items.Count == 0`; `Collapsed` otherwise.

### Section header + Edit affordance
- `private UIElement BuildSectionHeader(ServiceItem item)` — title + icon (reusing `MakeItemRow`
  icon mapping) left, non-functional `ORDER [Original]` chip, `Edit` button right wired to
  `EditServiceItemDirect(item)` which opens the existing `AddServiceItemDialog` with
  `ItemToEdit = item`. Inline brushes only.

## XAML changes
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml`: the center `ScrollViewer` gains
  `x:Name="SlideSectionsScrollViewer"`; `<WrapPanel x:Name="SlideThumbnailsPanel">` replaced by
  `<StackPanel x:Name="SlideSectionsPanel">` inside the same `ScrollViewer`/`Grid`. Zoom slider and
  all inline brushes unchanged.
- StaticResource-free invariant confirmed: `Select-String ... -Pattern StaticResource` matches only
  pre-existing *comments*, no actual `StaticResource` usages.

## Perf / safety notes
- Eager synchronous build mirrors the proven single-item path cost, looped over a handful of items;
  no `async`/`Task` and no long blocking loop is introduced on the UI thread (avoids the prior
  "Not Responding"). `_sectionByItem` is the hook for lazy per-section fill if a very large service
  later needs it.
- The live path is byte-for-byte unchanged: `GenerateSlidesFromSong` still publishes under
  `_stateLock`; sections only ever call the pure `GenerateSlidesForSong`, so go-live / F2-F3 /
  backtrack flows are untouched. No data files were modified.
