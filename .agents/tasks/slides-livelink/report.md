# Completion Report — Slides "Add to current service" SNAPSHOT → LIVE LINK

Branch: `feature/slides-live-link` (worktree `.worktrees/slides-livelink`). First iteration
(no `review.json` present). Implemented the plan in `.agents/tasks/slides-livelink/plan.md`.

## Summary of behavior change

Adding a Slides-library presentation to a service no longer copies (snapshots) its slides onto the
service item. Instead the item stores the presentation's id and the renderer resolves the
presentation's CURRENT slides on demand, mirroring how Song items resolve their linked library song.
Editing the presentation now updates what the linked service item shows.

## Link-field decision + confirmed serialization convention

Added `ServiceItem.SlidePresentationId` as a plain nullable `string?` with only
`[JsonPropertyName("slidePresentationId")]`. No per-property `WhenWritingNull` attribute: the
`ServiceOrderManager._jsonOptions` global `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`
(`WorshipPlayer.Core/Services/ServiceOrderManager.cs:78`) already omits null properties from JSON.
This matches the existing optional fields `ThemeId` / `AutoAdvanceSettings` / `BackgroundType`
(`WorshipPlayer.Core/Models/ServiceOrder.cs`). Older service orders with no `slidePresentationId`
key deserialize to `null` → backward compatible; new link items omit the key when null.

## On-demand resolve path

- The add form produces a LINK via the pure Core helper `SlidePresentationMapper.ToServiceItemLink`
  (Type=Slides, Title=presentation Name with "Slides Item" fallback, SlidePresentationId=Id, **empty
  Slides**).
- `OperatorWindow` keeps a synchronous in-memory cache `_slidePresentationCache`
  (`Dictionary<string, SlidePresentation?>`) primed asynchronously by
  `PrimeSlidePresentationCacheAsync()` (the only place that awaits the async
  `ISlidePresentationService.GetByIdAsync`). This mirrors the Song path's "resolve from an
  already-loaded in-memory collection" shape and avoids blocking async on the UI thread (a pattern
  that exists nowhere else in the file).
- The synchronous `GetSlidesForItem` converts the cached presentation's CURRENT slides on demand via
  the pure Core helper `SlidePresentationMapper.ToSlides(SlidePresentation)` → edits go live.

## Not-found guard

If the linked presentation is absent from the cache or stored as `null` (deleted / not found),
`GetSlidesForItem` logs via `Debug.WriteLine` and falls through to `item.Slides` (empty for a pure
link). The caller's existing `slides.Count == 0` branch renders the safe
"No slides to show for this item." note. `PrimeSlidePresentationCacheAsync` and the add handler are
both wrapped in try/catch that never throws into the UI. No crash on a deleted presentation.

## Backward-compat fallback

A Slides item with NO `SlidePresentationId` (legacy snapshot) skips the link branch entirely and
renders its stored `item.Slides` unchanged. The snapshot `ToServiceItem` helper and all its existing
tests are retained intact.

## When it refreshes

A live edit to a linked presentation is reflected:
1. Immediately after returning from the slide editor (`OnEditPresentationRequested` primes the cache
   then calls `RefreshSlideSections`).
2. Immediately after adding a presentation (`OnAddPresentationToServiceRequested` primes then
   refreshes).
3. On the next service open / Slides-tab return (`OnItemChanged`, reached by the load path's
   `GoToItem(0)`, schedules a non-blocking prime+refresh via `Dispatcher.InvokeAsync`), or any
   `RefreshSlideSections` that follows a cache prime.

Edits made from elsewhere while the grid is already displayed show on the next prime+refresh, not
instantaneously (accepted minimum per the brief). No polling added. Synchronous-only callers
(`ZoomSlider_ValueChanged`, init, close, `ItemChanged`-driven rebuilds) render from whatever is
already cached and were NOT made async.

## File:line trace

- Add flow sets `SlidePresentationId` with no snapshot:
  `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:3377`
  (`var serviceItem = SlidePresentationMapper.ToServiceItemLink(presentation);`), via
  `SlidePresentationMapper.ToServiceItemLink` in
  `WorshipPlayer.Core/Services/SlidePresentationMapper.cs` (empty `Slides`, sets
  `SlidePresentationId`).
- Link field: `WorshipPlayer.Core/Models/ServiceOrder.cs` — `public string? SlidePresentationId`
  with `[JsonPropertyName("slidePresentationId")]`.
- Cache + async prime: `OperatorWindow.xaml.cs` — field `_slidePresentationCache` near the other
  service fields; `PrimeSlidePresentationCacheAsync()` (clears cache, resolves distinct linked ids,
  stores null when missing, syncs Title to current Name, swallows errors).
- `GetSlidesForItem` resolves linked CURRENT slides: `OperatorWindow.xaml.cs` — Slides-link branch
  returns `SlidePresentationMapper.ToSlides(presentation)` when cached and non-null.
- Fallback to `item.Slides` for old snapshot items: same method, final
  `return item.Slides ?? new List<Slide>();`.
- Not-found guard: same branch logs and falls through; `RefreshSlideSections` per-item try/catch and
  the `slides.Count == 0` safe note remain.
- Refresh wiring: `OnEditPresentationRequested` (prime + refresh on dialog `true`), add handler
  (prime before refresh), `OnItemChanged` (non-blocking `Dispatcher.InvokeAsync` prime + refresh).
- `app.db` NOT reset: no changes to `SlidePresentationService`/`SlidePresentation` storage; no db
  reset code touched.
- `OperatorWindow` StaticResource-free: all changes are code-behind only; no XAML resource added.

## Tests added/updated

New file `WorshipPlayer.Tests/SlidePresentationLinkTests.cs` (7 facts, mirrors
`SlidePresentationMapperTests` style):
- `ToSlides(SlidePresentation)` orders by `Order` then index, preserves text.
- `ToSlides(SlidePresentation)` null → `ArgumentNullException`.
- `ToSlides(SlidePresentation)` empty → non-null empty list.
- `ToServiceItemLink` sets Type=Slides, Title=Name, SlidePresentationId=Id, empty Slides.
- `ToServiceItemLink` blank name → "Slides Item".
- `ToServiceItemLink` null → `ArgumentNullException`.
- Add-flow: `AddItem(ToServiceItemLink(p))` yields one item with `SlidePresentationId` set and empty
  `Slides` (uses a temp-dir `ServiceOrderManager`, no `SaveAsync`, no data files touched).

Existing snapshot tests (`SlidePresentationMapperTests`) left unchanged and still pass.

## Exact build / test results

- `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded, 0 errors, 4 warnings**
  (the 4 pre-existing warnings: MediaLibraryPanel `_currentView` CS0414, ChordChartWindow CS8604,
  WaveformEditorWindow `_inReviewMode` CS0414 — unchanged).
- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` →
  **total: 665, passed: 665, failed: 0, skipped: 0** (was 658; +7 new facts). The
  "Error loading service order: 't' is an invalid start of a property name" console line is expected
  output from a pre-existing negative test, not a failure.

## Click-test checklist (manual)

- [ ] Open a service.
- [ ] Add a presentation → sidebar + center grid show its slides.
- [ ] Slides tab → Edit presentation → change a slide's text → Save + close.
- [ ] Return to the service → the linked item reflects the edit LIVE.
- [ ] Delete the presentation → the linked item renders the safe "No slides to show for this item."
      note, no crash.
- [ ] Old snapshot services (Slides item with stored slides, no `slidePresentationId`) still render
      their slides.

## Constraints honored

Scope limited to `ServiceOrder.cs`, `SlidePresentationMapper.cs`, `OperatorWindow.xaml.cs`, and the
new Core test file. Reused `ISlidePresentationService.GetByIdAsync` and `ServiceOrderManager`
(AddItem/SaveAsync). No new services/storage. `SlidePresentation` storage unchanged; `app.db` never
reset. `OperatorWindow` stays StaticResource-free. Existing features/events preserved (the snapshot
`ToServiceItem` and its tests remain). OWC branding untouched.
