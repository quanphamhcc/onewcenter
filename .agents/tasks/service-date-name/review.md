# Service name + US-format service date/time on service creation

Adds a user-chosen service name and a US-format service date (`MM/DD/YYYY` with an optional 12-hour `hh:mm tt` time) to the service-creation flow. A new `ServiceInfoDialog` under `Views/ServiceBuilder/` collects the values; it is its own `Window` that merges `OwcDesignSystem.xaml`, so it may safely use the keyed dark styles. `OperatorWindow.NewService_Click` shows the dialog when no service is loaded and persists Name + Date + StartTime; a new "Edit Service" sidebar button reopens the dialog pre-filled. `ServiceOrder` gains a nullable `StartTime`, and a new `ServiceDateFormatter` pins all parsing/formatting to en-US. The date-only string storage contract is unchanged.

Watch for: the new DatePicker sets a dark `Foreground="#0F1117"` on the control while the inner editable textbox typically renders on the WPF default white background — the entered date text is still readable but the DatePicker is not visually consistent with the surrounding dark inputs (possible). Everything else — the StaticResource ban on OperatorWindow, en-US pinning, legacy date-only load, and CreateNew/Save wiring — checks out against the diff and the verification evidence.

**Verdict**: APPROVED

## High-level view

The behavioral change is a modal gate on service creation. When `NewService_Click` fires with no current service, the dialog runs first; cancel means nothing is created, confirm runs `CreateNew(name, date)`, sets `StartTime`, saves, and refreshes before the existing AddServiceItemDialog path continues. Edit Service is a symmetric path over the already-loaded order. Both paths are code-behind in OperatorWindow and add no resource reference to its XAML — the only StaticResource-shaped text in OperatorWindow.xaml is an explanatory comment, so the twice-seen crash condition is avoided.

US formatting is centralized in `ServiceDateFormatter`, which holds a single `en-US` `CultureInfo` and uses `TryParseExact`/`ToString` against it for every operation. A vi-VN thread culture test confirms `MM/DD/YYYY` + `AM/PM` output survives a non-US locale. Parsing is layered: exact date+time, then exact date-only, with a tolerant stored-date reader that accepts both `yyyy-MM-dd` and `MM/dd/yyyy` plus an en-US general fallback.

Backward compatibility rests on `ServiceOrder.StartTime` being nullable plus the manager's existing `WhenWritingNull` serializer option, so a date-only service writes no `startTime` key and legacy files load with `StartTime == null`. The `Date` string stays `yyyy-MM-dd`, so filename and sort logic are untouched.

The one soft spot is cosmetic: the DatePicker is given a dark control background but a near-black foreground, and WPF's default DatePicker textbox template does not reliably inherit that background onto the inner edit box. The visible result is likely dark text on white rather than the dark-on-light-text treatment the other inputs use.

<details>
<summary>Issues (1)</summary>

1. **DatePicker theming inconsistency** — `DatePickerControl` sets `Foreground="#0F1117"` with `Background="{StaticResource OwcSurfaceBrush}"` (#252A36); the inner `DatePickerTextBox` typically keeps the default white background so the date reads as dark-on-white, visually inconsistent with the sibling dark inputs. Non-blocking; consider styling the DatePicker textbox or using a light foreground to match the theme.

</details>

<details>
<summary>Details</summary>

### en-US pinning and the parse layers

`ServiceDateFormatter` resolves one `CultureInfo.GetCultureInfo("en-US")` and routes every format/parse through it, so OS locale never leaks in. `FormatUs` emits `MM/dd/yyyy hh:mm tt` when a `StartTime` is present and `MM/dd/yyyy` otherwise, falling back to the raw stored string rather than throwing when the stored date is unparseable. `TryParseUs` tries the full date+time exact format first, then date-only, normalizing the date to `yyyy-MM-dd` for storage on success. `TryParseStoredDate` is deliberately tolerant — exact `yyyy-MM-dd`, exact `MM/dd/yyyy`, then an en-US general `TryParse` — which is what lets both the canonical storage form and any US-typed value round-trip. The vi-VN test exercises both the format and parse direction under a hostile culture (confirmed by reading the test and the formatter).

One behavioral note on `TryParseTime`: it accepts `hh:mm tt`, `h:mm tt`, and 24-hour `HH:mm`/`H:mm`. That is more permissive than the "US format" label, but it only widens acceptance of user input and still stores a normalized value, so it is not a correctness risk (confirmed).

### Backward-compatible storage

`StartTime` is `DateTime?` with `[JsonPropertyName("startTime")]`, and the manager's `JsonSerializerOptions` already sets `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` (confirmed in `ServiceOrderManager`). A date-only service therefore writes no `startTime` key, and the test `DateOnlyService_SavesAndReloadsWithoutStartTime` asserts the key is absent and reloads with a null time. `DateOnlyLegacyJson_LoadsWithNullStartTime` confirms a pre-existing file without the key deserializes cleanly. `Date` remains a `yyyy-MM-dd` string, so filename derivation and date sorting are unaffected (confirmed).

### OperatorWindow stays resource-free

The CRITICAL constraint is met. The diff to `OperatorWindow.xaml` adds only one inline-styled Button (hardcoded `Background`/`Foreground`/`BorderBrush`, matching its siblings) and no `{StaticResource}`, `{DynamicResource}`, or `MergedDictionaries`. A full-file search of OperatorWindow.xaml returns only a source comment containing the word "StaticResource," not an actual reference (confirmed). The new dialog is where the keyed styles live, and it explicitly merges `OwcDesignSystem.xaml`; every key it references — `OwcTextBox`, `OwcButtonBase`, `OwcAccentButton`, `OwcTextSecondaryBrush`, `OwcTextMutedBrush`, `OwcWarningBrush`, `OwcSurfaceBrush`, `OwcBorderBrush` — exists in that dictionary (confirmed). The STA instantiation harness in the evidence shows OperatorWindow, AddServiceItemDialog, and ServiceInfoDialog all construct and show without a StaticResourceExtension crash, and the exe launches responsive.

### Dialog input readability

The Name and Time boxes use `OwcTextBox` (dark surface background, light primary foreground) and are readable. The DatePicker is the exception: it sets `Background="{StaticResource OwcSurfaceBrush}"` (#252A36) but `Foreground="#0F1117"` (near-black). A WPF DatePicker's inner `DatePickerTextBox` does not reliably pick up the control-level `Background` through the default template, so the selected date most likely renders as near-black text on the default white edit box. That is still legible, so it is not a blocker, but it diverges from the dark-input requirement the sibling fields satisfy (possible — would need a visual run to confirm the exact rendered colors; the STA harness verifies construction, not pixel colors).

### Create/Edit wiring

`CreateNew(string name, string date, ...)` matches the call site, and `NewService_Click` sets `CurrentServiceOrder.StartTime` from the dialog before `SaveServiceOrderAsync()`. `EditService_Click` guards the no-service case with a message box, pre-fills via `Initialize`, and writes Name/Date/StartTime back before saving. `NameDateTime_FlowsThroughCreateSaveLoad` drives the create → save → reload path and asserts all three values persist and reformat to `10/18/2026 08:00 AM` (confirmed via the test and `CreateNew` signature). The dialog validates empty name, missing date, and malformed time before confirming.

### Scope

The diff touches only `ServiceOrder.cs` (added field), the new `ServiceDateFormatter.cs`, the two new dialog files, `OperatorWindow.xaml` (one button) and `.xaml.cs` (two handlers), and the new test file. No change to chord rendering, `LoadStandaloneSong`, F2/F3 `LoadSong`, or the ComboBox theme, and no implicit keyless styles added (confirmed from the file list and diff).

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Core/Models/ServiceOrder.cs` — added nullable `StartTime` with `[JsonPropertyName("startTime")]`.
- `WorshipPlayer.Core/Services/ServiceDateFormatter.cs` — NEW; en-US pinned US date/time parse+format helpers.
- `WorshipPlayer.Presenter/Views/ServiceBuilder/ServiceInfoDialog.xaml(.cs)` — NEW dark dialog merging OwcDesignSystem.xaml; name + DatePicker + optional time.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` — one inline-styled "Edit Service" button, no StaticResource.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `NewService_Click` shows the dialog + persists; new `EditService_Click`.
- `WorshipPlayer.Tests/ServiceDateNameTests.cs` — NEW; 10 Core-only tests covering format/parse, legacy load, round-trip, vi-VN culture.

Full diff: `git show HEAD` on branch `main`.

</details>
