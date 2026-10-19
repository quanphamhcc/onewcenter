# All-service-items stacked sections in the center pane

The center/Live pane in `OperatorWindow` no longer shows one selected item's slides. It now renders every service item as its own vertically stacked section (header with title + icon + an Edit affordance + a non-functional "ORDER [Original]" chip, then a `WrapPanel` of that item's slide thumbnails). Clicking an item in the left SERVICE ORDER sidebar scrolls the right pane to the matching section instead of replacing the content, so all sections stay rendered. The single `SlideThumbnailsPanel` WrapPanel was replaced by a `SlideSectionsPanel` StackPanel inside the same ScrollViewer, and the per-slide thumbnail visual was extracted verbatim into `BuildSlideThumbnail` so sections reuse the exact existing look.

Watch for: the eager synchronous rebuild regenerates every song's slides and loads every background bitmap on the UI thread on each refresh (including every zoom tick) — accepted by the task for a handful of items but the one scalability caveat (possible). The live-highlight loop assumes the live section's WrapPanel holds only Border thumbnails (confirmed safe as written). Build/test evidence is present and specific (confirmed).

**Verdict**: APPROVED

## High-level view

The slide generator was split cleanly. `PresenterEngine.GenerateSlidesForSong` is a new public pure method holding the exact block/timestamp logic and returning a fresh list with no lock and no field writes; the old private `GenerateSlidesFromSong` now calls it and publishes under `_stateLock`, so the live / go-live / backtrack path is byte-for-byte unchanged. This is the right place for the method since it depends on the engine's chord parser, and it is unit-tested for both parity and purity.

The center pane rebuild (`RefreshSlideSections`) iterates `CurrentServiceOrder.Items` in order, builds a header plus a thumbnail grid per item inside a per-item try/catch, and registers each section's root in a `ServiceItem -> FrameworkElement` map by reference. Song items resolve via `SongPathResolver` and generate on-demand through the pure generator; other types use pre-baked `item.Slides`; unresolved songs and build failures render a titled "could not load"/"could not be loaded" note instead of crashing. Generated slides are never persisted.

The live RED treatment is scoped to the live item only: a section paints live when its index equals `CurrentItemIndex`, and within it the active slide is `CurrentSlideIndex`. `UpdateLiveSectionHighlights` recolors only the live section's WrapPanel and falls back to a full rebuild if the map is stale. Sidebar selection keeps `GoToItem` (so transport/go-live stay intact) and then calls `ScrollToSection` (`BringIntoView`) without clearing other sections. Zoom resizes thumbnails across all sections. The XAML stays StaticResource-free with inline brushes, OWC branding is untouched, and no data files were modified.

The one caveat is responsiveness: the build is eager and synchronous, regenerating all songs' slides and loading all background bitmaps on the UI thread every refresh — same per-slide cost as the proven single-item path, now multiplied by item count. The task explicitly accepts this for small services and the `_sectionByItem` map is left as the hook for lazy fill later.

<details>
<summary>Issues (2)</summary>

1. **Eager synchronous rebuild cost** — every `RefreshSlideSections` (including each zoom-slider tick) re-resolves and regenerates all song slides and reloads all background bitmaps synchronously on the UI thread. Fine for a handful of items per the task; could stutter on a large service. Consider debouncing zoom-driven rebuilds or caching generated slides per item if services grow. (possible)
2. **Live-highlight index assumption** — `UpdateLiveSectionHighlights` walks the live section's WrapPanel by child index and treats child `i` as slide `i`. Safe as written because a populated section's WrapPanel contains only Border thumbnails (the TextBlock note only appears when there are zero slides, i.e. no Borders), but the invariant is implicit. Keep it in mind if non-thumbnail children are ever added to a populated section. (confirmed, non-blocking)

</details>

<details>
<summary>Details</summary>

### Pure generator split in PresenterEngine

`GenerateSlidesForSong(Song)` is now public and pure: it builds a fresh `List<Slide>` with the same section loop, `_chordParser.ExtractSlideBlocks`, timestamp matching, and `Slide` construction, and returns it with no `_stateLock` and no writes to `_currentSlides`/`_currentSlideIndex`. The private `GenerateSlidesFromSong` reduces to calling the pure method and publishing the result under the lock (`_currentSlides = generated; _currentSlideIndex = generated.Count > 0 ? 0 : -1;`). The empty-song early return that previously mutated state under lock now simply returns an empty list, so the public method is genuinely side-effect-free. Because the generator leans on the engine's own `_chordParser`, placing it on the engine rather than a static Core helper is the correct call, and the sections match live rendering since both go through the same generator.

The three tests in `GenerateSlidesForSongTests.cs` cover parity (a two-section, three-block song yields three slides with the right `SectionName`s and non-empty text), purity (`engine.TotalSlides` stays 0 and `CurrentSlide` stays null after a call), and empty-song safety. That is the right coverage for this change. Layout/visual wiring is not unit-tested, which is expected for WPF code-behind and explicitly out of scope.

### Per-item slide acquisition

`GetSlidesForItem` dispatches on item type: for a Song it returns pre-baked `item.Slides` if present, otherwise resolves the linked library song via `SongPathResolver.Resolve(_songManager.Songs, item.SongFolder, item.Title)` and generates through the pure engine method; for every other type it returns `item.Slides` (or an empty list). Null `_engine`/`_songManager` and an unresolved song each return an empty list, and the caller renders a titled "could not load" note rather than crashing. This mirrors the old selection logic at the removed `LoadStandaloneSong` block but without mutating engine state — exactly what the sections need. The pre-baked-wins ordering keeps F2/F3 item slides authoritative.

### Live RED slide scoped to the live section

`RefreshSlideSections` computes `isLiveSection = (itemIndex == _engine.CurrentItemIndex)` and marks a thumbnail active only when `isLiveSection && slideIndex == _engine.CurrentSlideIndex`; every other section renders neutral. `UpdateLiveSectionHighlights` replaces the old `UpdateSlideThumbnailHighlights`: it locates the live item via `CurrentItemIndex`, finds its section root in `_sectionByItem`, grabs that section's WrapPanel, and reapplies the RED/cyan/neutral border plus `UpdateLabelChipLiveState` scoped to that grid only. If the live section is missing or not a `StackPanel` (stale map) it falls back to a full `RefreshSlideSections`. `OnSlideChanged` and `EditModeToggle_Click` both route through this method, and the edit-mode thumbnail click calls it after setting `_selectedSlideIndex`.

The loop indexes `thumbs.Children[i]` as slide `i`. This holds because a populated section's WrapPanel contains only Border thumbnails — the "could not load" TextBlock is only added when the slide list is empty, in which case there are no Borders to iterate. The assumption is correct as written; it is implicit rather than enforced, so it is worth remembering if non-thumbnail children are ever mixed into a populated section.

### Selection scrolls instead of replacing

`ServiceOrderList_SelectionChanged` keeps its early-outs and the `items.IndexOf(display.Item)` + `_serviceOrderManager?.GoToItem(index)` call, so engine/live state and the go-live/transport flow are intact. The old `LoadStandaloneSong` block and trailing `RefreshSlideThumbnails()` were removed; it now calls `ScrollToSection(display.Item)`, which looks the item up in `_sectionByItem` and calls `BringIntoView()` on the UI thread. `GoToItem` fires `ItemChanged -> RefreshSlideSections` synchronously before the scroll, so the map is fresh when the scroll runs, and no section is cleared.

### Section header and Edit affordance

`BuildSectionHeader` lays out icon + title (the icon comes from `MakeItemRow(item).Icon`, reusing the sidebar mapping), the "ORDER [Original]" chip, and an "Edit" button wired to `EditServiceItemDirect(item)`. `EditServiceItemDirect` opens the existing `AddServiceItemDialog` with `ServiceOrderManager`, `SongLibrary`, and `ItemToEdit = item`; on `ItemEdited` it saves, refreshes the sidebar, and rebuilds sections. All four dialog members it sets (`ServiceOrderManager`, `SongLibrary`, `ItemToEdit`, `ItemEdited`) exist on the dialog, so no new editing surface is introduced and the wiring type-checks. Calling `MakeItemRow` just to read `.Icon` allocates a throwaway display object per header; negligible, not worth changing.

### Responsiveness

No `async`/`Task` and no long synchronous loop beyond the per-item build is introduced. The per-slide work (`GenerateSlidesForSong` chord parsing, `TryLoadImageBrush` synchronous bitmap load with `OnLoad`) is the exact cost the pre-existing single-item path already paid; the diff only changed how many items run through it at once. For the stated "handful of items" this matches the proven path and should not reproduce the prior "Not Responding". The caveat is that `RefreshSlideSections` runs on every `ZoomSlider_ValueChanged` as well as every item change, so dragging the zoom slider on a large, image-heavy service regenerates and reloads everything repeatedly on the UI thread. Acceptable under the task's scope; `_sectionByItem` is the designated hook for lazy fill or caching if services grow.

### Invariants and scope

`Select-String` finds no leftover references to `RefreshSlideThumbnails`, `SlideThumbnailsPanel`, or `UpdateSlideThumbnailHighlights` in the code-behind, and the XAML's only `StaticResource` hit is inside a pre-existing comment. The XAML change is limited to naming the ScrollViewer and swapping the WrapPanel for a StackPanel, with inline brushes preserved. The diff touches only `PresenterEngine.cs`, `OperatorWindow.xaml(.cs)`, the new test file, and `verification.md` — no `app.db`, `ServiceOrders/*.json`, or `Songs/*`. OWC branding is untouched. Scope stayed within OperatorWindow plus the small pure Core-adjacent engine helper, as required.

### Build and test evidence

`verification.md` and the commit message both record `dotnet build` succeeding with 0 errors and 4 pre-existing warnings (named and unrelated), and `dotnet test` at total 624 / 0 failed (621 baseline + 3 new generator tests). The evidence is present, specific, and internally consistent, so per the task the suites were not re-run. The one console line about malformed JSON is the expected `DefensiveLoadTests` log, not a failure.

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Presenter/Services/PresenterEngine.cs` — split the slide generator into a public pure `GenerateSlidesForSong` + a thin private publisher.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` — named the center ScrollViewer; replaced the single WrapPanel with a sections StackPanel.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — all-sections rebuild, per-item slide acquisition, extracted thumbnail builder, section header + Edit, section map, scroll-to-section, live-section-only highlights, callers redirected.
- `WorshipPlayer.Tests/GenerateSlidesForSongTests.cs` — new parity/purity/empty-song tests for the pure generator.
- `.agents/tasks/all-sections/verification.md` — build/test evidence and file:line trace.

Full diff: `git -C .worktrees/all-sections diff main`.

</details>
