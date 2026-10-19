# Add authored Slides presentation to the current service

The Stage-3 "Add to current service" action on a Slides-library card now actually mutates the open service: it resolves the full `SlidePresentation` by id, snapshots its slides into a new `ServiceItem` (new additive `ServiceItemType.Slides`), appends it through the existing `ServiceOrderManager`, persists, and repaints the sidebar plus the center slide grid. The conversion is a pure Core helper (`SlidePresentationMapper`) with 10 unit tests, mirroring the existing `ServiceItemMapper`. The "coming soon" stub is gone. The change is small (3 production files + 1 test file, 372 insertions) and follows the Songs "+ Add" precedent closely.

Watch for: nothing blocking. One behavioral caveat worth knowing (confirmed): the add is a one-time snapshot, so later edits to the source presentation do not retro-update an already-added service item — this is documented in the mapper and plan as an intentional Stage-3 tradeoff, not a defect.

**Verdict**: APPROVED

## High-level view

The conversion lives in `SlidePresentationMapper.ToServiceItem`, a static UI-free Core helper that sets `Type = Slides`, a title from the presentation name (falling back to "Slides Item"), and projects each `PresentationSlide` into a runtime `Slide` ordered by `Order` then original index, text preserved, backgrounds carried. Keeping it in Core is what makes it testable from the net8.0 Tests project, and the 10 tests cover ordering, title fallback, both background mappings, the empty-list case, and the null-argument guard.

Representation is an additive `ServiceItemType.Slides` enum value appended after `Custom`. Because the enum is string-serialized via `JsonStringEnumConverter`, appending a member shifts no ordinals and existing service JSON still deserializes — the backward-compatibility claim holds. The slides are stored on `ServiceItem.Slides`, which already round-trips through the ServiceOrder JSON save path, so no model, store, or render changes were needed.

The handler `OnAddPresentationToServiceRequested` replaces the stub and mirrors `OnAddSongToServiceRequested`: no-service guard → `ShowToolError("Add to Service", "Open or create a service first.")`, resolve via `GetByIdAsync(item.Id)` (the list item carries only id/name/count), not-found guard, convert, then `AddItem` → `SaveServiceOrderAsync` → `RefreshServiceOrderList` → `RefreshSlideSections`. The whole body is wrapped so a failure surfaces as a tool error rather than crashing.

Center rendering needed no change: `GetSlidesForItem` special-cases only `Song` and otherwise returns `item.Slides`, which is exactly what a snapshotted Slides item carries, so `RefreshSlideSections`/`BuildSlideThumbnail` render the section with the populated slides.

<details>
<summary>Issues (1)</summary>

1. **Snapshot is not a live link** (informational, confirmed) — adding a presentation copies its slides at that moment; later edits to the source presentation won't update an item already in the service. Intentional and documented for Stage 3; no action needed unless a future stage wants live linking.

</details>

<details>
<summary>Details</summary>

### SlidePresentation -> ServiceItem conversion

`ToServiceItem` is pure and self-contained: null presentation throws `ArgumentNullException`, `Type` is set to the new `Slides` value, and `BuildTitle` trims the name or returns "Slides Item" for blank/whitespace — matching the `"{type} Item"` fallback convention in `ServiceItemMapper`. Slide projection sorts by `Order` then original index, so the test that feeds `Order` values 2/0/1 correctly yields First/Middle/Second with `Order` 0/1/2. Text maps to `Content.Text` (null coalesced to `""`), `BackgroundImage` only sets `Content.BackgroundImage` when non-empty, and `BackgroundColor` only produces a solid-color `ThemeOverride` when non-empty — leaving both unset otherwise so the renderer falls through to the theme/default background. All field names and types referenced (`PresentationSlide.Order/Text/BackgroundImage/BackgroundColor`, `Slide.Content/ThemeOverride/Order/Type`, `SlideContent.BackgroundImage`, `SlideTheme.BackgroundColor`, `BackgroundType.SolidColor`, `SlideType.Custom`) exist in the Core models, consistent with the recorded 0-error build.

### Representation: additive enum, snapshot into item.Slides

The enum change appends `Slides` after `Custom` with no reordering. The type carries `[JsonConverter(typeof(JsonStringEnumConverter))]`, so persistence is by name and existing Song/Scripture/Media orders deserialize unchanged — the backward-compatibility requirement is met. Storing the converted slides on `ServiceItem.Slides` reuses the field that already serializes through the ServiceOrder save path, so no store or model surgery was required.

The snapshot-vs-link choice is the one behavioral caveat. At add time the presentation's slides are frozen into the item; subsequent edits to the source presentation do not propagate. This is called out in the mapper XML doc and the plan as a deliberate Stage-3 scope decision, and it is an acceptable tradeoff for this stage rather than a bug.

### Handler, guards, and persistence

The handler mirrors the Songs path. Both guards fail safely: a missing service shows the open/create notice and returns; a deleted/unresolvable presentation shows a "could not be found" notice and returns. `RefreshSlideSections` is called in addition to the song precedent so the new section appears immediately in the center grid — needed because a slides item must render on add rather than on navigation. The body-wide try/catch routes any exception to `ShowToolError`, so the action never crashes the operator window. All four called members (`ShowToolError`, `SaveServiceOrderAsync`, `RefreshServiceOrderList`, `RefreshSlideSections`) exist in the file as used.

### Center rendering unchanged

`GetSlidesForItem` branches only on `ServiceItemType.Song`; every other type returns `item.Slides ?? new List<Slide>()`. A snapshotted Slides item populates `Slides`, so it renders through the existing non-Song path with no edit — confirmed by reading the method, which validates the commit's "no change needed" note.

### Test coverage

The 10 Core tests cover order-with-text-preservation, `Type`/`Title` from name, blank-name fallback, background-image mapping, background-color-to-solid-theme mapping, no-background leaves both unset, null/empty slides yields a non-null empty list, null presentation throws, `SlideType.Custom`, and a round-trip through `ServiceOrderManager.AddItem` asserting title/type/count without touching disk. This matches the plan's enumerated cases and the `ServiceItemMapperTests` style.

Not tested (acceptable per task scope — no WPF/layout unit tests required): the OperatorWindow handler itself (guards, GetByIdAsync resolution, refresh calls) is not unit-tested, since it is WPF-bound. Its logic is thin and delegates to the tested mapper and existing, already-tested service/refresh methods, so the risk is low; manual UI verification would be the way to exercise it.

### Verification evidence

The verification note and commit message record a clean build (0 errors, 4 pre-existing warnings: MediaLibraryPanel CS0414, ChordChartWindow CS8604, WaveformEditorWindow CS0414) and a green suite (658 passed = 648 prior + 10 new, 0 failed), plus the filtered run of the 10 new mapper tests. Per instructions the suites were not re-run; a spot read of the diff, the mapper, the tests, the handler, and the referenced Core model fields found no inconsistency with the recorded results, so no narrow spot-check was warranted.

### Scope and constraints

The diff touches only `ServiceOrder.cs` (one additive enum member), the new `SlidePresentationMapper.cs`, `OperatorWindow.xaml.cs` (stub → handler), the new test file, and the verification note. No changes to `SlidePresentation` storage, `SlidePresentationService`, or `app.db`; no StaticResource introduced into OperatorWindow; OWC branding untouched; other add paths (Songs/Scripture/Media) and Stage 1-2 library/editor are not modified. Scope is correctly limited to Stage 3.

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Core/Models/ServiceOrder.cs` — append additive `Slides` member to `ServiceItemType` (string-serialized, backward-compatible).
- `WorshipPlayer.Core/Services/SlidePresentationMapper.cs` — new pure `ToServiceItem` helper (snapshot projection, title fallback, background mapping).
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — replace "coming soon" stub with real async handler (guards → resolve → convert → add → save → refresh).
- `WorshipPlayer.Tests/SlidePresentationMapperTests.cs` — 10 new unit tests for the mapper.
- `.agents/tasks/slides-stage3/verification.md` — coder's build/test evidence.

Full diff: `git -C <worktree> diff main...HEAD`

</details>
