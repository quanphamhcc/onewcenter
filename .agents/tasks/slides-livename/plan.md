# Implementation Plan — Slides linked item: LIVE DISPLAYED NAME in sidebar + center header

## Goal (restated)

When a Slides-library presentation is live-linked into a service (`ServiceItem.Type == Slides`
with `SlidePresentationId` set) and the user RENAMES that presentation in the Slide editor, the
service item's label must update too — in the left SERVICE ORDER sidebar row AND the center section
header — resolved at DISPLAY time from the presentation's CURRENT `Name`. Non-Slides items are
unchanged. Stored `ServiceItem.Title` is NOT rewritten (display-time resolution only). No crash when
the presentation is missing/deleted.

## Root cause (verified in source)

Slide CONTENT already goes live via the primed cache, but the TITLE does not, for two reasons:

1. The sidebar and center header read the stored `ServiceItem.Title` captured at add time:
   - Sidebar: `MakeItemRow` sets `Title = item.Title`
     (`WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:1416`), built by `RefreshServiceOrderList`
     (`OperatorWindow.xaml.cs:1323`, rows added at `:1343` / `:1362` / `:1379`). The row model
     `ServiceItemDisplay.Title` is at `OperatorWindow.xaml.cs:1231`.
   - Center header: `BuildSectionHeader` sets the title `TextBlock.Text` from `item.Title`
     (`OperatorWindow.xaml.cs:2164`), called from `RefreshSlideSections` (`OperatorWindow.xaml.cs:2053`).

2. The ONLY place the current code pushes the live name in is a mutation inside the cache prime —
   `PrimeSlidePresentationCacheAsync` writes `linkedItem.Title = presentation.Name`
   (`OperatorWindow.xaml.cs:3430`). That has two problems:
   - It rewrites STORED data (`ServiceItem.Title`), which the brief forbids.
   - After the Slide editor saves a rename, `OnEditPresentationRequested` primes the cache then calls
     only `RefreshSlideSections()` (`OperatorWindow.xaml.cs:3339-3340`) — it never calls
     `RefreshServiceOrderList()`, so the SIDEBAR is never rebuilt. This is exactly the user's bug:
     "tôi đổi tên slide nhưng sidebar tên slide ko thay đổi" (renamed the slide, sidebar label
     didn't change). The center header happens to update only because the mutation at `:3430` changed
     the stored Title before `RefreshSlideSections`.

The fix: resolve the live name at DISPLAY time from the already-primed `_slidePresentationCache`
(field at `OperatorWindow.xaml.cs:113`, primed by `PrimeSlidePresentationCacheAsync` at
`:3402`, the same cache `GetSlidesForItem` reads at `:2274`), remove the stored-Title mutation, and
make the editor-save path also refresh the sidebar.

## Design decisions

- **Pure Core helper + test — YES (warranted).** Add a tiny pure function that maps
  `(ServiceItem, SlidePresentation?)` → the display name. Rationale: the resolution rule (Slides-link
  → current `Name`, else stored `Title`, with safe fallbacks) is pure and the Tests project is net8.0
  and cannot reference the WPF Presenter, so a Core helper is the only way to unit-test it. It also
  keeps `OperatorWindow` free of branching logic. This mirrors the existing `SlidePresentationMapper`
  (`WorshipPlayer.Core/Services/SlidePresentationMapper.cs`) placement + style.
  - Signature: `public static string ResolveDisplayName(ServiceItem item, SlidePresentation? linked)`
    in a new `WorshipPlayer.Core/Services/ServiceItemDisplayName.cs`.
  - Rule: if `item.Type == ServiceItemType.Slides` AND `!string.IsNullOrEmpty(item.SlidePresentationId)`
    AND `linked != null` AND `!string.IsNullOrWhiteSpace(linked.Name)` → `linked.Name.Trim()`.
    Otherwise → `item.Title` (returned as-is, even if empty; the existing UI callers already handle
    empty: `BuildSectionHeader` shows "(untitled)"; the sidebar shows an empty label as it does
    today). Guard: `ArgumentNullException` when `item` is null (matches `SlidePresentationMapper`'s
    null-guard convention).
  - Note: the helper takes the already-resolved `SlidePresentation?` (which the UI pulls from the
    cache). It stays pure/UI-free — it does not touch the cache or any service.

- **Display-time resolution location.** The UI resolves `linked` by reading the existing primed
  cache and passes it to the helper — no new async, no new service call on the UI thread:
  - Sidebar: change `MakeItemRow` from `static` to an instance method so it can read
    `_slidePresentationCache`; set `Title = ServiceItemDisplayName.ResolveDisplayName(item, linked)`
    where `linked` is `item.Type == Slides && item.SlidePresentationId` present
    `? _slidePresentationCache.GetValueOrDefault(item.SlidePresentationId) : null`. All three
    `_serviceOrderItems.Add(MakeItemRow(item))` call sites (`:1343`, `:1362`, `:1379`) stay the same
    (instance call). The one other caller, `BuildSectionHeader` at `:2156`
    (`MakeItemRow(item).Icon`), also works as an instance call (it only wants the icon; it passes a
    null `linked` implicitly via the same lookup — icon is name-independent so this is fine).
  - Center header: in `BuildSectionHeader` (`:2132`) replace the `item.Title` used for the title
    `TextBlock` at `:2164` with the resolved name, keeping the existing "(untitled)" fallback:
    `var displayName = ServiceItemDisplayName.ResolveDisplayName(item, linked);`
    `Text = string.IsNullOrWhiteSpace(displayName) ? "(untitled)" : displayName;` — where `linked`
    is looked up from `_slidePresentationCache` the same way.
  - Both read from `_slidePresentationCache`, which `PrimeSlidePresentationCacheAsync` populates
    before every refresh that must reflect a live edit — so no new blocking/async is introduced
    (reuses the exact pattern `GetSlidesForItem` already relies on at `:2274`).

- **Missing / deleted presentation fallback.** The cache stores `null` for a not-found id (see the
  prime at `:3421` and field comment at `:109-113`), and `GetValueOrDefault` also returns null for an
  id not yet primed. In both cases `linked == null`, so the helper falls back to `item.Title`. For a
  pure link whose stored `Title` was the original name, that is a sensible last-known label; for a
  blank Title the center header still shows "(untitled)". No crash, no exception path.

- **Remove the stored-Title mutation.** Delete the `linkedItem.Title = presentation.Name` block in
  `PrimeSlidePresentationCacheAsync` (`OperatorWindow.xaml.cs:3427-3433`, the `if (presentation !=
  null && !string.IsNullOrWhiteSpace(presentation.Name)) { foreach ... linkedItem.Title = ... }`).
  After this change the prime only populates the cache; the live name is purely display-derived, so
  stored `ServiceItem.Title` is never rewritten (honors "don't rewrite stored data"). The XML-doc
  lines above it referencing title-sync should be trimmed to match.

- **Refresh timing — IMMEDIATE (trivial and safe).** The missing sidebar refresh after an editor
  rename is a one-line add and is the user's actual complaint, so do it: in
  `OnEditPresentationRequested` (`:3337-3340`), after `await PrimeSlidePresentationCacheAsync();` add
  `RefreshServiceOrderList();` alongside the existing `RefreshSlideSections();`. Order: prime first
  (so the cache has the new name), then both refreshes. The add path
  (`OnAddPresentationToServiceRequested`, `:3380-3383`) already calls `RefreshServiceOrderList()`
  then prime then `RefreshSlideSections()`; re-order it to prime BEFORE `RefreshServiceOrderList()`
  so the newly added row shows the live name immediately (move the existing `RefreshServiceOrderList()`
  at `:3381` to after `await PrimeSlidePresentationCacheAsync()` at `:3382`). Both are in-process,
  non-blocking (the single prime `await` already exists), and add no polling.

- **Non-Slides items untouched.** The helper returns `item.Title` for every non-Slides item and for
  Slides items with no `SlidePresentationId` (legacy snapshot), so songs/scripture/etc. and legacy
  snapshot Slides items keep their stored title exactly as today.

- **Scope / safety.** Changes are limited to `OperatorWindow.xaml.cs` (code-behind only — no XAML,
  so `OperatorWindow` stays StaticResource-free) plus one new pure Core file and its test. No change
  to `ServiceOrder` storage, `SlidePresentationService`, or `SlidePresentation`; `app.db` is never
  reset. OWC branding untouched. Existing events/features preserved.

## Ordered steps

- [ ] 1. Add the pure Core helper `ServiceItemDisplayName.ResolveDisplayName(ServiceItem item,
      SlidePresentation? linked)`. Returns the linked presentation's trimmed `Name` for a Slides-link
      item when `linked` is non-null with a non-blank name; otherwise returns `item.Title`. Throws
      `ArgumentNullException` when `item` is null. UI-free, mirrors `SlidePresentationMapper` style
      and namespace (`WorshipPlayer.Core.Services`).
      Files: `WorshipPlayer.Core/Services/ServiceItemDisplayName.cs` (new)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors, 4 pre-existing warnings.

- [ ] 2. Add unit tests for `ServiceItemDisplayName.ResolveDisplayName` covering: (a) Slides item +
      `SlidePresentationId` set + non-null presentation with a name → returns that name (trimmed);
      (b) presentation `null` (deleted/not primed) → falls back to `item.Title`; (c) presentation
      with blank/whitespace `Name` → falls back to `item.Title`; (d) Slides item with NO
      `SlidePresentationId` (legacy snapshot) → returns `item.Title` regardless of `linked`;
      (e) non-Slides item (e.g. Song) → returns `item.Title`; (f) null `item` → `ArgumentNullException`.
      Follow the xUnit `[Fact]` style of `WorshipPlayer.Tests/SlidePresentationLinkTests.cs`.
      Files: `WorshipPlayer.Tests/ServiceItemDisplayNameTests.cs` (new)
      Verify: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug` → all pass; total
      rises from ~665 by the number of new facts (6), expect ~671 passed, 0 failed.

- [ ] 3. In `OperatorWindow.xaml.cs`, make `MakeItemRow` an instance method (remove `static` at
      `:1399`) and resolve the live name: look up `_slidePresentationCache` by
      `item.SlidePresentationId` (only when `item.Type == Slides` and the id is non-empty; else null)
      and set `Title = ServiceItemDisplayName.ResolveDisplayName(item, linked)` (replaces
      `Title = item.Title` at `:1416`). Leave the icon mapping unchanged. The three call sites in
      `RefreshServiceOrderList` (`:1343`, `:1362`, `:1379`) and the `MakeItemRow(item).Icon` use in
      `BuildSectionHeader` (`:2156`) compile unchanged as instance calls.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors, 4 warnings.

- [ ] 4. In `BuildSectionHeader` (`OperatorWindow.xaml.cs:2132`), resolve the live name the same way
      (cache lookup → `ServiceItemDisplayName.ResolveDisplayName(item, linked)`) and use it for the
      title `TextBlock.Text` at `:2164`, keeping the existing "(untitled)" fallback for a blank
      result. Do not touch the icon `TextBlock`.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors, 4 warnings.

- [ ] 5. In `PrimeSlidePresentationCacheAsync` (`OperatorWindow.xaml.cs:3402`), remove the
      stored-Title mutation block (the `if (presentation != null && ...) { foreach ... linkedItem.Title
      = presentation.Name; }` at `:3427-3433`) and trim the XML-doc/comment lines that describe
      syncing the Title. The prime now only populates the cache; the displayed name is derived at
      render time by steps 3-4. Stored `ServiceItem.Title` is no longer rewritten.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors, 4 warnings.

- [ ] 6. Fix refresh timing so a rename shows immediately. In `OnEditPresentationRequested`
      (`OperatorWindow.xaml.cs:3337-3340`), after `await PrimeSlidePresentationCacheAsync();` call
      BOTH `RefreshServiceOrderList();` (new — rebuilds the sidebar with the live name) and the
      existing `RefreshSlideSections();`. In `OnAddPresentationToServiceRequested`
      (`:3380-3383`), reorder so the prime runs BEFORE `RefreshServiceOrderList()` (prime →
      `RefreshServiceOrderList()` → `RefreshSlideSections()`) so a newly added row also shows the live
      name on first paint. No polling added.
      Files: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors, 4 warnings; then
      `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug` → all pass (~671), 0 failed.

- [ ] 7. Final verification pass: run the full build and test suite once more and confirm no
      regressions. Confirm no XAML/resource was added to `OperatorWindow` (code-behind only), no
      change under `WorshipPlayer.Core/Models/ServiceOrder.cs` storage, no `SlidePresentationService`
      change, and `app.db` untouched.
      Files: (none — verification only)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors, 4 pre-existing warnings;
      `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug` → ~671 passed, 0 failed.

## Manual (user visual) check — WPF can't be screenshotted headless

1. Open a service; add a Slides presentation → sidebar row + center header show its name.
2. Slides tab → Edit that presentation → rename it → Save + close the editor.
3. Return to the service: the sidebar row label AND the center section header now show the NEW name
   immediately (no tab switch needed).
4. Delete the presentation → the linked item does not crash; its label falls back to the stored
   Title (and the grid shows the existing "No slides to show for this item." note).
5. A song/scripture item's label is unchanged by all of the above.

## Notes / assumptions

- The brief allows an optional pure Core helper "if cleaner"; it is cleaner and testable here, so it
  is included (steps 1-2).
- `GetValueOrDefault` on `Dictionary<string, SlidePresentation?>` returns `null` both for a missing
  key and for a stored `null` entry — either way the helper falls back safely.
- Removing the stored-Title mutation (step 5) is required to satisfy "don't rewrite stored data";
  without steps 3-4 it would regress the center header, so steps 3-5 must land together (they do, in
  one feature/loop pass). Each step still leaves the solution buildable.
