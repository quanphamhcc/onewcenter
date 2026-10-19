# Completion Report — All-service-items stacked sections in the center pane

Branch: `feature/all-item-sections`
Worktree: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections`
Review verdict: **APPROVED** (`.agents/tasks/all-sections/review.json`)

## What shipped

The center/Live pane in `OperatorWindow` no longer shows only the one selected item's slides. It
now renders every service item as its own vertically stacked section (a header with the item icon +
title + an Edit affordance + a non-functional `ORDER [Original]` chip, then a `WrapPanel` of that
item's slide thumbnails). Clicking an item in the left SERVICE ORDER sidebar scrolls the right pane
to the matching section via `BringIntoView` instead of replacing the content, so all sections stay
rendered. The single `SlideThumbnailsPanel` WrapPanel was replaced by a `SlideSectionsPanel`
StackPanel inside the same `ScrollViewer`, and the per-slide thumbnail visual was extracted verbatim
into `BuildSlideThumbnail` so sections reuse the exact existing look.

## File:line trace

All paths relative to the worktree root. Line numbers verified against the committed source.

### New all-sections rebuild — one section per item, reusing the thumbnail visual
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:1867` — `private void RefreshSlideSections()`.
  Clears `SlideSectionsPanel.Children` and the section map, shows the empty-state and returns when
  there is no order/items, otherwise reads `thumbWidth` from `ZoomSlider`, then iterates
  `order.Items` in order. Each iteration builds a section root `StackPanel` (`:1908`) with
  `BuildSectionHeader(item)` (`:1911`) + a `WrapPanel` grid (`:1914`) populated by `BuildSlideThumbnail`
  (`:1936`), inside a per-item `try/catch` (`:1910`, `:1963`) so one bad item cannot abort the build.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:2132` — `private Border BuildSlideThumbnail(...)`
  extracted verbatim from the old per-slide loop: `ResolveSlideBackground` + scrim, center lyric text,
  label chip (`Tag = "labelChip"`, `:2223`), number badge, and the RED live / cyan selected / neutral
  border frame.

### Section map (ServiceItem -> section element)
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:1854` —
  `private readonly Dictionary<ServiceItem, FrameworkElement> _sectionByItem`. Cleared at the top of
  `RefreshSlideSections` (`:1870`) and populated per section via `_sectionByItem[item] = sectionRoot;`
  (`:1974`). Keyed by reference against the same `ServiceItem` instances the sidebar rows hold via
  `ServiceItemDisplay.Item`.

### ServiceOrderList selection -> scroll-to-section via BringIntoView
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:1434` — `ServiceOrderList_SelectionChanged`
  keeps its early-outs + `items.IndexOf(display.Item)` + `_serviceOrderManager?.GoToItem(index)`
  (`:1453`), then calls `ScrollToSection(display.Item)` (`:1458`). `GoToItem` fires `ItemChanged ->
  RefreshSlideSections` synchronously first, so the map is fresh before the scroll. The old
  `LoadStandaloneSong` block + `RefreshSlideThumbnails()` call were removed; no section is cleared.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:2270` — `private void ScrollToSection(ServiceItem item)`:
  `_sectionByItem.TryGetValue(item, out var el)` then `el.BringIntoView()` (`:2276`) on the UI thread,
  non-blocking.

### Per-item slide acquisition — Song resolve+generate vs pre-baked
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:2099` — `private List<Slide> GetSlidesForItem(ServiceItem item)`.
  Song item: returns pre-baked `item.Slides` if present, else resolves via
  `SongPathResolver.Resolve(_songManager.Songs, item.SongFolder, item.Title)` and generates on-demand
  through `_engine.GenerateSlidesForSong(song)`; unresolved song / missing engine or song manager
  returns an empty list (caller renders a "could not load" note at `:1966`). Other item types return
  `item.Slides` (or empty). Generated slides are never persisted.
- `WorshipPlayer.Presenter/Services/PresenterEngine.cs:466` — `public List<Slide> GenerateSlidesForSong(Song song)`:
  pure, builds a fresh list, no `_stateLock`, no writes to `_currentSlides`/`_currentSlideIndex`.
- `WorshipPlayer.Presenter/Services/PresenterEngine.cs:445` — `private void GenerateSlidesFromSong(Song song)`
  now calls the pure method (`:447`) then publishes under `_stateLock`, leaving the live / go-live /
  backtrack path byte-for-byte unchanged.

### Live-slide RED still works for the active item
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` (inside `RefreshSlideSections`) —
  `isLiveSection = (itemIndex == _engine.CurrentItemIndex)` and
  `isActive = isLiveSection && slideIndex == _engine.CurrentSlideIndex` (`:1935` area); non-live
  sections render every thumbnail neutral.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:2287` — `private void UpdateLiveSectionHighlights()`
  locates the live item via `_engine.CurrentItemIndex` -> `order.Items[liveIndex]` -> `_sectionByItem[liveItem]`,
  recolors only that section's thumbnail `WrapPanel` (RED/cyan/neutral + `UpdateLabelChipLiveState`),
  and falls back to a full `RefreshSlideSections()` (`:2303`) if the map is stale. Called from
  `OnSlideChanged` (`:626`) and `EditModeToggle_Click` (`:1080`).

### Zoom still resizes
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:1102` — `ZoomSlider_ValueChanged` calls
  `RefreshSlideSections()`; `thumbWidth = ZoomSlider.Value` is shared across every section, so zoom
  resizes every thumbnail in every section.

### Empty-state still shows
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` (top of `RefreshSlideSections`, `:1873` area) —
  sets `EmptyStateText.Visibility = Visible` and returns when `_engine == null`,
  `CurrentServiceOrder == null`, or `Items.Count == 0`; `Collapsed` otherwise.

### Section header + Edit affordance
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:1984` — `private UIElement BuildSectionHeader(ServiceItem item)`:
  icon (reusing the `MakeItemRow` icon mapping) + title, the non-functional `ORDER [Original]` chip,
  and an `Edit` button (`:2050`) wired to `EditServiceItemDirect(item)`. Inline brushes only.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:2063` — `private void EditServiceItemDirect(ServiceItem item)`
  opens the existing `AddServiceItemDialog` with `ItemToEdit = item`; `ItemEdited` saves, refreshes
  the sidebar, and rebuilds sections (`:2080`). No new editing surface.

### XAML
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` — the center `ScrollViewer` gained
  `x:Name="SlideSectionsScrollViewer"`; `<WrapPanel x:Name="SlideThumbnailsPanel">` was replaced by
  `<StackPanel x:Name="SlideSectionsPanel">` inside the same `ScrollViewer`/`Grid`. Zoom slider and
  all inline brushes unchanged.

## How per-item slides are obtained

`GetSlidesForItem` dispatches on item type. A Song returns its pre-baked `item.Slides` when present
(F2/F3 item slides stay authoritative); otherwise it resolves the linked library song with
`SongPathResolver.Resolve(_songManager.Songs, item.SongFolder, item.Title)` and generates slides
on-demand via the new pure `PresenterEngine.GenerateSlidesForSong(song)`. Every non-Song type returns
its pre-baked `item.Slides` (or empty). Missing engine/song manager or an unresolved song yields an
empty list and the section renders a titled "could not load" note instead of crashing. Generated
slides are never written back to the model, so the live/go-live/backtrack flow is untouched.

## Scroll-to-section mechanism

On sidebar selection, `ServiceOrderList_SelectionChanged` calls `GoToItem(index)` (which fires
`ItemChanged -> RefreshSlideSections` synchronously, refreshing the section map) and then
`ScrollToSection(display.Item)`. `ScrollToSection` looks the item up in `_sectionByItem` by reference
and calls `BringIntoView()` on the resolved section root. The map is fresh because the rebuild runs
before the scroll. No sections are cleared or replaced — all remain rendered and only the viewport
moves.

## Responsiveness approach (no UI-thread hang pattern)

The build is eager and synchronous: no `async`/`Task` and no long blocking loop beyond iterating a
handful of items. The per-slide work (`GenerateSlidesForSong` chord parsing + synchronous
`TryLoadImageBrush` bitmap load with `OnLoad`) is the exact per-slide cost the proven single-item
path already paid; the change only loops that same cost over the service's items. For the stated
"handful of items" this matches the proven path and does not reproduce the prior "Not Responding"
state. The one accepted caveat (review finding, non-blocking): `RefreshSlideSections` also runs on
every `ZoomSlider_ValueChanged` tick, so dragging zoom on a very large, image-heavy service
regenerates/reloads everything repeatedly on the UI thread — the `_sectionByItem` map is the
designated hook for lazy fill or per-item slide caching if services grow. No behavioral change was
made here beyond committing outstanding work and writing this report.

## Build and test results (exact, re-run this step)

- `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo`
  -> **Build succeeded with 4 warning(s)** — **0 errors**. The 4 warnings are pre-existing and
  unrelated to this change:
  - `MediaLibraryPanel.xaml.cs(16,20): CS0414` — `_currentView` assigned but never used (emitted
    once in the WPF temp-assembly pass and once in the main pass).
  - `ChordChartWindow.xaml.cs(235,51): CS8604` — possible null arg `lyricText`.
  - `WaveformEditorWindow.xaml.cs(1775,18): CS0414` — `_inReviewMode` assigned but never used.

- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --nologo`
  -> **total: 624, failed: 0, succeeded: 624, skipped: 0** (duration 8.5s). Baseline was 621; this
  change adds 3 tests in `GenerateSlidesForSongTests.cs` (parity, purity, empty-song). No
  regressions. The console line `Error loading service order: 't' is an invalid start...` is the
  expected log from `DefensiveLoadTests`, which asserts the loader survives malformed JSON — not a
  failure.

## Invariants confirmed

- `OperatorWindow.xaml` is StaticResource-free: the only `StaticResource` text is inside a
  pre-existing comment; all brushes are inline.
- No data files were touched: the diff is limited to `PresenterEngine.cs`,
  `OperatorWindow.xaml(.cs)`, the new `GenerateSlidesForSongTests.cs`, and the task's
  `verification.md`. No `app.db`, `ServiceOrders/*.json`, or `Songs/*` were modified.
- OWC branding intact.
- All handlers and `x:Name`s preserved: the XAML change only named the `ScrollViewer` and swapped
  the WrapPanel for a StackPanel; no handler or named element was removed.

## Click-test checklist

- [ ] Open a multi-song service.
- [ ] The right pane shows ALL items as stacked sections (header + thumbnail grid per item).
- [ ] Scrolling the right pane reveals every item's section.
- [ ] Clicking a sidebar item scrolls to its section without clearing the others.
- [ ] The live slide is RED on the active item's section (non-live sections stay neutral).
- [ ] The zoom slider resizes thumbnails across every section.
- [ ] The app stays responsive (no "Not Responding") for a normal-sized service.

## Final state

- Branch: `feature/all-item-sections`
- Worktree: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections`
- Not merged, worktree left as-is per instructions.
- Feature commit: `39b0706` (`feat: show all service items as stacked sections in center pane`).
  Task artifacts (plan, code review, review verdict, this report) committed separately as docs.
