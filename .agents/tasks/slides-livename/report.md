# Completion Report — Slides linked item: LIVE DISPLAYED NAME in sidebar + center header

## Summary

A live-linked Slides service item (`ServiceItem.Type == Slides` with `SlidePresentationId` set) now
DISPLAYS the linked presentation's CURRENT `Name` in the left SERVICE ORDER sidebar row AND the
center section header, resolved at render time from the already-primed presentation cache. Renaming
the presentation updates the labels without rewriting stored data. Non-Slides items are unchanged.

## File:line trace

### Sidebar row title uses the linked presentation's current Name (via the primed cache)
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `MakeItemRow(ServiceItem item)` is now an
  INSTANCE method (was `static`) and sets
  `Title = ServiceItemDisplayName.ResolveDisplayName(item, ResolveLinkedPresentation(item))`.
- `ResolveLinkedPresentation(ServiceItem item)` (new private helper) reads
  `_slidePresentationCache.GetValueOrDefault(item.SlidePresentationId)` ONLY for Slides items with a
  non-empty link id; returns null for everything else.

### Center section header uses the current Name
- `OperatorWindow.xaml.cs` — `BuildSectionHeader(ServiceItem item)` title `TextBlock.Text` now uses
  `ServiceItemDisplayName.ResolveDisplayName(item, ResolveLinkedPresentation(item))`, keeping the
  existing `"(untitled)"` fallback when the resolved name is blank. The icon `TextBlock` still uses
  `MakeItemRow(item).Icon` (name-independent).

### Missing-presentation fallback (no crash)
- `WorshipPlayer.Core/Services/ServiceItemDisplayName.cs` — when `linked == null` (deleted/not primed)
  or its `Name` is blank, `ResolveDisplayName` returns the stored `item.Title`. The cache stores
  `null` for a not-found id and `GetValueOrDefault` returns `null` for an un-primed id, so both map to
  the safe fallback. No exception path.

### Non-Slides items unchanged
- `ServiceItemDisplayName.ResolveDisplayName` returns `item.Title` for any non-Slides item and for a
  Slides item with no `SlidePresentationId` (legacy snapshot). Songs/scripture/etc. keep their stored
  title exactly as before.

### No UI-thread blocking added
- `ResolveLinkedPresentation` is a synchronous in-memory dictionary read. The only async await is the
  existing `PrimeSlidePresentationCacheAsync` (unchanged location), the same cache `GetSlidesForItem`
  already relies on. No new async/blocking on the UI thread.

### Stored data not rewritten / app.db not reset
- `OperatorWindow.xaml.cs` — `PrimeSlidePresentationCacheAsync` no longer contains the
  `linkedItem.Title = presentation.Name` mutation block; the prime only populates the cache. Stored
  `ServiceItem.Title` is never rewritten. No `ServiceOrder` storage change, no
  `SlidePresentationService` change, `app.db` untouched.

### OperatorWindow StaticResource-free
- Changes are code-behind only (no XAML / no resource added), so OperatorWindow stays
  StaticResource-free.

## Cache-reuse approach

The displayed name is derived at render time from the existing `_slidePresentationCache`
(`Dictionary<string, SlidePresentation?>`), the SAME cache `GetSlidesForItem` reads and the SAME one
`PrimeSlidePresentationCacheAsync` populates before every refresh that must reflect a live edit. No
new cache, no new service call, no new async on the UI thread. A tiny pure Core helper
(`ServiceItemDisplayName.ResolveDisplayName`) applies the resolution rule and is unit-tested in Core
(the Tests project is net8.0 and can't reference the WPF Presenter).

## Refresh-timing behavior shipped — IMMEDIATE

- `OnEditPresentationRequested` (editor save/rename): after `await PrimeSlidePresentationCacheAsync();`
  it now calls BOTH `RefreshServiceOrderList();` (new) and `RefreshSlideSections();`. So a rename shows
  in the SIDEBAR and the CENTER HEADER immediately on closing the editor — this was the user's actual
  complaint ("tôi đổi tên slide nhưng sidebar tên slide ko thay đổi").
- `OnAddPresentationToServiceRequested` (add): reordered so prime runs BEFORE
  `RefreshServiceOrderList()`, so a newly added row shows the live name on first paint.

## Build / test results

- `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded, 0 Error(s), 4 Warning(s)**
  (all 4 pre-existing and unrelated).
- `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug` →
  **Passed! Failed: 0, Passed: 671, Skipped: 0, Total: 671** (baseline ~665; +6 new facts, no regressions).

See `verification.md` for exact command output.

## Click-test checklist (manual — WPF can't be screenshotted headless)

1. Open a service → add a Slides presentation → sidebar row + center header show its name.
2. Slides tab → Edit that presentation → rename it → Save + close editor.
3. Back in the service: sidebar row label AND center section header now show the NEW name
   immediately (no tab switch needed).
4. Delete the presentation → the linked item does not crash; its label falls back to the stored
   Title; the grid shows the existing "No slides to show for this item." note.
5. A song/scripture item's label is unchanged by all of the above.
