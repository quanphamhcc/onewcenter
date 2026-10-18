# Completion Report — Consolidate OperatorWindow LEFT panel

Branch: `feature/left-panel-consolidate`
Worktree: `.worktrees\left-panel`
Iteration: FIRST (no `review.json` present)

## Layout change (what the user sees)

- **OperatorWindow LEFT panel (ROW 3, Column 0)** is a single service-order panel:
  - Tidy minimal header `SERVICE ORDER` with compact cyan chips `§ Group`, `☑ Bulk`, and a `◀` collapse button.
  - The `ServiceOrderList` ListBox (the service-order item list).
  - A dashed-border empty-state overlay (`SidebarEmptyState`) that reads "To begin, select a service from the Services list. Your service items will appear here." It is **Visible only when no service is loaded** and hidden once a service has items.
  - A compact `WrapPanel` toolbar of small chips: `+ Add`, `Edit`, `Settings`, `Remove`, `Apply to selected…` (collapsed until Bulk is on), `Edit Service`, `Load Service`.
- **Home center (HomeView)** now shows only the Quick start card + Upcoming services card, filling the full width. The duplicate dashed placeholder card was removed, so the placeholder exists only in the LEFT panel.

## Files changed (file:line)

The OperatorWindow LEFT-panel consolidation (header, list, empty-state overlay, compact toolbar) and the code-behind toggle were already present in the worktree and verified intact — no edits were required there. The single substantive edit this iteration was in HomeView.

- `WorshipPlayer.Presenter\Views\Home\HomeView.xaml:15-21`
  - Removed the 3-column `Grid.ColumnDefinitions` (`320 / 20 / *`).
  - Deleted the LEFT dashed-placeholder `Border` (Grid.Column="0") that held the dashed `Rectangle` and the two "To begin…" / "Your service items…" `TextBlock`s.
  - Changed the remaining Quick start + Upcoming services container from `<StackPanel Grid.Column="2">` to a full-width `<StackPanel>`.
  - `UserControl.Resources` (merged `OwcDesignSystem.xaml`), the Quick start `UniformGrid`, `ServicesListView`/`GridView`, and `+ Add a new service` button are unchanged.

Verified present (no edit needed) in the worktree:
- `WorshipPlayer.Presenter\Views\OperatorWindow.xaml:218-357` — LEFT SIDEBAR Border, header Grid (Row 0), `ServiceOrderList` ListBox (Row 1), `SidebarEmptyState` dashed overlay (Row 1, `OperatorWindow.xaml:309-319`), compact `WrapPanel` toolbar (Row 2, `OperatorWindow.xaml:326-356`).
- `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs:1327-1336` — empty-state toggle in the `finally` of `RefreshServiceOrderList()`.

## Empty-state toggle location (code-behind)

`WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`, inside the `finally` block of `RefreshServiceOrderList()` (lines ~1331-1336):

```csharp
if (SidebarEmptyState != null)
    SidebarEmptyState.Visibility = (_engine?.CurrentServiceOrder?.Items.Count ?? 0) == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
```

Keyed off the service-order **item count** (not row count, which would include group headers), so the placeholder shows when no items are listed and hides once a service has items.

## Per-button x:Name + handler preservation checklist

LEFT panel header (OperatorWindow.xaml Row 0):
- [x] `AddGroupBtn` → `AddGroup_Click`
- [x] `BulkEditToggle` → `BulkEditToggle_Checked` / `BulkEditToggle_Unchecked`
- [x] `SidebarCollapseBtn` → `SidebarCollapse_Click`

LEFT panel list (Row 1):
- [x] `ServiceOrderList` → `ServiceOrderList_SelectionChanged`, `ServiceOrderList_MouseDoubleClick`
- [x] Context menu → `ServiceOrderContextMenu_Opened`; items `EditServiceItem_Click`, `ItemSettings_Click`, `MoveToGroupMenu` (named), `RemoveServiceItem_Click`, `AddGroup_Click`, `RenameGroup_Click` (`RenameGroupMenu`), `DeleteGroup_Click` (`DeleteGroupMenu`)
- [x] `SidebarEmptyState` (named; toggled from code-behind) — present, overlaying Row 1

LEFT panel compact toolbar (Row 2 WrapPanel):
- [x] `+ Add` → `NewService_Click`
- [x] `Edit` → `EditServiceItem_Click`
- [x] `Settings` → `ItemSettings_Click`
- [x] `Remove` → `RemoveServiceItem_Click`
- [x] `ApplyToSelectedBtn` → `ApplyToSelected_Click` (Visibility=Collapsed; flipped by Bulk toggle)
- [x] `Edit Service` → `EditService_Click`
- [x] `Load Service` → `LoadService_Click`

HomeView (after placeholder removal):
- [x] `ServicesListView` (named; bound by `SetServices`) — preserved
- [x] Quick start: `Tour_Click`, `Services_Click`, `Songs_Click`, `Slides_Click`, `Media_Click`, `Scripture_Click`
- [x] Row actions: `Overflow_Click`, `Open_Click`, `Edit_Click`, `Rename_Click`, `Delete_Click`, `AddService_Click`

## Crash-safety invariant confirmation

OperatorWindow.xaml contains **NO** `StaticResource` or `ResourceDictionary`. The only textual occurrence of "StaticResource" is inside an explanatory code comment (`OperatorWindow.xaml:255`); all brushes in the LEFT panel are inline hex values. HomeView.xaml keeps its merged `OwcDesignSystem.xaml` theme resources (allowed). OWC branding retained; no "Presenter" logo added.

## Build / test results

- `dotnet build WorshipBacktrackPlayer.sln` → **Build succeeded. 0 Error(s), 4 Warning(s)** (the 4 are pre-existing: `MediaLibraryPanel._currentView` CS0414 ×2, `ChordChartWindow` CS8604, `WaveformEditorWindow._inReviewMode` CS0414).
- `dotnet test WorshipBacktrackPlayer.sln` → **Passed! Failed: 0, Passed: 620, Skipped: 0, Total: 620.** No new tests added, no regressions.

## Visual-confirmation checklist (WPF cannot be screenshotted headless)

Please confirm on screen:
1. Open the operator window with **no service loaded**: the LEFT panel shows the dashed box "To begin, select a service from the Services list. Your service items will appear here."
2. The Home center shows **only** Quick start + Upcoming services, full width — **no** dashed placeholder in the center anymore.
3. **Load a service**: the LEFT panel dashed placeholder disappears and the service-order item list appears.
4. LEFT panel header chips `§ Group`, `☑ Bulk`, `◀` collapse all respond.
5. Toolbar chips `+ Add`, `Edit`, `Settings`, `Remove`, `Edit Service`, `Load Service` all respond; turning `☑ Bulk` on reveals `Apply to selected…`.
6. Right-click an item in the list: context menu (Edit, Item Settings…, Move to Group, Remove, Add Group…, Rename Group…, Delete Group) works.
7. Home: Quick start buttons (Tour/Services/Songs/Slides/Media/Scripture), the Upcoming services list, row ⋮ menu (Open/Edit/Rename/Delete), and + Add a new service all respond.
