# Implementation Plan: Robust scroll-to-section on sidebar selection

## Goal

Clicking ANY item in the left SERVICE ORDER sidebar must reliably scroll the center
all-sections pane to that item's section, for EVERY item type (song, slides, scripture,
media, blank, future types), every time — not just sometimes. The fix is GENERAL: it lives
in the shared selection-timing / `ScrollToSection` path keyed by the `ServiceItem`. NO
per-item-type scroll code. No change to section rendering content, models, storage, or data.

## Root cause (re-verified against current worktree code)

All citations: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` in the worktree
`.worktrees/scroll-fix` (branch `feature/scroll-to-section-fix`), unless noted. Line numbers
below were re-confirmed on the current file (they had drifted from the investigation doc).

1. **`BringIntoView` fires before layout.** `ServiceOrderList_SelectionChanged`
   (lines 1477–1503) calls `_serviceOrderManager?.GoToItem(index)` (line 1495), which fires
   `ItemChanged` -> `OnItemChanged` (line 648) -> synchronous `RefreshSlideSections()`
   (line 685). `RefreshSlideSections` does `SlideSectionsPanel.Children.Clear()` +
   `_sectionByItem.Clear()` (lines 2032–2033) then rebuilds and re-registers every item
   (`_sectionByItem[item] = sectionRoot;` at line 2143). Control returns synchronously and
   `ScrollToSection(display.Item)` (line 1501) calls `el.BringIntoView()` (line 2469) in the
   SAME UI message — before WPF has run a Measure/Arrange pass on the brand-new section
   elements, so the element has no arranged position and the scroll is a no-op / snaps to top.

2. **A second async rebuild clobbers the scroll.** `OnItemChanged` also queues
   `Dispatcher.InvokeAsync(async () => { await PrimeSlidePresentationCacheAsync(); RefreshSlideSections(); })`
   (lines 695–699; second `RefreshSlideSections()` at line 698). This runs AFTER the
   synchronous `ScrollToSection`, clears + rebuilds `SlideSectionsPanel` again, resets scroll
   to the top, and replaces the very element `BringIntoView` targeted. For `Type == Slides`
   items, `PrimeSlidePresentationCacheAsync` (line 3432) repopulates
   `_slidePresentationCache`, so `GetSlidesForItem` (line 2272) resolves real slides on this
   second build and the section's height changes after the fact — so even a correct first
   scroll would land wrong.

The design is otherwise sound: `_sectionByItem` is keyed by `ServiceItem` reference
(declaration line 2017) and every item is registered unconditionally (line 2143), so the map
lookup in `ScrollToSection` (line 2466) succeeds for all item types. The fix is purely about
WHEN the scroll runs, not about the map or per-type handling.

## Design decisions

- **Defer the selection scroll to `DispatcherPriority.Loaded`.** `Loaded` priority runs after
  WPF's Measure/Arrange, so the freshly rebuilt `sectionRoot` has a real arranged position and
  `BringIntoView` works. Chosen over `ScrollViewer.ScrollToVerticalOffset` math because
  `BringIntoView` already works once layout is done and keeps the fix type-agnostic (no
  per-section offset computation). Capture the selected `ServiceItem` in a local before
  deferring so a rapid re-selection does not scroll to a stale item.

- **Re-issue the scroll after the async rebuild, keyed off the engine's current item.** After
  the async `RefreshSlideSections()` at line 698 completes, re-scroll to
  `_engine.CurrentItem` (the `ServiceItem` the engine currently points at — the same instance
  used as the `_sectionByItem` key, exposed by `PresenterEngine.CurrentItem`,
  `WorshipPlayer.Presenter/Services/PresenterEngine.cs` line 47). Keying off the engine's
  current item (not the captured selection) means a rapid second selection naturally wins —
  the last rebuild re-scrolls to the latest live item. This is the only scroll that is
  guaranteed to run after the LAST rebuild and after the cache is primed, so Slides sections
  are at their final height. Also deferred at `DispatcherPriority.Loaded` for the same
  layout-timing reason.

- **Both changes together are required.** #1 alone still loses to the async rebuild (reason 2);
  #2 alone still fires `BringIntoView` on un-arranged elements on the first pass (reason 1) if
  not deferred. Together they guarantee a post-layout, post-last-rebuild scroll to the selected
  item.

- **Type-agnostic, shared path only.** All edits are in the selection-timing path and reuse the
  single shared `ScrollToSection(ServiceItem)`. No branching on `ServiceItemType`. New item
  types added later get the same behavior for free because they already register in
  `_sectionByItem` at line 2143.

- **Out of scope (follow-up, per task):** the cache-change optimization (#3 in the
  investigation — prime once and only rebuild when a cache entry actually changed). This fix
  compensates for the redundant second rebuild rather than removing it; removing it has a
  higher regression surface (live rename/edit-reflect behavior) and is explicitly a follow-up.

- **Constraints honored:** no section-rendering/content, model, storage, or data changes;
  `OperatorWindow.xaml` stays StaticResource-free (no XAML edit at all — all edits are in
  `OperatorWindow.xaml.cs`); engine/live/transport flow (`GoToItem` -> `ItemChanged`) is
  untouched, so interaction is not broken.

## Edits

All edits are confined to
`WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` (worktree). Blast radius: the operator
Live view's center-pane scroll only. No API, engine, XAML, model, or data changes.

- [ ] 1. Defer the selection scroll to `DispatcherPriority.Loaded` with a captured target.
      In `ServiceOrderList_SelectionChanged`, replace the immediate `ScrollToSection(display.Item);`
      call (currently line 1501) with: capture `var target = display.Item;` then
      `Dispatcher.InvokeAsync(() => ScrollToSection(target), System.Windows.Threading.DispatcherPriority.Loaded);`.
      This makes `BringIntoView` run after the synchronous `RefreshSlideSections` rebuild (line 685)
      has been laid out, fixing reason 1. The captured local prevents scrolling to a stale item on
      rapid re-selection. Leave `GoToItem(index)` (line 1495) and the surrounding guards unchanged.
      Files: WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs (method lines 1477–1503; edit at line 1501).
      Verify: `dotnet build WorshipBacktrackPlayer.sln` reports 0 errors; the method still reads
      as capture-then-deferred-scroll; this item leaves the file compiling.

- [ ] 2. Re-issue the scroll after the async rebuild, keyed off the engine's current item.
      In `OnItemChanged`, inside the existing `Dispatcher.InvokeAsync(async () => { ... })` block
      (lines 695–699), immediately AFTER the second `RefreshSlideSections();` (line 698) add a
      deferred re-scroll to the engine's current item:
      `var current = _engine?.CurrentItem; if (current != null) Dispatcher.InvokeAsync(() => ScrollToSection(current), System.Windows.Threading.DispatcherPriority.Loaded);`.
      Because this runs after the LAST (async) rebuild and after `PrimeSlidePresentationCacheAsync`
      (line 696) has primed the cache, the target section is at its final height — fixing reason 2
      and the Slides-item height-shift case. Keying off `_engine.CurrentItem` (the live `ServiceItem`,
      PresenterEngine.cs line 47) makes the latest selection win on rapid navigation. Do NOT change
      the prime call or the existing `RefreshSlideSections()`; only append the re-scroll.
      Files: WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs (lines 695–699; insert after line 698).
      Verify: `dotnet build WorshipBacktrackPlayer.sln` reports 0 errors; `ScrollToSection` (lines
      2463–2470) and `_sectionByItem` (declaration line 2017, registration line 2143) are left
      unchanged — the re-scroll reuses the existing shared lookup.

- [ ] 3. Full build + regression test run.
      No new unit tests: `OperatorWindow.xaml.cs` is NOT compiled into `WorshipPlayer.Tests`
      (the Presenter project is `net8.0-windows` and only selected Presenter *service* sources are
      linked into the test project per `WorshipPlayer.Tests.csproj`), and WPF layout/`BringIntoView`
      timing cannot be exercised headless. Verification is build-green + no regression in the
      existing engine/service-order suite, plus the manual click-test checklist below.
      Files: none (verification only).
      Verify:
        1. `dotnet build WorshipBacktrackPlayer.sln` -> Build succeeded, 0 errors (warnings
           unchanged from baseline).
        2. `dotnet test WorshipBacktrackPlayer.sln` -> all existing tests pass (the
           `ServiceOrderManager` / `PresenterEngine` suites — e.g. `PresenterEngineSlideSourceTests`,
           `GenerateSlidesForSongTests`, `AutoAdvanceSettingsPersistenceTests` — stay green; no
           behavior change to engine navigation).

## file:line trace the coder must confirm before/after the edit

1. Sidebar click -> `ServiceOrderList_SelectionChanged` (line 1477) -> `GoToItem(index)` (line 1495).
2. `GoToItem` -> `ItemChanged` -> `OnItemChanged` (line 648) -> synchronous `RefreshSlideSections()`
   (line 685): `Children.Clear()` + `_sectionByItem.Clear()` (lines 2032–2033) + rebuild +
   `_sectionByItem[item] = sectionRoot;` (line 2143).
3. BEFORE the fix: `ScrollToSection(display.Item)` (line 1501) -> `BringIntoView()` (line 2469)
   runs synchronously, pre-layout -> no-op / top. AFTER edit 1: the scroll is queued at
   `Loaded` priority, so it runs post-layout.
4. `OnItemChanged` async block (lines 695–699): `PrimeSlidePresentationCacheAsync()` (line 696,
   impl line 3432) then second `RefreshSlideSections()` (line 698) rebuilds again. AFTER edit 2:
   a `Loaded`-priority `ScrollToSection(_engine.CurrentItem)` runs after this last rebuild and
   after the cache is primed, landing on the selected item at final height.
5. `ScrollToSection` (lines 2463–2470) and `_sectionByItem` (line 2017 / 2143) are unchanged —
   the fix only changes WHEN `ScrollToSection` is invoked.

## Manual click-test checklist (WPF cannot be screenshotted headless)

Run `dotnet run --project WorshipPlayer.UI`, open a service that contains a mix of item types,
then verify the center pane scrolls to the clicked item's section EVERY time:

- [ ] Click a Song item far down the list -> center pane scrolls so that song's section is in view.
- [ ] Click a Slides-linked item (e.g. "test slides") -> center pane scrolls to its section and
      lands on it after thumbnails resolve (does NOT snap back to the first section / top).
- [ ] Click a Scripture item -> scrolls to its section.
- [ ] Click a Media item -> scrolls to its section.
- [ ] Click a Blank item -> scrolls to its section.
- [ ] Rapidly click two different items in succession -> the pane ends on the LAST clicked item,
      not a stale earlier one.
- [ ] Click the first item, then the last, then the first again -> each click lands on the correct
      section; no stuck-at-top behavior.
- [ ] Confirm interaction is intact: selecting an item still drives engine/live navigation
      (transport, previews, go-live) exactly as before — only the scroll timing changed.

## Deliverable confirmation

- Edits are code-only, confined to `OperatorWindow.xaml.cs`; no data reset, no model/storage
  change, no XAML change (OperatorWindow stays StaticResource-free).
- The fix is general and type-agnostic: it reuses the single shared `ScrollToSection(ServiceItem)`
  keyed by `_sectionByItem`; no per-item-type scroll branch is added.
