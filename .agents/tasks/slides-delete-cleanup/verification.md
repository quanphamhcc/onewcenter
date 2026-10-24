# Verification — Slides delete cleanup (orphaned live-link references)

Worktree: `.worktrees\slides-delete-cleanup` · Branch: `feature/slides-delete-cleanup`

## Build

Command (from the worktree, incremental — no clean):
`dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug`

Result: **Build succeeded, 0 errors, 6 warnings** — all pre-existing, none introduced by this change:
- CS4014 `OperatorWindow.xaml.cs(728)` (fire-and-forget prime — pre-existing)
- CS0414 `MediaLibraryPanel._currentView`
- CS0414 `WaveformEditorWindow._inReviewMode`
- CS8604 `ChordChartWindow.BuildChordLine`

(An initial pass produced a NEW CS8604 at the `CountItemsLinkingPresentation` call site because the
helper took a non-nullable `ServiceOrder`. Fixed by annotating `SlideLinkReconciler`'s `order`
parameters as `ServiceOrder?` — which matches their documented null-tolerant behavior — so the
warning count is back to the 6 known pre-existing ones.)

## Tests

Command: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`

Result: **Passed! Failed: 0, Passed: 854, Skipped: 0, Total: 854.**
Previously ~842; the +12 are the new `SlideLinkReconcilerTests`.

New `WorshipPlayer.Tests\SlideLinkReconcilerTests.cs` covers the pure Core helper:
- `FindOrphanedSlideItemIndices`: orphaned Slides item returned; resolvable Slides item NOT
  returned; empty/null link id NOT returned; non-Slides item with a dangling id NOT returned; mixed
  order returns exactly the orphan indices ascending; null order and empty items → empty; null
  existing-set → `ArgumentNullException`.
- `CountItemsLinkingPresentation`: counts only matching Slides items; non-Slides match not counted;
  null order / empty / null id → 0.

## UI verification

A headless/automated run of the WPF app is NOT feasible in this environment (OperatorWindow needs an
interactive Windows desktop session), so UI behavior was verified by build + the pure-logic unit
tests above, NOT by reading XAML. Visual correctness is left for manual confirmation. The app.db was
NOT reset and `bin`/`obj` were NOT cleaned.

### Expected visual behavior (manual confirmation steps)

1. **Load-time reconciliation of the two already-orphaned items.** Open the user's current service
   (it has two Slides items whose `SlidePresentationId` points at presentations already deleted from
   the library). On service open, `OperatorWindow.PrimeSlidePresentationCacheAsync` primes the cache,
   detects those two ids resolve to nothing, removes those two items via
   `ServiceOrderManager.RemoveItem` (descending index), persists via `SaveAsync`, and the deferred
   `OnItemChanged` block then calls `RefreshServiceOrderList` / `RefreshSlideSections`.
   EXPECTED: the two stale sidebar rows disappear after load; the center grid no longer shows those
   sections; the saved service file no longer contains those items. Song/Scripture/other items and
   any Slides item with an empty link id or a still-existing presentation are untouched.

2. **Delete a LINKED presentation (richer warning + live removal).** Create a presentation, add it to
   the current service (⋮ → Add to current service), then in the Slides library open ⋮ → Delete on
   that presentation.
   EXPECTED: a Yes/No warning dialog reading approximately *"Delete the presentation "<name>"? This
   presentation is used by 1 item(s) in the current service. Deleting it will also remove those
   item(s) from the service. This cannot be undone."* On Yes: the presentation is deleted, the linked
   sidebar row disappears immediately (and the center section), and the library card is gone. On No:
   nothing changes.

3. **Delete an UNLINKED presentation.** Delete a library presentation that no current-service item
   points at.
   EXPECTED: the plain confirm *"Delete the presentation "<name>"? This cannot be undone."* appears;
   on Yes the card disappears and the service order is unaffected.

## Invariants confirmed

- Additive only: no change to the live-link design or `ServiceItem`/`ServiceOrder` model shape.
  Cleanup is a removal from the `Items` list via the existing `RemoveItem` + save path.
- Only `Type == Slides` items whose `SlidePresentationId` matches the deleted/missing id are touched
  (enforced by the pure, unit-tested `SlideLinkReconciler`).
- No `ResourceDictionary`/`StaticResource` added to `OperatorWindow`; the confirm reuses `MessageBox`.
- No diagnostic/DiagLog file logging introduced (searched the worktree — no `DiagLog` references).
