# Service order items now persist, with add/edit/delete UI

The change closes the data-loss bug the user reported ("tôi thêm rồi tắt mở lại thì mất hết"): added service items lived only in `ServiceOrderManager.CurrentServiceOrder.Items` and were never written to disk because `SaveAsync()` had zero call sites. The dialog now awaits `SaveAsync()` immediately after `AddItem`, and the operator window routes every edit and delete through a single `SaveServiceOrderAsync()` helper. On top of the fix it adds the missing edit/delete feature: the read-only `ItemsControl` of plain strings becomes a selectable `ListBox` bound to a display wrapper that carries a reference back to the underlying `ServiceItem`, with double-click, Edit/Remove buttons, and a right-click context menu. The dialog gains an `ItemToEdit` + `ItemEdited` edit mode that mutates an existing item in place. Scope stayed tight — only the two Presenter views, the dialog, and a new test file changed.

Watch for: the edit flow mutates **only `Type`, never `Title`** (confirmed) — the sidebar displays `Title`, and `Title` is auto-generated as `"{Type} Item"` on add, so editing a Song to Scripture leaves the row reading "Song Item" with a scripture icon until the next add. Behavioral gap, not data loss.

**Verdict**: APPROVED

## High-level view

The core fix is a persistence call in the right place. `AddItem_Click` became `async void` and awaits `ServiceOrderManager.SaveAsync()` right after `AddItem`, so a freshly added item is on disk before the dialog closes. The operator window centralizes every other save behind `SaveServiceOrderAsync()`, which no-ops when nothing is loaded and funnels failures through `ShowToolError`. The startup auto-load path was correctly left alone.

The sidebar moved from a string-only `ItemsControl` to a `ListBox` whose items are a small `ServiceItemDisplay` wrapper (icon, title, and a reference to the real `ServiceItem`). Selection index and `SelectedItem` both map back to the live model, so edit and delete act on the right object. Edit and delete each save then refresh.

The XAML regression that the task flagged as blocking does not reappear. The `ListBox` uses the keyed `OwcServiceListBox` style only; there is exactly one `TargetType="ListBox"` style in the design system and it is keyed, sets `BorderThickness=0`, and never assigns a `BorderBrush` that could resolve to `UnsetValue`.

The known gap is edit completeness: the dialog has no title field, so edit mode changes only the item type. Combined with the auto-derived title, a type edit is visually inconsistent in the sidebar even though it persists correctly.

<details>
<summary>Issues (2)</summary>

1. **Edit changes type but not title** — `AddItem_Click`'s edit branch sets `ItemToEdit.Type` only; `Title` was auto-set to `"{Type} Item"` at add time and is never updated, so a Song→Scripture edit still shows "Song Item" in the sidebar. Either update `Title` to match the new type in the edit branch, or add a title field to the dialog. Non-blocking.
2. **Edit test doesn't cover the UI's actual mutation** — `EditItem_ThenSave_SurvivesRestart` sets `Title` and `Type` directly on the model, which the UI edit path never does for `Title`. The round-trip is proven, but the test gives false confidence that the edit flow persists a title change. Consider aligning the test with what the dialog actually mutates, or add the title field so they match.

</details>

<details>
<summary>Details</summary>

### The persistence fix: save on the add path

`AddItem_Click` now awaits `ServiceOrderManager.SaveAsync()` immediately after `AddItem(serviceItem)`, inside a try/catch that surfaces failures via a `MessageBox` rather than crashing the dialog. `DialogResult`/`Close()` run after the await, so the window stays open until the write completes. This is the exact minimal fix the investigation prescribed (section A), placed in the dialog so every add is covered regardless of caller. The `CreateNew("New Service", today)`-when-null defense is preserved, so an add with no loaded service still produces and persists a `{Date}_New Service.json`.

### One save path for edit and delete

`SaveServiceOrderAsync()` is the single reuse point (section B): it returns early when no service order is loaded and wraps `SaveAsync()` in try/catch → `ShowToolError("Save Service", …)`. Delete calls `RemoveItem(index)` then `SaveServiceOrderAsync()` then `RefreshServiceOrderList()`; edit saves on the `ItemEdited` callback then refreshes. The edit branch in the dialog deliberately does **not** call `SaveAsync` itself, leaving the operator window as the sole owner of the edit save — a clean single-writer design with no double-save.

### Sidebar selection maps back to the live model

```
ListBox (OwcServiceListBox, keyed)
  └─ item: ServiceItemDisplay { Icon, Title, Item ──► ServiceItem }
          SelectedItem ─────────────────────────────┘
          SelectedIndex ──► CurrentServiceOrder.Items[index]  (delete path)
```

Edit resolves the target via `SelectedItem is ServiceItemDisplay display` and passes `display.Item` as `ItemToEdit`, so it edits the exact object rather than relying on index. Delete uses `SelectedIndex` with a bounds check against `Items.Count` before `RemoveItem`. Both are guarded and no-op on no selection. The display wrapper replaced the old `ObservableCollection<string>`, and the icon `switch` still produces the per-type glyph into `ServiceItemDisplay.Icon`.

### Edit mode mutates type only

The dialog's edit mode (section C step 2) pre-populates the type combo from `ItemToEdit.Type` via `ItemTypeToText` + `SelectComboBoxItem` on `Loaded`, relabels the window to "Edit Service Item" and the button to "Save". On confirm it sets `ItemToEdit.Type = itemType` and raises `ItemEdited`.

The gap: the dialog exposes no title field, and on the add path `Title` is hard-coded to `"{SelectedItemType} Item"`. Edit never touches `Title`. So editing a "Song Item" to type Scripture persists `Type=Scripture` but leaves `Title="Song Item"`, and since the sidebar binds to `Title`, the row still reads "Song Item" (with a scripture icon). The change survives restart — it's just not what a user expects "edit" to do. The plan permitted a type-only edit, so this is within scope, but it's the most visible rough edge and worth a follow-up: update `Title` alongside `Type` in the edit branch, or give the dialog a real title field.

The `TextToItemType` map was extended to include `Blank` and `Custom`, matching the combo box's actual entries. This resolves the pre-existing mismatch the plan called out (combo listed Blank/Custom while the old switch mapped Offering/Prayer) in exactly the narrow way the edit feature needs — reading a type back by its enum now round-trips through the combo. No unrelated refactor beyond that.

### XAML crash regression — clear

The task gated this as blocking. The new `ListBox` applies `Style="{StaticResource OwcServiceListBox}"`, which is keyed. A workspace search for `TargetType="ListBox"` returns exactly one style and it is this keyed one — no implicit/keyless `ListBox` style exists to re-trigger the `(DependencyProperty.UnsetValue) is not a valid value for property 'BorderBrush'` crash. The style sets `BorderThickness=0` and sets no `BorderBrush` at all, so there is no path to an unset brush. The inline buttons set `BorderBrush="Transparent"` explicitly. The verification note's claim that the launched exe stayed alive and responsive and closed gracefully is consistent with this static reading.

### Forbidden areas untouched

The commit's file list is the two Presenter views, the dialog pair, and the new test file (plus the three task docs). Chord rendering, the F2/F3 `LoadSong` paths, the Songs-tab `LoadStandaloneSong`, and the ComboBox theme are not in the diff — the `ChordProParserService` and `LoadStandaloneSong` references that appear in the file are pre-existing context lines outside the changed hunks. No unrelated refactor bundled in.

### Tests

Three round-trip tests in `ServiceOrderPersistenceTests.cs`, each with its own temp `ServiceOrders` dir cleaned up in `Dispose`, deriving the saved file by listing `*.json` (asserting exactly one) rather than hard-coding the name — matching the project's `IDisposable` + temp-dir convention. `AddItem_ThenSave_SurvivesRestart` and `RemoveItem_ThenSave_SurvivesRestart` exercise exactly the Core contract the add and delete UI paths depend on, with a fresh manager simulating restart — this proves the data-loss fix at the Core level.

Not tested: `EditItem_ThenSave_SurvivesRestart` mutates `Title` and `Type` directly on the model, but the UI edit path only mutates `Type`. The test passes and the round-trip is real, but it doesn't mirror what the dialog actually does, so it masks the title gap above rather than exposing it. The UI edit/delete handlers and the dialog's edit mode are not unit-tested (the Presenter project is net8.0-windows and can't be referenced from the net8.0 test project) — this is an accepted structural limit, covered only by the manual runtime check in the verification note.

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` — sidebar `ItemsControl`→`ListBox` (keyed style, double-click, context menu); Edit/Remove buttons added.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `ServiceItemDisplay` wrapper; `SaveServiceOrderAsync` helper; edit/delete handlers; refresh populates wrappers.
- `WorshipPlayer.Presenter/Views/ServiceBuilder/AddServiceItemDialog.xaml` — named the confirm button for relabeling.
- `WorshipPlayer.Presenter/Views/ServiceBuilder/AddServiceItemDialog.xaml.cs` — `async` add path with `SaveAsync`; `ItemToEdit`/`ItemEdited` edit mode; type↔text maps.
- `WorshipPlayer.Tests/ServiceOrderPersistenceTests.cs` — three add/remove/edit persistence round-trip tests.

Full diff: `git show fa5038d`.

</details>
