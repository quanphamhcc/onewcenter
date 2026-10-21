# Bug: Clicking a sidebar item does not scroll the center pane to its section

Investigation of main repo HEAD `90bd844`. All citations are
`WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs` unless noted. READ-ONLY — no code changed.

## Summary answer

**(a) Is sidebar click wired to scroll the center? YES.**
`ServiceOrderList_SelectionChanged` (line 1477) ends by calling `ScrollToSection(display.Item)` (line 1502), which does `el.BringIntoView()` on the section element looked up from the `_sectionByItem` map (lines 2463–2470).

**(b) Why does it not scroll (and why 'test slides' in particular)?**
The section map is NOT the problem — every item, including the Slides-linked `test slides`, gets a section built and registered in `_sectionByItem` unconditionally (line 2162), even when its slide list is empty. The scroll fails for **two compounding timing reasons**:

1. **`BringIntoView` is called on freshly rebuilt, not-yet-laid-out elements.**
   Selection calls `GoToItem(index)` (line 1496) which fires `ItemChanged` → `OnItemChanged` (line 648) → `RefreshSlideSections()` (line 685). That method does `SlideSectionsPanel.Children.Clear()` + full rebuild (lines 2033, 2034, 2162). Control returns synchronously, then `ScrollToSection` calls `BringIntoView()` **in the same UI message** — before WPF has run a layout (Measure/Arrange) pass on the brand-new `sectionRoot` elements. An element with no arranged position produces a no-op / scroll-to-top, which is exactly the reported symptom (pane stays on the FIRST section, TheBlessing).

2. **A second, asynchronous rebuild clobbers the scroll.**
   `OnItemChanged` also queues `Dispatcher.InvokeAsync(async () => { await PrimeSlidePresentationCacheAsync(); RefreshSlideSections(); })` (lines 696–699). This runs AFTER the synchronous `ScrollToSection` has already executed, clears the cache (line 3437) and rebuilds `SlideSectionsPanel` again (line 697) — resetting scroll position back to the top and replacing the very element BringIntoView targeted.

**Why `test slides` specifically looks worse than songs:** `test slides` is `Type==Slides` with `SlidePresentationId` set and `item.Slides` empty. Its real slides only resolve from `_slidePresentationCache` once `PrimeSlidePresentationCacheAsync` finishes (`GetSlidesForItem`, lines 2296–2302). On the FIRST (synchronous) build inside `OnItemChanged`, the cache may be empty/stale (it is `Clear()`ed in the async prime at line 3437), so the section is built SHORT (the "No slides to show for this item." note, lines 2080–2093) or at a different height. Then the async rebuild (reason #2) changes that section's height again. So even if a layout-aware scroll fired, the target's position shifts under it. For a plain Song, the section has stable content on the first build, so only reasons #1/#2 apply; for `test slides` the async cache-prime reorders height on top of that, making the mis-scroll reliably visible.

**(c) It is NOT "section missing from map" and NOT "wrong key".** The map is keyed by `ServiceItem` reference (line 2019), the same instances held by `ServiceItemDisplay.Item` and by `CurrentServiceOrder.Items` (comment lines 2016–2018), and `RefreshSlideSections` registers EVERY item (line 2162). The lookup in `ScrollToSection` (line 2467) succeeds. The failure is **BringIntoView firing on un-arranged elements + a later async rebuild resetting scroll**, aggravated for Slides items by the async cache-prime changing section height.

## Evidence

### 1. Selection handler and the scroll call
`ServiceOrderList_SelectionChanged` (line 1477):
- Guards: `_suppressServiceOrderSelection` (1480), `_bulkEditMode` (1482), non-`ServiceItemDisplay` (1483), null `Item` group headers (1485).
- `int index = items.IndexOf(display.Item);` (1491) — reference-based index into `CurrentServiceOrder.Items`.
- `_serviceOrderManager?.GoToItem(index);` (1496).
- `ScrollToSection(display.Item);` (1502).

`ScrollToSection` (line 2463):
```
if (item == null) return;                                  // 2466
if (_sectionByItem.TryGetValue(item, out var el))          // 2467
    el.BringIntoView();                                    // 2468
```
No `Dispatcher.InvokeAsync`/`Loaded` deferral — runs immediately, in the same turn as the synchronous rebuild.

### 2. Map keying and unconditional registration
- Map declaration: `private readonly Dictionary<ServiceItem, FrameworkElement> _sectionByItem = new();` (line 2019), keyed by `ServiceItem` reference per the comment at 2013–2018.
- `RefreshSlideSections` loops `order.Items` (line 2064), builds `sectionRoot` (2071), and at the END of each iteration **unconditionally** registers and adds it:
  ```
  _sectionByItem[item] = sectionRoot;         // 2162
  SlideSectionsPanel.Children.Add(sectionRoot); // 2163
  ```
  This is OUTSIDE the `slides.Count == 0` branch (2080) and outside the try/catch, so empty Slides items and even failed items still get a map entry. **Conclusion: `test slides` always has a map entry.**

### 3. GoToItem → ItemChanged → synchronous rebuild
- `ServiceOrderManager.GoToItem` (`WorshipPlayer.Core\Services\ServiceOrderManager.cs` line 375) fires `ItemChanged` **unconditionally** (no "index unchanged" early-return), so selection always re-enters the rebuild.
- `OnItemChanged` (line 648) runs inside `Dispatcher.Invoke` (651, synchronous) and calls `RefreshSlideSections()` at line 685 — this is the "map is fresh" rebuild the selection handler's comment (1500–1501) relies on.

### 4. The clobbering async rebuild
`OnItemChanged` lines 696–699:
```
_ = Dispatcher.InvokeAsync(async () =>
{
    await PrimeSlidePresentationCacheAsync();   // clears + repopulates cache, line 3437
    RefreshSlideSections();                     // full rebuild #2, line 697
});
```
This is scheduled on the dispatcher queue, so it runs after the current message (which contained `ScrollToSection`) completes. Result: scroll position reset and the target element replaced.

### 5. Slides resolution is cache-dependent (height instability)
`GetSlidesForItem` for `Type==Slides` (lines 2296–2311): returns real slides only if `_slidePresentationCache` has a non-null entry; otherwise logs "cache not primed" and falls back to empty `item.Slides`, which drives the empty-note branch (2080–2093). `PrimeSlidePresentationCacheAsync` (3432) calls `_slidePresentationCache.Clear()` first (3437) then awaits `GetByIdAsync` per id (3454) — so between the synchronous build and the async rebuild the `test slides` section can switch from "empty note" to "N thumbnails," changing its vertical offset.

### 6. XAML container
`OperatorWindow.xaml`: `SlideSectionsScrollViewer` (line 418, `VerticalScrollBarVisibility="Auto"`) wraps `SlideSectionsPanel` StackPanel (line 427). `BringIntoView` on a descendant raises `RequestBringIntoView` that this ScrollViewer handles — but only once the descendant has an arranged position.

## Conclusions

- The design (map keyed by `ServiceItem`, section-per-item, `ScrollToSection` via `BringIntoView`) is sound and the map is correctly populated for all items, including the Slides-linked `test slides`.
- The scroll fails because `BringIntoView` runs **synchronously right after a full `Children.Clear()`/rebuild**, before layout, and because a **second async `RefreshSlideSections` resets scroll** afterward. For Slides items, the async cache-prime additionally changes the target section's height, so even a correct scroll would land wrong.

## Recommended minimal fix

1. **Defer the scroll until after layout.** In `ServiceOrderList_SelectionChanged`, replace the immediate `ScrollToSection(display.Item)` (line 1502) with a dispatcher-deferred call at `DispatcherPriority.Loaded` (or lower), e.g.:
   ```csharp
   var target = display.Item;
   Dispatcher.InvokeAsync(() => ScrollToSection(target), DispatcherPriority.Loaded);
   ```
   `Loaded` runs after Measure/Arrange, so the section has a real position for `BringIntoView`.

2. **Make the scroll survive the async rebuild.** The async prime+rebuild in `OnItemChanged` (696–699) runs after the deferred scroll above only if scheduled at a higher priority. Safest: after the async `RefreshSlideSections()` at line 697, re-scroll to the engine's current item (the item just selected). Add, inside that InvokeAsync after the rebuild:
   ```csharp
   var order = _engine?.CurrentServiceOrder;
   int idx = _engine?.CurrentItemIndex ?? -1;
   if (order != null && idx >= 0 && idx < order.Items.Count)
       Dispatcher.InvokeAsync(() => ScrollToSection(order.Items[idx]), DispatcherPriority.Loaded);
   ```
   This guarantees the final scroll happens after the LAST rebuild and after the cache is primed (so Slides sections are at their final height).

3. (Optional, lower effort) Prime the cache once on service open and only rebuild-on-prime when a cache entry actually changed, to avoid the redundant second rebuild on every navigation (compare pre/post prime snapshot before calling `RefreshSlideSections` at line 697). This removes the clobber entirely rather than compensating for it.

The smallest reliable change is #1 + #2 together: defer the selection scroll to `Loaded` priority AND re-issue it after the async rebuild. #1 alone still loses to the async rebuild; #2 alone still fires on un-arranged elements on the first pass.

## Risks

- **Deferred scroll (`InvokeAsync`/`Loaded`)**: capture the `ServiceItem` in a local (as shown) so a rapid second selection doesn't scroll to a stale item; the re-scroll in #2 keyed off `_engine.CurrentItemIndex` naturally lands on the latest selection. Low risk.
- **Re-scroll after async rebuild**: if the user manually scrolled away between selection and prime completion, the re-scroll will yank them back to the selected item. Acceptable — it matches the intent of "clicking an item scrolls to it," and the window is milliseconds.
- **Cache-change optimization (#3)**: changes refresh frequency; must keep the live rename/edit-reflect behavior that the current unconditional rebuild guarantees (comment 3376–3384). Higher regression surface — treat as a follow-up, not part of the minimal fix.
- No API/engine changes required; all edits are confined to `OperatorWindow.xaml.cs`, so blast radius is the operator Live view only.
