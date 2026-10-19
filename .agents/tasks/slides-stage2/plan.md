# Slides — Stage 2 (Slide Editor) Implementation Plan

Branch: `feature/slides-editor`
Worktree (ALL absolute paths below live here):
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slides-stage2`

Goal: make the library's ⋮ → **Edit** action open a real modal **SlideEditorWindow** that edits a
`SlidePresentation` (add / delete / reorder / edit-text slides + rename), then **Save and close**
persists via `SlidePresentationService.UpdateAsync` and refreshes the library grid; **Close** discards.
Match the WorshipTools Presenter slide-editor layout, OWC-branded. Code only; do not reset `app.db`.

---

## What already exists (file:line trace, read and verified)

Model — `WorshipPlayer.Core\Models\SlidePresentation.cs`:
- `SlidePresentation`: `Id` (line 25), `Name` (28), `CreatedAt` (31), `UpdatedAt` (34), `Slides` List<PresentationSlide> (37), nullable `ThumbnailPath` (44).
- `PresentationSlide`: `Id` (56), `Order` (59), `Text` (62), nullable `BackgroundImage` (66), nullable `BackgroundColor` (69). (All JSON-named camelCase; leave nullable bg fields intact.)

Service — `WorshipPlayer.Core\Services\SlidePresentationService.cs` + `WorshipPlayer.Core\Services\Interfaces\ISlidePresentationService.cs`:
- `GetByIdAsync(id)` returns the whole deserialized presentation (svc line ~96–122) or null.
- `UpdateAsync(presentation)` (svc line ~150–157) re-stamps `UpdatedAt = UtcNow` then `UpsertAsync`.
- `UpsertAsync`/`BindPresentation` (svc ~195–235) promote `SlideCount = Slides.Count` and serialize the
  whole object to `DataJson` — so UpdateAsync persists slides + order; SlideCount/UpdatedAt are owned by
  the service, never the UI. `GetAllAsync` orders by `UpdatedAt DESC`.
- Schema is additive `CREATE TABLE IF NOT EXISTS` (svc ~46–57); app.db is never reset.

Library UI — `WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml(.cs)`:
- Raises `EditPresentationRequested` with a `SlidePresentationListItem` (xaml.cs line ~42, `EditMenu_Click` ~190).
- `SlidePresentationListItem` has `Id`, `Name`, `SlideCount`, `UpdatedAt`, `RelativeModified`, `SubLine` (xaml.cs ~250–270).
- `Refresh()` → `LoadAsync()` re-queries `GetAllAsync` and rebuilds the cards (xaml.cs ~72–112). Public `Refresh()` exists.
- Panel MERGES `Themes/OwcDesignSystem.xaml` → StaticResource allowed here and in any new Window (SlideNameDialog proves this).

Host — `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`:
- `_slidePresentationService` field (line 106); injected by `SetServices(...)` (line ~538/563).
- `_slidesLibraryPanel` created lazily in `SwitchToTab("Slides")` (line ~2753); subscribes
  `EditPresentationRequested += OnEditPresentationRequested` (~2761).
- `OnEditPresentationRequested` currently calls `ShowComingSoon("Edit Slides", ...)` (line ~3282). This is the ONLY wiring that changes for Stage 2. `ShowComingSoon` is a `MessageBox.Show` one-liner (~3279).

Modal window pattern (to mirror):
- `WorshipPlayer.Presenter\Views\SongLibrary\SongMetadataEditorWindow.xaml(.cs)` — a `Window` that merges
  `OwcDesignSystem.xaml`, sets `Background="#0F1117"`, `WindowStartupLocation="CenterOwner"`, hosts content,
  bridges Save/Cancel → `DialogResult` + `Close()`.
- `WorshipPlayer.Presenter\Views\Slides\SlideNameDialog.xaml(.cs)` — nearest sibling: own Window, merges theme,
  `Owner = Window.GetWindow(this)`, `ShowDialog()`, `DialogResult`. Opened from `SlidesLibraryPanel.AddSlides_Click`.

Theme keys available (all StaticResource, `Themes\OwcDesignSystem.xaml`): `OwcTextBox`, `OwcButtonBase`,
`OwcBlueButton`, `OwcPanelBorder`, `OwcSurfaceBorder`, `OwcBackgroundBrush`, `OwcPanelBrush`, `OwcSurfaceBrush`,
`OwcBorderBrush`, `OwcTextPrimaryBrush`, `OwcTextSecondaryBrush`, `OwcTextMutedBrush`, `OwcBlueBrush`.

Tests — `WorshipPlayer.Tests\WorshipPlayer.Tests.csproj`: **xUnit** (2.6.6), references **Core only**
(Presenter is net8.0-windows, not referenced). `SlidePresentationServiceTests.cs` shows the temp-`app.db`
ctor-injection + `SqliteConnection.ClearAllPools()` teardown pattern to reuse. WPF editor interaction is NOT unit-tested headless.

Baseline verified green: `dotnet build WorshipPlayer.Tests` and `dotnet build WorshipPlayer.Presenter` both succeed (one pre-existing unrelated CS0414 warning in MediaLibraryPanel).

---

## Design decisions (made here; Stage-2 scope)

1. **Pure slide-op logic in a Core static helper**, `WorshipPlayer.Core\Services\SlideOperations.cs`, NOT in the
   service. Rationale: the ops are pure list transforms over `List<PresentationSlide>` with no DB/UI dependency, so
   they unit-test directly against Core (the Tests project references Core only) and the WPF editor just calls them.
   Keeping them out of the service keeps `SlidePresentationService` a thin CRUD store (its stated role).
2. **Order is kept dense and 0-based** (`Order == index`) by every op (add/delete/move all renumber). Rationale:
   the editor renders slides sorted by `Order`; dense 0-based order makes the list, the thumbnails ("Slide N" = Order+1),
   and persistence trivially consistent, and makes move/delete assertions exact.
3. **The editor edits a deep COPY** of the presentation (JSON round-trip via `GetByIdAsync`, which already returns a
   fresh object) and only `UpdateAsync`s it on Save — so **Close discards** with no extra bookkeeping. Rationale:
   simplest correct discard semantics; mirrors how DuplicateAsync deep-clones.
4. **Window result contract** mirrors `SongMetadataEditorWindow`: `DialogResult=true` on Save, `false`/null on
   Close/X; host refreshes the library only when `ShowDialog()==true`. Rationale: matches the established pattern.
5. **Reorder = move up / move down** (the required minimum). Rationale: scope says "at least move up/down"; buttons are
   simplest and fully testable via the helper. Drag-reorder is out of scope.

---

## Ordered implementation plan

- [ ] 1. Add the pure slide-op helper `SlideOperations` (static) in Core.
      Methods over `List<PresentationSlide>` that mutate in place and keep `Order` dense/0-based:
      `AddSlide(list)` → appends a new `PresentationSlide { Order = list.Count, Text = "" }` and returns it;
      `DeleteSlide(list, index)` → removes at index then renumbers `Order = position`;
      `MoveUp(list, index)` / `MoveDown(list, index)` → swap with neighbor then renumber (no-op at the ends);
      `Normalize(list)` → sort by current `Order` then renumber 0..n-1 (used when loading).
      Guard out-of-range indices (return without throwing). Do NOT touch `BackgroundImage`/`BackgroundColor`.
      Files: `WorshipPlayer.Core\Services\SlideOperations.cs`
      Verify: `dotnet build WorshipPlayer.Core\WorshipPlayer.Core.csproj` succeeds.

- [ ] 2. Add Core unit tests for `SlideOperations` and the UpdateAsync round-trip.
      New xUnit class `SlideOperationsTests` covering: add appends with next Order; delete removes + renumbers
      (and middle-delete leaves 0..n-1 dense); MoveUp/MoveDown swap Order and are no-ops at ends; Normalize sorts
      by Order and redensifies. Extend the service round-trip in `SlidePresentationServiceTests`: build a presentation
      via the helper ops, `UpdateAsync`, re-`GetByIdAsync`, assert slide count, text, and Order sequence survive, and
      that `GetAllAsync` reflects the new `SlideCount`. Reuse the temp-`app.db` ctor + `ClearAllPools()` teardown.
      Files: `WorshipPlayer.Tests\SlideOperationsTests.cs`, `WorshipPlayer.Tests\SlidePresentationServiceTests.cs`
      Verify: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --filter "SlideOperations|SlidePresentation"` — all pass.

- [ ] 3. Add the modal `SlideEditorWindow` (XAML + code-behind) under Views\Slides.
      A `Window` mirroring `SongMetadataEditorWindow`: merges `OwcDesignSystem.xaml`, `Background="#0F1117"`,
      `WindowStartupLocation="CenterOwner"`, `MinWidth/MinHeight`, `ResizeMode="CanResize"`, OWC-branded title (no "Presenter" logo).
      Layout:
        • Header (DockPanel top): editable Name `TextBox` (left, `OwcTextBox`) + right-aligned "Close" (`OwcButtonBase`)
          and "Save and close" (`OwcBlueButton`) buttons.
        • Left "SLIDES" column: `ListBox` bound to an `ObservableCollection` of slide rows showing "Slide N" + a small
          text preview, with a blue "+" add button and "Delete" / "Move up" / "Move down" buttons under/near it.
        • Center: multiline editable `TextBox` (`AcceptsReturn`, `TextWrapping=Wrap`, `VerticalScrollBarVisibility=Auto`)
          bound to the selected slide's Text, with a "Click to add text" placeholder shown when empty.
      Code-behind: ctor takes the loaded `SlidePresentation` (a deep copy); `Normalize` its slides on load; wire
      add/delete/move buttons to `SlideOperations` + refresh the list + keep selection sensible; two-way-sync the
      center TextBox to the selected row's `Text`; push the Name TextBox into the presentation. Expose
      `public SlidePresentation? Result` set on Save; Save → `DialogResult=true`, Close/X → `DialogResult=false`.
      Do NOT call the service here (host owns persistence) — OR optionally accept the service and Save inside; pick ONE:
      **decision: the host passes the service and the window performs `UpdateAsync` on Save** (keeps OperatorWindow thin
      and matches SlidesLibraryPanel, which itself calls the service for functional actions). Guard against empty Name
      (fall back to existing name; service also normalizes blank → "New Presentation").
      Files: `WorshipPlayer.Presenter\Views\Slides\SlideEditorWindow.xaml`, `...\SlideEditorWindow.xaml.cs`
      Verify: `dotnet build WorshipPlayer.Presenter\WorshipPlayer.Presenter.csproj` succeeds (XAML compiles, no new errors).

- [ ] 4. Wire OperatorWindow's Edit action to open the editor and refresh the library.
      Rewrite `OnEditPresentationRequested` (OperatorWindow.xaml.cs ~3282): guard `_slidePresentationService != null`;
      `var p = await _slidePresentationService.GetByIdAsync(item.Id)` (guard null → ShowComingSoon/not-found message);
      `new SlideEditorWindow(p, _slidePresentationService) { Owner = this }`; if `ShowDialog() == true` then
      `_slidesLibraryPanel?.Refresh()` so SlideCount/Modified update. Change the handler to `async void` (matches the
      existing async `_Click` handlers in the panel). Remove the "Stage 2 STUB" comment. Leave the Add-to-service stub
      (`OnAddPresentationToServiceRequested`) unchanged. Keep OperatorWindow StaticResource-free (no XAML change there).
      Files: `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipPlayer.Presenter\WorshipPlayer.Presenter.csproj` succeeds.

- [ ] 5. Full integration verification.
      Build the whole solution and run the test suite; confirm no regressions and the new tests pass.
      Files: (none — verification only)
      Verify: `dotnet build WorshipBacktrackPlayer.sln` succeeds; `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` — all pass.

---

## Save / persist / refresh flow

Edit chosen → `GetByIdAsync(id)` returns a fresh (deep) presentation → editor `Normalize`s slide Order →
user edits (add/delete/move/text/name via `SlideOperations` + bindings) → **Save and close**: editor pushes Name +
slides into the object and calls `UpdateAsync` (service re-stamps UpdatedAt, re-derives SlideCount, serializes whole
object incl. order) → `DialogResult=true` → OperatorWindow calls `_slidesLibraryPanel.Refresh()` → `GetAllAsync`
rebuilds cards with new SlideCount + relative Modified. **Close/X**: `DialogResult=false`, the copy is dropped, nothing
persisted, library unchanged. `app.db` is extended in place (additive upsert), never reset.

## Build / test verification (project-real commands)
- Core helper compiles: `dotnet build WorshipPlayer.Core\WorshipPlayer.Core.csproj`
- Unit tests: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` (xUnit)
- WPF editor compiles: `dotnet build WorshipPlayer.Presenter\WorshipPlayer.Presenter.csproj`
- Whole solution: `dotnet build WorshipBacktrackPlayer.sln`

## Gaps / assumptions
- No AGENTS.md/CONTRIBUTING/.kiro steering in the repo; conventions inferred from sibling code (SongMetadataEditorWindow, SlideNameDialog, existing tests).
- Thumbnails: scope allows a small text preview instead of a rendered image; `ThumbnailPath` left untouched this stage.
- The static "Text" layer label + notes box are optional and omitted; per-slide text editing is the required minimum and is implemented.
