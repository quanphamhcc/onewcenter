# Findings: Slides rename not reflected in service-order sidebar / center header

Investigation scope: READ-ONLY. Main repo
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter`,
`main` at HEAD `38cf287`. The sibling folder `...-presenter 2` was ignored.

## Summary answer (read this first)

The source code at the current HEAD (`38cf287`) **already contains the complete, correct fix** for
this bug. The single rename path for a Slides presentation
(`SlidesLibraryPanel` ⋮ → Edit → `SlideEditorWindow` → `ISlidePresentationService.UpdateAsync`)
is handled by `OnEditPresentationRequested`, which — after the editor saves — re-primes
`_slidePresentationCache` **and** refreshes BOTH the sidebar and the center grid:

```
OperatorWindow.xaml.cs  OnEditPresentationRequested (lines 3365-3390)
    _slidesLibraryPanel?.Refresh();        // library card  -> updates (user confirms this works)
    await PrimeSlidePresentationCacheAsync(); // 3386 re-reads the NEW name from SQLite
    RefreshServiceOrderList();              // 3387 rebuilds sidebar rows (MakeItemRow -> live name)
    RefreshSlideSections();                 // 3388 rebuilds center headers (BuildSectionHeader)
```

So the stale sidebar/center name the user is seeing is **not reproducible from the HEAD source**.
The most likely cause is that **the running application is an older build that predates commit
`ff8bca1`**, which is exactly the commit that added the sidebar re-prime + `RefreshServiceOrderList()`
to this handler. Before `ff8bca1`, the Edit handler refreshed only the library card and the center
grid and did **not** refresh the sidebar nor re-prime the cache for the name. This matches the
reported symptom precisely (library card updates, sidebar/center stay stale).

This is consistent with the surrounding conversation: the user had been pulling `dev1` into local
`main` and was unsure whether code had synced. A stale binary (app launched before pulling/rebuilding
`ff8bca1`/`38cf287`) fully explains the symptom.

Primary recommendation: **rebuild and relaunch the app from HEAD `38cf287`, then retry the rename.**
If the bug persists against a confirmed fresh build, it is a new/different defect; see "If it still
repros" below.

## Evidence

### 1. Name resolution for linked Slides items (pure rule + render-time read)

- `WorshipPlayer.Core\Services\ServiceItemDisplayName.cs:32` — `ResolveDisplayName(ServiceItem, SlidePresentation?)`:
  for a live-linked Slides item it returns the linked presentation's **current** `Name`, falling
  back to the stored `Title` when there is no link / blank name. Pure, unit-tested
  (`WorshipPlayer.Tests\ServiceItemDisplayNameTests.cs`).
- Sidebar: `OperatorWindow.xaml.cs:1435` — `MakeItemRow` sets each row's `Title` to
  `ServiceItemDisplayName.ResolveDisplayName(item, ResolveLinkedPresentation(item))`.
- Center header: `OperatorWindow.xaml.cs:2205` — `BuildSectionHeader` computes `displayName` the
  same way.
- Both read the presentation via `ResolveLinkedPresentation(item)`
  (`OperatorWindow.xaml.cs:1448-1455`), which is a **synchronous** lookup into
  `_slidePresentationCache` (`OperatorWindow.xaml.cs:1453`,
  `_slidePresentationCache.GetValueOrDefault(item.SlidePresentationId)`).

### 2. The cache and when it is (re)primed

- `_slidePresentationCache` declared at `OperatorWindow.xaml.cs:113`
  (`Dictionary<string, SlidePresentation?>`).
- `PrimeSlidePresentationCacheAsync` (`OperatorWindow.xaml.cs:3451-3483`): **clears** the cache
  (`:3455`) then, for every live-linked Slides item in the open service, calls
  `_slidePresentationService.GetByIdAsync(id)` and stores the result (`:3470`). Because it clears
  first, a re-prime always picks up a renamed presentation. Stored `ServiceItem.Title` is
  intentionally never rewritten (comment at `:3472`).
- Primed/refreshed at: initial load (`:247-249`), Slides-tab activation / service open
  (`:696-698`), **after edit** (`:3386`), and **after add-to-service** (`:3431`).

### 3. The rename path — there is exactly ONE

- Entry: `SlidesLibraryPanel.xaml.cs:187` `EditMenu_Click` → raises `EditPresentationRequested`
  (`:42`). No inline/card rename and no second "Edit Slides" dialog exists for presentations —
  `OnEditSlidesRequested` (`OperatorWindow.xaml.cs:3066`) and `SongSlideEditorWindow` are the
  **Song** lyrics-slides editor, unrelated to `SlidePresentation`.
- Host: `OnEditPresentationRequested` (`OperatorWindow.xaml.cs:3365-3390`) opens
  `new SlideEditorWindow(presentation, _slidePresentationService)` (`:3376`).
- Save: `SlideEditorWindow.xaml.cs` `SaveAndClose_Click` — `_presentation.Name = entered` (`:175`),
  `await _service.UpdateAsync(_presentation)` (`:177`), then `DialogResult = true` (`:180`). A pure
  rename DOES set `DialogResult = true`, so the host's `if (win.ShowDialog() == true)` branch runs.
- Persistence: `SlidePresentationService.UpdateAsync` (`SlidePresentationService.cs:143`) upserts to
  SQLite; `GetByIdAsync` (`:97`) always re-reads from the DB with **no in-memory caching**. So after
  Save, `PrimeSlidePresentationCacheAsync`'s `GetByIdAsync` necessarily returns the new name. This is
  also why the library card (`SlidesLibraryPanel.LoadAsync` → `GetAllAsync`) shows the new name.

### 4. Why the symptom maps to a pre-`ff8bca1` build

`git show ff8bca1 -- WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` shows this commit ADDED,
inside `OnEditPresentationRequested`, both `await PrimeSlidePresentationCacheAsync();` and
`RefreshServiceOrderList();` (the sidebar refresh). The pre-`ff8bca1` handler kept only
`_slidesLibraryPanel?.Refresh()` + `RefreshSlideSections()` — i.e. it refreshed the library card and
the center grid but **neither re-primed the cache for the name nor refreshed the sidebar**. That older
behavior is an exact match for "card updates, sidebar stays stale." Commit `ff8bca1` is reachable
from HEAD `38cf287` (`38cf287` is its child), so HEAD is already fixed.

## Conclusions

- (a) Why stale while the card updates: In a build that predates `ff8bca1`, the Edit handler did not
  re-prime `_slidePresentationCache` and did not call `RefreshServiceOrderList()`, so the sidebar rows
  and center headers kept resolving the OLD cached name while the library card (served directly from
  SQLite via `GetAllAsync`) showed the new one.
- (b) Cause category: in the OLD code it was **both** — un-refreshed/not-re-primed cache **and** a
  missing sidebar refresh on the edit path. In the CURRENT HEAD code, neither gap exists.
- (c) The "current tab is Slides" theory does not hold: `RefreshServiceOrderList` /
  `RefreshSlideSections` rebuild the sidebar `ServiceOrderList` and the `SlideSectionsPanel`
  regardless of the active tab, and the screenshot shows those panels visible on the Slides tab. The
  decisive factor is whether the running binary includes the `ff8bca1` handler, not the active tab.

## Recommendation (minimal, already present in source)

No source change is required at HEAD. The correct, minimal fix is the one already committed:

> After `SlideEditorWindow` saves, re-prime the cache and refresh both panels —
> `OperatorWindow.xaml.cs:3386-3388`:
> `await PrimeSlidePresentationCacheAsync(); RefreshServiceOrderList(); RefreshSlideSections();`

Action for the user: **do a clean rebuild from HEAD `38cf287` and relaunch**, then rename again. The
sidebar row and center header should update immediately.

### If it still repros on a confirmed fresh HEAD build

Then investigate these (all currently look correct, listed by likelihood):

1. Whether the running `OperatorWindow` instance and the `SlidesLibraryPanel` use the **same**
   `ISlidePresentationService` instance that was handed `_slidePresentationService`
   (`OperatorWindow.xaml.cs:3376` vs the panel's `SlidePresentationService` setter). Since the
   service is stateless over SQLite this should not matter, but confirm one is injected.
2. `async void` ordering in `OnEditPresentationRequested`: `ShowDialog()` is modal/synchronous and
   the prime+refresh run after it returns on the UI thread, so there is no race; confirm no exception
   is swallowed inside `PrimeSlidePresentationCacheAsync` (it logs and swallows at `:3479-3481`).
3. That the edited presentation's `SlidePresentationId` actually equals the linked service item's id
   (a mismatched/duplicated id would leave the item resolving a different/old presentation).

## Risks

- None from this investigation (read-only; no code changed).
- The build-staleness hypothesis is the highest-probability explanation but is **not something this
  static read can 100% confirm** — it depends on the user's actual running binary, which was not
  inspected. The source-level conclusion (HEAD is fixed) IS confirmed by reading the files and the
  `ff8bca1` diff.
