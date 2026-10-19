# Completion Report — Service "overflow" (⋮) menu

Branch: `feature/service-overflow-menu`
Worktree: `.worktrees/service-menu`

## What changed

Two source files touched (plus this report):

1. `WorshipPlayer.Core\Services\ServiceOrderManager.cs` — added a minimal `CloseCurrent()`.
2. `WorshipPlayer.Presenter\Views\OperatorWindow.xaml` — added the ⋮ button + ContextMenu.
3. `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs` — added the opener, the enable/disable
   guard, and the Print / Open-in-Planning / Close handlers.

No other views, models, storage, or data files were touched. No `app.db`, no `ServiceOrders/*.json`,
no `Songs/*` were written or deleted by this change.

## file:line trace

### The ⋮ button + ContextMenu (XAML)
- `OperatorWindow.xaml` — inside the sidebar header `StackPanel` (`Grid.Row="0"`), a new
  `Button x:Name="ServiceMenuBtn"` is placed BEFORE `SidebarCollapseBtn`. Inline-styled to match the
  neighbors: `Content="&#x22EE;"` (⋮), `Background="Transparent"`, `Foreground="#8B92A5"`,
  `BorderBrush="Transparent"`, `BorderThickness="0"`, `Padding="3,1"`, `Margin="0,0,6,0"`,
  `Cursor="Hand"`, `ToolTip="Service menu"`, `Click="ServiceMenu_Click"`.
- Its `<Button.ContextMenu><ContextMenu x:Name="ServiceOverflowMenu" Opened="ServiceOverflowMenu_Opened">`
  holds the items IN ORDER:
  1. `MenuItem x:Name="MenuCreateItem" Header="Create new service item" Click="NewService_Click"`
  2. `MenuItem x:Name="MenuCreateGroup" Header="Create new group header" Click="AddGroup_Click"`
  3. `MenuItem x:Name="MenuEditSettings" Header="Edit service settings" Click="EditService_Click"`
  4. `<Separator />`
  5. `MenuItem x:Name="MenuPrintService" Header="Print service" Click="OnPrintService"`
  6. `MenuItem x:Name="MenuOpenInPlanning" Header="Open service in Planning" Click="OnOpenInPlanning"`
  7. `<Separator />`
  8. `MenuItem x:Name="MenuCloseService" Header="Close service" Click="OnCloseService"`

### Each menu item -> its handler (reused or new)
- Create new service item -> `NewService_Click` (REUSED, existing; opens `AddServiceItemDialog { CreateNewService = true }`).
- Create new group header -> `AddGroup_Click` (REUSED, existing; guards null order then `CreateGroup`).
- Edit service settings -> `EditService_Click` (REUSED, existing; guards null order then `ServiceInfoDialog`).
- Print service -> `OnPrintService` (NEW).
- Open service in Planning -> `OnOpenInPlanning` (NEW, placeholder notice; `IsEnabled=true`).
- Close service -> `OnCloseService` (NEW).

### Opener + guard (code-behind)
- `ServiceMenu_Click` — mirrors `ServicesListView.Overflow_Click`: sets
  `btn.ContextMenu.PlacementTarget = btn`, `Placement = PlacementMode.Bottom`, `IsOpen = true`.
- `ServiceOverflowMenu_Opened` — computes `hasService = _serviceOrderManager?.CurrentServiceOrder != null`
  and sets `MenuCreateItem.IsEnabled = true` (new-service always allowed),
  `MenuCreateGroup/MenuEditSettings/MenuPrintService/MenuCloseService.IsEnabled = hasService`,
  `MenuOpenInPlanning.IsEnabled = true`. This is the disable-on-open guard; the reused handlers keep
  their own null-guards as a second line of defense.

### OnPrintService opening PrintExportDialog
- `OnPrintService` — guards `order == null` (no-op), maps `order.Items` to
  `ServiceBuilder.ServiceOrderEntry { Title = i.Title, Type = i.Type.ToString(), Lyrics = null }`,
  constructs `new PrintExportDialog { Owner = this, ExportService = _exportService }`, calls
  `dlg.LoadServiceOrder(order.Name, entries)` then `dlg.ShowDialog()`. This matches the dialog's
  ACTUAL API (parameterless ctor, `ExportService` property, `LoadServiceOrder(string, List<ServiceOrderEntry>)`
  — read from `PrintExportDialog.xaml.cs`). Wrapped in try/catch -> `ShowToolError("Print Service", ex.Message)`.

### OnCloseService unload + return-to-Home path
- `OnCloseService` — guards `CurrentServiceOrder == null` (no-op), confirms with a Yes/No MessageBox,
  then:
  - `_serviceOrderManager.CloseCurrent()` — nulls the in-memory order + index and raises
    `ItemChanged` with `NewItem = null` so the engine (its only `ItemChanged` subscriber) clears its slides.
  - `_currentServicePath = null` (mirrors the delete path at `OnDeleteServiceRequested`).
  - Home-restore sequence copied from `Initialize()`: `EnsureHomeView(); HomePanelContent.Content = _homeView;
    HomePanel.Visibility = Visible; ServicesListPanel.Visibility = Collapsed;
    ServicesPanel.Visibility = Collapsed; SetSidebarVisible(false);`.
  - UI refresh: `RefreshServiceOrderList(); RefreshSlideSections(); UpdatePreviews(); UpdateSlideCounter();
    _ = RefreshServiceListingInitialAsync();`. All null-safe: `RefreshServiceOrderList` reads
    `_engine?.CurrentServiceOrder` and clears/returns on null; `RefreshSlideSections` shows the empty
    state when `order == null || order.Items.Count == 0`.
  - Wrapped in try/catch -> `ShowToolError("Close Service", ex.Message)`. No file delete.

### Minimal ServiceOrderManager.CloseCurrent()
- `ServiceOrderManager.cs` — placed next to `CreateNew`/`AddItem`. Returns early if nothing loaded,
  captures `previous`, nulls `CurrentServiceOrder` and sets `CurrentItemIndex = -1`, raises
  `ItemChanged` with `PreviousIndex = previous, NewIndex = -1, NewItem = null`. No file-IO touched.
  Justified because `CurrentServiceOrder` has a private setter and no existing unload/clear method exists.

### StaticResource-free confirmation
- `OperatorWindow.xaml` contains no real `StaticResource` reference; the only occurrence of the word
  is the pre-existing explanatory comment near the `ServiceOrderList` ListBox. Every new brush is inline.

### No data files touched
- Only `OperatorWindow.xaml(.cs)` and `ServiceOrderManager.cs` were edited. The close path only
  mutates in-memory state + refreshes the UI; it writes/deletes no JSON and touches no `app.db`.

### All existing handlers / x:Names preserved
- No existing `x:Name` or Click handler was removed or renamed. The change is purely additive:
  new `ServiceMenuBtn`, `ServiceOverflowMenu`, and six `MenuItem` x:Names; new handlers
  `ServiceMenu_Click`, `ServiceOverflowMenu_Opened`, `OnPrintService`, `OnOpenInPlanning`, `OnCloseService`;
  new `CloseCurrent()`. `AddGroupBtn`, `BulkEditToggle`, `SidebarCollapseBtn` and their handlers are intact.

## Guards (never crash)
- Menu opens with service-dependent items disabled when no service is loaded (`ServiceOverflowMenu_Opened`).
- Each new handler also guards defensively: `OnPrintService` / `OnCloseService` no-op on null order;
  the reused `AddGroup_Click` / `EditService_Click` keep their own null-guards.
- `OnOpenInPlanning` only shows an informational MessageBox (future cloud feature) and does nothing else.

## Build / test results
- `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded, 0 errors, 4 warnings**
  (all 4 pre-existing: `MediaLibraryPanel._currentView` CS0414, `ChordChartWindow` CS8604,
  `WaveformEditorWindow._inReviewMode` CS0414 — unrelated to this change).
- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` →
  **total: 624, failed: 0, succeeded: 624, skipped: 0**. (The console line
  "Error loading service order: 't' is an invalid start of a property name" is expected output from
  an existing negative test feeding malformed JSON; it is not a failure.)

## Click-test checklist (manual smoke — not a gate, no UI-automation tests exist)
- [ ] App launches on Home; ⋮ present in the sidebar header.
- [ ] With no service loaded: ⋮ opens; Create group / Edit settings / Print / Close are greyed;
      "Create new service item" and "Open in Planning" remain enabled; nothing crashes.
- [ ] Open a service: ⋮ items all enabled.
- [ ] "Print service" opens `PrintExportDialog` with the current service entries.
- [ ] "Open service in Planning" shows the "future cloud feature" notice and nothing else.
- [ ] "Close service" asks to confirm, then unloads and returns to Home with the sidebar hidden,
      no crash, and the service listing refreshed.
- [ ] Existing header buttons (Group, Bulk, collapse) and all prior behavior (sidebar show/hide,
      all-sections center pane, Songs "+ Add", open-on-click, transport, zoom, tab switching) unaffected.
