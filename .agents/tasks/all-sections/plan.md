# Implementation Plan — All-Sections Center Pane (ProPresenter-style)

Redesign the center/Live content in `OperatorWindow` from showing ONE selected item's slides to
showing ALL items of the current service as vertically stacked SECTIONS (header + thumbnail grid
per item). Left sidebar selection scrolls the right pane to the matching section instead of
replacing content. Reuse the existing per-slide thumbnail visual exactly.

## Design decisions (made here, grounded in the code)

- **Per-item slide acquisition without mutating engine state.** The live single-item path uses
  `PresenterEngine.LoadStandaloneSong(song)` → private `GenerateSlidesFromSong(song)`, which runs
  `_chordParser.ExtractSlideBlocks(...)` and mutates `_currentSlides`/`_currentSlideIndex`
  (`WorshipPlayer.Presenter/Services/PresenterEngine.cs:381-407`, `:445-520`). To build sections for
  NON-live items we must NOT mutate that state. Decision: add a **public, pure** method
  `PresenterEngine.GenerateSlidesForSong(Song song) : List<Slide>` that contains the SAME block/
  timestamp logic as `GenerateSlidesFromSong` but returns a new list and takes no lock / writes no
  fields. Then refactor the existing private `GenerateSlidesFromSong` to call it and publish the
  result under `_stateLock` (so the live path stays byte-for-byte identical and the F2/F3/backtrack
  flow is untouched). Rationale: `GenerateSlidesFromSong` is the exact generator the current
  selection path feeds through `LoadStandaloneSong`, so sections will match the live rendering; it
  depends on the engine's `_chordParser` instance, so the method belongs on the engine, not a static
  Core helper. `WorshipPlayer.Tests` already compiles `PresenterEngine.cs` in and targets `net8.0`
  (`WorshipPlayer.Tests/WorshipPlayer.Tests.csproj:4,:31`), so this method is unit-testable without WPF.

- **`GetSlidesForItem(ServiceItem)` dispatch.** Decision: a private helper in the code-behind returns
  the slide list for any item: for `ServiceItemType.Song` (`WorshipPlayer.Core/Models/ServiceOrder.cs:180-182`),
  resolve via `SongPathResolver.Resolve(_songManager.Songs, item.SongFolder, item.Title)`
  (`WorshipPlayer.Core/Services/SongPathResolver.cs:57`) then `_engine.GenerateSlidesForSong(song)`;
  for every other type, return `item.Slides` (`WorshipPlayer.Core/Models/ServiceOrder.cs:133`,
  pre-baked). Returns an empty list on unresolved song (caller renders a "could not load" note). This
  mirrors the existing selection logic at `OperatorWindow.xaml.cs:1448-1468` but without `LoadStandaloneSong`.

- **Live RED slide only on the live item.** The engine's `CurrentSlides`/`CurrentSlideIndex`
  (`PresenterEngine.cs:57-61`) hold ONLY the live item's slides, and `CurrentItemIndex`
  (`PresenterEngine.cs:63`) identifies the live item. Decision: a section paints the live treatment
  only when its item index equals `_engine.CurrentItemIndex`; within that section the live slide is
  the one at `_engine.CurrentSlideIndex`. All other sections render every thumbnail in the neutral
  (non-live) style. For the live section, keep rendering from `item.Slides`/generated slides (same
  content the engine generated) and highlight by index — do NOT swap in `_engine.CurrentSlides`
  (keeps one code path per thumbnail).

- **Responsiveness.** A service here is a handful of items; eager synchronous build mirrors the
  proven single-item path (same per-slide cost, just looped over items) and is acceptable per the
  task. Decision: eager build, but (a) reuse one `LyricSlideGenerator`-free generator call per song,
  (b) guard each item in its own try/catch so one bad item can't abort the whole build, and (c) keep
  zero blocking I/O beyond the image loads the existing thumbnail builder already does
  (`TryLoadImageBrush`, `OperatorWindow.xaml.cs:~2130`). No `Task`/async is introduced on the build
  path; if a future service proves heavy, the ServiceItem→section map (step 5) is the hook for lazy
  per-section thumbnail fill, noted but not implemented.

- **Edit-mode selection highlight** (`_isEditMode`/`_selectedSlideIndex`, `OperatorWindow.xaml.cs:85-87`)
  stays a property of the LIVE section only (it drives `_engine.GoToSlide` today). Non-live sections'
  thumbnails fire `_engine`-independent navigation is out of scope — clicking a non-live section's
  thumbnail is handled in step 6.

## x:Names / handlers — preserve vs add

- PRESERVE (still referenced): `ServicesPanel`, `EmptyStateText`, `ZoomSlider`
  (`OperatorWindow.xaml:391,394,410`), `ServiceOrderList` + `ServiceOrderList_SelectionChanged`
  (`:323,:1429`), `CurrentItemHeader`/`CurrentItemTitle`/`CurrentItemSubtitle` (`:361,:365,:368`),
  `EditModeToggleBtn`/`EditModeToggle_Click` (`:1061`), `ZoomSlider_ValueChanged` (`:1094`),
  and every other sidebar/tab/transport handler. Keep `UpdateSlideThumbnailHighlights` reachable.
- ADD in XAML: replace `<WrapPanel x:Name="SlideThumbnailsPanel">` with
  `<StackPanel x:Name="SlideSectionsPanel">` inside the SAME `ScrollViewer` (`:393-402`); give that
  `ScrollViewer` `x:Name="SlideSectionsScrollViewer"` so code-behind can scroll it.
- ADD in code-behind: `RefreshSlideSections()`, `GetSlidesForItem(ServiceItem)`,
  `BuildSlideThumbnail(Slide, int slideNumber, bool isActive, bool isSelected, double thumbWidth, Action<int> onClick)`
  (extracted from the current thumbnail loop so sections reuse the identical visual),
  `BuildSectionHeader(ServiceItem)`, a `Dictionary<ServiceItem, FrameworkElement>` section map, and
  `ScrollToSection(ServiceItem)`.

---

- [ ] 1. Add a pure slide generator to `PresenterEngine` and refactor the existing private generator to use it.
      Add `public List<Slide> GenerateSlidesForSong(Song song)` containing the exact logic of the current
      private `GenerateSlidesFromSong` body (the `song.Sections` loop, `_chordParser.ExtractSlideBlocks`,
      ChartSlide timestamp matching, `Slide` construction) BUT returning a fresh `List<Slide>` with NO
      `_stateLock` and NO writes to `_currentSlides`/`_currentSlideIndex`. Then rewrite private
      `GenerateSlidesFromSong(song)` to `var generated = GenerateSlidesForSong(song);` followed by the
      existing `lock (_stateLock) { _currentSlides = generated; _currentSlideIndex = generated.Count > 0 ? 0 : -1; }`.
      Behavior of `LoadStandaloneSong`/`LoadSong`/backtrack path must be unchanged.
      Files: c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Presenter\Services\PresenterEngine.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` from the worktree root — 0 errors, 4 warnings.

- [ ] 2. Add a focused unit test for `GenerateSlidesForSong` (purity + parity).
      In the Tests project (which already compiles `PresenterEngine.cs`), add a test that builds a `Song`
      with 2 sections of multi-block text, calls `GenerateSlidesForSong`, and asserts: the returned slide
      count/sections/text match expectations AND that calling it does NOT change `engine.TotalSlides`
      (still 0 / unchanged) — i.e. no state mutation. Follow the xUnit `[Fact]` style in
      `WorshipPlayer.Tests\SongPathResolverTests.cs`. Construct `PresenterEngine` the same way existing
      engine tests do (search the Tests project for an existing `new PresenterEngine(` to copy the ctor args).
      Files: c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Tests\GenerateSlidesForSongTests.cs
      Verify: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --nologo` from the worktree root — total is 621 + new tests, 0 failed.

- [ ] 3. Swap the center panel container in XAML from a single WrapPanel to a sections StackPanel.
      In `ServicesPanel`'s `ScrollViewer` (`OperatorWindow.xaml:391-402`): name the `ScrollViewer`
      `x:Name="SlideSectionsScrollViewer"`; keep the inner `Grid` and `EmptyStateText` as-is; replace
      `<WrapPanel x:Name="SlideThumbnailsPanel" Margin="6" />` with
      `<StackPanel x:Name="SlideSectionsPanel" Margin="6" />`. Keep the zoom slider block (`:404-415`)
      and all inline brushes (NO StaticResource).
      Files: c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Presenter\Views\OperatorWindow.xaml
      Verify: build fails to compile code-behind yet (expected; done in step 4). After step 4, the solution builds 0 errors.

- [ ] 4. Extract the per-slide thumbnail builder and rewrite thumbnail rendering as `RefreshSlideSections`.
      (a) Extract the body of the current `RefreshSlideThumbnails` per-slide loop (`OperatorWindow.xaml.cs:1885-2037`)
      into `private Border BuildSlideThumbnail(Slide slide, int slideNumber, bool isActive, bool isSelected, double thumbWidth, Action<int> onClick)`
      — identical visual (background via `ResolveSlideBackground`, scrim, lyric text, label chip tagged
      "labelChip", number badge, RED live frame when `isActive`, cyan when `isSelected`); the click
      handler calls `onClick(slideNumber-1)`. (b) Add `private List<Slide> GetSlidesForItem(ServiceItem item)`
      per the Design decisions (Song → resolve + `_engine.GenerateSlidesForSong`; else `item.Slides`; empty
      on unresolved, guarded). (c) Add `private UIElement BuildSectionHeader(ServiceItem item)`: a header row
      with the item `Title` on the left (reuse `MakeItemRow` icon mapping, `OperatorWindow.xaml.cs:1375-1384`),
      an "Edit" affordance button on the right wired to open the existing edit dialog for that item (reuse
      `EditSelectedServiceItem` logic / `AddServiceItemDialog` with `ItemToEdit = item`), and an optional
      non-functional "ORDER [Original]" chip — inline brushes only (#1A1D27 header bg, #F0F2F5 title,
      #8B92A5 muted). (d) Rewrite `RefreshSlideThumbnails()` as `RefreshSlideSections()`:
      clear `SlideSectionsPanel.Children` and the section map (step 5); read
      `order = _engine?.CurrentServiceOrder` (`PresenterEngine.cs:63`); if null or no items →
      show `EmptyStateText`, return; else hide `EmptyStateText`; compute `thumbWidth` from `ZoomSlider`
      exactly as today (`:1870-1876`); for each `item` in `order.Items` (in order), in a per-item try/catch:
      build a header (BuildSectionHeader) + a `WrapPanel` of thumbnails; `var slides = GetSlidesForItem(item);`
      if empty → add a "could not load" note TextBlock under the header; else for each slide build
      `BuildSlideThumbnail`, with `isActive = (itemIndex == _engine.CurrentItemIndex && slideIndex == _engine.CurrentSlideIndex)`,
      `isSelected = (itemIndex == _engine.CurrentItemIndex && _isEditMode && slideIndex == _selectedSlideIndex)`,
      and `onClick = idx => { if (_isEditMode) { _selectedSlideIndex = idx; UpdateLiveSectionHighlights(); } else { _serviceOrderManager?.GoToItem(itemIndex); _engine?.GoToSlide(idx); } }`;
      wrap the header+grid in a container (e.g. a `StackPanel`/`Border`), register it in the section map
      (step 5), and add it to `SlideSectionsPanel`. Keep `_lastActiveSlideIndex`/`_selectedSlideIndex`
      reset behavior from `:1879-1884`. (e) Point all existing callers
      (`OperatorWindow.xaml.cs:239,673,1099,1469` and the removed `:1856`) at `RefreshSlideSections()`.
      Files: c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` from the worktree root — 0 errors, 4 warnings.

- [ ] 5. Add the ServiceItem→section UI map and populate it during the section build.
      Add `private readonly Dictionary<ServiceItem, FrameworkElement> _sectionByItem = new();` Clear it at
      the top of `RefreshSlideSections` and add `_sectionByItem[item] = sectionContainer;` for each built
      section (the header element or the section's root container — choose the root container so
      `BringIntoView` scrolls the whole section into view). Use reference equality (ServiceItem instances
      come straight from `order.Items`, same instances the sidebar rows hold via `ServiceItemDisplay.Item`).
      Files: c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` — 0 errors, 4 warnings.

- [ ] 6. Change `ServiceOrderList_SelectionChanged` from "load one item" to "scroll to section".
      Rewrite `ServiceOrderList_SelectionChanged` (`OperatorWindow.xaml.cs:1429-1471`): keep the early-outs
      (`_suppressServiceOrderSelection`, `_bulkEditMode`, non-`ServiceItemDisplay`, null `Item` header rows,
      and the `items.IndexOf` + `_serviceOrderManager?.GoToItem(index)` call so engine/live state and the
      go-live/transport flow stay intact). REMOVE the `LoadStandaloneSong` block (`:1448-1467`) and the
      trailing `RefreshSlideThumbnails()`; instead call `ScrollToSection(display.Item)` and optionally
      highlight that section's header. Add `private void ScrollToSection(ServiceItem item)`: if
      `_sectionByItem.TryGetValue(item, out var el)` call `el.BringIntoView()` (no manual offset math;
      runs on the UI thread, non-blocking). The other sections MUST remain rendered — do not clear
      `SlideSectionsPanel`. Note: `GoToItem` still fires `ItemChanged` → `RefreshSlideSections` (full
      rebuild) + `UpdatePreviews`; that keeps previews/live correct and the map fresh before scrolling,
      so call `ScrollToSection` after the selection logic (the rebuild has already run synchronously via
      the event). If timing proves flaky, wrap `ScrollToSection` in `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)`.
      Files: c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` — 0 errors, 4 warnings.

- [ ] 7. Redirect live-slide highlight updates to the live item's section only.
      `OnSlideChanged` (`OperatorWindow.xaml.cs:620-633`) currently calls `UpdateSlideThumbnailHighlights()`,
      which assumed `SlideThumbnailsPanel.Children` == the one item's slides. Replace with
      `UpdateLiveSectionHighlights()`: locate the live item via `_engine.CurrentItemIndex` →
      `order.Items[liveIndex]` → `_sectionByItem[liveItem]`, find that section's thumbnail `WrapPanel`,
      and reapply the RED/cyan/neutral border + `UpdateLabelChipLiveState` logic (reuse the existing
      `UpdateSlideThumbnailHighlights` body, `:2041-2075`, but scoped to the live section's thumbnail
      children and indexed by `_engine.CurrentSlideIndex`). Non-live sections are never recolored.
      If the live section isn't found (e.g. map stale), fall back to a `RefreshSlideSections()`.
      Keep `UpdateLabelChipLiveState` (`:2080-2099`) as-is. Update the `EditModeToggle_Click`
      (`:1061`) and edit-mode thumbnail click paths to call `UpdateLiveSectionHighlights()`.
      Files: c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` — 0 errors, 4 warnings.

- [ ] 8. Confirm zoom, empty-state, and OperatorWindow StaticResource-free invariant.
      Ensure `ZoomSlider_ValueChanged` (`OperatorWindow.xaml.cs:1094-1102`) calls `RefreshSlideSections()`
      so zoom resizes thumbnails across ALL sections (the per-thumbnail size already derives from
      `ZoomSlider.Value`). Ensure `EmptyStateText` shows when `CurrentServiceOrder` is null or has no
      items and hides otherwise (handled in `RefreshSlideSections`). Grep-confirm no `StaticResource`
      was introduced in `OperatorWindow.xaml`.
      Files: c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Presenter\Views\OperatorWindow.xaml, c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\all-sections\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs
      Verify: `Select-String -Path WorshipPlayer.Presenter\Views\OperatorWindow.xaml -Pattern StaticResource` returns nothing; then `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` — 0 errors, 4 warnings.

- [ ] 9. Full regression: build clean + all tests green.
      Run the whole suite and confirm no behavior regressed (transport/go-live, live RED slide, zoom,
      tab switching, sidebar show/hide, Songs "+ Add", open-service, context menus all still wired —
      none of their x:Names/handlers were removed).
      Files: (none — verification only)
      Verify: from the worktree root, `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` → 0 errors / 4 warnings, and `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --nologo` → 0 failed, total ≥ 621 (621 + the step-2 additions).

## Notes / assumptions / gaps

- The reference screenshots were not available to this planner; the header layout (title left, Edit
  right, optional "ORDER [Original]" chip) follows the task's written description. The implementer
  should match the screenshots for spacing/colors within the inline-brush palette already used
  (#0F1117 page, #1A1D27 panels, #2A2E3A borders, #F0F2F5 text, #8B92A5 muted, #00D4FF cyan accent,
  LivePink/#FF3366 live).
- The "Edit" affordance in each section header reuses the existing per-item edit dialog
  (`AddServiceItemDialog` with `ItemToEdit`), so no new editing surface is introduced.
- Generated slides are never persisted (generation is on-demand in `GetSlidesForItem`); data files
  (`app.db`, `ServiceOrders/*.json`, `Songs/*`) are not touched.
- No `async`/long synchronous loop is added on the UI thread; the eager build mirrors the proven
  single-item cost per slide. The `_sectionByItem` map is the designated hook if lazy per-section
  thumbnail fill is later needed for very large services.
