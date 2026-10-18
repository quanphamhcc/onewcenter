# Completion Report — Sidebar on Service Open

## Summary

The left SERVICE ORDER sidebar and the center current-item header are now **hidden on every
non-presentation screen** (Home, Services list, Songs, Scripture, Media) and **shown only on the
Live / slide-grid presentation surface**. Opening a service (clicking a Services row, or the Live
tab) brings the slide grid up, the left sidebar reappears populated with that service's items under
its "SERVICE ORDER" header, and the center header shows the service's title/subtitle.

This is a **code-only UI change** (two `OperatorWindow` files). No data file was read or written.
The change lives on branch `feature/sidebar-on-service-open` in the worktree and is **not merged**.

- Branch: `feature/sidebar-on-service-open`
- Worktree: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sidebar-toggle`
- Diffstat (`git diff main...HEAD --stat`): 2 files changed, +34 / -2
  - `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` — +2 / -2 (two `x:Name` attributes)
  - `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — +32 (one helper + four call sites)

---

## 1. file:line control-flow trace

Line numbers are the final (post-change) positions in
`WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`.

### SetSidebarVisible(false) — the three non-presentation entry points

- **Initialize (Home landing)** — `OperatorWindow.xaml.cs:250`
  Inside the full `Initialize(...)` overload, right after the Home-setup panel block
  (`HomePanel.Visibility = Visible; ServicesListPanel = Collapsed; ServicesPanel = Collapsed`),
  the code calls `SetSidebarVisible(false);` before `RefreshServiceListingInitialAsync()`. The app
  starts on Home with no sidebar and no center header.

- **ShowServicesList (Services table)** — `OperatorWindow.xaml.cs:343`
  After the panel-visibility and tab-state lines (`SetTabActive(TabServices)`), the method calls
  `SetSidebarVisible(false);` just before `await RefreshServiceListingAsync();`. The Services table
  renders with no sidebar/header.

- **SwitchToTab prologue** — `OperatorWindow.xaml.cs:2277`
  In the generic "hide all panels" prologue (after the `*.Visibility = Collapsed` lines, before the
  `SetTabInactive(...)` resets), the code calls `SetSidebarVisible(false);`. This covers
  Home/Songs/Scripture/Media. The `"Live"` case later calls `ShowServiceEditor()`, which re-shows the
  sidebar, so Live ends up visible while every other tab stays hidden.

### SetSidebarVisible(true) — the Live / presentation surface

- **ShowServiceEditor** — `OperatorWindow.xaml.cs:368`
  After `ServicesPanel.Visibility = Visibility.Visible;`, the method calls `SetSidebarVisible(true);`.
  This is the single funnel every "open a service" path converges on, so showing the slide grid always
  re-shows the sidebar + header.

### OnOpenServiceRequested → load → ShowServiceEditor → sidebar shown + items populated

`OnOpenServiceRequested(object?, string path)` loads the service order, then on success runs
`ShowServiceEditor()` (which calls `SetSidebarVisible(true)` → sidebar + header visible) followed by
`RefreshServiceOrderList()` (rebinds `ServiceOrderList.ItemsSource` from `_serviceOrderItems` and
toggles `SidebarEmptyState` off the item count). Result: opening a service shows the slide grid AND a
populated sidebar. If the load fails, the method stays on the current screen and never calls
`ShowServiceEditor`, so no empty sidebar is forced open.

### The helper itself — `OperatorWindow.xaml.cs:1038` (SetSidebarVisible)

```csharp
private void SetSidebarVisible(bool visible)
{
    if (visible)
    {
        ServiceOrderSidebar.Visibility = Visibility.Visible;
        SidebarColumn.Width = new GridLength(_isSidebarCollapsed ? 48 : 200);
        CurrentItemHeader.Visibility = Visibility.Visible;
    }
    else
    {
        ServiceOrderSidebar.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        CurrentItemHeader.Visibility = Visibility.Collapsed;
    }
}
```

Re-show restores `GridLength(_isSidebarCollapsed ? 48 : 200)`, honoring the user's existing
narrow/wide collapse preference without mutating `_isSidebarCollapsed`, so that preference survives a
Home round-trip and the existing `SidebarCollapse_Click` behavior is not regressed.

---

## 2. Handler / x:Name preservation checklist (all confirmed intact)

The diff adds only two `x:Name` attributes (`ServiceOrderSidebar`, `CurrentItemHeader`) and one method
plus four call sites. No handler, binding, or named element was removed or renamed.

Event handlers — all still wired to their XAML `Click=` / `SelectionChanged=`:

- `ServiceOrderList_SelectionChanged` — intact
- `ServiceOrderList_MouseDoubleClick` — intact
- `NewService_Click` — intact
- `EditServiceItem_Click` — intact
- `ItemSettings_Click` — intact
- `RemoveServiceItem_Click` — intact
- `EditService_Click` — intact
- `LoadService_Click` — intact
- `ApplyToSelected_Click` — intact
- `AddGroup_Click` — intact
- Bulk toggle (`BulkEditToggle_Checked` / `BulkEditToggle_Unchecked`) — intact
- `EditModeToggle_Click` — intact
- All tab click handlers (Home / Songs / Scripture / Media / Live, plus Services) — intact
- HomeView events (service list / quick-start subscriptions) — intact
- `SetServices` — intact

Named elements present in the visual tree (not removed, only their container Border collapses):

- `CurrentItemTitle` — present
- `CurrentItemSubtitle` — present
- `EditModeToggleBtn` — present

`ShowServiceEditor` still writes `CurrentItemTitle.Text` / `CurrentItemSubtitle.Text` for the
empty/none-current order, so there is no dangling reference to a removed element.

---

## 3. No invisible click-eating strip

The hit-testing failure mode that broke a prior attempt is explicitly avoided:

- When hidden, `SidebarColumn.Width` is set to `new GridLength(0)` **in addition to** collapsing
  `ServiceOrderSidebar.Visibility`. `SidebarColumn` is Column 0 of the ROW 3 content grid and the
  center content is Column 1 (`Width="*"`), so zeroing Column 0 hands the full width to the content
  column. No residual 200px strip remains over the clickable area.
- The center header (`CurrentItemHeader`) is Row 0 of the center grid; collapsing it (`Collapsed`,
  not `Hidden`) removes it from layout so the rows reflow and no zero-size element intercepts input.
- The change adds no stretch overlay, no `Background="Transparent"` hit-test catcher, and no
  `IsHitTestVisible` toggles. `SidebarEmptyState` is unchanged and only stretches inside the sidebar
  cell, which is harmless when the whole sidebar is collapsed.

Confirmed: no hidden element overlaps content with hit-testing.

---

## 4. Build and test results (as recorded by the coder/reviewer)

- **Build**: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **0 errors**, **4 pre-existing
  warnings** (MediaLibraryPanel CS0414, ChordChartWindow CS8604, WaveformEditorWindow CS0414) — OK,
  not introduced by this change.
- **Tests**: `dotnet test WorshipPlayer.Tests` → **620 total / 620 passed / 0 failed / 0 skipped** —
  no regressions.

Note: the test project (`WorshipPlayer.Tests`, `net8.0`) cannot reference the Presenter WPF project
and compiles only Presenter *Services* files, so no unit test exercises `OperatorWindow`. The compiler
is the automated guard for this layout change; the manual click-test below is the behavioral guard
(WPF cannot be screenshotted/verified headless). Per scope, the suites were not re-run in this step;
the above figures are self-reported in the commit evidence and judged credible by the reviewer because
they name the exact pre-existing warnings and give concrete counts.

---

## 5. Branch and worktree status

- Branch: `feature/sidebar-on-service-open`
- The change lives in the worktree at
  `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sidebar-toggle`
  and is **NOT merged** into `main`.
- `git diff main...HEAD --stat`: only `OperatorWindow.xaml` and `OperatorWindow.xaml.cs` are modified
  (2 files, +34 / -2). No data file (app.db, song-usage.json, ServiceOrders/*.json, Songs/*) is in the
  changeset.

**Review verdict: APPROVED.** One non-blocking note: build/test evidence is self-reported in the
commit message rather than an attached log.

---

## 6. CLICK-TEST CHECKLIST (manual — WPF can't be screenshotted headless)

Run `dotnet run --project WorshipPlayer.UI` and walk through:

1. **Home on startup** — the left sidebar is GONE and there is no center header strip. The Home
   "Quick start" buttons and the "Upcoming services" list are fully clickable; nothing is dead.
2. **Services tab** — click the Services top-nav tab: the services table shows with NO sidebar and NO
   center header; every service row is clickable.
3. **Open a service** — click a Services row (e.g. "Sunday"): the service opens, the center shows the
   slide grid, and the LEFT sidebar appears populated with THAT service's items under the
   "SERVICE ORDER" header; the center header shows the service name/subtitle.
4. **Tabs switch correctly** — Home / Songs / Scripture / Media / Live all switch to the right screen;
   Songs/Scripture/Media show with NO sidebar/header and their content is fully interactive; Live
   shows the slide surface WITH the sidebar.
5. **Sidebar interactions** — on the Live/slide surface, the sidebar item list, its right-click context
   menu (Edit / Item Settings / Move to Group / Remove / Add Group), §Group, Bulk toggle, and the
   collapse chevron all work.
6. **Slide grid & transport** — slide thumbnails respond (go-live / edit-select) and the transport bar
   is clickable.
7. **Collapse preference survives** — on Live, click the collapse chevron (narrows to 48px / back to
   200px); switch to Home and back to Live and confirm the width preference is preserved and nothing
   dead-clicks.
8. **No data loss** — the service list and item lists are intact; app.db / ServiceOrders JSON unchanged.

Every screen must have NO unclickable region and NO data change.

---

## Deliverable pointers

- Final branch: `feature/sidebar-on-service-open`
- Report path: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sidebar-toggle\.agents\tasks\sidebar-toggle\report.md`
