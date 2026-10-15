# Design — Home & Services-List Screens (OWC Presenter)

## Overview

We add two new UI surfaces to the existing `OperatorWindow` (WPF, .NET 8 Windows) so the Presenter opens on a WorshipTools-style **Home** screen (Quick start + Upcoming services) instead of an empty slide grid, and the top-nav **Services** tab becomes a full **Services list** table (Date / Type / Title / Last modified / Last modified by) with search, a `+ New Service` button, per-row overflow menus, and row-click load. Both are built as two new WPF `UserControl`s that are hosted inside `OperatorWindow`'s existing center content host. All the service-listing/formatting logic that the two views need is extracted into **cross-platform Core helpers** so it is unit-testable in `WorshipPlayer.Tests` (net8.0, no WPF).

The guiding constraint is crash-avoidance: `OperatorWindow.xaml` deliberately uses **no resource dictionaries** (every brush/style is hardcoded inline), because implicit/keyless styles and forward `StaticResource` references previously caused `DependencyProperty.UnsetValue` and `StackOverflow` crashes at load. The two new UserControls each **merge `OwcDesignSystem.xaml`** in their own `UserControl.Resources` (exactly like `ServiceInfoDialog` already does) and use the keyed dark styles — this is safe because a UserControl carries its own resource scope and never relies on `OperatorWindow`'s (nonexistent) dictionary. We never add a `{StaticResource …}` reference to `OperatorWindow.xaml` itself.

## Technology stack (locked)

- **Language / runtime:** C# / .NET 8. `WorshipPlayer.Presenter` is `net8.0-windows` (WPF). `WorshipPlayer.Core` is `net8.0` (no WPF). `WorshipPlayer.Tests` is `net8.0`, xUnit 2.6.6, `Microsoft.NET.Test.Sdk` 17.8.0.
- **UI:** WPF with the existing OWC dark theme. New views are `UserControl`s compiled by the WPF SDK's default XAML globbing (no csproj edits needed to add `.xaml`/`.xaml.cs`).
- **Table control:** a styled **`ListView` with a `GridView`** for both tables. Chosen over `DataGrid` (see Decision 4).
- **Data:** existing `ServiceOrderManager` + `ServiceOrder` model; JSON files under the app's `ServiceOrders/` folder. No schema changes.
- **No new NuGet dependencies.**

## Existing structure this design builds on

- `OperatorWindow.xaml` Row 2 is the nav tab bar (`TabServices`, `TabSongs`, `TabScripture`, `TabMedia`, `TabTools`). Row 3 is the main content: a left `SidebarColumn` (the SERVICE ORDER list, already wired to `ServiceOrderManager.GoToItem`) and a center `Grid` (Column 1).
- The center `Grid` (Row 3 / Column 1) has a header `Border` (`CurrentItemTitle`/`CurrentItemSubtitle`) and a content `Grid` (Row 1) that stacks overlapping panels toggled by visibility: `ServicesPanel` (the slide-thumbnail grid, visible by default), `SongsPanel`, `ScripturePanel`, `MediaPanel`, `ErrorPanel`. `SwitchToTab(string)` in `OperatorWindow.xaml.cs` hides all of them and shows one, lazily creating the Songs/Scripture/Media `UserControl`s.
- The left sidebar's empty state is implicit today (an empty `ServiceOrderList`). The screenshots call for explicit dashed-border empty-state text.
- `ServiceOrderManager` (Core) owns `LoadAsync(path)`, `SaveAsync(path?)`, `CreateNew(name,date,type)`, and `LoadAllAsync()` which scans `ServiceOrders/*.json` (the `Directory.GetFiles(_serviceOrdersBasePath, "*.json")` loop inside `LoadAllAsync`) and returns `List<ServiceOrder>` **ordered by `Date` desc** — but it throws away each file's path and `LastWriteTime`, and its per-file failure handler is a **bare `catch { }`** (no logging). The Services list needs both path and timestamp, so we add a path-aware listing method rather than reusing `LoadAllAsync` as-is (Decision 5).
- **`LoadAsync(path)` is not a side-effect-free read.** It sets `CurrentServiceOrder`, resets `CurrentItemIndex`, and fires `ServiceOrderLoaded`. `PresenterIntegration` subscribes `ServiceOrderLoaded += OnServiceOrderLoaded` (→ `GoToItem(0)` → sidebar repopulate). So calling `LoadAsync` purely to *read* a file would silently swap the live/open service and repopulate the sidebar. This directly shapes the Rename flow (Decision 7).
- **`SaveAsync(path?)` always serializes `CurrentServiceOrder`** (returns early when it is null). With no path it derives `{Date}_{SanitizedName}.json` via `GetDefaultFilePath`; with an explicit path it writes exactly there. Any flow that saves must therefore first ensure the object being edited *is* `CurrentServiceOrder`.
- `ServiceOrder`: `Name` (JSON `name`, shown as **Title**), `Date` (JSON `date`, string `yyyy-MM-dd`), `ServiceType` (JSON `serviceType`, shown as **Type**), nullable `StartTime`. `ServiceDateFormatter` (Core) already does US date/time formatting and tolerant parsing.
- `ServiceInfoDialog` (Views/ServiceBuilder/) is a `Window` that merges `OwcDesignSystem.xaml`; `ShowDialog()` returns `true` with `Confirmed`, `ServiceName`, `ServiceDate`, `StartTime`. `OperatorWindow.NewService_Click` and `EditService_Click` already use it.
- `PresenterIntegration` constructs `ServiceOrderManager` against `AppDomain.CurrentDomain.BaseDirectory + "ServiceOrders"` and auto-loads the **most recent file by `LastWriteTimeUtc`** on startup. That auto-load is why the app currently opens showing *TheBlessing Service* — an artifact we leave intact but make irrelevant, because Home now gives the user an explicit entry point (Decision 1 keeps auto-load; the user picks what they want from the list).

## Ambiguity resolution — how the screens slot in

**Decision 1 — Home mounts as the initial center content, as a sibling panel of `ServicesPanel`.** We do **not** replace `ServicesPanel` (the slide grid is still the live editing surface once a service is open). We add a new `HomePanel` (`Border` host, same overlapping-panel pattern) in Row 3 / Column 1, **visible by default on startup**, with `ServicesPanel` hidden until a service is actually opened. `HomePanel` hosts one instance of the new `HomeView` UserControl. Rationale: it reuses the proven visibility-toggle host, needs no new window, and keeps all crash-sensitive hosting inside the pattern that already works.

**Decision 2 — the `Services` top-nav tab shows the Services **list**, not the slide grid.** Today `TabServices_Click → SwitchToTab("Services")` shows `ServicesPanel` (slides). We repoint it: the `Services` tab now shows a new `ServicesListPanel` hosting the `ServicesListView` UserControl. The slide-editing grid (`ServicesPanel`) is no longer reached via the tab; it is shown programmatically when a service is **opened** (from Home or the Services list) via a new internal state `"Live"`. So `SwitchToTab` gains two logical targets — `"Home"` and `"ServicesList"` — and a helper `ShowServiceEditor()` that reveals `ServicesPanel`. This matches the screenshots (Services tab = table of services; opening one drops you into the editor) and the user's message *"service sẽ là list của service cần phát, khi chọn danh sách bên trái"* (Services = a list; pick one to drive the left sidebar).

**Decision 3 — opening a service from either screen funnels through one method, and the center only switches on a successful load.** Both `HomeView` (Upcoming services row / overflow→Open) and `ServicesListView` (row click / overflow→Open) raise an event the host handles by calling the private `LoadServiceOrderAsync(path)` (which wraps `ServiceOrderManager.LoadAsync` with the non-fatal messagebox). `LoadServiceOrderAsync` is changed from `private async Task` to **`private async Task<bool>`** returning `(loaded != null)` and `false` in the catch (see item 14 / the "Changed — OperatorWindow.xaml.cs" edits), so the host can branch on success. The handler is therefore `if (await LoadServiceOrderAsync(path)) { _currentServicePath = path; ShowServiceEditor(); RefreshServiceOrderList(); }` — the center switches to the editor **only** when the load actually succeeded. The sidebar is repopulated by the existing `OnItemChanged` wiring when `LoadAsync` sets the first item current, so we do not duplicate that.

**Decision 4 — table control is `ListView`+`GridView`, dark-themed via keyed styles in `OwcDesignSystem.xaml`.** `DataGrid` defaults to white cells/headers and editable cells; getting it fully dark requires overriding `RowBackground`, `Foreground`, `ColumnHeaderStyle`, `CellStyle`, and gridlines, which is more surface area for exactly the white-on-white bug the task warns about. A read-only `ListView`/`GridView` with a dark `ControlTemplate` on `ListViewItem` and a styled `GridViewColumnHeader` is simpler and already idiomatic in this repo's panels. We add two **new keyed styles** to `OwcDesignSystem.xaml`: `OwcDarkListView` (transparent/dark background, light foreground, no default item chrome) and `OwcDarkGridViewColumnHeader` (dark header row, secondary text). These are keyed (never implicit) and only consumed by the two new UserControls, which merge the dictionary — `OperatorWindow.xaml` is untouched.

**Verified theme resource keys (use these exact names).** The brush resources in `OwcDesignSystem.xaml` are all `*Brush`-suffixed: `OwcTextPrimaryBrush`, `OwcTextSecondaryBrush`, `OwcTextMutedBrush`, `OwcPanelBrush`, `OwcSurfaceBrush`, `OwcSurfaceHoverBrush`, `OwcBorderBrush`, `OwcAccentBrush`, `OwcBackgroundBrush`. There are **no** keys named `OwcTextPrimary`, `OwcTextSecondary`, `OwcPanel`, or `OwcSurfaceHover` — those would be unresolved `{StaticResource}` lookups that **throw at load**, which is precisely the crash class this task guards against. Every `{StaticResource}` in the new UserControls and in the two new styles must use the `*Brush` names above (e.g. header text `Foreground="{StaticResource OwcTextSecondaryBrush}"`, row hover `Background="{StaticResource OwcSurfaceHoverBrush}"`). The keyed *style* names (`OwcPanelBorder`, `OwcButtonBase`, `OwcAccentButton`, `OwcTextBox`, `OwcDarkComboBox`) are correct as written.

**Decision 5 — add a path-aware listing to Core, don't overload `LoadAllAsync`.** `LoadAllAsync` returns bare `ServiceOrder`s and is used elsewhere (`GlobalSearchService`); changing its shape risks regressions. Instead add `ServiceOrderManager.ListServiceFilesAsync()` returning `List<ServiceFileInfo>` where each record carries `FilePath`, `LastWriteTimeUtc`, and the already-deserialized `ServiceOrder`. The pure projection into display rows (Date/Type/Title/Last-modified strings) is done by a new static Core helper so it is testable without touching the filesystem (Decision 6).

**Decision 6 — the testable Core helpers.** Three pieces of logic the views need, all cross-platform, go in `WorshipPlayer.Core` so `WorshipPlayer.Tests` can cover them with no WPF/`<Compile Include>` link:
- **Relative-time formatter** — `RelativeTimeFormatter.Format(DateTime whenUtc, DateTime nowUtc)` → `"just now"`, `"N minutes ago"`, `"yesterday"`, `"N days ago"`, else a US short date via `ServiceDateFormatter`. Pure, deterministic (takes `nowUtc` as a parameter so tests pin the clock).
- **US date formatter** — reuse the existing `ServiceDateFormatter.FormatUs(date, startTime)`; no new code, just referenced from the view and from the row-projection helper.
- **Service-listing projection** — `ServiceListingBuilder.Build(IEnumerable<ServiceFileInfo> files, DateTime nowUtc)` → ordered `List<ServiceListRow>` (newest/upcoming first) with `Title`, `Type`, `DateDisplay`, `LastModifiedDisplay`, `LastModifiedBy` (`"Local"`), and the backing `FilePath`. Ordering is done by sorting the `IEnumerable<ServiceFileInfo>` **before** projecting (sort keys read from `ServiceFileInfo.Order.StartTime` / `.Date` and `LastWriteTimeUtc`), so `ServiceListRow` stays purely presentational and carries no sort-only fields (see Decision 8). Also exposes `Filter(IEnumerable<ServiceListRow>, string query)` for the search box (case-insensitive contains on Title + Type). All pure.

**Decision 7 — the new-service and rename flows are their own host methods, not reuses of `NewService_Click` / `LoadAsync`.** These are the two findings the review flagged; both reuse paths have side effects that break the "views are passive renderers" invariant, so the design pins exact behavior:

*New service (from Home's `+ Add a new service` or the Services list's `+ New Service`).* `NewService_Click` cannot be reused: it only shows `ServiceInfoDialog` when `CurrentServiceOrder == null` and then **unconditionally chains `AddServiceItemDialog`** (the per-item flow this design explicitly must not auto-open). The new handler is a distinct method that **always** prompts the dialog and never opens `AddServiceItemDialog`:
```csharp
var dlg = new ServiceInfoDialog { Owner = this };
if (dlg.ShowDialog() != true || !dlg.Confirmed) return;
_serviceOrderManager.CreateNew(dlg.ServiceName, dlg.ServiceDate); // always, regardless of CurrentServiceOrder
_serviceOrderManager.CurrentServiceOrder!.StartTime = dlg.StartTime;
await SaveServiceOrderAsync();        // persists the new CurrentServiceOrder (null path → default name)
_currentServicePath = _serviceOrderManager.GetFilePathFor(_serviceOrderManager.CurrentServiceOrder!); // Decision 9
RefreshServiceOrderList();            // sidebar won't self-refresh for an empty order (see below)
await RefreshServiceListingAsync();   // re-scan; both tables rebind
// no AddServiceItemDialog; no ShowServiceEditor() — the user stays on the refreshed list
```
Documented consequence: because `CreateNew` sets `CurrentServiceOrder` (and fires `ServiceOrderLoaded`), creating a service **replaces the current in-memory order**. The sidebar does **not** refresh itself in this case: verified that `PresenterIntegration.OnServiceOrderLoaded` calls `GoToItem(0)` only when `order.Items.Count > 0`, and a brand-new service has zero items, so no `ItemChanged` fires and `OperatorWindow.OnItemChanged` (the sole reactive caller of `RefreshServiceOrderList`) never runs. We therefore call `RefreshServiceOrderList()` explicitly right after `CreateNew` — exactly as the existing `NewService_Click` already does — so the sidebar reflects the new (empty) service. `_currentServicePath` is set from the Core accessor `GetFilePathFor` (Decision 9), never re-derived in the Presenter. The user remains on the Home/Services-list screen after creation, matching the screenshots.

*Rename / edit a row.* `LoadAsync(path)` must **not** be used to read a row for the dialog — it would set `CurrentServiceOrder`, fire `ServiceOrderLoaded`, and repopulate the sidebar from the renamed file, silently hijacking whatever service is live. Chosen approach: **restrict Rename to the currently-open service only.** The row's overflow "Rename" is enabled only when its `FilePath` matches the open service's file under normalized equality — `SamePath(row.FilePath, _currentServicePath)` (Decision 10), **not** raw `==` — where `_currentServicePath` is tracked by the host (set in `OpenServiceRequested` and in create via `GetFilePathFor`). On all other rows Rename is omitted/disabled. The handler therefore operates on `CurrentServiceOrder` directly — no `LoadAsync` — and saves back to the original path via the now-parameterized wrapper (Decision 9):
```csharp
// SamePath(row.FilePath, _currentServicePath) is already guaranteed by enablement
if (!SamePath(row.FilePath, _currentServicePath) || _serviceOrderManager.CurrentServiceOrder == null) return;
var order = _serviceOrderManager.CurrentServiceOrder;
var dlg = new ServiceInfoDialog { Owner = this };
dlg.Initialize(order.Name, order.Date, order.StartTime);
if (dlg.ShowDialog() != true || !dlg.Confirmed) return;
order.Name = dlg.ServiceName; order.Date = dlg.ServiceDate; order.StartTime = dlg.StartTime; // order == CurrentServiceOrder
await SaveServiceOrderAsync(_currentServicePath); // explicit original path → in-place write, no new file (Decision 9)
RefreshServiceOrderList();                        // sidebar labels may depend on the order; cheap and consistent with create
await RefreshServiceListingAsync();
```
Rejected alternative — a side-effect-free Core `ReadAsync(path)` that renames arbitrary rows — is heavier (new Core API, must then save back to the untouched live session carefully) and is not needed for the screenshots, where editing the open service covers the use case. We keep the surface minimal.

**Decision 8 — sort `ServiceFileInfo` before projecting; `ServiceListRow` is purely presentational.** `ServiceListingBuilder.Build` sorts the incoming `IEnumerable<ServiceFileInfo>` first (keys read directly from `info.Order.StartTime`, `info.Order.Date`, and `info.LastWriteTimeUtc`), then projects the already-ordered sequence into `ServiceListRow`s. `ServiceListRow` therefore holds only display strings plus the `FilePath` identity — no `RawDate`/`StartTime` sort-only fields. This shrinks the public surface the tests pin and keeps the row model a dumb DTO, with ordering owned entirely by the builder.

**Decision 9 — the Presenter wrapper `SaveServiceOrderAsync` gains a `path` parameter; the host never re-derives filenames by hand.** Two separate problems from the review converge here, and both are solved in Core/host plumbing rather than by re-implementing private logic in the Presenter:

- *The save wrapper must accept an explicit path.* Verified in `OperatorWindow.xaml.cs`, the only save helper today is parameterless `private async Task SaveServiceOrderAsync()` → `_serviceOrderManager.SaveAsync()` (no path) → `GetDefaultFilePath` derives `{Date}_{SanitizedName}.json`. A rename that changes the name would therefore write a *new* file and orphan the old one. We **edit the existing method** (not add a second one) to take an optional path, forwarding to the Core `SaveAsync(string? = null)` that already writes exactly where told:
  ```csharp
  private async Task SaveServiceOrderAsync(string? path = null)
  {
      if (_serviceOrderManager?.CurrentServiceOrder == null) return;
      try { await _serviceOrderManager.SaveAsync(path); }   // null → default path; explicit → in-place write
      catch (Exception ex) { ShowToolError("Save Service", ex.Message); }
  }
  ```
  All existing call sites pass no argument and keep today's behavior (the default parameter is source-compatible); the rename flow passes `_currentServicePath` for an in-place overwrite.

- *The host must know the new file's path without re-deriving it.* `GetDefaultFilePath` and `SanitizeFileName` are **both `private`** on `ServiceOrderManager` (verified), so the Presenter cannot reproduce the exact filename, and re-implementing `SanitizeFileName` would be a correctness trap (it must match byte-for-byte). We add one additive public accessor to Core that exposes the manager's own derivation:
  ```csharp
  // ServiceOrderManager.cs — additive, pure, no side effects
  public string GetFilePathFor(ServiceOrder order) => GetDefaultFilePath(order);
  ```
  After a create, the host sets `_currentServicePath = _serviceOrderManager.GetFilePathFor(_serviceOrderManager.CurrentServiceOrder!);`. This is deterministic (no dependency on scan timing) and keeps the one true sanitation rule in Core.

**Decision 10 — path identity uses normalized full-path equality, not raw `==`.** Row `FilePath` comes from a filesystem scan (`FileInfo.FullName` via `ListServiceFilesAsync`), while `_currentServicePath` for a just-created service comes from `GetFilePathFor` → `Path.Combine` (Decision 9). On Windows these are normally equal, but the repo already carries `SongPathResolver` precisely because scan-vs-derived paths can differ by casing/separators, so a raw `==` is fragile for the very row a user is most likely to rename right after creating. We define one host helper and use it everywhere a path is compared to `_currentServicePath` (Rename-enablement, the Rename handler assertion, and the Delete clear):
```csharp
private static bool SamePath(string? a, string? b) =>
    a != null && b != null &&
    string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
```
Specified once here; referenced by name in item 14 rather than re-stated.

## File-by-file plan

### New — `WorshipPlayer.Core` (testable, no WPF)

1. **`WorshipPlayer.Core/Models/ServiceFileInfo.cs`** — plain record:
   `public sealed record ServiceFileInfo(string FilePath, DateTime LastWriteTimeUtc, ServiceOrder Order);`
2. **`WorshipPlayer.Core/Models/ServiceListRow.cs`** — purely presentational view-model row:
   `Title`, `Type`, `DateDisplay`, `LastModifiedDisplay`, `LastModifiedBy`, `FilePath`. **No** sort-only fields (`RawDate`/`StartTime` are not carried — ordering happens on `ServiceFileInfo` before projection, per Decision 8). No WPF, no `INotifyPropertyChanged` needed (lists are rebuilt, not mutated in place). `FilePath` is the stable row identity used for Open/Rename/Delete and survives rename (the file is written in place).
3. **`WorshipPlayer.Core/Services/RelativeTimeFormatter.cs`** — static `Format(DateTime whenUtc, DateTime nowUtc)` as above. Guards: if `whenUtc > nowUtc` (clock skew) treat as `"just now"`.
4. **`WorshipPlayer.Core/Services/ServiceListingBuilder.cs`** — static `Build(...)` and `Filter(...)` as above. `Build` **sorts the `IEnumerable<ServiceFileInfo>` first, then projects** (Decision 8). Ordering: primary key = effective date desc. Effective date = `info.Order.StartTime` if present else parsed `info.Order.Date` (via `ServiceDateFormatter.TryParseStoredDate`); unparseable dates sort last. Secondary tiebreak = `info.LastWriteTimeUtc` desc. Projection maps `Order.Name→Title`, `Order.ServiceType→Type`, `ServiceDateFormatter.FormatUs(Order.Date, Order.StartTime)→DateDisplay`, `RelativeTimeFormatter.Format(LastWriteTimeUtc, nowUtc)→LastModifiedDisplay`, `"Local"→LastModifiedBy`, and carries `FilePath`.

### Changed — `WorshipPlayer.Core/Services/ServiceOrderManager.cs`

5. Add `public async Task<List<ServiceFileInfo>> ListServiceFilesAsync()`. It mirrors the existing `LoadAllAsync` scan (`Directory.GetFiles(_serviceOrdersBasePath, "*.json")`, same `_jsonOptions`, same per-file `try/catch` that skips bad files) but captures `new FileInfo(file).LastWriteTimeUtc` and the path alongside the deserialized order. `LoadAllAsync` is left exactly as-is. This is the single reuse point for both new screens.
5b. Add `public string GetFilePathFor(ServiceOrder order) => GetDefaultFilePath(order);` — a one-line additive accessor exposing the manager's own filename derivation (Decision 9). This exists so the Presenter can learn a just-created service's path without re-implementing the private `GetDefaultFilePath`/`SanitizeFileName`. Pure, no side effects. `GetDefaultFilePath`/`SanitizeFileName` stay private and unchanged.

### New — `WorshipPlayer.Presenter/Views/Home/`

6. **`HomeView.xaml` / `HomeView.xaml.cs`** — `UserControl` that merges `OwcDesignSystem.xaml` in `UserControl.Resources`. Layout:
   - **Quick start card** (`Border` styled `OwcPanelBorder`): a `UniformGrid`/horizontal `StackPanel` of 6 square icon buttons — Tour, Services, Songs, Slides, Media, Scripture — each an `OwcButtonBase`-styled `Button` with a glyph + label. Clicks raise typed events (below); Tour is a no-op placeholder.
   - **Upcoming services card** (`OwcPanelBorder`): an `OwcDarkListView`/`GridView` with columns **Date | Type | Title** and a trailing column holding a `⋮` overflow `Button` whose `ContextMenu` has Open / Rename / Delete. Below the list, a centered accent `+ Add a new service` button (`OwcAccentButton`).
   - Public events: `TourRequested`, `ServicesRequested`, `SongsRequested`, `SlidesRequested`, `MediaRequested`, `ScriptureRequested`, `AddServiceRequested`, `OpenServiceRequested(string filePath)`, `RenameServiceRequested(string filePath)`, `DeleteServiceRequested(string filePath)`.
   - Public method `SetServices(IEnumerable<ServiceListRow>)` to (re)bind the table `ItemsSource`.
   - Note: "Slides" has no existing tab; `SlidesRequested` maps to showing the service editor (`ServicesPanel`) in the host, documented as such.

### New — `WorshipPlayer.Presenter/Views/ServiceBuilder/`

7. **`ServicesListView.xaml` / `ServicesListView.xaml.cs`** — `UserControl` merging `OwcDesignSystem.xaml`. Layout:
   - Top bar: a **Filters** `ComboBox` styled **`OwcDarkComboBox`** (minimal placeholder: "All") and a **Search** `TextBox` styled `OwcTextBox`; a right-aligned `+ New Service` `OwcAccentButton`.
   - Main: `OwcDarkListView`/`GridView`, columns **Date | Type | Title | Last modified | Last modified by** plus the trailing `⋮` overflow column (Open / Rename / Delete). Row click (ListView `SelectionChanged` or item `MouseLeftButtonUp`) raises `OpenServiceRequested`.
   - Public events: `NewServiceRequested`, `OpenServiceRequested(string)`, `RenameServiceRequested(string)`, `DeleteServiceRequested(string)`.
   - Public method `SetServices(IEnumerable<ServiceListRow>)`; the search `TextChanged` calls `ServiceListingBuilder.Filter(...)` against the last-set full list and rebinds. Keeping the unfiltered list in a private field keeps filtering pure/host-free.

### Changed — `WorshipPlayer.Presenter/Views/OperatorWindow.xaml`

8. In Row 3 / Column 1 content `Grid`, add two sibling hosts in the **same Row-3/Column-1 content Grid cell as the existing `ServicesPanel`** (which is itself a `<Grid x:Name="ServicesPanel">`, not a `Border`), toggled by `Visibility` exactly like the existing overlapping panels (`ServicesPanel`/`SongsPanel`/`ScripturePanel`/`MediaPanel`/`ErrorPanel`). The two new hosts use hardcoded inline brushes (**no `StaticResource`**):
   - `<Border x:Name="HomePanel" Background="#0F1117"><ContentPresenter x:Name="HomePanelContent"/></Border>` — `Visibility="Visible"` initially; set `ServicesPanel` to `Collapsed` initially.
   - `<Border x:Name="ServicesListPanel" Visibility="Collapsed" Background="#0F1117"><ContentPresenter x:Name="ServicesListPanelContent"/></Border>`.
   These are siblings in the shared content cell; they are plain `Border` hosts (they do not need to replicate `ServicesPanel`'s internal `ScrollViewer`+`WrapPanel` structure), and like every panel in that cell they are shown/hidden purely via `Visibility`.
9. Add the left-sidebar empty-state box: inside the sidebar `Grid` Row 1, overlay a `Border` (`x:Name="SidebarEmptyState"`) with a **dashed** `BorderBrush="#2A2E3A"` (`BorderThickness`, `StrokeDashArray` via a `Rectangle` with `StrokeDashArray` since `Border` has no dashes — use a `Rectangle` `Stroke="#2A2E3A" StrokeDashArray="4 3"` behind a `TextBlock`) showing the exact text: *"To begin, select a service from the Services list. Your service items will appear here."* Hardcoded colors (`#8B92A5` text). Visible when `ServiceOrderList` has no items; collapsed once a service is loaded (toggled in `RefreshServiceOrderList`). **Do not hardcode a pixel width on this box** — size it to its parent cell (`HorizontalAlignment="Stretch"` with margins). The sidebar column is declared `Width="200"` in XAML but `SidebarCollapse_Click` restores it to `220` (a pre-existing 200/220 drift, verified); a fixed-width empty-state box would misalign after a collapse/expand. Fixing the 200/220 drift itself is out of scope — just avoid depending on either value.

### Changed — `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`

10a. **Edit the existing `SaveServiceOrderAsync` to take an optional path (Decision 9).** Change its signature from `private async Task SaveServiceOrderAsync()` to `private async Task SaveServiceOrderAsync(string? path = null)` and forward `path` into `_serviceOrderManager.SaveAsync(path)`. The default parameter keeps all existing call sites (lines ~515, ~556, ~1051, ~1072) source-compatible and behavior-identical (they call it with no argument → `null` → default path). Only the rename flow passes an explicit path. This is an edit to one method, not a new overload.
10a-bis. **Edit the existing `LoadServiceOrderAsync` to return a success flag (Decision 3 / finding 1).** Change its signature from `private async Task LoadServiceOrderAsync(string path)` to `private async Task<bool> LoadServiceOrderAsync(string path)`: after the wrapped `ServiceOrderManager.LoadAsync` call, `return (loaded != null);` (true when a `ServiceOrder` came back, false when `LoadAsync` returned null and showed the non-fatal messagebox), and `return false;` from the `catch`. This is an edit to one method, not a new overload. The existing `LoadService_Click` caller ignores the returned value, so it stays source-compatible; the new `OpenServiceRequested` handler (item 14) branches on it so the center only switches to the editor on a successful load.
10b. **Add the `SamePath` static helper (Decision 10)** `private static bool SamePath(string? a, string? b) => a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);` — used wherever a scanned row `FilePath` is compared to the derived `_currentServicePath`.
10. Add fields `HomeView? _homeView;`, `ServicesListView? _servicesListView;`, `string? _currentServicePath;` and `private const string TabHome = "Home";` etc. **`Initialize(...)` is a synchronous `public void` (the full overload is `Initialize(PresenterEngine, OutputWindow?, ThemeManager?, DisplayManager?, Action<PresenterSettings>?)`) — you cannot `await` inside it.** So split the work: do the synchronous part inline at the end of `Initialize` — lazily create `_homeView`, wire its events, host it in `HomePanelContent`, show `HomePanel`, collapse `ServicesPanel`, and keep the existing `RefreshServiceOrderList()` call (sidebar still reflects any auto-loaded order). Kick off the async scan with **fire-and-forget that observes its own exceptions**:
    ```csharp
    // end of Initialize(...), after the synchronous view setup
    _ = RefreshServiceListingInitialAsync();
    ```
    where `private async Task RefreshServiceListingInitialAsync()` simply `try { await RefreshServiceListingAsync(); } catch (Exception ex) { ShowToolError("Services", ex.Message); }`. (A `this.Loaded += async (_, _) => await RefreshServiceListingAsync();` hook is an acceptable equivalent; the design picks fire-and-forget to avoid adding a window-level `Loaded` handler that does not exist today.) `RefreshServiceListingAsync` itself is also internally try/caught, so this is belt-and-suspenders — never an unobserved task exception, never a crash.
11. Repoint `TabServices_Click` to a new `async void ShowServicesList()` (or an `async void` click handler calling an `async Task` helper) that lazily creates `_servicesListView`, wires events, hosts it in `ServicesListPanelContent`, shows `ServicesListPanel`, hides the others, sets `TabServices` active, and `await RefreshServiceListingAsync()`. The existing `TabServices_Click` is already `void`; making the new path `async void` at the event-handler boundary is the standard WPF pattern (the awaited refresh is itself try/caught). Extend `SwitchToTab`'s "hide all panels" block to also hide `HomePanel` and `ServicesListPanel`.
12. Add `private async Task RefreshServiceListingAsync()`: `var files = await _serviceOrderManager.ListServiceFilesAsync(); var rows = ServiceListingBuilder.Build(files, DateTime.UtcNow); _homeView?.SetServices(rows); _servicesListView?.SetServices(rows);`. Null-guarded; wrapped in try/catch that routes to `ShowToolError` (non-fatal, never crashes). Called after any create/delete/rename and on initial load.
13. Add `private void ShowServiceEditor()`: hides Home/ServicesList/other panels and shows `ServicesPanel`, so opening a service drops into the slide grid. **Center-header behavior is explicit:** `CurrentItemTitle`/`CurrentItemSubtitle` are owned by `OnItemChanged` (verified: it sets `CurrentItemTitle.Text = e.NewItem?.Title` and `CurrentItemSubtitle.Text = e.NewItem?.Type.ToString()`). When a **non-empty** service is opened, the `GoToItem(0)` chain fires `OnItemChanged` and sets both before `ShowServiceEditor()` paints — nothing to do. When the service is **empty** (or none is current), there is no `OnItemChanged`, so `ShowServiceEditor()` writes the header itself from the current order: `CurrentItemTitle.Text = _serviceOrderManager.CurrentServiceOrder?.Name ?? ""` and `CurrentItemSubtitle.Text = _serviceOrderManager.CurrentServiceOrder?.ServiceType ?? ""`. This guarantees the header never shows a stale previous-service title. `ShowServiceEditor()` does not itself call `GoToItem` and does not mutate the order. **Note (intended):** for an empty service the subtitle shows the service type (`CurrentServiceOrder.ServiceType`), whereas once items exist `OnItemChanged` shows the current item's type (`e.NewItem?.Type.ToString()`). This asymmetry is deliberate and kept simple — no behavior gymnastics to force the two paths to match.
14. Event handlers wiring the two views to existing behavior (all async handlers are `async void` at the WPF boundary, delegating to try/caught async helpers):
    - `OpenServiceRequested(path)` → `if (await LoadServiceOrderAsync(path)) { _currentServicePath = path; ShowServiceEditor(); RefreshServiceOrderList(); }` — the center switches **only on success**; we do not switch if the load failed (`LoadServiceOrderAsync` has already shown the non-fatal messagebox). This requires a **second edit to an existing helper** alongside item 10a: change `LoadServiceOrderAsync` from `private async Task LoadServiceOrderAsync(string path)` to **`private async Task<bool> LoadServiceOrderAsync(string path)`** that returns `(loaded != null)` after the `LoadAsync` call and `false` from the `catch`. The existing `LoadService_Click` caller ignores the return value, so it stays source-compatible.
    - `AddServiceRequested` / `NewServiceRequested` → **a dedicated handler, not a reuse of `NewService_Click`** (that handler is gated on `CurrentServiceOrder == null` and always chains `AddServiceItemDialog`, neither of which we want). The flow, per Decision 7 (with the fixes from Decisions 9/10):
      ```csharp
      var dlg = new ServiceInfoDialog { Owner = this };
      if (dlg.ShowDialog() != true || !dlg.Confirmed) return;
      _serviceOrderManager.CreateNew(dlg.ServiceName, dlg.ServiceDate);
      _serviceOrderManager.CurrentServiceOrder!.StartTime = dlg.StartTime;
      await SaveServiceOrderAsync();     // null path → default name
      _currentServicePath = _serviceOrderManager.GetFilePathFor(_serviceOrderManager.CurrentServiceOrder!); // Decision 9 (public Core accessor)
      RefreshServiceOrderList();          // ADD — empty new order fires no ItemChanged, so the sidebar won't self-refresh
      await RefreshServiceListingAsync();
      ```
      Explicitly: **do not** open `AddServiceItemDialog`, **do not** `ShowServiceEditor()`. Creating a service **replaces the current in-memory order** (`CreateNew` sets `CurrentServiceOrder` and fires `ServiceOrderLoaded`); the user stays on the refreshed list, matching the screenshots. The explicit `RefreshServiceOrderList()` is required because `OnServiceOrderLoaded` only calls `GoToItem(0)` for a non-empty order (verified), so a brand-new empty service never triggers the reactive `OnItemChanged → RefreshServiceOrderList` path. `_currentServicePath` is taken from the Core accessor `GetFilePathFor` (Decision 9), **never** re-derived in the Presenter (`GetDefaultFilePath`/`SanitizeFileName` are private).
    - `RenameServiceRequested(path)` → **restricted to the currently-open service** (Decision 7). The view only enables "Rename" on the row where `SamePath(row.FilePath, _currentServicePath)` holds (Decision 10 normalized equality, not raw `==`); the handler asserts `SamePath(path, _currentServicePath) && _serviceOrderManager.CurrentServiceOrder != null`, then edits `CurrentServiceOrder` in place — **never calls `LoadAsync`** (which would hijack the live session and repopulate the sidebar). On confirm, mutate `Name/Date/StartTime` on `CurrentServiceOrder` and `await SaveServiceOrderAsync(path)` (the now-parameterized wrapper from Decision 9 → explicit original path → in-place overwrite, no orphan file), then `RefreshServiceOrderList()` and `await RefreshServiceListingAsync()`. Because the object saved is the already-live `CurrentServiceOrder`, nothing else in the session shifts.
    - `DeleteServiceRequested(path)` → confirm via `MessageBox` (Yes/No, default No), then `File.Delete(path)` in a try/catch (→ `ShowToolError` on failure), then `await RefreshServiceListingAsync()`. If `SamePath(path, _currentServicePath)` (Decision 10), clear `_currentServicePath`. **This is the one destructive operation; it is gated behind an explicit confirmation dialog.**
    - Quick-start tab routers: `ServicesRequested` → `ShowServicesList()`; `SongsRequested`/`ScriptureRequested`/`MediaRequested` → existing `SwitchToTab("Songs"|"Scripture"|"Media")`; `SlidesRequested` → `ShowServiceEditor()`; `TourRequested` → no-op. Note: with **no** service open, `SlidesRequested → ShowServiceEditor()` reveals `ServicesPanel`, which shows its existing slide-grid empty state (`EmptyStateText`). That is expected behavior, not a bug — called out here so it is not flagged during manual smoke.

### Changed — `WorshipPlayer.Presenter/Themes/OwcDesignSystem.xaml`

15. Add two **keyed** styles, using the **verified `*Brush` resource keys** (Decision 4): `OwcDarkListView` (TargetType `ListView`: `Background="Transparent"`, `Foreground="{StaticResource OwcTextPrimaryBrush}"`, `BorderThickness="0"`, and a keyed `ItemContainerStyle` `OwcDarkListViewItem` with a `ControlTemplate` giving a dark row bg and `Background="{StaticResource OwcSurfaceHoverBrush}"` on `IsMouseOver`/`IsSelected`) and `OwcDarkGridViewColumnHeader` (TargetType `GridViewColumnHeader`: `Background="{StaticResource OwcPanelBrush}"`, `Foreground="{StaticResource OwcTextSecondaryBrush}"`, left-aligned, thin bottom border via `OwcBorderBrush`). All keyed (never implicit); the ComboBox dark-theme definitions (`OwcDarkComboBox` et al.) are **not** modified. Since these styles live in `OwcDesignSystem.xaml` and are consumed only by the two UserControls that merge it, `OperatorWindow.xaml` stays resource-dictionary-free.

### Changed — `WorshipPlayer.Tests/WorshipPlayer.Tests.csproj`

16. **No change needed** for the Core helpers — they live in `WorshipPlayer.Core`, which Tests already references via `<ProjectReference>`. (The whole point of Decision 6 is to avoid new `<Compile Include>` links or WPF in the test project.)

### New — `WorshipPlayer.Tests`

17. **`RelativeTimeFormatterTests.cs`** — boundaries at `now`, 59s (`just now`), 1–59 min, exactly 24h / "yesterday" window, multi-day, and the clock-skew (`whenUtc > nowUtc`) guard.
18. **`ServiceListingBuilderTests.cs`** — ordering (StartTime vs Date precedence; unparseable dates sort last; LastWriteTime tiebreak), row projection (Title=Name, Type=ServiceType, DateDisplay via `ServiceDateFormatter.FormatUs`, `LastModifiedBy=="Local"`), and `Filter` (case-insensitive contains on Title+Type; empty query returns all; whitespace trimmed).

## Error handling (per fallible operation)

- **`ListServiceFilesAsync` — bad/corrupt JSON file.** Recoverable, per-file. Each file is parsed in its own `try/catch` that **skips** the file. To truly mirror `LoadAllAsync` (whose per-file handler is a **bare `catch { }`** with no logging), the new method also uses a bare `catch { }` — chosen for exact parity so the two scans behave identically; a corrupt file simply drops out of both the player and the Services list. Caller receives the successfully-parsed subset. Rationale: one hand-edited bad file must never blank the whole list.
- **`ListServiceFilesAsync` — missing `ServiceOrders` directory.** Recoverable. Mirror `LoadAllAsync`: create the directory and return an empty list → both screens render empty states, no error shown.
- **Open a service (`LoadServiceOrderAsync`) — missing/invalid file.** Recoverable, user-visible. Reuses the existing method: `LoadAsync` returns `null` → non-fatal `MessageBox` ("could not be loaded"); exceptions → non-fatal `MessageBox`. The center stays on the current screen (we only call `ShowServiceEditor()` after a non-null load). Logged via the existing `Console.WriteLine` in `LoadAsync`.
- **Create/Rename save (`SaveServiceOrderAsync`) — IO failure.** Recoverable, user-visible. The now-parameterized `SaveServiceOrderAsync(string? path = null)` (item 10a / Decision 9) wraps `SaveAsync(path)` in try/catch and routes exceptions to `ShowToolError`. After a failed save we still call `RefreshServiceListingAsync()` so the UI reflects on-disk truth.
- **Delete a service — IO failure / file locked.** Recoverable, user-visible. `File.Delete` in a `try/catch` → `ShowToolError("Delete Service", …)`. Always confirmed first via Yes/No `MessageBox` (default No). Fatal nothing.
- **`RefreshServiceListingAsync` — any unexpected exception.** Recoverable. Wrapped in try/catch → `ShowToolError`; the previously-bound rows remain. Never crashes the window.
- **Rename produces a new filename (name changed).** `ServiceOrderManager.SaveAsync()` with no path uses `GetDefaultFilePath` = `{Date}_{SanitizedName}.json`, so a rename *could* **write a new file and leave the old one**. Decision: on rename, pass the **original path** explicitly through the parameterized `SaveServiceOrderAsync(_currentServicePath)` (item 10a / Decision 9) → `SaveAsync(originalPath)` so we overwrite in place and the on-disk filename is stable (avoids orphan/duplicate rows). The old parameterless wrapper did not allow this; the overload is the mechanism that makes the "no orphan file" guarantee real rather than aspirational. The accepted consequence is that after a rename the filename no longer matches `{Date}_{SanitizedName}`. This is fine because **the Services-list row identity is `FilePath`, not the derived name** (Decision 8 / item 2): the next `RefreshServiceListingAsync` re-scans the same file and shows the new display name against the unchanged path, so the row stays stable. No normal code path re-derives the filename from a renamed service during this feature — only a fresh `CreateNew`+`SaveAsync()` (no path) derives a name-based filename, and that only happens for brand-new services, never on rename. (A hypothetical future "Save As"/export would re-derive a name-based filename; that is out of scope here and noted so it is not a surprise later.)

## Input validation (per external input)

- **Search text (`ServicesListView`).** Optional, string, no length cap enforced (UI). Trimmed; empty/whitespace → unfiltered list. Matching is case-insensitive `Contains` on Title and Type. Never throws (null → treated as empty).
- **Service name / date / time (via `ServiceInfoDialog`).** Already validated inside the dialog (name required; date required; time optional in US `hh:mm tt`). We do not re-validate; we consume `Confirmed`/`ServiceName`/`ServiceDate`/`StartTime` only when `ShowDialog()==true`.
- **File paths from table rows.** Internal (produced by our own scan), not user-typed. Still: before Open/Rename/Delete, guard `File.Exists(path)`; if gone (deleted out-of-band), show the non-fatal "could not be loaded" path and refresh the list.
- **Stored `Date` string.** Parsed tolerantly via `ServiceDateFormatter.TryParseStoredDate`; unparseable → row still shows (raw string in Date column) and sorts last. No exception.

## Invariant ownership

- **"The on-screen list reflects the `ServiceOrders` folder."** Owned by the **host** (`OperatorWindow`): every mutation (create/rename/delete) calls `RefreshServiceListingAsync()` which re-scans via Core. The views are passive renderers (`SetServices`), so they can never drift from disk on their own.
- **Row ordering + display projection.** Owned by **Core** (`ServiceListingBuilder`) so it is deterministic and unit-tested, independent of WPF sort behavior.
- **Date storage format (`yyyy-MM-dd`) and filename derivation.** Unchanged — owned by `ServiceOrder`/`ServiceOrderManager` as today. New code only reads these.
- **Dark theming of the new tables/combos.** Owned by the UserControls' merged `OwcDesignSystem.xaml` scope — never by `OperatorWindow`'s inline brushes, preserving the no-resource-dictionary invariant of `OperatorWindow.xaml`.

## Edge cases

- **No service files at all.** Both tables empty; Home still shows Quick start + the `+ Add a new service` button; sidebar shows the dashed empty-state. Startup auto-load finds nothing → Home is the landing screen (desired).
- **A service is auto-loaded on startup** (current behavior shows *TheBlessing*). Home is still shown as the center; the sidebar reflects the auto-loaded order. The user picks what they want from Home/Services, which calls `ShowServiceEditor()`. We do not change auto-load.
- **File without `startTime` (date-only).** `ServiceDateFormatter.FormatUs` renders `MM/dd/yyyy`; ordering uses the parsed `Date`. Verified against the sample files (`2026-08-03_Sunday Morning.json`, `TheBlessing Service.json`, `Sunday Morning Demo.json`).
- **Duplicate titles / same date.** Allowed; `FilePath` is the row identity for Open/Rename/Delete, so duplicates are unambiguous.
- **Rename to a name with invalid filename chars.** `SaveAsync(originalPath)` writes in place, so the stored filename never changes on rename — invalid chars in the display name are fine.
- **Very long title/type.** `GridViewColumn` truncates with `TextTrimming` on the cell `TextBlock`.
- **Clock skew (`LastWriteTime` in the future).** `RelativeTimeFormatter` returns "just now".

## Testability

- **Unit-testable (net8.0, no WPF):** `RelativeTimeFormatter`, `ServiceListingBuilder` (Build + Filter), and `ServiceOrderManager.ListServiceFilesAsync` (via a temp folder, exactly like `ServiceOrderPersistenceTests` — write JSON files, assert rows/order/skip-on-corrupt). These cover all the non-trivial logic.
- **Integration/manual (WPF, Windows):** the two UserControls' rendering, dark theming (no white-on-white), tab repointing, row-click→editor navigation, overflow menus, and the delete confirmation. These require the WPF app and are verified by build + manual smoke on Windows; they are intentionally kept thin (events + `SetServices`) so almost no logic lives in code-behind.
- A design note: because the views are passive (`SetServices` in, events out) and all decisions live in Core, the hard-to-test WPF surface is minimized — this is the signal that the split is right.

## Forbidden / untouched areas

- **No `{StaticResource …}` references added to `OperatorWindow.xaml`**; its new hosts use hardcoded inline brushes only.
- **No implicit/keyless styles** anywhere; the two new `OwcDesignSystem.xaml` styles are keyed.
- **The ComboBox dark-theme definitions** (`OwcDarkComboBox`, `OwcDarkComboBoxItem`, `OwcComboBoxToggleButton`) are **not modified**; the Filters combo *consumes* `OwcDarkComboBox`.
- **Not touched:** chord rendering, `LoadStandaloneSong`, F2/F3 `LoadSong`, the sidebar item-click (`ServiceOrderList_SelectionChanged` → `GoToItem`) behavior, `LoadAllAsync`, and the existing per-item dialogs' internals.
- **Backward compatible:** existing service files load unchanged; no JSON schema or storage-format change; `ServiceOrderManager` gains one additive method only.
- **Behavior-preserving** except the new Home landing screen, the Services-tab repointing to the list, and the add/open/rename/delete wiring described above.

---

## Responses to design review (revision 2)

All eight findings from `design-review.md` / `design-review.json` were verified against source and resolved. Verdict was CHANGES_REQUESTED (1 HIGH, 4 MEDIUM, 3 NIT).

1. **HIGH — `await` inside synchronous `void Initialize(...)`.** *Addressed.* Confirmed `Initialize(PresenterEngine, OutputWindow?, ThemeManager?, DisplayManager?, Action<PresenterSettings>?)` is a synchronous `public void` with no `Loaded` handler. Item 10 now keeps all synchronous view setup inline and kicks off the scan via fire-and-forget `_ = RefreshServiceListingInitialAsync();` (an `async Task` wrapper with its own try/catch → `ShowToolError`). Item 11 likewise makes the Services-list path `async void` at the handler boundary. Noted `Loaded +=` as an acceptable equivalent and why fire-and-forget was chosen.

2. **MEDIUM — wrong brush resource keys.** *Addressed.* Verified against `OwcDesignSystem.xaml` that all brushes are `*Brush`-suffixed (`OwcTextPrimaryBrush`, `OwcTextSecondaryBrush`, `OwcPanelBrush`, `OwcSurfaceBrush`, `OwcSurfaceHoverBrush`, `OwcBorderBrush`, …) and that `OwcTextPrimary`/`OwcPanel`/`OwcSurfaceHover` do **not** exist. Decision 4 now carries an explicit "verified theme resource keys" paragraph, and item 15 spells out each `{StaticResource …Brush}` by its correct name, calling out that a wrong key throws at load.

3. **MEDIUM — "reuse `NewService_Click`" misleading.** *Addressed.* Verified `NewService_Click` is gated on `CurrentServiceOrder == null` and unconditionally chains `AddServiceItemDialog`. New Decision 7 and item 14 specify a dedicated handler that always prompts `ServiceInfoDialog`, `CreateNew` + set `StartTime` + `SaveServiceOrderAsync()` + `RefreshServiceListingAsync()`, never opens `AddServiceItemDialog`, never `ShowServiceEditor()`, and documents that create replaces the in-memory order and leaves the user on the list.

4. **MEDIUM — Rename hijacks the live service via `LoadAsync`.** *Addressed.* Verified `LoadAsync` sets `CurrentServiceOrder`, resets `CurrentItemIndex`, and fires `ServiceOrderLoaded` (which `PresenterIntegration` turns into `GoToItem(0)` + sidebar refresh). Decision 7 restricts Rename to the **currently-open** service: the view enables Rename only on the row matching `_currentServicePath`, and the handler edits `CurrentServiceOrder` directly (no `LoadAsync`), saving with `SaveServiceOrderAsync(originalPath)`. The rejected `ReadAsync` alternative is documented.

5. **MEDIUM — stale filename vs display after rename.** *Addressed.* The rename error-handling bullet now states that row identity is `FilePath` (not the derived name), that the next scan re-reads the same file with the new display name, and that no normal code path re-derives the filename from a renamed service (only brand-new `CreateNew`+`SaveAsync()` derives a name-based filename). A possible future "Save As" is flagged as out of scope.

6. **NIT — stale "line ~263".** *Addressed.* The "Existing structure" section now refers to `LoadAllAsync` by method name only; the line number is removed.

7. **NIT — "no logging beyond `Debug.WriteLine`".** *Addressed.* Verified the existing per-file handler is a bare `catch { }`. The error-handling bullet now states `ListServiceFilesAsync` uses a bare `catch { }` for exact parity, with no claim of `Debug.WriteLine`.

8. **NIT — `ServiceListRow` carries sort inputs.** *Addressed (adopted the suggestion).* New Decision 8: `Build` sorts `IEnumerable<ServiceFileInfo>` before projecting; `ServiceListRow` drops `RawDate`/`StartTime` and is purely presentational (display strings + `FilePath`). Items 2 and 4 updated to match.

## Responses to design review (latest round — host-integration findings)

The most recent `design-review.md` / `design-review.json` (verdict CHANGES_REQUESTED: 1 HIGH, 3 MEDIUM, 3 NIT) focused on host-integration flows. Each finding was re-verified against the actual source before resolving.

1. **HIGH — `SaveServiceOrderAsync(path)` overload does not exist.** *Addressed.* Verified in `OperatorWindow.xaml.cs`: the only save helper is parameterless `private async Task SaveServiceOrderAsync()` → `_serviceOrderManager.SaveAsync()` (no path), and no `(string)` overload exists; `ServiceOrderManager.SaveAsync(string? = null)` already honors an explicit path. New **Decision 9** and new **item 10a** edit the existing wrapper to `SaveServiceOrderAsync(string? path = null)` forwarding `path` to Core, keeping all current no-arg call sites source-compatible. The Rename and Save-related error-handling bullets now reference this parameterized wrapper as the mechanism that makes the "no orphan file" guarantee real.

2. **MEDIUM — create flow leaves the sidebar stale; `ServiceOrderLoaded` does not refresh for an empty service.** *Addressed.* Verified `PresenterIntegration.OnServiceOrderLoaded` calls `GoToItem(0)` only when `order.Items.Count > 0`, so a brand-new (zero-item) service never fires `ItemChanged` and never reaches `OperatorWindow.OnItemChanged` (the sole reactive caller of `RefreshServiceOrderList`). Decision 7 and item 14 now add an explicit `RefreshServiceOrderList();` right after `CreateNew` — exactly as the existing `NewService_Click` already does — and the "documented consequence" prose is corrected to say the sidebar is refreshed explicitly, not reactively.

3. **MEDIUM — `_currentServicePath` for a new service was unspecified/invalid.** *Addressed.* Verified `GetDefaultFilePath` and `SanitizeFileName` are both `private` on `ServiceOrderManager`, so the Presenter cannot reproduce the filename. New **item 5b** adds a one-line additive public accessor `GetFilePathFor(ServiceOrder) => GetDefaultFilePath(order)`; the create flow now sets `_currentServicePath = _serviceOrderManager.GetFilePathFor(_serviceOrderManager.CurrentServiceOrder!)`. No sanitation logic is re-implemented in the Presenter (adopted option (a) from the review).

4. **MEDIUM — raw `==` path equality is fragile (scan vs. derived).** *Addressed.* New **Decision 10** defines one host helper `SamePath(a, b)` using `string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)` (item 10b), and item 14 uses it in all three places the review named: Rename-enablement, the Rename-handler assertion, and the Delete `_currentServicePath` clear. The repo's `SongPathResolver` precedent for scan-vs-derived mismatch is cited as the motivation.

5. **NIT — `ShowServiceEditor()` header behavior underspecified.** *Addressed.* Item 13 now states exactly what the header shows: for a non-empty service the `GoToItem(0)` chain has already set `CurrentItemTitle`/`CurrentItemSubtitle` via `OnItemChanged`; for an empty/none-current service `ShowServiceEditor()` writes them itself from `CurrentServiceOrder.Name`/`.ServiceType` (falling back to empty), so no stale previous-service title can show.

6. **NIT — sidebar empty-state must not hardcode a width (200 vs 220 drift).** *Addressed.* Item 9 now specifies `HorizontalAlignment="Stretch"` with margins (sized to the parent cell, not a fixed pixel width), notes the verified 200/220 drift between the XAML declaration and `SidebarCollapse_Click`, and flags that fixing the drift itself is out of scope.

7. **NIT — "Slides" quick-start with no service open falls back to the existing empty state.** *Addressed.* Item 14's quick-start router note now documents that `SlidesRequested → ShowServiceEditor()` with no open service reveals `ServicesPanel`'s existing slide-grid empty state (`EmptyStateText`), and that this is expected behavior, not a bug to flag during manual smoke.

The remaining "unverified" item from the review — `RelativeTimeFormatter` bucket boundaries — is pinned by the test names in item 17 (59s → "just now", 1–59 min, the 24h "yesterday" window, multi-day → "N days ago", clock-skew guard); those boundaries are the authority the implementer codes to.

## Final design-review resolutions

These are the three (and only three) open items from the latest `design-review.json`. All are now baked into the design above; this section records the committed resolution for each so the design is final and self-consistent.

1. **(MEDIUM) `LoadServiceOrderAsync` returns no success signal.** *Resolved.* `LoadServiceOrderAsync` is changed from `private async Task` to **`private async Task<bool>`**, returning `(loaded != null)` after the `LoadAsync` call and `false` in the `catch`. The `OpenServiceRequested` handler now does `if (await LoadServiceOrderAsync(path)) { _currentServicePath = path; ShowServiceEditor(); RefreshServiceOrderList(); }`, so the center view switches to the editor **only on a successful load**. This is documented as a second edit to an existing helper alongside item 10a (see new **item 10a-bis**), and the existing `LoadService_Click` caller ignores the return value, so it stays source-compatible. Updated in place in **Decision 3** and **item 14**.

2. **(NIT) Empty-service header subtitle.** *Resolved.* It is intentional that the subtitle shows the service type (`CurrentServiceOrder.ServiceType`) for an empty service, versus the item's type (`e.NewItem?.Type.ToString()`) once items exist. This asymmetry is kept consistent and sane with no behavior gymnastics. A one-line note documenting the intent was added to **item 13**.

3. **(NIT) New hosts "mirror ServicesPanel structure exactly".** *Resolved.* `ServicesPanel` is a `Grid` (not a `Border`). **Item 8** was reworded: the two new hosts (`HomePanel`, `ServicesListPanel`) are sibling `Visibility`-toggled elements in the **same Row-3/Column-1 content Grid cell** as `ServicesPanel`, added as siblings toggled by `Visibility` like the other panels. The "mirror ServicesPanel structure exactly" wording was dropped.
