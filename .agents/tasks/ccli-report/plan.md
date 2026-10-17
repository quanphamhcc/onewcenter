# Implementation Plan — J4 CCLI Song Usage Reporting (report path + capture fields + Footer license)

All paths below are absolute inside the worktree:
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\ccli-report`
(abbreviated `<WT>` from here on). Relative paths land in the parent workspace — always use `<WT>`.

Branch: `feature/ccli-report`. Solution: `<WT>\WorshipBacktrackPlayer.sln`.

## Verified context (confirmed against code, HEAD of the worktree)

- Log site: `<WT>\WorshipPlayer.UI\Services\PresenterIntegration.cs:190` fires `_ = LogSongUsageAsync(song);`. `LogSongUsageAsync` (`PresenterIntegration.cs:197-209`) builds `serviceName` (`:203`) + `serviceDate` (`:204`) and calls the **4-arg** `SongUsageService.LogUsageAsync(song.Title, song.FolderPath, serviceName, serviceDate)` (`:205`) — **Artist and CcliNumber are left blank**.
- `SongUsageService` (`<WT>\WorshipPlayer.Core\Services\SongUsageService.cs`): record overload `LogUsageAsync(SongUsageRecord)` at `:33`; 4-arg overload at `:49`; `GetByDateRangeAsync(from,to)` filters inclusive on `DatePlayed` (`:71`); `GetAllAsync()` (`:84`). `LogUsageAsync` overwrites `record.DatePlayed = DateTime.UtcNow` every call (`:40`).
- `SongUsageRecord` (`<WT>\WorshipPlayer.Core\Models\SongUsageRecord.cs`): has `SongTitle`, `SongId`, `ServiceName`, `ServiceDate`, `DatePlayed`, `CcliNumber`, `Artist`, `OrganizationId` — all JSON-annotated (backward-compatible with existing `song-usage.json`).
- `Song` (`<WT>\WorshipPlayer.Core\Models\Song.cs`): `Artist` (`:14`, json `artist`) and **`Ccli`** (`:29`, json `ccli`) — note the property is `Ccli`, not `CcliNumber`. `FolderPath` (`:84`, `[JsonIgnore]`) is what the current log site passes as `songId`; keep that mapping.
- `CcliReportService` (`<WT>\WorshipPlayer.Core\Services\CcliReportService.cs`): fully implemented + tested. `GetReportAsync(from,to)` groups by `{SongTitle,SongId,CcliNumber,Artist}` → `CcliReportEntry` (has `CcliNumber`). `ExportCsvAsync(from,to,outputPath)` writes header `Song Title,Artist,CCLI Number,Play Count,Last Played`. Constructed ONLY in tests (`new CcliReportService` appears only in `CcliReportServiceTests.cs`).
- Shipped UI bypasses it:
  - `<WT>\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`: `ToolCcliReport_Click` (`:2423`) opens `new CcliReportDialog()` and, if `_songUsageService != null`, kicks off `LoadCcliReportDataAsync(dialog)` (`:2431`). `LoadCcliReportDataAsync` (`:2468`) calls `_songUsageService.GetAllAsync()` (ignores date range), groups by `SongTitle` only, projects `CcliReportItem { Title, Artist, TimesPlayed, LastPlayed }` — **no CcliNumber**.
  - `<WT>\WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml` grid columns: Title / Artist / Times Played / Last Played (no CCLI Number). `.xaml.cs` `GenerateButton_Click` (`:23`) does NOT re-query (date pickers decorative); `ExportCsvButton_Click` (`:50`) writes its own weak CSV header `Title,Artist,Times Played,Last Played`; `CcliReportItem` class (bottom of file) has no CcliNumber.
  - OperatorWindow holds `_songUsageService` (set in `SetServices`, `:486`) and `_engine.Settings` (`PresenterSettings`). This is where `CcliReportService` will be constructed, matching the "construct a service from its dependency" pattern used in `PresenterIntegration`'s ctor.
- Footer settings:
  - `<WT>\WorshipPlayer.Presenter\Views\Settings\FooterSection.xaml.cs` is a static placeholder (ctor + `InitializeComponent()` only). XAML has `CcliNumberTextBox`.
  - `PresenterSettings.CcliLicenseNumber` is a **flat** property (`<WT>\WorshipPlayer.Core\Models\PresenterSettings.cs:139`), json `ccliLicenseNumber`. `PresenterSettings` has **no** nested `FooterSettings` property. The `FooterSettings` model (`<WT>\WorshipPlayer.Core\Models\FooterSettings.cs`) also has `CcliLicenseNumber` but is not wired into `PresenterSettings`; persist against **`PresenterSettings.CcliLicenseNumber`** (that is what actually serializes via `SavePresenterSettings` → `presenter-settings.json`).
  - SettingsWindow pattern (`<WT>\WorshipPlayer.Presenter\Views\Settings\SettingsWindow.xaml.cs`): `PresenterSettings` is set on the window before `ShowDialog` (OperatorWindow `DisplaySettings_Click:838-847` sets it from `_engine.Settings` and wires `OnSaveRequested` → `_onSaveSettings`). Sections are lazy-loaded in `GetSection` (`:57`). `GetScreenConfigSection` (`:117`) is the load-into-section pattern; `SaveButton_Click` (`:90`) copies section values back into `PresenterSettings` then calls `OnSaveRequested`. Follow this exact pattern for Footer.
- Save pipeline: `OnSaveRequested` → OperatorWindow `_onSaveSettings` (`:43`) → `PresenterIntegration.SavePresenterSettings` serializes `PresenterSettings` to `presenter-settings.json`.

## Test-project constraint (drives the test design)

`<WT>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` targets `net8.0`, references **only** `WorshipPlayer.Core`, and pulls selected **Presenter service** `.cs` files via `<Compile Include>`. It is xUnit. It **cannot** reference the WPF `WorshipPlayer.Presenter` (net8.0-windows) or `WorshipPlayer.UI` projects, so `OperatorWindow`, `CcliReportDialog`, and `FooterSection` are **not** unit-testable. Therefore:
- The CSV/report assertions must run through `CcliReportService` (already in Core) — that is the real export path once wiring is done.
- The log-site Song→SongUsageRecord mapping must be extracted into a **pure Core method** so it can be unit-tested without the UI project. `PresenterIntegration` lives in `WorshipPlayer.UI` and is not compiled into Tests; the mapping logic cannot stay only there.

## Deferred / out of scope (do NOT implement)

- **Auto-submit to a CCLI online API is blocked and deferred** — no CCLI API/endpoint/credentials exist in the repo. Do NOT add any stub/fake submit. Only document it as a deferred follow-up (item 8).
- No changes to unrelated features (song editors, templates, media, scripture, service groups, bulk edit, video).

---

## Plan

- [ ] 1. Add a pure, testable mapping helper in Core that builds a `SongUsageRecord` from a `Song` + service context.
      Add a static method, e.g. `SongUsageRecord.FromSong(Song song, string serviceName, DateTime serviceDate)` (or a small `SongUsageMapper` static class in `WorshipPlayer.Core\Services`), mapping `SongTitle=song.Title`, `SongId=song.FolderPath`, `Artist=song.Artist`, `CcliNumber=song.Ccli`, `ServiceName`, `ServiceDate`. Do NOT set `DatePlayed` (the service overwrites it) and do NOT set `Id` (service assigns a GUID when empty). Leave `OrganizationId` default "".
      Files: `<WT>\WorshipPlayer.Core\Models\SongUsageRecord.cs` (or new `<WT>\WorshipPlayer.Core\Services\SongUsageMapper.cs`).
      Verify: `dotnet build <WT>\WorshipPlayer.Core\WorshipPlayer.Core.csproj` succeeds (full test run happens in item 7).

- [ ] 2. Switch the log site to the record overload using the item-1 helper so Artist + CCLI number are captured.
      In `<WT>\WorshipPlayer.UI\Services\PresenterIntegration.cs` `LogSongUsageAsync` (~`:197-209`), replace the 4-arg `LogUsageAsync(song.Title, song.FolderPath, serviceName, serviceDate)` call (`:205`) with `await SongUsageService.LogUsageAsync(SongUsageRecord.FromSong(song, serviceName, serviceDate));`. Keep the surrounding try/catch and the `serviceName`/`serviceDate` derivation unchanged. This stays backward-compatible with existing `song-usage.json` (same model, extra fields default to "").
      Files: `<WT>\WorshipPlayer.UI\Services\PresenterIntegration.cs`.
      Verify: `dotnet build <WT>\WorshipBacktrackPlayer.sln` succeeds.

- [ ] 3. Add a CCLI Number column to the dialog grid and carry it on the row model.
      In `<WT>\WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml`, add a `DataGridTextColumn Header="CCLI Number" Binding="{Binding CcliNumber}"` (place it after Artist). In `<WT>\WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml.cs`, add `public string CcliNumber { get; set; } = string.Empty;` to the `CcliReportItem` class.
      Files: `<WT>\WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml`, `<WT>\WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml.cs`.
      Verify: `dotnet build <WT>\WorshipBacktrackPlayer.sln` succeeds (grid binding resolves at build; runtime render not unit-testable per the test-project constraint).

- [ ] 4. Route the dialog through `CcliReportService` for both data load and date range, and make Generate re-query.
      Construct `CcliReportService` in OperatorWindow from the existing `_songUsageService` (lazy field, following the construct-from-dependency pattern), and give `CcliReportDialog` what it needs to query by date range. Concretely: add a way for the dialog to call back for data — simplest coherent option: give `CcliReportDialog` a `Func<DateTime,DateTime,Task<List<CcliReportItem>>>? QueryAsync` (or pass a `CcliReportService` reference plus a small projector from `CcliReportEntry`→`CcliReportItem` including `CcliNumber`). Then:
      - In `OperatorWindow.xaml.cs` `ToolCcliReport_Click` (`:2423`), construct/assign the service and set the dialog's query callback instead of (or in addition to) `LoadCcliReportDataAsync`. Rewrite `LoadCcliReportDataAsync` (`:2468`) to call `CcliReportService.GetReportAsync(from,to)` and project each `CcliReportEntry` to `CcliReportItem { Title=SongTitle, Artist, CcliNumber, TimesPlayed=PlayCount, LastPlayed }` (drop the `GetAllAsync()`/group-by-title path). Use the dialog's current From/To for the initial load.
      - In `CcliReportDialog.xaml.cs` `GenerateButton_Click` (`:23`), after the null-date guard, invoke the query callback with the selected `From`/`To`, repopulate via `SetReportData`, and update empty-state/export-enabled exactly as `SetReportData` already does. Remove the "In production, this would query…" placeholder behavior.
      Files: `<WT>\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`, `<WT>\WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml.cs`.
      Verify: `dotnet build <WT>\WorshipBacktrackPlayer.sln` succeeds. (Date-range/CCLI-number correctness is covered by item 7 against `CcliReportService` directly, the real query path.)

- [ ] 5. Make the dialog's CSV export use `CcliReportService.ExportCsvAsync` (CCLI Number + date range + escaping).
      In `<WT>\WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml.cs` `ExportCsvButton_Click` (`:50`), after the `SaveFileDialog` returns a path, call the injected `CcliReportService.ExportCsvAsync(from, to, dialog.FileName)` using the currently selected From/To (same values used for the last Generate), instead of hand-writing the `Title,Artist,Times Played,Last Played` CSV. Keep the success `MessageBox`. This produces the tested header `Song Title,Artist,CCLI Number,Play Count,Last Played` and respects the date range. To do this the dialog needs the service reference from item 4 (pass the `CcliReportService` plus the active From/To, or a `Func<DateTime,DateTime,string,Task>` export callback set alongside the query callback in `ToolCcliReport_Click`).
      Files: `<WT>\WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml.cs`, `<WT>\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`.
      Verify: `dotnet build <WT>\WorshipBacktrackPlayer.sln` succeeds; export format verified via `CcliReportService` tests in item 7.

- [ ] 6. Persist the CCLI license number from the Footer settings section.
      In `<WT>\WorshipPlayer.Presenter\Views\Settings\FooterSection.xaml.cs`, add a public property/method pair mirroring `ScreenConfigSection`: a `LoadFrom(string ccliLicenseNumber)` (or `SetCcliLicenseNumber`) that sets `CcliNumberTextBox.Text`, and a read accessor `string CcliLicenseNumber => CcliNumberTextBox.Text ?? ""`. In `<WT>\WorshipPlayer.Presenter\Views\Settings\SettingsWindow.xaml.cs`: in `GetSection` where `_footerSection ??= new FooterSection()` (`:64`), call the loader with `PresenterSettings?.CcliLicenseNumber` (use a `GetFooterSection()` helper like `GetScreenConfigSection`); in `SaveButton_Click` (`:90`), if `_footerSection != null && PresenterSettings != null` set `PresenterSettings.CcliLicenseNumber = _footerSection.CcliLicenseNumber;` before `OnSaveRequested?.Invoke()`. The existing `OnSaveRequested` → `_onSaveSettings` → `SavePresenterSettings` chain writes it to `presenter-settings.json`.
      Files: `<WT>\WorshipPlayer.Presenter\Views\Settings\FooterSection.xaml.cs`, `<WT>\WorshipPlayer.Presenter\Views\Settings\SettingsWindow.xaml.cs`.
      Verify: `dotnet build <WT>\WorshipBacktrackPlayer.sln` succeeds. (Round-trip through `PresenterSettings` JSON is covered by item 7's settings assertion.)
      Optional (do only if it does not expand scope): surface the license number as a leading header line in the exported CSV. Since the shared `CcliReportService.ExportCsvAsync` is the single export path (items 4-5) and lives in Core with no settings access, the clean place is an optional overload `ExportCsvAsync(from,to,outputPath,string? ccliLicenseNumber)` that, when non-empty, writes a first line `CCLI License #,<value>` before the column header; the dialog passes `PresenterSettings.CcliLicenseNumber`. If adding the overload risks touching the tested signature, SKIP this optional part and note it as a follow-up — the required items 1-6 do not depend on it.

- [ ] 7. Add tests in the net8.0 Tests project for the real/testable paths.
      Add a new test class (e.g. `<WT>\WorshipPlayer.Tests\SongUsageMappingTests.cs`, xUnit, pattern per existing `CcliReportServiceTests.cs`) covering: (a) item-1 mapping — `SongUsageRecord.FromSong(song, name, date)` carries `Artist` from `Song.Artist` and `CcliNumber` from `Song.Ccli`, maps `SongId` to `Song.FolderPath`, and leaves `Id` empty + does not pre-set `DatePlayed`. Extend/add to `CcliReportService` coverage (reuse `CcliReportServiceTests` style) to assert: (b) `ExportCsvAsync` output contains the `CCLI Number` header column and the CCLI value for a logged song; (c) a date range excludes out-of-range plays (log two records, one inside and one outside the window via distinct `DatePlayed` — note the service stamps `DatePlayed=UtcNow` on log, so insert the out-of-range record directly through a `SongUsageRecord` whose range you then query, or query a window that excludes `UtcNow`; assert `GetReportAsync`/`ExportCsvAsync` honor it). If the optional CSV license header (item 6) was implemented, add one assertion that the header line appears when a license number is supplied and is absent when empty. Add a `PresenterSettings` JSON round-trip test asserting `CcliLicenseNumber` serializes/deserializes (model-level, no UI).
      Files: `<WT>\WorshipPlayer.Tests\SongUsageMappingTests.cs` (new), and additions to `<WT>\WorshipPlayer.Tests\CcliReportServiceTests.cs` / a `PresenterSettings` test file.
      Verify: `dotnet test <WT>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` — all tests pass, including the new ones.

- [ ] 8. Document that CCLI auto-submit is deferred/blocked (no stub).
      Add a short "Deferred: CCLI auto-submit (J4)" note stating that online submission to CCLI requires an external CCLI reporting/SongSelect API endpoint, authentication/credentials, a persisted CCLI account/license number scope, and networking/retry handling — none of which exist in this codebase — so it is intentionally NOT implemented and no stub was added; the manual CSV export (items 1-7) is the shipped J4 capability. Put the note where project tasks live, e.g. append to `<WT>\.agents\tasks\ccli-report\plan.md` follow-ups section or the task's notes doc; do not add fake submit code anywhere.
      Files: a docs/notes file under `<WT>\.agents\tasks\ccli-report\` (or the repo task docs).
      Verify: `dotnet build <WT>\WorshipBacktrackPlayer.sln` still succeeds (docs-only change; no code impact).

## Deferred follow-up: CCLI auto-submit (J4)

Online submission of usage to CCLI requires an external CCLI reporting/SongSelect API endpoint,
authentication/credentials, a persisted CCLI account/license scope, and networking/retry handling —
none of which exist in this codebase. It is intentionally NOT implemented and no stub was added.
The shipped J4 capability is the manual, date-ranged CSV export (with the CCLI Number column and the
optional license header) plus Artist/CCLI capture at play time. Auto-submit is deferred pending an
external CCLI API + credentials.

## Final verification (whole task)

- `dotnet build <WT>\WorshipBacktrackPlayer.sln` — solution builds with no errors.
- `dotnet test <WT>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` — all tests pass (existing + new).

## Design decisions (brief rationale)

- **Construct `CcliReportService` in OperatorWindow, not PresenterIntegration.** The dialog is created and populated by OperatorWindow, which already holds `_songUsageService`; constructing the report service there (lazily) keeps the dependency where it is used and matches the construct-from-dependency pattern. No DI container exists in this app.
- **Extract the Song→record mapping into Core.** `PresenterIntegration` is in `WorshipPlayer.UI`, which the Tests project cannot reference; a pure Core helper is the only way to unit-test the Artist/CcliNumber mapping (satisfies item 6 of the brief).
- **Persist against flat `PresenterSettings.CcliLicenseNumber`, not the unwired `FooterSettings`.** `PresenterSettings` is what the existing save pipeline serializes; `FooterSettings` is not referenced by `PresenterSettings`, so writing there would not persist. Chosen for correctness and to match the ScreenConfig section pattern.
- **Single export path via `CcliReportService.ExportCsvAsync`.** Removes the dialog's duplicate weaker CSV and guarantees the shipped export matches the tested header and escaping.
- **Optional CSV license header is isolated behind an overload.** Keeps the existing tested `ExportCsvAsync(from,to,path)` signature intact; skip if it would disturb current tests.
