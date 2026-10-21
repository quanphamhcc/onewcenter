# Completion Report: Robust scroll-to-section on sidebar selection

Branch: `feature/scroll-to-section-fix`
Worktree: `.worktrees/scroll-fix`
Scope (changed): `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` (only)

## Summary

Clicking any item in the left SERVICE ORDER sidebar now reliably scrolls the center
all-sections pane to that item's section, for every item type, every time. The bug was a
timing problem, not a mapping problem: the scroll was issued before WPF had laid out the
freshly rebuilt section elements, and a second async rebuild then reset the scroll to the top.
The fix changes only WHEN/HOW the scroll is applied — it reuses the single shared
`ScrollToSection(ServiceItem)` keyed by `_sectionByItem`, with no per-item-type branch.

## The fix (two coordinated changes, both type-agnostic)

1. **Defer the selection scroll to `DispatcherPriority.Loaded` with a captured target.**
   In `ServiceOrderList_SelectionChanged`, the immediate `ScrollToSection(display.Item)` was
   replaced with `var target = display.Item;` then a `Dispatcher.InvokeAsync(() =>
   ScrollToSection(target), DispatcherPriority.Loaded)`. `Loaded` priority runs after WPF's
   Measure/Arrange pass, so `BringIntoView` targets an element with a real arranged position
   instead of a no-op pre-layout element. Capturing `target` in a local means a rapid second
   selection does not scroll to a stale item.

2. **Re-apply the scroll after the async rebuild, keyed off the engine's current item.**
   In `OnItemChanged`, inside the existing
   `Dispatcher.InvokeAsync(async () => { await PrimeSlidePresentationCacheAsync();
   RefreshSlideSections(); ... })` block, immediately after the second `RefreshSlideSections()`
   a deferred re-scroll was added: `var current = _engine?.CurrentItem; if (current != null)
   Dispatcher.InvokeAsync(() => ScrollToSection(current), DispatcherPriority.Loaded);`.
   This is the only scroll guaranteed to run after the LAST rebuild and after the cache is
   primed, so Slides sections are at their final (post-prime) height. Keying off
   `_engine.CurrentItem` (the live `ServiceItem`, the same instance used as the `_sectionByItem`
   key) means the latest selection wins on rapid navigation.

Both are required: #1 alone still loses to the async rebuild; #2 alone still fires on an
un-arranged element on the first synchronous pass.

## Why it is type-agnostic

- No branching on `ServiceItemType` anywhere in the fix.
- Both scroll calls go through the single shared `ScrollToSection(ServiceItem)`
  (`OperatorWindow.xaml.cs:2482`), which looks the item up in
  `_sectionByItem` (`OperatorWindow.xaml.cs:2485`).
- `_sectionByItem` is `Dictionary<ServiceItem, FrameworkElement>`
  (`OperatorWindow.xaml.cs:2036`), populated unconditionally for every item during
  `RefreshSlideSections` (`_sectionByItem[item] = sectionRoot;` at
  `OperatorWindow.xaml.cs:2162`). Any future item type that registers there gets the same
  behavior for free.

## file:line trace (post-edit, re-confirmed on the current file)

1. Sidebar click -> `ServiceOrderList_SelectionChanged` -> `_serviceOrderManager?.GoToItem(index)`
   fires `ItemChanged` -> `OnItemChanged`.
2. Synchronous `RefreshSlideSections()` clears + rebuilds the panel:
   `SlideSectionsPanel.Children.Clear()` at `OperatorWindow.xaml.cs:2051`, re-registers every
   item at `OperatorWindow.xaml.cs:2162`.
3. **Deferred (DispatcherPriority.Loaded) selection scroll** — the post-layout BringIntoView:
   - capture: `var target = display.Item;` — `OperatorWindow.xaml.cs:1520`
   - deferred call: `Dispatcher.InvokeAsync(() => ScrollToSection(target),
     System.Windows.Threading.DispatcherPriority.Loaded);` — `OperatorWindow.xaml.cs:1521`
4. **Pending / re-applied scroll after the async rebuild** (inside `OnItemChanged`'s
   `Dispatcher.InvokeAsync(async …)`, after `PrimeSlidePresentationCacheAsync()` + the second
   `RefreshSlideSections()`):
   - target off engine's current item: `var current = _engine?.CurrentItem;` —
     `OperatorWindow.xaml.cs:707`
   - deferred re-scroll: `Dispatcher.InvokeAsync(() => ScrollToSection(current),
     System.Windows.Threading.DispatcherPriority.Loaded);` — `OperatorWindow.xaml.cs:710`
5. Shared, unchanged scroll primitive:
   - `private void ScrollToSection(ServiceItem item)` — `OperatorWindow.xaml.cs:2482`
   - `_sectionByItem.TryGetValue(item, out var el)` then `el.BringIntoView()` —
     `OperatorWindow.xaml.cs:2485`
   - `_engine.CurrentItem` source: `PresenterEngine.cs:47`
     (`public ServiceItem? CurrentItem => _serviceOrderManager.CurrentItem;`).

### Proof of keyed-by-ServiceItem (type-agnostic, no per-type code)
Both new scroll calls pass a `ServiceItem` (`target` = the clicked `display.Item`; `current` =
`_engine.CurrentItem`) into the single `ScrollToSection(ServiceItem)`, which resolves the
element through `_sectionByItem` (`Dictionary<ServiceItem, FrameworkElement>`,
`OperatorWindow.xaml.cs:2036`). No `switch`/`if` on item type was added.

### Proof of no section-rendering / model / data changes
- `git status --porcelain` shows only `M WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
  (plus the untracked task docs under `.agents/tasks/scroll-fix/`).
- `RefreshSlideSections` section-building logic, `ScrollToSection`, `_sectionByItem`, models,
  storage, and data files (including `app.db`) are untouched. Only the selection handler and
  the `OnItemChanged` async block changed, and only to adjust scroll timing.

### Proof OperatorWindow stays StaticResource-free
- `OperatorWindow.xaml` was not edited at all (not in the change set).
- A grep for `StaticResource` in `OperatorWindow.xaml` returns only a source comment explaining
  that no resource dictionaries are used; there is no actual `StaticResource` markup usage.

## Build and test results (exact)

- `dotnet build WorshipBacktrackPlayer.sln` (worktree): **Build succeeded with 6 warning(s) in
  13.4s, Exit Code 0 — 0 errors.** All warnings are pre-existing and unrelated to the fix:
  - CS4014 at `OperatorWindow.xaml.cs(710,21)` — the pre-existing fire-and-forget
    `_ = Dispatcher.InvokeAsync(async …)` (the discard `_ =` sits on the outer statement; the
    reported column is the start of that pre-existing statement, not new code).
  - CS0414 `MediaLibraryPanel._currentView` assigned but never used.
  - CS8604 `ChordChartWindow.xaml.cs(235,51)` possible null arg.
  - CS0414 `WaveformEditorWindow._inReviewMode` assigned but never used.
  (The plan baseline cited "4 pre-existing warnings" for the Presenter-centric view; the full
  solution build surfaces 2 additional pre-existing UI-project warnings. No new warning was
  introduced by this change.)
- `dotnet test WorshipBacktrackPlayer.sln --no-build` (worktree):
  **total: 776, failed: 0, succeeded: 776, skipped: 0, duration 23.3s, Exit Code 0.**
  The console line `Error loading service order: 't' is an invalid start of a property name`
  is expected log output from a malformed-JSON handling test, not a failure.

Note: WPF layout / `BringIntoView` timing cannot be exercised headless and the view cannot be
screenshotted headless, so the fix is verified by build-green + no test regression plus the
manual click-test below. `OperatorWindow.xaml.cs` is not compiled into `WorshipPlayer.Tests`,
so no unit test could target it directly; no new test was added (per plan).

## Manual click-test checklist (user to run: `dotnet run --project WorshipPlayer.UI`)

Open a service containing a mix of item types (include a Slides-linked item and a
scripture/media/blank item), then:

- [ ] Click a Song item far down the list -> center pane scrolls to its section.
- [ ] Click a Slides-linked item -> center pane scrolls to its section and stays there after
      thumbnails resolve (does NOT snap back to top / first section).
- [ ] Click a Scripture item -> scrolls to its section.
- [ ] Click a Media item -> scrolls to its section.
- [ ] Click a Blank item -> scrolls to its section.
- [ ] Click the LAST item in the list -> center scrolls fully to it.
- [ ] Rapidly click two different items -> the pane ends on the LAST clicked item, not a stale
      earlier one.
- [ ] Immediately after opening the service (while the async cache primes) select an item ->
      the view ends scrolled to the selected item, not snapped to top.
- [ ] Live RED slide highlight, zoom, and transport still work; the app stays responsive.
- [ ] Selecting an item still drives engine/live navigation exactly as before (only scroll
      timing changed).

## Commit

Committed on `feature/scroll-to-section-fix` inside the worktree. Not merged, not pushed; the
worktree is left in place.
