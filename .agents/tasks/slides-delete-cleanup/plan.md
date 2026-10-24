# Implementation Plan — Slides delete cleanup (orphaned live-link references)

All work targets the worktree:
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slides-delete-cleanup`
Never edit the user's checkout at `...\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter`.

Build command (from the worktree):
`dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug`
Tests: `dotnet test` (xUnit; the Tests project is `WorshipPlayer.Tests\WorshipPlayer.Tests.csproj`, net8.0, references Core only — no WPF). NEVER clean or delete `bin`/`obj`.

## The bug (confirmed against source)

- A Slides service item is a LIVE LINK to a library presentation via `ServiceItem.SlidePresentationId` (`WorshipPlayer.Core\Models\ServiceOrder.cs`). Slides are resolved on demand; the item stores no snapshot (`SlidePresentationMapper.ToServiceItemLink`).
- `SlidePresentationService.DeleteAsync(id)` (`WorshipPlayer.Core\Services\SlidePresentationService.cs`) only runs `DELETE FROM SlidePresentations WHERE Id=@Id`. It never touches any service order.
- `SlidesLibraryPanel.DeleteMenu_Click` (`WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml.cs`) shows a plain Yes/No confirm, calls `DeleteAsync(item.Id)`, then `Refresh()` which reloads ONLY the library grid.
- Result: every service item with `SlidePresentationId == deletedId` becomes a dangling link. In `OperatorWindow`, `ResolveLinkedPresentation(item)` reads `_slidePresentationCache.GetValueOrDefault(id)` → null → `ServiceItemDisplayName.ResolveDisplayName(item, null)` falls back to the stale stored `Title`, so the sidebar + center keep the old name and the item can never show slides. The user already has two such orphans in live data.

## Chosen behavior (option A, chosen by the user)

- On delete of a presentation that is linked by N ≥ 1 items in the CURRENT service: a richer confirm ("used by N item(s) … will also remove those item(s) … Delete anyway?"); on Yes, delete the presentation AND remove every current-order item whose `SlidePresentationId == deletedId`, persist, and refresh sidebar + center. On No, do nothing. When not linked, keep the existing plain confirm and plain delete+grid-refresh.
- Already-orphaned items in live data are cleaned up at LOAD time (**chosen reconciliation strategy below**).

### Chosen reconciliation strategy and why

**On the host's cache-prime path (`OperatorWindow.PrimeSlidePresentationCacheAsync`), silently remove Slides items whose `SlidePresentationId` is non-empty but resolves to no existing presentation, then persist the cleaned order.** This is option A ("remove"), explicitly chosen by the user over the "(deleted presentation)" label alternative.

Why here rather than in `ServiceOrderManager.LoadAsync`:
- `PrimeSlidePresentationCacheAsync` is the one place that already resolves every linked presentation by id against `_slidePresentationService` (the async service call the Core manager deliberately does not have — the manager has no `ISlidePresentationService` dependency and reconciling there would force a new dependency + async in a path documented as best-effort/file-authoritative). The prime already runs on every service open (via `OnItemChanged`'s deferred `Dispatcher.InvokeAsync`) and after every edit/add, so reconciliation naturally covers auto-load, manual Load Service, and tab returns.
- The prime builds `_slidePresentationCache[id] = presentation|null`. After building it, the set of ids whose value is null is exactly the orphan set — reconciliation is a cheap read of data already fetched.
- Keep the REMOVAL DECISION pure/testable by extracting a Core helper `SlideLinkReconciler` (below). The host feeds it the order + the set of existing ids and applies the returned indices through the existing `ServiceOrderManager.RemoveItem` + `SaveServiceOrderAsync` flow. Safety: only items with `Type == Slides` AND non-empty `SlidePresentationId` AND id not in the existing set are returned; unlinked Slides items (empty id), resolvable Slides items, and all non-Slides items are never returned.

## Ordered implementation items

- [ ] 1. Create the pure Core helper `SlideLinkReconciler`.
      Add `public static class SlideLinkReconciler` with
      `public static IReadOnlyList<int> FindOrphanedSlideItemIndices(ServiceOrder order, ISet<string> existingPresentationIds)`.
      Rules (pure, UI-free, no service/cache access): return the indices (ascending, into `order.Items`) of every item where `item.Type == ServiceItemType.Slides` AND `!string.IsNullOrEmpty(item.SlidePresentationId)` AND `!existingPresentationIds.Contains(item.SlidePresentationId)`. Return an empty list for a null `order` or null/empty `order.Items`. Throw `ArgumentNullException` when `existingPresentationIds` is null (matches the null-guard convention in `ServiceItemDisplayName`/`SlidePresentationMapper`). Also add a convenience `public static int CountItemsLinkingPresentation(ServiceOrder order, string presentationId)` returning how many items are `Slides` with `SlidePresentationId == presentationId` (used by the delete confirm); returns 0 for null order or null/empty id.
      Files: `WorshipPlayer.Core\Services\SlideLinkReconciler.cs`
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds (Core compiles as part of the UI build graph).

- [ ] 2. Add unit tests for `SlideLinkReconciler` (xUnit, net8.0, Core-only).
      Mirror the style of `WorshipPlayer.Tests\ServiceItemDisplayNameTests.cs`. Cover `FindOrphanedSlideItemIndices`:
      (a) orphaned — a Slides item with a non-empty id NOT in the existing set is returned;
      (b) linked/resolvable — a Slides item whose id IS in the existing set is NOT returned;
      (c) unlinked — a Slides item with empty/null `SlidePresentationId` is NOT returned;
      (d) non-Slides — a Song/Scripture item (even with a dangling `SlidePresentationId`) is NOT returned;
      (e) mixed order — returns exactly the orphan indices in ascending order, leaving others untouched;
      (f) empty/null order → empty list; null `existingPresentationIds` → `ArgumentNullException`.
      Also cover `CountItemsLinkingPresentation`: counts only matching Slides items; 0 for null order / empty id / non-Slides matches.
      Files: `WorshipPlayer.Tests\SlideLinkReconcilerTests.cs`
      Verify: `dotnet test` — the new `SlideLinkReconcilerTests` pass and the existing suite stays green.

- [ ] 3. Add a `DeletePresentationRequested` event to `SlidesLibraryPanel` and route Delete through the host when a service is loaded.
      In `WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml.cs` add `public event EventHandler<SlidePresentationListItem>? DeletePresentationRequested;` (mirror the existing `EditPresentationRequested` / `AddPresentationToServiceRequested` declarations and their XML-doc style). Change `DeleteMenu_Click` so that when a host handler is subscribed it raises `DeletePresentationRequested?.Invoke(this, item)` and returns WITHOUT showing a MessageBox or calling the service (the host owns confirm + delete + cleanup). Keep a safe fallback: when `DeletePresentationRequested` has no subscriber, preserve the current behavior exactly (plain Yes/No confirm → `_slidePresentationService.DeleteAsync(item.Id)` → `Refresh()`). Add a small `public void RefreshGrid()` that calls the existing private `Refresh()` so the host can refresh the library grid after it deletes (do not widen other internals).
      Files: `WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml.cs`
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds.

- [ ] 4. Wire and implement the host delete handler `OnDeletePresentationRequested` in `OperatorWindow`, mirroring `OnAddPresentationToServiceRequested`.
      In `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`, in the `"Slides"` case of the panel-creation switch (where `EditPresentationRequested` / `AddPresentationToServiceRequested` are subscribed, ~line 2963), add `_slidesLibraryPanel.DeletePresentationRequested += OnDeletePresentationRequested;`.
      Add `private async void OnDeletePresentationRequested(object? sender, SlidePresentationListItem item)` that:
      1. Guards `_slidePresentationService == null` → return.
      2. Computes `int linkedCount` via `SlideLinkReconciler.CountItemsLinkingPresentation(_serviceOrderManager?.CurrentServiceOrder, item.Id)` (0 when no service loaded).
      3. Shows the confirm MessageBox (owner `Window.GetWindow(this)` or `this`, `YesNo`, `MessageBoxImage.Warning`): when `linkedCount > 0`, the richer text — e.g. `Delete the presentation "{item.Name}"? This presentation is used by {linkedCount} item(s) in the current service. Deleting it will also remove those item(s) from the service. This cannot be undone.`; otherwise the existing plain text `Delete the presentation "{item.Name}"? This cannot be undone.`. On `!= Yes` → return.
      4. `await _slidePresentationService.DeleteAsync(item.Id);`
      5. If a service is loaded, remove every current-order item whose `Type == Slides` && `SlidePresentationId == item.Id`: compute indices via `SlideLinkReconciler.FindOrphanedSlideItemIndices(order, existingIdsWithoutDeleted)` OR a direct id match, and call `_serviceOrderManager.RemoveItem(index)` in DESCENDING index order (so earlier removals don't shift later indices), then `await SaveServiceOrderAsync();`. (`RemoveItem` already purges the id from groups and reorders — see `ServiceOrderManager.RemoveItem`.)
      6. `await PrimeSlidePresentationCacheAsync(); RefreshServiceOrderList(); RefreshSlideSections();` then `_slidesLibraryPanel?.RefreshGrid();` so the grid and sidebar/center all update.
      7. Wrap the body in try/catch calling `ShowToolError("Delete Slides", ex.Message)` (same pattern as `OnAddPresentationToServiceRequested`). Add no `ResourceDictionary`/`StaticResource` — inline/MessageBox only (host crash-safety invariant).
      Files: `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds.

- [ ] 5. Add load-time reconciliation inside `PrimeSlidePresentationCacheAsync`.
      In `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`, after the loop that fills `_slidePresentationCache[id] = presentation`, build the set of EXISTING presentation ids (ids whose cached value is non-null), call `SlideLinkReconciler.FindOrphanedSlideItemIndices(order, existingIds)`, and if it returns any indices: remove them via `_serviceOrderManager.RemoveItem(index)` in DESCENDING order, then `await _serviceOrderManager.SaveAsync();` (persist the cleaned order). Guard so this only runs when a service is loaded and `_slidePresentationService != null`; keep the whole method's existing try/catch-and-swallow behavior so a failure never crashes the UI. Do NOT re-trigger a prime recursively — the removal here is terminal for this pass. Because `OnItemChanged`'s deferred block already calls `PrimeSlidePresentationCacheAsync()` then `RefreshServiceOrderList()` / `RefreshSlideSections()`, the user's two existing orphans disappear on the next service open with no extra wiring. Only genuinely orphaned items are removed (non-empty id resolving to nothing); unlinked and resolvable Slides items and all non-Slides items are untouched (enforced by the pure helper).
      Files: `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds; `dotnet test` stays green.

- [ ] 6. Final verification — build + full test run.
      Confirm no diagnostic file logging was introduced (only existing `System.Diagnostics.Debug.WriteLine` / `ShowToolError` patterns used). Confirm no `ResourceDictionary`/`StaticResource` added in `OperatorWindow`. Confirm the live-link design and model shape are unchanged (only `Items`-list cleanup through the existing manager/save API; no new persisted fields).
      Files: none (verification only)
      Verify: from the worktree run `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` (succeeds) and `dotnet test` (all tests pass, including the new `SlideLinkReconcilerTests`). Do not clean/delete `bin`/`obj`.

## Invariants honored

- Additive/behavioral only: no change to the live-link design or `ServiceItem`/`ServiceOrder` model shape. Orphan removal is a data cleanup of the `Items` list via `ServiceOrderManager.RemoveItem` + the existing `SaveServiceOrderAsync`/`SaveAsync` path.
- Only `Type == Slides` items whose `SlidePresentationId` equals the deleted/missing id are touched; Song/Scripture/other items and resolvable or unlinked Slides items are never removed (enforced by the pure `SlideLinkReconciler`, unit-tested in item 2).
- OWC branding/dark theme untouched; reuse `MessageBox` for the confirm (already used in `DeleteMenu_Click`). No `ResourceDictionary`/`StaticResource` in `OperatorWindow`.
- No temporary diagnostic file logging reintroduced.

## Notes / assumptions

- The delete handler removes by descending index to keep indices valid across successive `RemoveItem` calls (the manager reorders on each remove).
- `_slidesLibraryPanel.RefreshGrid()` is added because the panel's `Refresh()` is currently referenced from the host only via `OnEditPresentationRequested` (`_slidesLibraryPanel?.Refresh()`); `Refresh()` is already public, so `RefreshGrid()` is a thin, intention-revealing alias — if preferred, the implementer may call the existing public `Refresh()` directly and skip adding `RefreshGrid()`. Either is acceptable; the behavior (reload the library grid after host-side delete) is what matters.
