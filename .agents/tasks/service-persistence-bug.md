# Service Order Persistence Bug — Investigation Report

## Summary (the answer)

Added service items **are** correctly inserted into the shared in-memory
`ServiceOrderManager.CurrentServiceOrder.Items`, which is why they appear in the
SERVICE ORDER sidebar during the current session. **They vanish on restart
because nothing ever persists them to disk.** `ServiceOrderManager.SaveAsync(...)`
exists and works, but a solution-wide search shows it is **never called from any
code path** after an Add (or anywhere else). On the next launch,
`AutoLoadMostRecentServiceOrderAsync` reads the most-recent `ServiceOrders/*.json`
file from the *previous* save — which never happened for a freshly-added item, so
either no file exists or an older file without the new item is loaded. That is the
exact data-loss mechanism.

Separately, there is **no edit capability at all**: the SERVICE ORDER sidebar is a
read-only `ItemsControl` bound to plain strings, with no selection, double-click,
rename, reorder, or delete UI. `AddServiceItemDialog` is add-only; it is not an
editor.

Root cause in one line: **`ServiceOrderManager.AddItem` mutates memory only, and no
one calls `SaveAsync`.**

---

## Evidence (file:line citations)

### 1. The "+ Add Item" flow adds to memory but never saves

- Button: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml:231` — `+ Add Item`
  `Button` wired to `Click="NewService_Click"`.
- Handler: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs:487` `NewService_Click`
  creates `AddServiceItemDialog`, passes the shared manager
  (`dialog.ServiceOrderManager = _serviceOrderManager;`, line ~494), and on
  `ItemAdded` only calls `RefreshServiceOrderList()` (line ~497). **No save.**
- Dialog: `WorshipPlayer.Presenter/Views/ServiceBuilder/AddServiceItemDialog.xaml.cs:41`
  `AddItem_Click`:
  - If `ServiceOrderManager.CurrentServiceOrder == null` it calls
    `CreateNew("New Service", today)` (line ~62) — good, it creates one.
  - Builds a `ServiceItem` and calls `ServiceOrderManager.AddItem(serviceItem)`
    then raises `ItemAdded` (lines ~79–81). **No `SaveAsync` call anywhere in the
    dialog.**
- In-memory add: `WorshipPlayer.Core/Services/ServiceOrderManager.cs:112` `AddItem`
  appends to `CurrentServiceOrder.Items` and (if first item) fires `ItemChanged`.
  Purely in-memory; no disk write.

So the item **is** inserted into the real `CurrentServiceOrder.Items` (not a throwaway
local list). The sidebar reflects it because
`PresenterEngine.CurrentServiceOrder => _serviceOrderManager.CurrentServiceOrder`
(`WorshipPlayer.Presenter/Services/PresenterEngine.cs:59`) is the **same object** that
`RefreshServiceOrderList` reads (`OperatorWindow.xaml.cs:874`, `_engine?.CurrentServiceOrder`).

### 2. `SaveAsync` is never called on the service manager

Solution-wide grep for `SaveAsync`:
- `ServiceOrderManager.SaveAsync` is **defined** at
  `WorshipPlayer.Core/Services/ServiceOrderManager.cs:76` but has **zero call sites**.
- The only `SaveAsync` calls in the solution are unrelated:
  `SettingsService.SaveAsync` (MainWindow) and `SongUsageService.SaveAsync`.

Owner of the instance: `WorshipPlayer.UI/Services/PresenterIntegration.cs` constructor
creates `ServiceOrderManager = new ServiceOrderManager(serviceOrdersPath)` where
`serviceOrdersPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServiceOrders")`
(PresenterIntegration.cs ~ lines 78–84). The same instance is handed to
`OperatorWindow.SetServices(...)` (PresenterIntegration.cs, `OpenPresenterWindow`), so the
operator window and dialog share the one manager rooted at `.../ServiceOrders`.

### 3. Default save path logic (no `CurrentFilePath` concept)

`SaveAsync(string? filePath = null)` (ServiceOrderManager.cs:76):
- Returns early if `CurrentServiceOrder == null`.
- `filePath ??= GetDefaultFilePath(CurrentServiceOrder)`.
- `GetDefaultFilePath` builds `"{Date}_{SanitizeFileName(Name)}.json"` under
  `_serviceOrdersBasePath` (ServiceOrderManager.cs, `GetDefaultFilePath`).
- Creates the directory if missing, sets `UpdatedAt`, serializes with indented JSON +
  `JsonStringEnumConverter`, writes the file.

Grep confirms **no `CurrentFilePath` / `_currentFilePath`** anywhere — the manager always
*derives* the path from `Date` + `Name`. Consequence: a plain `SaveAsync()` after Add is
safe and will write/overwrite `ServiceOrders/{Date}_{Name}.json`. For a dialog-created
order this is `ServiceOrders/{yyyy-MM-dd}_New Service.json`.

### 4. Startup reload confirms the loss mechanism

`PresenterIntegration.AutoLoadMostRecentServiceOrderAsync` picks the newest
`ServiceOrders/*.json` by `LastWriteTimeUtc` and calls `ServiceOrderManager.LoadAsync`
(PresenterIntegration.cs, `AutoLoadMostRecentServiceOrderAsync`). `LoadAsync`
(ServiceOrderManager.cs:48) deserializes the file into `CurrentServiceOrder`, sets index
0, and fires `ServiceOrderLoaded`; `OnServiceOrderLoaded` then `GoToItem(0)`.

Because the Add never wrote a file, the newest file on disk is from an earlier explicit
load/save (or none). The reload therefore shows the old content without the just-added
items — exactly the user's "I added, closed, reopened, everything's gone" report.

### 5. No edit path exists

- Sidebar: `OperatorWindow.xaml:219` `ServiceOrderList` is an `ItemsControl` whose
  `ItemTemplate` is a non-interactive `TextBlock Text="{Binding}"` (XAML ~221–226).
- It is bound to `_serviceOrderItems`, an
  `ObservableCollection<string>` (OperatorWindow.xaml.cs:795) filled with
  `"{icon} {item.Title}"` strings (`RefreshServiceOrderList`, OperatorWindow.xaml.cs:868).
- There is **no** `SelectionChanged`, `MouseDoubleClick`, rename, reorder, or delete
  handler on `ServiceOrderList`. The only buttons under it are `+ Add Item` and
  `Load Service` (OperatorWindow.xaml ~228–234).
- `AddServiceItemDialog` is **add-only** — no constructor or property to load an existing
  `ServiceItem` for editing. The manager has `RemoveItem` and `MoveItem`
  (ServiceOrderManager.cs:135, :155) but **no UI invokes them**.

### 6. CurrentServiceOrder lifecycle / no-service case

- Loading only a song via the Songs tab does **not** create a `ServiceOrder`;
  `CurrentServiceOrder` stays `null` until a service is loaded or `CreateNew` is called.
- The dialog already handles the null case defensively: `AddItem_Click` calls
  `CreateNew("New Service", today)` before `AddItem` (AddServiceItemDialog.xaml.cs ~60–63).
  So "+ Add Item with nothing loaded" already produces a valid in-memory order — it just
  never gets saved.

---

## Conclusions

1. **Persistence is the only real bug.** The data model, `AddItem`, `CreateNew`,
   `SaveAsync`, `LoadAsync`, and startup auto-load are all correct and wired — the single
   missing link is a `SaveAsync` call after every mutation.
2. **Edit/delete/reorder is a missing feature**, not a bug. Core methods exist
   (`RemoveItem`, `MoveItem`) but there is no UI and no editor dialog.
3. The save path auto-derives from `Date` + `Name`, so calling `SaveAsync()` with no
   argument is sufficient and idempotent for a given order.

---

## Minimal fix design (concrete)

### A. Add + Save (the core bug)
Make the add flow persist. Simplest, lowest-risk: after a successful add, call
`SaveAsync()`.
- Preferred location — `AddServiceItemDialog.AddItem_Click`
  (`AddServiceItemDialog.xaml.cs:41`): after `ServiceOrderManager.AddItem(serviceItem);`
  add `await ServiceOrderManager.SaveAsync();` (make `AddItem_Click` `async void`, or
  fire-and-forget with error handling). This guarantees save regardless of caller.
- Alternative/defense-in-depth — in `NewService_Click`'s `ItemAdded` lambda
  (`OperatorWindow.xaml.cs:496`): after `RefreshServiceOrderList()` add
  `_ = _serviceOrderManager?.SaveAsync();` wrapped in try/catch → `ShowToolError`.

Recommendation: put the authoritative save in the dialog (A) so every add is covered,
and keep the operator-side refresh as-is.

### B. Persist helper (so edit/delete reuse one path)
Add a tiny private helper in `OperatorWindow` and call it after any service mutation:

```csharp
private async Task SaveServiceOrderAsync()
{
    if (_serviceOrderManager?.CurrentServiceOrder == null) return;
    try { await _serviceOrderManager.SaveAsync(); }
    catch (Exception ex) { ShowToolError("Save Service", ex.Message); }
}
```

### C. Edit an item (new, small UI)
1. Make the sidebar selectable: switch `ServiceOrderList` from `ItemsControl` to a
   `ListBox` (or add a per-row context menu) in `OperatorWindow.xaml:219`, and bind to the
   `ServiceItem` objects (or a lightweight view-model) instead of plain strings so the
   selected index maps back to `CurrentServiceOrder.Items[i]`.
2. Add a double-click / "Edit" handler that opens `AddServiceItemDialog` in edit mode.
   Extend the dialog with an optional `ServiceItem? ItemToEdit` property: when set,
   pre-populate the combo boxes and, on confirm, mutate the existing item's `Type`/`Title`
   instead of calling `AddItem`. Raise an `ItemEdited` event.
3. After edit confirm, call `SaveServiceOrderAsync()` (helper B) and
   `RefreshServiceOrderList()`.

### D. Delete + Save
Add a "Remove" button/context-menu item on the selected row → call
`_serviceOrderManager.RemoveItem(selectedIndex)` (already exists,
ServiceOrderManager.cs:135) → `SaveServiceOrderAsync()` → `RefreshServiceOrderList()`.
(Optionally expose `MoveItem` for reorder the same way.)

### E. Startup reload
No change needed. `AutoLoadMostRecentServiceOrderAsync` + `LoadAsync` + `OnServiceOrderLoaded`
already reload the newest file and show item 0. Once A/B/C/D write to disk, the reload will
show the user's additions. (Minor nicety: since the default filename is derived from
`Date`+`Name`, repeated edits of the same order overwrite the same file — correct behavior.)

### F. No-current-service-order case
Already handled by `CreateNew` in `AddItem_Click`. With fix A, that new order is saved to
`ServiceOrders/{yyyy-MM-dd}_New Service.json` on the first add, so it survives restart.
Optional polish: let the user name/date the new service (today it's always
"New Service") to avoid all fresh orders colliding on the same filename.

### Methods/handlers to change (checklist)
- `AddServiceItemDialog.AddItem_Click` — add `await SaveAsync()` after `AddItem` (fix A).
- `OperatorWindow` — add `SaveServiceOrderAsync` helper (B); add Edit and Delete handlers
  (C, D); change `ServiceOrderList` to a selectable control and bind to items (C step 1).
- `AddServiceItemDialog` — add optional `ItemToEdit` + `ItemEdited` for edit mode (C step 2).
- No changes required to `ServiceOrderManager` (SaveAsync/RemoveItem/MoveItem already exist)
  or to the startup auto-load path.
