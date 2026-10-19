# Implementation Plan — Slides "Add to current service" as a LIVE LINK (not snapshot)

## Goal

Change the Slides module's "Add to current service" from a SNAPSHOT (copying slides into
`ServiceItem.Slides`) to a LIVE LINK: store the presentation's id on the service item and resolve
its CURRENT slides on demand at render time, mirroring how Song items resolve their linked library
song. Editing the presentation then updates what the service item shows.

All paths are ABSOLUTE under the worktree
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slides-livelink` (branch `feature/slides-live-link`).
Relative paths would land in the parent workspace — always use the absolute worktree path.

## Exploration findings (verified by reading source)

- **Serialization convention (CONFIRMED).** `ServiceOrderManager._jsonOptions` sets
  `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`
  (`WorshipPlayer.Core/Services/ServiceOrderManager.cs:78`). So a nullable property is globally
  omitted from JSON when null — no per-property `[JsonIgnore(Condition = WhenWritingNull)]` is used
  anywhere in `ServiceOrder.cs`. The existing optional fields `ThemeId`, `AutoAdvanceSettings`,
  `BackgroundType` are plain nullable (`?`) with only a `[JsonPropertyName(...)]`
  (`WorshipPlayer.Core/Models/ServiceOrder.cs:175, 184, 190`). **Match this: add a nullable
  `string?` with only `[JsonPropertyName("slidePresentationId")]` — no `WhenWritingNull` attribute.**
  Older service orders without the key deserialize to `null` (backward compatible); new link items
  omit the key entirely when null.

- **Mapper (Stage 3 snapshot).** `SlidePresentationMapper.ToServiceItem(SlidePresentation)`
  (`WorshipPlayer.Core/Services/SlidePresentationMapper.cs:34`) builds a `ServiceItem` with
  `Type = Slides`, `Title = BuildTitle(presentation.Name)` (fallback `"Slides Item"`), and
  `Slides = ToSlides(presentation.Slides)`. `ToSlides(List<PresentationSlide>?)`
  (`SlidePresentationMapper.cs:71`) is the pure conversion to KEEP — currently `private`. `BuildTitle`
  is `private` at `SlidePresentationMapper.cs:50`.

- **ServiceItem model.** `ServiceItem` has `Type`, `Title`, `Slides` (`List<Slide>`), and
  type-specific fields (`WorshipPlayer.Core/Models/ServiceOrder.cs:66`). There is NO slide-presentation
  id field yet. The `Slides` property is `WorshipPlayer.Core/Models/ServiceOrder.cs:164`.

- **Add handler.** `OnAddPresentationToServiceRequested`
  (`WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:3305`) guards for a loaded service,
  `await _slidePresentationService.GetByIdAsync(item.Id)`, calls
  `SlidePresentationMapper.ToServiceItem(presentation)` (`:3326`), then
  `AddItem` + `await SaveServiceOrderAsync()` + `RefreshServiceOrderList()` + `RefreshSlideSections()`
  (`:3328-3330`). The whole body is wrapped in try/catch → `ShowToolError`.

- **Render path.** `GetSlidesForItem(ServiceItem)`
  (`WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:2229`) is **synchronous**. For `Song` it
  returns pre-baked `item.Slides` if present, else resolves the song and generates slides; for every
  other type it returns `item.Slides ?? new List<Slide>()` (`:2254`). It is called from
  `RefreshSlideSections()` (`:1991`, call at `:2039`), which is itself synchronous and iterates all
  items building thumbnails; a per-item `try/catch` already isolates one bad item (`:2093`), and
  `slides.Count == 0` renders a safe "No slides to show for this item." note (`:2041-2056`).

- **Sync/async constraint (KEY DESIGN POINT).** `GetSlidesForItem`/`RefreshSlideSections` are
  synchronous but `ISlidePresentationService.GetByIdAsync` is async (SQLite read,
  `WorshipPlayer.Core/Services/SlidePresentationService.cs:100`). There is NO existing
  `.GetAwaiter().GetResult()`/`.Result` blocking-async pattern in `OperatorWindow.xaml.cs` (verified),
  and blocking on the UI thread is a risk. **Decision:** keep `GetSlidesForItem` synchronous and feed
  it from an in-memory cache (`Dictionary<string, SlidePresentation?>`) that is primed asynchronously
  right before each `RefreshSlideSections` that can observe a change. This mirrors the Song path's
  "resolve from an already-loaded in-memory collection" shape (`SongPathResolver.Resolve(_songManager.Songs, ...)`,
  `:2238`) rather than introducing blocking async.

- **Refresh callers.** `RefreshSlideSections()` is called from: init (`:242`), the display-state
  dispatcher block (`:678`, inside a `Dispatcher.Invoke`), `ZoomSlider_ValueChanged` (`:1107`),
  `GoToItem`→`ItemChanged` driven rebuilds, close-service (`:1768`), add-presentation (`:3330`),
  and the edit-slides dialog save (`:2210`). `OnEditPresentationRequested`
  (`OperatorWindow.xaml.cs:3284`) opens `SlideEditorWindow` and on `true` only calls
  `_slidesLibraryPanel?.Refresh()` (`:3297`) — it does **NOT** currently call `RefreshSlideSections()`,
  so after editing a linked presentation the center grid is stale until the next refresh.

- **Service id resolution.** `_slidePresentationService` (`ISlidePresentationService`) is injected via
  `SetServices` (`OperatorWindow.xaml.cs:563`). `GetByIdAsync` returns the current presentation with
  latest slides, or `null` if deleted (`SlidePresentationService.cs:100-124`).

- **Test project (CONFIRMED path).** Tests live in `WorshipPlayer.Tests` (xUnit, net8.0) — NOT
  `WorshipPlayer.Core.Tests`. The existing mapper tests are
  `WorshipPlayer.Tests/SlidePresentationMapperTests.cs`. The project references
  `WorshipPlayer.Core` and selectively `<Compile Include>`s a few Presenter *services* but NOT the WPF
  `OperatorWindow` (`WorshipPlayer.Tests/WorshipPlayer.Tests.csproj:22-40`) — so the live-resolve/convert
  logic must stay a pure Core helper to be unit-testable.

- **Baseline (verified by running).** `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors,
  4 warnings. `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` → 658 passed, 0 failed.

## Design decisions (made here, grounded in the above)

1. **Link field:** add `ServiceItem.SlidePresentationId` as `string?` with only
   `[JsonPropertyName("slidePresentationId")]`, matching the file's `ThemeId`/`BackgroundType`
   convention + the manager's global `WhenWritingNull`. Lower risk than inventing a new serialization
   style.
2. **Keep the mapper pure and add a link-producing path there (not ad-hoc in the handler).** Expose a
   public `ToSlides(SlidePresentation)` for on-demand conversion, and add a public
   `ToServiceItemLink(SlidePresentation)` that produces the LINK form (`Type=Slides`,
   `Title=Name` fallback, `SlidePresentationId=Id`, **empty `Slides`**). Reason: all projection logic
   stays in Core where it is unit-testable; the handler just calls it. The old `ToServiceItem`
   (snapshot) is retained unchanged so existing tests keep passing and nothing else that might call it
   breaks.
3. **On-demand resolve via a synchronous cache primed asynchronously**, not blocking async in
   `GetSlidesForItem`. Reason: avoids a UI-thread block pattern that exists nowhere else, keeps the
   proven synchronous `RefreshSlideSections` loop intact.
4. **Backward compat:** a Slides item with no `SlidePresentationId` but existing snapshot `Slides`
   still renders its `Slides` (unchanged fall-through). A linked item whose presentation is missing
   renders the existing safe "no slides" note.

---

# Implementation Plan

- [ ] 1. Add the additive link field `SlidePresentationId` to `ServiceItem`.
      In `ServiceItem`, directly after the `ThemeId` property (`ServiceOrder.cs:175`) or alongside the
      Slides region, add:
      `[JsonPropertyName("slidePresentationId")] public string? SlidePresentationId { get; set; }`
      with a doc-comment: when set, this Slides item is a LIVE LINK to a library presentation and its
      slides are resolved on demand; when null the item uses its snapshot `Slides` (backward compat).
      Do NOT add `WhenWritingNull` — the manager's global `DefaultIgnoreCondition` already omits nulls
      (`ServiceOrderManager.cs:78`). Also update the `ServiceItemType.Slides` enum comment
      (`ServiceOrder.cs:243`) from "snapshotted slides" to note it may be a live link.
      Files: `WorshipPlayer.Core/Models/ServiceOrder.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors, 4 warnings (unchanged).

- [ ] 2. Expose the pure conversion and add a link-producing factory in the mapper.
      In `SlidePresentationMapper`: (a) change `ToSlides(List<PresentationSlide>?)`
      (`SlidePresentationMapper.cs:71`) from `private` to `public static` (keep signature/behavior
      identical). (b) Add `public static List<Slide> ToSlides(SlidePresentation presentation)` that
      null-guards (`ArgumentNullException`) and returns `ToSlides(presentation.Slides)` — the on-demand
      converter the renderer calls. (c) Add `public static ServiceItem ToServiceItemLink(SlidePresentation presentation)`
      that null-guards and returns a `ServiceItem { Type = Slides, Title = BuildTitle(presentation.Name),
      SlidePresentationId = presentation.Id, Slides = new List<Slide>() }` (LINK form: NO snapshot).
      Leave the existing `ToServiceItem` (snapshot) and `BuildTitle` as-is. Update the class/`ToServiceItem`
      XML doc to note the new link path. This is ONE item (one cohesive change to one file).
      Files: `WorshipPlayer.Core/Services/SlidePresentationMapper.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

- [ ] 3. Add Core unit tests for the new mapper helpers (TDD-style; may precede wiring).
      In a new `WorshipPlayer.Tests/SlidePresentationLinkTests.cs` (xUnit, mirrors
      `SlidePresentationMapperTests.cs` style), cover:
      (a) `ToSlides(SlidePresentation)` returns slides in `Order` then original-index order with text
      preserved (reuse the ordering assertion from `SlidePresentationMapperTests.ToServiceItem_MapsSlidesInOrder_WithTextPreserved`);
      (b) `ToSlides(SlidePresentation)` with null presentation throws `ArgumentNullException`;
      (c) `ToSlides(SlidePresentation)` with empty slides returns a non-null empty list;
      (d) `ToServiceItemLink` sets `Type == Slides`, `Title == Name` (and `"Slides Item"` on blank
      name), `SlidePresentationId == presentation.Id`, and `Slides` is empty;
      (e) add-flow: `CreateNew` a `ServiceOrderManager` in a temp dir, `AddItem(ToServiceItemLink(p))`,
      assert the single added item has `SlidePresentationId` set and `Slides` empty (mirrors
      `AddPresentationToService_ProducesExpectedTitleTypeAndSlideCount`; no `SaveAsync`, no data files).
      Files: `WorshipPlayer.Tests/SlidePresentationLinkTests.cs`
      Verify: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` — new tests pass; total goes
      from 658 to 658 + number of added facts, 0 failed.

- [ ] 4. Switch the add flow to produce the LINK form.
      In `OnAddPresentationToServiceRequested` (`OperatorWindow.xaml.cs:3326`) replace
      `var serviceItem = SlidePresentationMapper.ToServiceItem(presentation);` with
      `var serviceItem = SlidePresentationMapper.ToServiceItemLink(presentation);`. Leave the guards,
      `AddItem`, `await SaveServiceOrderAsync()`, `RefreshServiceOrderList()`, `RefreshSlideSections()`
      and try/catch unchanged. Because the add path is already `async` and already awaited
      `GetByIdAsync`, prime the cache (item 6) for this id before `RefreshSlideSections()` so the new
      section shows immediately. Update the handler's comment block (`:3301-3305`) from "snapshot" to
      "live link".
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

- [ ] 5. Add a synchronous presentation cache + async prime helper on `OperatorWindow`.
      Add a private field `private readonly Dictionary<string, SlidePresentation?> _slidePresentationCache = new();`
      near the other service fields (`OperatorWindow.xaml.cs:106`). Add
      `private async Task PrimeSlidePresentationCacheAsync()` that: returns early if
      `_slidePresentationService == null` or no `CurrentServiceOrder`; collects the distinct non-empty
      `SlidePresentationId`s of all `Slides`-type items in `_engine.CurrentServiceOrder.Items`; for each,
      `_slidePresentationCache[id] = await _slidePresentationService.GetByIdAsync(id);` (missing → stores
      `null`). Clear the cache at the start so a deleted/renamed presentation is re-resolved. Wrap in
      try/catch that logs via `System.Diagnostics.Debug.WriteLine` (never throws into the UI). This is
      the only place that awaits; `GetSlidesForItem` stays synchronous.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

- [ ] 6. Resolve linked slides on demand in `GetSlidesForItem`.
      In `GetSlidesForItem` (`OperatorWindow.xaml.cs:2229`), before the final
      `return item.Slides ?? new List<Slide>();` (`:2254`), add a Slides-link branch:
      if `item.Type == ServiceItemType.Slides` AND `!string.IsNullOrEmpty(item.SlidePresentationId)`:
      look up `_slidePresentationCache.TryGetValue(item.SlidePresentationId, out var p)`. If found and
      `p != null`, return `SlidePresentationMapper.ToSlides(p)` (CURRENT slides → live). If not in cache
      or `p == null` (deleted/not found), log via `Debug.WriteLine` and fall through to
      `item.Slides` (empty for a pure link → the caller's existing `slides.Count == 0` path renders the
      safe "No slides to show for this item." note; NO crash). If easy, when `p != null` also refresh the
      section header title to `p.Name` — but the title is built by `BuildSectionHeader(item)` in
      `RefreshSlideSections`, so instead (optional, low-risk) have `PrimeSlidePresentationCacheAsync`
      update `item.Title = p.Name` for linked items before the rebuild; if this complicates the diff,
      keep the stored `Title` and note it. A **Slides item with NO `SlidePresentationId`** skips this
      branch entirely and uses `item.Slides` (backward-compat snapshot) unchanged.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

- [ ] 7. Prime the cache before the refreshes that must reflect a live edit.
      Await `PrimeSlidePresentationCacheAsync()` immediately before `RefreshSlideSections()` in the
      async contexts that can observe a change:
      (a) the add handler (`OperatorWindow.xaml.cs:3330`, from item 4);
      (b) `OnEditPresentationRequested` (`OperatorWindow.xaml.cs:3296`): on `ShowDialog() == true`, after
      `_slidesLibraryPanel?.Refresh()`, add `await PrimeSlidePresentationCacheAsync(); RefreshSlideSections();`
      so returning from the slide editor updates the center grid immediately (this closes the current
      refresh gap — the handler is already `async void`);
      (c) the service-load/display-state block (`OperatorWindow.xaml.cs:678`): this runs inside a
      `Dispatcher.Invoke`; make its enclosing flow await the prime before `RefreshSlideSections()` if the
      enclosing method is async, otherwise schedule the prime via `await Dispatcher.InvokeAsync` and
      refresh when it completes — pick whichever keeps the method's existing signature; if neither is
      low-risk, leave this call site priming on next explicit refresh and rely on (b)/(d).
      For the remaining synchronous-only callers (`ZoomSlider_ValueChanged:1107`, `ItemChanged`-driven
      rebuilds, init `:242`, close `:1768`): do NOT make them async; they render from whatever is already
      cached. This satisfies the "updates on next service open / tab switch / RefreshSlideSections"
      minimum.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors, 4 warnings.

- [ ] 8. When-it-refreshes note (documented behavior, no extra code beyond item 7).
      Record in the handler comments: a live edit to a linked presentation is reflected (i) immediately
      after returning from the slide editor (item 7b), (ii) immediately after adding (item 7a), and
      (iii) on the next service open / Slides-tab return / any `RefreshSlideSections` that follows a
      cache prime. Edits made to a presentation from elsewhere while the service grid is already
      displayed show on the next prime+refresh, not instantaneously — this is the accepted minimum per
      the brief. No polling is added.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` (comments only)
      Verify: n/a (covered by the item 7 build).

- [ ] 9. Full verification.
      Run the full build and the test suite; confirm no regressions in existing Slides/Song/Scripture/
      Media add, all-sections center grid, and Stage 1-2 library/editor behavior (compilation + green
      tests are the automated signal; the brief forbids WPF/layout unit tests).
      Files: none (verification only).
      Verify:
      `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors, 4 warnings (the same 4 pre-existing);
      `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` → 0 failed, total = 658 + new facts
      from item 3.

## Constraints honored

- Scope limited to `ServiceOrder.cs` (additive link field), `SlidePresentationMapper.cs`
  (link factory + public convert-to-slides helper), `OperatorWindow.xaml.cs` (add handler sets the
  link; cache + `GetSlidesForItem` resolves live; refresh wiring), and `WorshipPlayer.Tests` (Core tests).
- Reuses `ISlidePresentationService.GetByIdAsync` and `ServiceOrderManager` (AddItem/SaveAsync) — no
  new services, no new storage.
- No change to `SlidePresentation` storage; `app.db` is never reset.
- `OperatorWindow` stays StaticResource-free (no XAML resource added; code-behind only).
- Backward compatible: old service orders (no `slidePresentationId`) load and render their snapshot
  `Slides` unchanged; a linked presentation that is deleted renders the safe "no slides" note, never a
  crash.
- OWC branding and all existing features/events preserved (the snapshot `ToServiceItem` and its tests
  are left intact).
- Build must stay 0 errors / 4 pre-existing warnings; tests green (658 + the new Core facts).
