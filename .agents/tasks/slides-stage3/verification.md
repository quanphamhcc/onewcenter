# Verification — Slides Stage 3: "Add to current service"

## What changed
- `WorshipPlayer.Core/Models/ServiceOrder.cs` — appended additive `Slides` value to the
  `ServiceItemType` enum (string-serialized via `JsonStringEnumConverter`, so backward-compatible;
  no ordinals shifted).
- `WorshipPlayer.Core/Services/SlidePresentationMapper.cs` — new pure, UI-free helper
  `ToServiceItem(SlidePresentation)` that snapshots a presentation into a `ServiceItem`
  (Type = Slides, Title = trimmed Name or "Slides Item", slides projected in `Order` order with
  text preserved, background image → `Content.BackgroundImage`, background colour → solid-colour
  `ThemeOverride`). Mirrors `ServiceItemMapper`'s style.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — replaced the Stage-3 "coming soon"
  stub `OnAddPresentationToServiceRequested` with the real async handler: no-service guard
  (`ShowToolError("Add to Service", "Open or create a service first.")`), resolve full
  presentation via `_slidePresentationService.GetByIdAsync(item.Id)`, not-found guard, convert via
  `SlidePresentationMapper.ToServiceItem`, then `AddItem` → `SaveServiceOrderAsync` →
  `RefreshServiceOrderList` → `RefreshSlideSections`. Whole body try/catch → `ShowToolError`.
  Mirrors `OnAddSongToServiceRequested`.
- `WorshipPlayer.Tests/SlidePresentationMapperTests.cs` — 10 new unit tests for the Core helper.

## GetSlidesForItem — no change needed
`OperatorWindow.GetSlidesForItem` ends with `return item.Slides ?? new List<Slide>();`, which
already covers a `ServiceItemType.Slides` item (only `Song` has a special branch above it). The
snapshotted slides render a center section + thumbnails via `BuildSlideThumbnail` with no edit to
the method. Confirmed by reading `OperatorWindow.xaml.cs:2229-2255`.

## Design note — snapshot (not link)
At add time the presentation's `PresentationSlide`s are converted into the item's runtime `Slide`
list and stored on `ServiceItem.Slides`. `ServiceItem.Slides` already round-trips through the
existing `ServiceOrder` JSON save path, so this needs zero changes to the model, store, or render
path. Tradeoff (documented in the mapper XML doc and the plan): later edits to the source
presentation do NOT retro-update an already-added item.

## Commands run and results

### Build
`dotnet build ...\.worktrees\slides-stage3\WorshipBacktrackPlayer.sln -v q -nologo`
Result: Build succeeded, exit code 0, 0 errors, 4 pre-existing warnings only
(MediaLibraryPanel CS0414, ChordChartWindow CS8604, WaveformEditorWindow CS0414).

### New conversion tests
`dotnet test ...\WorshipBacktrackPlayer.sln --filter "FullyQualifiedName~SlidePresentationMapperTests"`
Result: Passed! — Failed: 0, Passed: 10, Skipped: 0, Total: 10.

### Full suite
`dotnet test ...\WorshipBacktrackPlayer.sln`
Result: Passed! — Failed: 0, Passed: 658, Skipped: 0, Total: 658
(648 prior + 10 new conversion tests).

## Constraints honored
- Scope limited to OperatorWindow.xaml.cs, the Core helper + tests, and the additive
  `ServiceItemType.Slides` value. Reused `ServiceOrderManager.AddItem` / `SaveServiceOrderAsync`
  and the existing refresh methods.
- OperatorWindow stays StaticResource-free; OWC branding untouched.
- No change to `SlidePresentation` storage, `SlidePresentationService`, or `app.db`.
- Existing features preserved (Songs/Scripture/Media add, all-sections center, overflow menu,
  Stage 1-2 library/editor); full suite stays green.
