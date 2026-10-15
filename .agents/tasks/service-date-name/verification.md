# Verification — Service name + Service date (US format)

Branch: `main`. Task: add a user-chosen **Service name** and a **Service date** with optional time in US format (`MM/DD/YYYY hh:mm tt`, e.g. `10/18/2026 08:00 AM`) to the service-creation flow. Not pushed.

## Files changed
- `WorshipPlayer.Core/Models/ServiceOrder.cs` — added optional `DateTime? StartTime` (`[JsonPropertyName("startTime")]`). `Date` stays a `yyyy-MM-dd` string; nullable + `WhenWritingNull` keeps date-only files backward compatible.
- `WorshipPlayer.Core/Services/ServiceDateFormatter.cs` — NEW. Culture-independent (en-US) US parse/format helpers: `FormatUs`, `TryParseUs`, `TryParseTime`, `FormatTime`, `TryParseStoredDate`.
- `WorshipPlayer.Presenter/Views/ServiceBuilder/ServiceInfoDialog.xaml` / `.xaml.cs` — NEW dark dialog that MERGES `OwcDesignSystem.xaml`. Name TextBox + a single "Service date" row (DatePicker forced to en-US `Short`/`MM/DD/YYYY` + optional `hh:mm AM/PM` time TextBox). Exposes `ServiceName`, `ServiceDate` (`yyyy-MM-dd`), `StartTime`, `Confirmed`; `Initialize(...)` pre-fills for edit mode.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` — added one "Edit Service" sidebar button using the **inline** sibling style (no `{StaticResource}`).
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `NewService_Click` now shows `ServiceInfoDialog` when no service is loaded, then `CreateNew(name, date)` + sets `StartTime` + `SaveServiceOrderAsync()`. Added `EditService_Click` for the existing-service path. Code-behind only; **no** StaticResource added to `OperatorWindow.xaml`.
- `WorshipPlayer.Tests/ServiceDateNameTests.cs` — NEW cross-platform (net8.0, Core-only) tests.

## Build
`dotnet build WorshipBacktrackPlayer.sln --nologo` → **0 Errors**. Only pre-existing warnings remain (CS0414 `MediaLibraryPanel._currentView`, CS8604 `ChordChartWindow.BuildChordLine`, CS0414 `WaveformEditorWindow._inReviewMode`). No new warnings introduced.

## Tests
`dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug` → **Passed: 460, Failed: 0** (was 450; +10 new in `ServiceDateNameTests`):
- `FormatUs` with time → `10/18/2026 08:00 AM`; without time → `10/18/2026`.
- `TryParseUs("10/18/2026 08:00 AM")` → date `2026-10-18`, `StartTime` 08:00; date-only parse → null time; PM round-trip; garbage → false.
- Legacy date-only JSON (no `startTime`) deserializes with `StartTime == null`.
- Name+date+time flow through `CreateNew` + `SaveAsync` + `LoadAsync` (restart) persists all three.
- Date-only service does NOT write a `startTime` key and reloads with null time.
- US formatting/parsing stays `MM/DD/YYYY` + `AM/PM` under a temporary `vi-VN` thread culture.

## CRITICAL runtime launch verification
An STA harness built against the compiled `WorshipPlayer.Presenter.dll` (`Assembly.LoadFrom`), with a WPF `Application` on the STA thread, `Activator.CreateInstance`'d, `Show()`/`UpdateLayout()`/`Close()`'d each affected window. Output:

```
OK   WorshipPlayer.Presenter.Views.OperatorWindow
OK   WorshipPlayer.Presenter.Views.ServiceBuilder.AddServiceItemDialog
OK   WorshipPlayer.Presenter.Views.ServiceBuilder.ServiceInfoDialog
ALL WINDOWS OK
```

| Window | Result |
| --- | --- |
| OperatorWindow | OK |
| AddServiceItemDialog | OK |
| ServiceInfoDialog (new) | OK |

Actual exe launch: built and started `WorshipPlayer.UI\bin\Debug\net8.0-windows\WorshipBacktrackPlayer.exe` → **ALIVE after 8s, Responding=True**, then closed cleanly (no `StaticResourceExtension` crash). The harness and its copied files were removed after the run.

## Manual test steps for the user
1. Launch the app. The Operator window opens (no crash).
2. With no service loaded, click **+ Add Item** in the sidebar → the new **Service Info** dialog appears with dark, readable inputs.
3. Enter a **Service name**, pick a date (shows `MM/DD/YYYY`), and optionally type a time like `08:00 AM`. Click **Create**, then add the item.
4. Use **Edit Service** in the sidebar to change the name/date/time of the current service; the dialog opens pre-filled.
5. Restart the app and **Load Service** → the chosen name and date (and time, if set) persist. The saved JSON in `ServiceOrders/` has `name`, `date` (`yyyy-MM-dd`), and `startTime` when a time was entered.
6. An existing date-only file (`ServiceOrders/2026-08-03_Sunday Morning.json`) still loads without error.

## Commit
See commit on `main` (hash recorded in the final step message). Not pushed.
