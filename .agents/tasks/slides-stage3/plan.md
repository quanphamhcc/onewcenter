# Implementation Plan — Slides Stage 3: "Add to current service"

Wire the Slides-library "Add to current service" action so an authored `SlidePresentation`
becomes a real `ServiceItem` in the open service and renders its slides in the center
all-sections grid exactly like songs/scripture. Replace the Stage-3 "coming soon" stub.

All paths below are absolute under
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slides-stage3`.

---

## Verified call trace (from reading source)

1. User clicks a Slides card ⋮ → "Add to current service".
   `...\WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml.cs:191` `AddToServiceMenu_Click`
   raises `AddPresentationToServiceRequested` with a **`SlidePresentationListItem`** (carries
   `Id`/`Name`/`SlideCount` only — NOT the full presentation). Event declared at
   `SlidesLibraryPanel.xaml.cs:52`.
2. Host subscribes at `...\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs:2762`
   (`_slidesLibraryPanel.AddPresentationToServiceRequested += OnAddPresentationToServiceRequested`).
3. Current stub handler: `OperatorWindow.xaml.cs:3300-3301`
   `OnAddPresentationToServiceRequested(...) => ShowComingSoon("Add to Service", "Adding slides to a service is coming in a later update.")`.
   `ShowComingSoon` is at `OperatorWindow.xaml.cs:3278`. **This is what Stage 3 replaces.**
4. New handler resolves the full presentation via
   `ISlidePresentationService.GetByIdAsync(item.Id)` (interface
   `...\WorshipPlayer.Core\Services\Interfaces\ISlidePresentationService.cs:17`; impl
   `...\WorshipPlayer.Core\Services\SlidePresentationService.cs:96`). The field
   `_slidePresentationService` already exists on OperatorWindow (`OperatorWindow.xaml.cs:106`,
   assigned at `:563`). This mirrors the Stage-2 `OnEditPresentationRequested` handler
   (`OperatorWindow.xaml.cs:3284-3298`), which already does exactly this `GetByIdAsync(item.Id)` lookup.
5. New handler builds a `ServiceItem` via the new Core helper (Item 1), then follows the exact
   Songs "+ Add" precedent `OnAddSongToServiceRequested` (`OperatorWindow.xaml.cs:2859-2872`):
   `_serviceOrderManager.AddItem(serviceItem)` → `await SaveServiceOrderAsync()` →
   `RefreshServiceOrderList()`. Also call `RefreshSlideSections()` so the center grid repaints
   (the song path doesn't need it because songs aren't shown until navigated; a presentation
   item must show its section immediately).
6. Center render path: `RefreshSlideSections` (`OperatorWindow.xaml.cs:1991`) calls
   `GetSlidesForItem(item)` (`OperatorWindow.xaml.cs:2229`) per item and builds thumbnails with
   `BuildSlideThumbnail` (`OperatorWindow.xaml.cs:2262`). For any non-Song type `GetSlidesForItem`
   already returns `item.Slides ?? new List<Slide>()` (`OperatorWindow.xaml.cs:2254`). So if the
   added item's `Slides` are populated at add time, the center section renders with **no change to
   `GetSlidesForItem`** — see Item 3 for the explicit confirmation/guard.
7. No-service guard: mirror `OnAddSongToServiceRequested` — if
   `_serviceOrderManager?.CurrentServiceOrder == null`, call
   `ShowToolError("Add to Service", "Open or create a service first.")`
   (`ShowToolError` at `OperatorWindow.xaml.cs:3264`) and return. No crash.

---

## Design decisions (made here, grounded in the code read)

- **Snapshot, not link.** At add time, convert the presentation's `PresentationSlide`s into the
  item's runtime `Slide` list and store them on `ServiceItem.Slides`. Rationale: `GetSlidesForItem`
  already returns `item.Slides` for non-Song items (`OperatorWindow.xaml.cs:2254`), and
  `ServiceItem.Slides` already round-trips through the existing `ServiceOrder` JSON save path
  (`ServiceOrder.cs:138`, `[JsonPropertyName("slides")]`). Snapshotting needs zero changes to the
  render path, the store, or the model — the simplest Stage-3-correct option. Documented tradeoff:
  later edits to the source presentation do NOT retro-update an already-added item (acceptable and
  called out in the task). We still store the source id in `ServiceItem.Subtitle`'s sibling — no:
  we keep it simple and store nothing extra; a future stage can add a link field if needed.
- **Additive `ServiceItemType.Slides` enum value.** Add one value to the `ServiceItemType` enum
  (`ServiceOrder.cs:173-187`). Justified: the enum is serialized as a string
  (`[JsonConverter(typeof(JsonStringEnumConverter))]` at `ServiceOrder.cs:170`), so appending a
  value is backward-compatible — existing service JSON (Song/Scripture/…) still deserializes, and
  no numeric ordinal shifts matter. A dedicated type makes the sidebar/center treat the item as a
  self-contained slide set (falls through `GetSlidesForItem`'s non-Song branch to `item.Slides`,
  which is exactly what we want) and reads clearly in the service file. Reusing `Custom`/`Blank`
  would work mechanically but muddies intent and risks other code paths that special-case those
  types; a new additive value is low-risk and self-documenting.
- **Background mapping.** `PresentationSlide.BackgroundImage` → `Slide.Content.BackgroundImage`
  (string; `""` when null). `PresentationSlide.BackgroundColor` (hex `#RRGGBB`) →
  `Slide.ThemeOverride = new SlideTheme { BackgroundType = BackgroundType.SolidColor,
  BackgroundColor = <hex> }`, because `SlideContent` has no color field but
  `ResolveSlideBackground` (`OperatorWindow.xaml.cs:2505`) honors a slide-image at
  `Content.BackgroundImage` (step 1) and a `ThemeOverride.BackgroundColor` via
  `TryParseColorBrush` (step 2). When both presentation background fields are null, leave both
  unset so the thumbnail falls through to the theme/default gradient — identical to how a plain
  song/scripture slide renders.
- **Title & slide text.** `ServiceItem.Title = presentation.Name` (fallback `"Slides Item"` when
  blank, mirroring `ServiceItemMapper.BuildTitle`'s `"{type} Item"` fallback). Each
  `PresentationSlide.Text` → `Slide.Content.Text`; `Slide.Order` carried from
  `PresentationSlide.Order`; `Slide.Type = SlideType.Custom` (no lyrics/scripture semantics).
  Slides are emitted **in `Order` order** (OrderBy `Order`, then stable by original index).

- **Decomposition decision:** this task is a single coherent unit (one Core helper + its tests,
  one OperatorWindow handler swap, one additive enum value). It does NOT decompose into separable
  features, so the workflow tail is left intact and the existing implement/review loop runs this
  plan. No FEAT artifacts authored; no `update_workflow` restructuring.

---

## Plan

- [ ] 1. Add the additive `Slides` value to the `ServiceItemType` enum.
      Append `Slides` as a new member at the end of the enum (after `Custom`) with a short comment,
      e.g. `Slides // Authored Slides-library presentation (snapshotted slides)`. Do NOT reorder
      existing members. Enum is string-serialized so this is backward-compatible.
      Files: `...\WorshipPlayer.Core\Models\ServiceOrder.cs` (enum at lines 170-187)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -v q -nologo` — Build succeeded, 0 errors,
      4 pre-existing warnings.

- [ ] 2. Create the pure conversion helper `SlidePresentationMapper` in Core.
      Mirror `ServiceItemMapper`'s style (static class, UI-free, XML doc explaining why it lives in
      Core for headless testability). Public API:
      `public static ServiceItem ToServiceItem(SlidePresentation presentation)` — throws
      `ArgumentNullException` on null; sets `Type = ServiceItemType.Slides`,
      `Title = BuildTitle(presentation.Name)` (new private helper: trimmed name, else `"Slides Item"`),
      and fills `Slides` by converting each `PresentationSlide` (ordered by `Order` then original
      index) to a runtime `Slide`: `Content.Text = ps.Text ?? ""`, `Order = ps.Order`,
      `Type = SlideType.Custom`, `SectionName = ""`; if `ps.BackgroundImage` non-empty set
      `Content.BackgroundImage`; if `ps.BackgroundColor` non-empty set
      `ThemeOverride = new SlideTheme { BackgroundType = BackgroundType.SolidColor, BackgroundColor = ps.BackgroundColor }`.
      A null/empty `presentation.Slides` yields an item with an empty (non-null) `Slides` list.
      Keep the method pure (no DB/UI calls); the caller resolves the presentation first.
      Files: create `...\WorshipPlayer.Core\Services\SlidePresentationMapper.cs`
      (namespace `WorshipPlayer.Core.Services`)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -v q -nologo` — Build succeeded, 0 errors.

- [ ] 3. Add unit tests for `SlidePresentationMapper` mirroring `ServiceItemMapperTests` style.
      Cover: (a) slides map in `Order` order with text preserved (give slides out-of-order `Order`
      values and assert the output list is sorted and text matches); (b) `Type` is
      `ServiceItemType.Slides` and `Title` equals the presentation `Name`; (c) blank/whitespace
      `Name` → `"Slides Item"`; (d) `BackgroundImage` maps to `Content.BackgroundImage`; (e)
      `BackgroundColor` maps to `ThemeOverride.BackgroundColor` with `BackgroundType.SolidColor`;
      (f) null/empty `Slides` yields a non-null empty list; (g) null presentation throws
      `ArgumentNullException`; (h) round-trip via `ServiceOrderManager.AddItem` leaves the item with
      the expected `Title`/`Type`/slide count (mirrors
      `ServiceItemMapperTests.AddSongToService_ProducesExpectedSidebarTitleAndSongFolder`, no
      `SaveAsync` so no files touched).
      Files: create `...\WorshipPlayer.Tests\SlidePresentationMapperTests.cs`
      Verify: `dotnet test WorshipBacktrackPlayer.sln --filter "FullyQualifiedName~SlidePresentationMapperTests" -v q --nologo`
      — all new tests pass.

- [ ] 4. Replace the Stage-3 stub handler `OnAddPresentationToServiceRequested` with the real action.
      Make it `private async void OnAddPresentationToServiceRequested(object? sender, SlidePresentationListItem item)`.
      Body, wrapped in try/catch → `ShowToolError("Add to Service", ex.Message)`:
      (1) if `_slidePresentationService == null` return; (2) no-service guard: if
      `_serviceOrderManager?.CurrentServiceOrder == null` →
      `ShowToolError("Add to Service", "Open or create a service first.")` and return (mirrors
      `OnAddSongToServiceRequested` at `OperatorWindow.xaml.cs:2861-2865`); (3)
      `var presentation = await _slidePresentationService.GetByIdAsync(item.Id);` if null →
      `ShowToolError("Add to Service", "This presentation could not be found. It may have been deleted.")`
      and return (mirrors the Stage-2 `OnEditPresentationRequested` null handling at
      `OperatorWindow.xaml.cs:3289-3293`); (4)
      `var serviceItem = SlidePresentationMapper.ToServiceItem(presentation);`
      (5) `_serviceOrderManager.AddItem(serviceItem); await SaveServiceOrderAsync();
      RefreshServiceOrderList(); RefreshSlideSections();`. Remove the old expression-bodied stub at
      `OperatorWindow.xaml.cs:3300-3301`. Add `using WorshipPlayer.Core.Services;` only if not
      already present (it is used elsewhere in the file for `ServiceItemMapper`). Keep OWC branding
      and StaticResource-free code.
      Files: `...\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -v q -nologo` — Build succeeded, 0 errors,
      4 pre-existing warnings only.

- [ ] 5. Confirm center rendering needs no `GetSlidesForItem` change (and add a guard only if
      verification shows one is needed). Re-read `GetSlidesForItem`
      (`OperatorWindow.xaml.cs:2229-2255`): the final `return item.Slides ?? new List<Slide>();`
      already covers `ServiceItemType.Slides`, so a snapshotted item renders a section + thumbnails
      via `BuildSlideThumbnail`. No code change expected. If (and only if) manual reasoning finds a
      type-specific branch that would intercept `Slides` items, add an explicit
      `if (item.Type == ServiceItemType.Slides) return item.Slides ?? new List<Slide>();` before the
      Song branch — otherwise leave the method unchanged and note "no change needed" in the commit.
      Files: `...\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs` (read-only unless a branch
      is found)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -v q -nologo` — Build succeeded, 0 errors.

- [ ] 6. Full regression: build clean and run the whole suite.
      Confirm the stub is gone, the handler compiles, and no existing feature regressed
      (Songs/Scripture/Media add, all-sections center, overflow menu, Stage 1-2 library/editor).
      Files: none (verification only)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -v q -nologo` → Build succeeded, 0 errors,
      exactly 4 warnings; then `dotnet test WorshipBacktrackPlayer.sln -v q --nologo` → all tests
      pass (the ~648 existing tests plus the new `SlidePresentationMapperTests`), 0 failures.

---

## Notes / assumptions

- `SlidePresentationListItem` carries only `Id`/`Name`/`SlideCount`, so the handler MUST resolve
  the full `SlidePresentation` via `GetByIdAsync(item.Id)` before conversion — confirmed by reading
  `SlidesLibraryPanel.xaml.cs:270-287` and the Stage-2 edit handler.
- Do NOT touch `SlidePresentation` storage, `SlidePresentationService`, or `app.db` schema — the
  feature only reads a presentation and writes a `ServiceItem` through the existing
  `ServiceOrderManager`/`SaveServiceOrderAsync` path.
- `RefreshSlideSections` is added to the handler (beyond the song precedent) so the new section
  appears immediately in the center grid; it is already the method both startup and item-change
  paths use to rebuild sections (`OperatorWindow.xaml.cs:242, 678, 2039`).
