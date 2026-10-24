# Clean up orphaned Slides live-links on delete and service load

Deleting a library presentation used to leave every current-service Slides item that live-linked it (via `ServiceItem.SlidePresentationId`) pointing at nothing, so the sidebar and center grid kept rendering the deleted deck's stale stored `Title` forever. This change routes the Slides-library Delete through the host so it can warn when N≥1 current-service items link the presentation, delete the presentation, remove those now-dangling items, persist, and refresh sidebar + center + library grid. It also reconciles already-orphaned items at service load inside `PrimeSlidePresentationCacheAsync`. The removal/count decisions are extracted to a pure Core helper (`SlideLinkReconciler`) with 12 unit tests. The approach is additive: no model-shape change, cleanup rides the existing `RemoveItem` + save path.

Watch for: load-time reconciliation persists a mutated service order silently on every open — correct per spec, but it means a transient presentation-store read failure could drop live items (possible, mitigated by the service's null-on-missing contract, see below). Everything else checks out.

**Verdict**: APPROVED

## High-level view

The delete path is now host-owned. `SlidesLibraryPanel` raises `DeletePresentationRequested` when a handler is subscribed and otherwise keeps its original standalone plain-confirm behavior, so the panel stays usable outside the OperatorWindow. The host computes the linked count with the pure helper, shows the richer or plain confirm accordingly, and only on Yes does it delete, remove linked items by descending index, persist, and refresh. The N=0 vs N≥1 branch is driven entirely by `CountItemsLinkingPresentation`, which counts only `Type == Slides` items matching the id.

Load-time reconciliation lives in `PrimeSlidePresentationCacheAsync`: after the cache is populated, the set of ids whose cache entry is non-null is the "still exists" set, and any Slides item with a non-empty link id outside that set is removed and the order saved. The pure `SlideLinkReconciler.FindOrphanedSlideItemIndices` guarantees empty-id items, resolvable items, and all non-Slides items are never touched. The removal is terminal for the pass (no re-prime), so there's no recursion.

The Core helper carries all the decision logic and is the only part with direct test coverage; the two WPF entry points are thin. Both removal sites delete by descending index so earlier removals don't shift later indices. Confirm reuses `MessageBox`, so the OperatorWindow StaticResource/ResourceDictionary-free invariant holds, and no DiagLog/file diagnostics were reintroduced.

<details>
<summary>Issues (2)</summary>

1. **Silent persist on load depends on null-on-missing read** — load-time reconciliation treats any Slides id with a null cache entry as orphaned and permanently removes + saves. This is correct only because `SlidePresentationService.GetByIdAsync` returns null (never throws) for a missing row and the prime loop swallows nothing per-id. Verified by existing `GetByIdAsync_UnknownId_ReturnsNull`. No change required; noting the coupling.
2. **Inlined index scan in delete handler duplicates helper intent** — `OnDeletePresentationRequested` re-implements the "Slides && id matches" index collection inline instead of reusing a helper. Non-blocking (the existing helper keys off an existing-set, not a single target id), but a `FindItemsLinkingPresentation(order, id)` helper would make both sites test-covered and symmetrical.

</details>

<details>
<summary>Details</summary>

## Host-owned delete via opt-in event

`SlidesLibraryPanel.DeleteMenu_Click` now checks whether `DeletePresentationRequested` has a subscriber; if so it raises the event and returns, otherwise it runs the original plain-confirm-then-delete-then-refresh path. This keeps the panel self-sufficient when used standalone and hands full ownership to the host when embedded in the OperatorWindow, where the host alone knows the current service order. The event carries the `SlidePresentationListItem`, giving the host both `Id` (for the count/removal) and `Name` (for the confirm text).

`OnDeletePresentationRequested` mirrors the established `OnAddPresentationToServiceRequested` shape: guard on `_slidePresentationService`, wrap in try/catch routing to `ShowToolError`, mutate through `_serviceOrderManager`, save via `SaveServiceOrderAsync`, then prime + refresh. The confirm message branches on `CountItemsLinkingPresentation`: N≥1 produces the richer warning naming the count and that those items will also be removed; N=0 produces the plain `"Delete the presentation \"<name>\"? This cannot be undone."`. On any result other than Yes it returns before deleting, so No is a true no-op.

After `DeleteAsync`, linked items are removed by collecting matching indices and deleting in descending order so earlier removals don't shift later indices. `RemoveItem` additionally purges the id from every group and renumbers `Order`, so no dangling group reference survives. The handler then re-primes the cache and calls `RefreshServiceOrderList` + `RefreshSlideSections` + `_slidesLibraryPanel?.RefreshGrid()`, so sidebar, center, and library card all update in the same turn.

## Load-time reconciliation inside the prime

The reconciliation is appended to `PrimeSlidePresentationCacheAsync`, which already walks every live-linked Slides item and fills `_slidePresentationCache[id]` with the presentation or null. The new block builds the existing-ids set from cache entries with a non-null value and feeds it plus the order to `FindOrphanedSlideItemIndices`, then removes returned indices descending and calls `SaveAsync` when anything changed.

```
prime: for each distinct Slides link id -> cache[id] = GetByIdAsync(id)  (null if deleted)
reconcile: existing = { id : cache[id] != null }
           orphans  = Slides items with non-empty id NOT in existing
           remove orphans (descending) -> SaveAsync
```

This fires on the service-load path (the `OnItemChanged` deferred `Dispatcher.InvokeAsync` block at the GoToItem(0) that ends a service open) and on tab-return primes. In the load-path block the refreshes (`RefreshServiceOrderList`/`RefreshSlideSections`) run after the awaited prime, so the removed rows are gone before the sidebar/center rebuild — the two already-orphaned items in the user's reported service will disappear on open. The removal does not re-prime, so there's no risk of a reconcile→prime→reconcile loop.

The one coupling worth stating: an item is removed whenever its cache entry is null, and the only thing that writes null is `GetByIdAsync` returning null for a genuinely-missing row. If that call could throw on a transient store error, the per-id prime loop would propagate out to the outer catch and skip reconciliation entirely (no partial removal), so the failure mode is fail-safe, not fail-destructive. Confirmed against `SlidePresentationServiceTests.GetByIdAsync_UnknownId_ReturnsNull` and `DeleteAsync_UnknownId_IsNoOp_DoesNotThrow`.

## Pure reconciler and its test coverage

`SlideLinkReconciler` is static, UI-free, and lives in Core so the net8.0 Tests project can exercise it without the WPF Presenter — the same placement rationale as `ServiceItemDisplayName`. `FindOrphanedSlideItemIndices` returns ascending indices of `Type == Slides` items with a non-empty `SlidePresentationId` not in the existing set; it throws `ArgumentNullException` on a null set and returns empty for a null/empty order. `CountItemsLinkingPresentation` counts only matching Slides items and returns 0 for null order / null-or-empty id.

The 12 tests cover the required matrix: orphaned Slides item returned; resolvable Slides item not returned; empty and null link ids not returned; non-Slides item with a dangling id not returned (both Song and Scripture); a mixed order returning exactly the orphan indices ascending; null order and empty items returning empty; null existing-set throwing. The count tests cover matching-only, non-Slides-not-counted, null order, and empty/null id. This is the behavioral core of the change, and it is the part that is directly tested — appropriate, since the two WPF call sites are thin wrappers over it.

The delete handler does not route its own index scan through the helper (the helper keys off an existing-set rather than a single target id), so that one inline loop is the only matching logic without direct coverage. A `FindItemsLinkingPresentation(order, id)` helper would close that gap, but the inline version is small and its behavior is identical to the tested count predicate.

## Invariants

Confirm uses `MessageBox.Show`, so no `StaticResource`/`ResourceDictionary` enters the OperatorWindow. A worktree grep for `DiagLog`/`StaticResource`/`ResourceDictionary` in the changed OperatorWindow surfaced only a pre-existing "StaticResource-free" comment and no diagnostic file logging; the prime's catch uses `Debug.WriteLine`, which is pre-existing and not file-based. No change to `ServiceItem`/`ServiceOrder` shape — cleanup is a list removal through the existing manager path. The build evidence notes the one new warning the first attempt introduced (CS8604 at the count call site) was resolved by widening the helper's `order` parameters to `ServiceOrder?`, matching their documented null tolerance, returning the warning count to the 6 known pre-existing ones.

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Core/Services/SlideLinkReconciler.cs` — new pure helper: `FindOrphanedSlideItemIndices` + `CountItemsLinkingPresentation`.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `OnDeletePresentationRequested` host handler + load-time reconciliation block in `PrimeSlidePresentationCacheAsync`; subscribes the panel's delete event.
- `WorshipPlayer.Presenter/Views/Slides/SlidesLibraryPanel.xaml.cs` — new `DeletePresentationRequested` event, `RefreshGrid` alias, opt-in routing in `DeleteMenu_Click`.
- `WorshipPlayer.Tests/SlideLinkReconcilerTests.cs` — new, 12 tests for the reconciler.

Full diff: `git -C .worktrees\slides-delete-cleanup diff origin/dev1`

</details>
