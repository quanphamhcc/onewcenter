# Completion Report — Slides Stage 3: "Add to current service"

**Branch:** `feature/slides-to-service`
**Worktree:** `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slides-stage3`
**Review verdict:** APPROVED (`.agents/tasks/slides-stage3/review.json`)
**Implementation commit:** `89d7205` — `feat: add Slides presentation to current service`
**Docs commit:** `dd175d9` — `docs: record slides-stage3 plan, code review, and approved verdict`

Stage 3 wires the Slides-library "Add to current service" action so an authored
`SlidePresentation` becomes a real `ServiceItem` in the open service and renders its slides in the
center all-sections grid, exactly like Songs/Scripture. The Stage-3 "coming soon" stub is gone.

---

## Item representation decision

**Snapshot, not link.** At add time the presentation's `PresentationSlide`s are converted into the
item's runtime `Slide` list and stored on `ServiceItem.Slides`. The render path
(`GetSlidesForItem`) already returns `item.Slides` for any non-Song item, and `ServiceItem.Slides`
already round-trips through the existing `ServiceOrder` JSON save path, so snapshotting needs zero
changes to the model, the store, or the renderer.

- **Justification:** It is the simplest Stage-3-correct option — no model surgery, no new store
  field, no render change. The slides a service file renders are self-contained inside the item.
- **Documented tradeoff:** later edits to the source presentation do NOT retro-update an item
  already added to the service. This is called out in the mapper XML doc and the plan as a
  deliberate Stage-3 scope decision (and confirmed by the review as informational, not a defect).
  A future stage can add a link field if live sync is wanted.

**Enum: additive `ServiceItemType.Slides`, not reuse.** A new member `Slides` is appended to the
`ServiceItemType` enum after `Custom` (no reordering of existing members).

- **Justification:** `ServiceItemType` carries `[JsonConverter(typeof(JsonStringEnumConverter))]`,
  so it is serialized by name. Appending a member shifts no numeric ordinals, and existing service
  JSON (Song/Scripture/Media/…) still deserializes unchanged — backward-compatible. A dedicated
  type lets the sidebar/center treat the item as a self-contained slide set (it falls through
  `GetSlidesForItem`'s non-Song branch to `item.Slides`, which is exactly the desired behavior) and
  reads clearly in the service file. Reusing `Custom`/`Blank` would work mechanically but muddies
  intent and risks other code paths that special-case those types; an additive value is low-risk
  and self-documenting.

**Background mapping.** `PresentationSlide.BackgroundImage` → `Slide.Content.BackgroundImage` (only
when non-empty). `PresentationSlide.BackgroundColor` (hex) →
`Slide.ThemeOverride = new SlideTheme { BackgroundType = BackgroundType.SolidColor, BackgroundColor = <hex> }`
(only when non-empty). When both are empty, both are left unset so the thumbnail falls through to
the theme/default background — identical to a plain song/scripture slide.

---

## File:line trace (Slides ⋮ "Add to current service" → rendered section)

All paths under the worktree root above.

1. **Slides library ⋮ "Add to current service"** —
   `WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml.cs:191` `AddToServiceMenu_Click`
   raises `AddPresentationToServiceRequested` (declared at `SlidesLibraryPanel.xaml.cs:52`) with a
   `SlidePresentationListItem` that carries only `Id` / `Name` / `SlideCount`.
2. **Host subscription** —
   `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs:2762`
   `_slidesLibraryPanel.AddPresentationToServiceRequested += OnAddPresentationToServiceRequested`.
3. **New handler** — `OperatorWindow.xaml.cs:3305`
   `private async void OnAddPresentationToServiceRequested(object? sender, SlidePresentationListItem item)`
   (replaces the old expression-bodied `ShowComingSoon` stub). Whole body wrapped in
   `try/catch → ShowToolError("Add to Service", ex.Message)` so it never crashes.
   - **No-service guard** — `OperatorWindow.xaml.cs:3311-3315`: if
     `_serviceOrderManager?.CurrentServiceOrder == null` →
     `ShowToolError("Add to Service", "Open or create a service first.")` and return.
   - (Null-service-service guard at `:3309` returns silently if the service is unavailable.)
4. **GetByIdAsync** — `OperatorWindow.xaml.cs:3317`
   `var presentation = await _slidePresentationService.GetByIdAsync(item.Id);` resolves the full
   `SlidePresentation` (interface `WorshipPlayer.Core\Services\Interfaces\ISlidePresentationService.cs:17`,
   impl `WorshipPlayer.Core\Services\SlidePresentationService.cs:96`). Not-found guard at
   `OperatorWindow.xaml.cs:3318-3324`: null →
   `ShowToolError("Add to Service", "This presentation could not be found. It may have been deleted.")`
   and return.
5. **Conversion helper** — `OperatorWindow.xaml.cs:3326`
   `var serviceItem = SlidePresentationMapper.ToServiceItem(presentation);`
   (`WorshipPlayer.Core\Services\SlidePresentationMapper.cs:30` `ToServiceItem`).
6. **AddItem** — `OperatorWindow.xaml.cs:3328` `_serviceOrderManager.AddItem(serviceItem);`.
7. **SaveServiceOrderAsync** — `OperatorWindow.xaml.cs:3329` `await SaveServiceOrderAsync();`.
8. **Refresh** — `OperatorWindow.xaml.cs:3330-3331`
   `RefreshServiceOrderList();` (sidebar) then `RefreshSlideSections();` (center grid, at
   `OperatorWindow.xaml.cs:1991`) so the new section appears immediately.
9. **GetSlidesForItem returns the presentation item's slides** —
   `OperatorWindow.xaml.cs:2229` `GetSlidesForItem`. It special-cases only
   `ServiceItemType.Song` (`:2233`); every other type — including `Slides` — falls through to
   `return item.Slides ?? new List<Slide>();`. A snapshotted Slides item populates `Slides`, so it
   renders through this non-Song path with **no change** to the method. Confirmed by reading it.

**app.db not reset / storage untouched.** The handler only reads a presentation via
`GetByIdAsync` and writes a `ServiceItem` through the existing
`ServiceOrderManager`/`SaveServiceOrderAsync` path. No change to `SlidePresentation` storage,
`SlidePresentationService`, or `app.db` schema.

**OperatorWindow StaticResource-free.** No `StaticResource` was introduced; the only occurrence in
`OperatorWindow.xaml.cs` is the comment at `:2112` documenting that the window uses inline brushes
only. The handler and section builders use inline brushes, consistent with the rest of the file.

---

## Conversion helper signature + tests

**Signature** (`WorshipPlayer.Core\Services\SlidePresentationMapper.cs`):

```csharp
public static class SlidePresentationMapper
{
    public static ServiceItem ToServiceItem(SlidePresentation presentation);
}
```

- Pure, UI-free, in Core so it is testable from the net8.0 Tests project (mirrors
  `ServiceItemMapper`).
- Throws `ArgumentNullException` on null `presentation`.
- Sets `Type = ServiceItemType.Slides`, `Title = BuildTitle(presentation.Name)` (trimmed name,
  else `"Slides Item"`), and projects each `PresentationSlide` into a runtime `Slide` ordered by
  `Order` then original index, with text preserved and optional image/color backgrounds carried.
- Null/empty slide list yields a non-null empty `Slides` list.

**Tests** — `WorshipPlayer.Tests\SlidePresentationMapperTests.cs` (10 tests, mirroring
`ServiceItemMapperTests` style):

1. Slides map in `Order` order with text preserved (out-of-order 2/0/1 → 0/1/2).
2. `Type == ServiceItemType.Slides` and `Title` equals the presentation `Name`.
3. Blank/whitespace `Name` → `"Slides Item"` fallback.
4. `BackgroundImage` maps to `Content.BackgroundImage`.
5. `BackgroundColor` maps to `ThemeOverride.BackgroundColor` with `BackgroundType.SolidColor`.
6. No background set leaves both `Content.BackgroundImage` and `ThemeOverride` unset.
7. Null/empty `Slides` yields a non-null empty list.
8. Null `presentation` throws `ArgumentNullException`.
9. Each produced slide's `Type == SlideType.Custom`.
10. Round-trip via `ServiceOrderManager.AddItem` leaves the item with the expected
    `Title`/`Type`/slide count (no `SaveAsync`, so no files touched).

---

## Build / test results

Run against
`c:\Users\Quang\...\.worktrees\slides-stage3\WorshipBacktrackPlayer.sln`.

**`dotnet build`** — Build succeeded, **0 errors**, exactly **4 warnings** (all pre-existing):

- `MediaLibraryPanel.xaml.cs(16,20)` CS0414 — `_currentView` assigned but never used
  (re-emitted once by the Presenter temp build).
- `ChordChartWindow.xaml.cs(235,51)` CS8604 — possible null reference argument for `lyricText`.
- `WaveformEditorWindow.xaml.cs(1775,18)` CS0414 — `_inReviewMode` assigned but never used.

**`dotnet test`** — Test summary: **total 658, failed 0, succeeded 658, skipped 0** (~648 prior +
10 new `SlidePresentationMapperTests`). The console line
`Error loading service order: 't' is an invalid start of a property name` is an expected log from a
negative-path deserialization test, not a failure.

---

## Click-test checklist (for the user)

1. **Happy path:** open a service → Slides tab → on a presentation card that has slides, click the
   ⋮ overflow → "Add to current service". Expect: the item appears in the left sidebar named after
   the presentation, and the center pane shows its slides as a new section.
2. **No-service guard:** with no service open, trigger the same action. Expect: the open/create-a-
   service notice ("Open or create a service first.") with **no crash**.
3. **Regression:** Songs "+ Add" and Scripture add still work; the all-sections center grid, the
   service overflow (⋮) menu, and the Stage 1-2 Slides library/editor are unchanged.

---

## Scope / constraints honored

- Do NOT merge into main; do NOT delete the worktree — both left intact.
- Diff limited to: `ServiceOrder.cs` (one additive enum member), new `SlidePresentationMapper.cs`,
  `OperatorWindow.xaml.cs` (stub → handler), new `SlidePresentationMapperTests.cs`, and the
  verification note. No storage/service/db changes; OWC branding untouched; OperatorWindow stays
  StaticResource-free.
