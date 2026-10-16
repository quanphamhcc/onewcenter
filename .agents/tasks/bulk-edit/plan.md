# Implementation Plan — Bulk Edit (roadmap C3)

Multi-select service items + a reachable "Apply to selected…" action that batch-applies
background / transition / auto-advance to all selected items, with per-field opt-in, reusing
the existing models and `ServiceOrderManager.SaveAsync` persistence. Must coexist with the
just-merged C2 grouped sidebar and must not disturb C4 auto-advance or the ChordPro/metadata/
slides/item-settings event wiring.

## Evidence (verified by reading source in the worktree)

- **BulkEditPanel is orphaned.** A repo-wide search for `BulkEdit` matches only
  `WorshipPlayer.Presenter/Views/ServiceBuilder/BulkEditPanel.xaml(.cs)` themselves — nothing
  constructs or references the panel.
- **What the panel batch-edits** (`BulkEditPanel.xaml.cs:52-66`, `BulkEditPanel.xaml:60-130`):
  three opt-in fields, each gated by a checkbox:
  - Background — `ApplyBackgroundCheck` + `BackgroundComboBox` with items `None / Solid Color /
    Image / Video` (`BulkEditPanel.xaml:84-96`).
  - Transition — `ApplyTransitionCheck` + `BatchTransitionSlider` (0–3000 ms)
    (`BulkEditPanel.xaml:99-110`).
  - Auto-Advance — `ApplyAutoAdvanceCheck` + `AutoAdvanceComboBox` with items `Manual /
    Time Delay / End of Media` (`BulkEditPanel.xaml:113-127`).
  It takes a count in via `SetSelectedCount(int)` (`BulkEditPanel.xaml.cs:34-50`) and emits a
  `BulkEditOptions` out via `ApplyRequested` (`BulkEditPanel.xaml.cs:52-67`). It also exposes a
  `DeleteRequested` event with its own confirm dialog (`BulkEditPanel.xaml.cs:69-82`).
  `BulkEditOptions` (fields `ApplyBackground/BackgroundType/ApplyTransition/TransitionTimeMs/
  ApplyAutoAdvance/AutoAdvanceMode`) currently lives *inside the WPF code-behind*
  (`BulkEditPanel.xaml.cs:86-95`), so it is NOT reachable from the test project.
- **Real batchable `ServiceItem` properties** (`WorshipPlayer.Core/Models/ServiceOrder.cs`):
  - `BackgroundType? BackgroundType` (line ~168) — enum `SolidColor / Image / Video / Gradient`
    (`WorshipPlayer.Core/Models/SlideTheme.cs:178-186`). Nullable; null = inherit theme.
  - `int TransitionTimeMs` (line ~178, default 500) — matches the panel's transition slider range.
  - `ServiceItemSettings? AutoAdvanceSettings` (line ~162) — the C4 object.
  `ServiceItemSettings` (`WorshipPlayer.Core/Models/ServiceItemSettings.cs`) has
  `AutoAdvanceAfterCondition` (enum `Manual / Timer / AudioEnd / VideoEnd`), `AutoAdvanceGoTo`,
  `AutoAdvanceDelayMs` (5000), `TransitionFadeTimeMs` (500), and `GoToItemId`.
  **The panel's "Auto-Advance" dropdown maps to the *after-condition* only.** `GoToItemId` is a
  per-item-specific pointer and MUST NOT be batch-set.
- **C4 label→enum mapping already exists** but is `internal` inside a WPF file
  (`ItemSettingsPanel.xaml.cs:155-184`, class `ItemSettingsMapping`):
  `"Time Delay" → Timer`, `"End of Media" → AudioEnd`, else `Manual`. The bulk-apply logic will
  reproduce this mapping in Core (and the UI will reuse the Core mapping) so the two stay
  consistent.
- **Sidebar is a single-select `ListBox` named `ServiceOrderList`** embedded in
  `OperatorWindow.xaml:242-291` (no `SelectionMode` set → `Single`). Its `DataTemplate` renders a
  group-header `StackPanel` (visible via `HeaderVisibility`) and an item-row `StackPanel`
  (`ItemVisibility`) (`OperatorWindow.xaml:249-275`). It has a context menu whose `Opened`
  handler enables actions per row type (`OperatorWindow.xaml:276-289`).
- **Rows are `ServiceItemDisplay`** (`OperatorWindow.xaml.cs:1103-1123`): item rows carry a
  non-null `Item`; group-header rows carry `Item == null` + `IsGroupHeader == true`. Backing
  collection is `_serviceOrderItems` (`OperatorWindow.xaml.cs:1124`), rebuilt by
  `RefreshServiceOrderList()` (`OperatorWindow.xaml.cs:1197-1270`) which renders flat when
  `Groups.Count == 0` and grouped otherwise.
- **Established mutation pattern** (used by edit/settings/remove/group handlers,
  e.g. `OpenSelectedItemSettings` `OperatorWindow.xaml.cs:1390-1416`, `MoveToGroup_Click`
  `:1530-1543`): resolve `ServiceOrderList.SelectedItem as ServiceItemDisplay`, skip rows where
  `display.Item == null`, mutate the model, `await SaveServiceOrderAsync()`
  (`OperatorWindow.xaml.cs:1296-1311`, wraps `ServiceOrderManager.SaveAsync`), then
  `RefreshServiceOrderList()`.
- **`RefreshServiceOrderList()` sets `_suppressServiceOrderSelection`** while clearing/repopulating
  (`OperatorWindow.xaml.cs:1200-1266`) so selection churn doesn't re-enter navigation — the
  bulk toggle/refresh path must respect this guard.
- **Test project is `net8.0`** (`WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`), references
  **only `WorshipPlayer.Core`** plus a hand-picked list of Presenter *service* `.cs` files linked
  in (the Presenter project is `net8.0-windows` and cannot be referenced). **Therefore the pure
  bulk-apply logic and its options type must live in `WorshipPlayer.Core`** to be unit-testable;
  they cannot live in the WPF code-behind. Existing group tests
  (`ServiceOrderManagerGroupTests.cs`) are the pattern to follow (xUnit, `[Fact]`, temp-folder
  save/reload).
- **Baseline verified now:** `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj` →
  525 passed, 0 failed. Build command (README.md:73): `dotnet build`.

## Design decisions (made here, grounded in the above)

1. **Put the pure operation in Core, not in the panel.** Create
   `WorshipPlayer.Core/Services/BulkEditService.cs` holding a plain-data `BulkEditOptions`
   (opt-in flags + chosen values) and a pure static `Apply(options, items)` method. Rationale:
   the test project can only reach Core; the current `BulkEditOptions` in the WPF code-behind is
   untestable. The panel will construct the Core `BulkEditOptions` and the UI handler will call
   `BulkEditService.Apply`.
2. **Opt-in semantics = each `Apply*` flag false ⇒ that property is left exactly as-is.** No
   field is touched unless its flag is true, so bulk edit never wipes untouched settings
   (requirement 3). For auto-advance, applying sets `AutoAdvanceAfterCondition` on each item's
   `AutoAdvanceSettings`, creating a `new ServiceItemSettings()` only if the item had none, and
   leaving every *other* settings field (GoTo, delay, fade, GoToItemId) untouched.
3. **Background "None" selection = do not apply** even if the Background checkbox is ticked —
   there is no `BackgroundType.None`; "None" is the panel's neutral/placeholder entry. Treat an
   unmapped/empty background string as "no change" rather than guessing an enum value.
4. **Reuse C4's label mapping in Core.** Move the label→enum logic into the Core service (public)
   and have the WPF `ItemSettingsMapping`/panel delegate to it, so bulk edit and per-item edit
   agree on `"Time Delay"→Timer`, `"End of Media"→AudioEnd`, else `Manual`. (If delegating from
   the WPF side proves awkward, duplicating only the three-way switch in Core is acceptable — the
   mapping is tiny and covered by tests; note the duplication in a comment.)
5. **Reach the panel via a modal host**, mirroring `ItemSettingsWindow`
   (`ItemSettingsWindow.xaml.cs`) which hosts `ItemSettingsPanel`. Create a `BulkEditWindow` that
   hosts `BulkEditPanel`, rather than wiring the panel into the main layout — this matches the
   established "editor UserControl shown modally" convention and avoids disturbing the C2 sidebar
   layout. The window exposes the chosen `BulkEditOptions` after Apply.
6. **Multi-select = an explicit toggle**, not always-on, so single-click navigation (C2/C4
   behavior) is preserved by default. A "Bulk Edit" toggle flips `ServiceOrderList.SelectionMode`
   to `Extended` and reveals the bulk action; turning it off restores `Single`. Group-header rows
   (`Item == null`) are filtered out of the selected set before applying.
7. **Scope guard:** only `ServiceItem.BackgroundType`, `ServiceItem.TransitionTimeMs`, and
   `ServiceItemSettings.AutoAdvanceAfterCondition` are mutated. No new JSON keys, no storage-format
   change (all three already serialize today). Songs/templates/media/scripture code is untouched.

## Tasks (ordered by dependency)

- [ ] 1. Create the Core bulk-edit service with a testable options type and pure apply method.
      Add `WorshipPlayer.Core/Services/BulkEditService.cs` containing: a public `BulkEditOptions`
      class (`bool ApplyBackground; BackgroundType? BackgroundType; bool ApplyTransition;
      int TransitionTimeMs; bool ApplyAutoAdvance; AutoAdvanceAfterCondition AutoAdvanceCondition;`),
      a public static `void Apply(BulkEditOptions options, IEnumerable<ServiceItem> items)` that
      for each item sets only the opted-in fields (per decisions 2–4; auto-advance lazily creates
      `AutoAdvanceSettings` and sets only `AutoAdvanceAfterCondition`), and public mapping helpers
      `ParseBackground(string) : BackgroundType?` (returns null for "None"/empty/unknown — decision
      3) and `ParseAfterCondition(string) : AutoAdvanceAfterCondition` (decision 4). Keep it pure
      (no WPF, no I/O).
      Files: WorshipPlayer.Core/Services/BulkEditService.cs
      Verify: `dotnet build WorshipPlayer.Core/WorshipPlayer.Core.csproj` succeeds (0 errors).

- [ ] 2. Add focused unit tests for `BulkEditService.Apply`.
      Add `WorshipPlayer.Tests/BulkEditServiceTests.cs` (xUnit, following
      `ServiceOrderManagerGroupTests` style) covering: (a) each chosen field is applied to EVERY
      selected item (background, transition, after-condition); (b) unchosen fields are left
      untouched — assert a pre-set `BackgroundType`/`TransitionTimeMs`/existing
      `AutoAdvanceSettings` (including its GoTo/delay/fade/GoToItemId) survive when their flags are
      false, and that applying auto-advance preserves the other settings fields; (c) a no-op
      options object (all flags false) over a set of items changes nothing; plus the mapping edge
      cases (`"None"`/empty background ⇒ no change; `"Time Delay"`/`"End of Media"`/other ⇒
      Timer/AudioEnd/Manual) and empty-selection safety.
      Files: WorshipPlayer.Tests/BulkEditServiceTests.cs
      Verify: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj` — all new tests pass and
      the prior 525 still pass.

- [ ] 3. Point `BulkEditPanel` at the Core options type and expose a modal host.
      Edit `BulkEditPanel.xaml.cs` to remove the local `BulkEditOptions` class and instead build
      and raise `WorshipPlayer.Core.Services.BulkEditOptions` from its checkboxes/controls
      (reusing `BulkEditService.ParseBackground`/`ParseAfterCondition` for the combo text so the
      panel and Core agree). Add `WorshipPlayer.Presenter/Views/ServiceBuilder/BulkEditWindow.xaml`
      (+ `.cs`) that hosts `BulkEditPanel`, calls `SetSelectedCount`, surfaces the chosen
      `BulkEditOptions` as a `Result` property on Apply (mirror `ItemSettingsWindow`), and closes
      with `DialogResult = true`. Do not wire `DeleteRequested` into this flow (out of scope for
      C3 batch-apply; leave the panel's event intact but unused by the host).
      Files: WorshipPlayer.Presenter/Views/ServiceBuilder/BulkEditPanel.xaml.cs,
      WorshipPlayer.Presenter/Views/ServiceBuilder/BulkEditWindow.xaml,
      WorshipPlayer.Presenter/Views/ServiceBuilder/BulkEditWindow.xaml.cs
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj` succeeds
      (0 errors; pre-existing warnings only).

- [ ] 4. Add a "Bulk Edit" toggle + "Apply to selected…" action to the sidebar that coexists with
      grouped rendering. In `OperatorWindow.xaml`: add a small toggle button next to `AddGroupBtn`
      in the sidebar header (`OperatorWindow.xaml:223-234`) and an "Apply to selected…" button in
      the bottom sidebar `StackPanel` (`OperatorWindow.xaml:308-333`), both using the same inline
      brush style as the surrounding buttons (OperatorWindow uses no resource dictionaries). In
      `OperatorWindow.xaml.cs`: add a `_bulkEditMode` flag; the toggle flips
      `ServiceOrderList.SelectionMode` between `Single` and `Extended` and shows/enables the
      "Apply to selected…" button; guard `ServiceOrderList_SelectionChanged`
      (`OperatorWindow.xaml.cs:1325-1342`) so it does not drive single-item navigation while
      `_bulkEditMode` is on. Ensure `RefreshServiceOrderList`'s `_suppressServiceOrderSelection`
      guard still holds (no behavior change needed, just don't regress it).
      Files: WorshipPlayer.Presenter/Views/OperatorWindow.xaml,
      WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs
      Verify: `dotnet build WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj` succeeds; app
      launches via `dotnet run --project WorshipPlayer.UI` and the toggle enables multi-select on
      item rows (manual smoke check; headers remain non-navigating).

- [ ] 5. Wire the "Apply to selected…" handler to collect the selection, open `BulkEditWindow`,
      apply, persist, and refresh — following the established manager→save→refresh pattern. In
      `OperatorWindow.xaml.cs` add an `async` click handler that: reads
      `ServiceOrderList.SelectedItems`, projects to `ServiceItem` filtering out rows where
      `Item == null` (group headers), returns early if the set is empty; constructs
      `BulkEditWindow` with the count, `ShowDialog()`; on `true`, calls
      `BulkEditService.Apply(window.Result, selectedItems)`, then `await SaveServiceOrderAsync()`
      and `RefreshServiceOrderList()`. Wrap in try/catch using `ShowToolError` like sibling
      handlers.
      Files: WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs
      Verify: `dotnet build` of the solution succeeds; manual smoke check — select 3 items, open
      the dialog, tick only Transition, Apply; confirm the 3 items' transition changes persist to
      the service JSON on disk and other fields are unchanged, and that C2 groups + C4 per-item
      settings still work.

- [ ] 6. Full regression gate.
      Run the full test suite and a clean solution build to confirm nothing regressed (C2 groups,
      C4 auto-advance, ChordPro/metadata/slides, persistence).
      Files: (none — verification only)
      Verify: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj` — 525 prior + new
      bulk-edit tests all pass; `dotnet build WorshipBacktrackPlayer.sln` — 0 errors, only the
      4 pre-existing warnings.

## Notes / assumptions

- The panel's `DeleteRequested` (bulk delete) is intentionally left out of the C3 batch-apply
  flow; C3 as scoped is "batch apply background/transition/auto-advance". The event stays in the
  panel but the host does not surface it. If bulk delete is wanted it can reuse
  `ServiceOrderManager.RemoveItem` in a follow-up.
- Background "None" and any unmapped combo text are treated as "leave as-is" (there is no
  `BackgroundType.None`). This keeps opt-in semantics intact.
- WPF UI steps are verified by build + manual smoke check because the headless `net8.0` test
  project cannot exercise WPF controls; the batch-apply *logic* is fully covered by Core unit
  tests, which is where the real risk lives.
