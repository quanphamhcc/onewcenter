# C2 — Service Order Groups: Completion Report

Feature: collapsible named service-order sections (e.g. Opening Worship, Message, Response).
Iteration: FIRST (no `review.json` present). Implemented from scratch per `plan.md`.
Worktree: `.worktrees\service-groups`, branch `feature/service-groups`.

## What was built vs. wired

- **Built (group LOGIC) — new, in the manager:** `CreateGroup`, `RenameGroup`, `DeleteGroup`
  (keeps items, drops only the group), `AssignItemToGroup` (single source of truth; null = ungroup),
  `SetGroupCollapsed`, `GetUngroupedItemIds`. Also fixed `RemoveItem` to purge the removed item's id
  from every group so no dangling references survive. All mutate `CurrentServiceOrder.Groups`/`.Items`
  in place and persist through the EXISTING `SaveAsync` path — no new serialization.
- **Wired (reachable UI):** the existing `ServiceOrderList` sidebar now renders group-header rows +
  item rows (collapsible), with a header "§ Group" button and a context menu (Add/Rename/Delete group,
  Move to Group ▸). The orphaned `ServiceGroupPanel` is intentionally NOT constructed.

### Rendering decision (justified)
Groups are rendered **directly in the existing `ServiceOrderList`** as a flat row list (header rows +
item rows), NOT via the orphaned `ServiceGroupPanel` and NOT via WPF `GroupStyle`. Rationale:
(a) sidebar navigation already resolves by `IndexOf(display.Item)`, not `SelectedIndex`, so interleaving
header rows (which carry a null `Item`) is low-risk; (b) `ServiceGroupPanel` has a divergent, name-keyed
`ServiceGroupItem` model with no persistence — reusing it would mean two UIs and two models;
(c) `GroupStyle` cannot cleanly express a persisted per-group collapse toggle + an "Ungrouped" bucket +
single-click navigation. `ServiceGroupPanel.xaml(.cs)` is left untouched as dead code (out of scope).

## File:line trace (add-group → event → handler → manager op → save → re-render)

1. Entry point A — header button: `WorshipPlayer.Presenter\Views\OperatorWindow.xaml:221-233`
   `<Button x:Name="AddGroupBtn" ... Click="AddGroup_Click">` ("§ Group").
   Entry point B — context menu: `OperatorWindow.xaml:258-275` ("Add Group…", "Rename Group…",
   "Delete Group", "Move to Group" submenu, built on `ServiceOrderContextMenu_Opened`).
2. Handler: `OperatorWindow.xaml.cs` `AddGroup_Click` → `PromptForText` → `ServiceOrderManager.CreateGroup`
   → `SaveServiceOrderAsync()` → `RefreshServiceOrderList()`.
   - Assign: `MoveToGroup_Click` → `AssignItemToGroup(itemId, groupId|null)` → save → refresh.
   - Collapse: `GroupCollapseToggle_Click` (chevron `Tag="{Binding}"`) → `SetGroupCollapsed` → save → refresh.
   - Rename/Delete: `RenameGroup_Click`/`DeleteGroup_Click` → `RenameGroup`/`DeleteGroup` → save → refresh.
3. Manager op: `WorshipPlayer.Core\Services\ServiceOrderManager.cs` — group methods added before `GoToItem`;
   `RemoveItem` updated to purge group ids.
4. Save: `ServiceOrderManager.SaveAsync` (unchanged) serializes `CurrentServiceOrder` incl. `groups`.
5. Re-render: `RefreshServiceOrderList` (`OperatorWindow.xaml.cs`) — when `Groups` is empty, items render
   flat (legacy). Otherwise: for each group by `Order`, emit a header row then (if not collapsed) its item
   rows; finally an "Ungrouped" header + ungrouped item rows (from `GetUngroupedItemIds`).
   Collapse hides rows by rebuilding (collapsed group contributes only its header).
   `ServiceItemDisplay` extended with `IsGroupHeader`, `Group`, `HeaderVisibility`, `ItemVisibility`,
   `CollapseGlyph`.

### Interleave-safety fixes (so header rows don't offset item operations)
- `ServiceOrderList_SelectionChanged`: ignores rows with `display.Item == null` (headers).
- `RemoveServiceItem_Click`: resolves target by `Items.IndexOf(display.Item)` instead of raw `SelectedIndex`.
- `EditSelectedServiceItem` / `OpenSelectedItemSettings`: guard `display.Item == null`.
- `OnServiceItemChanged` sidebar sync check: compares the ITEM-row count (`d.Item != null`) to
  `order.Items.Count`, not total row count (headers would otherwise force endless rebuilds).
- Empty-state visibility keyed off `order.Items.Count`, not row count (a group header is a row with 0 items).

## Backward compatibility
`ServiceOrder.Groups` defaults to `new()`; `_jsonOptions` ignores nulls. A legacy file with no `groups`
key deserializes to an empty list and renders flat/ungrouped. Verified by the `LegacyJson_...` test.

## Verification results

- `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded. 0 Error(s), 4 Warning(s)**.
  The 4 warnings are the pre-existing ones (MediaLibraryPanel CS0414, ChordChartWindow CS8604,
  WaveformEditorWindow CS0414) — none in files touched by this change. No new warnings.
- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` →
  **Passed! Failed: 0, Passed: 525, Skipped: 0, Total: 525** (was ~513; +12 new group tests).

### New tests (`WorshipPlayer.Tests\ServiceOrderManagerGroupTests.cs`)
Create group; sequential Order; assign moves between groups; null target ungroups; unknown ids return
false; delete keeps items (ungrouped) + compacts Order; rename; collapse-state round-trips through
save/reload; legacy no-groups JSON loads all items ungrouped; `RemoveItem` purges group id; ungrouped
order preserved.

## What was NOT verified (honest)
This is a WPF desktop app with no headless UI harness available, so the collapse/expand click, the
context-menu submenu population, and the group-header rendering were **not exercised at runtime** —
correctness of the UI wiring rests on the clean XAML+code build, the passing group-logic tests, and the
file:line wired-path trace above. No visual/interaction verification was performed.

## Scope
Changed only: `ServiceOrderManager.cs` (group ops + RemoveItem purge), `OperatorWindow.xaml` (header
button, grouped DataTemplate, context menu), `OperatorWindow.xaml.cs` (display wrapper, grouped refresh,
handlers, interleave fixes), new test file. No changes to songs, templates, media, auto-advance, scripture,
or the serialization format. Drag-to-reorder (11.5) intentionally out of scope.
