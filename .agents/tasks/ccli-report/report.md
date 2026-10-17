# J4 — CCLI Song Usage Reporting — Completion Report

Branch: `feature/ccli-report` (worktree `.worktrees\ccli-report`). First iteration (no `review.json` present).

Scope delivered: the CCLI report path (load + CSV export through the shared Core service, with a CCLI Number column and honored date range), the song-usage capture fields (Artist + CCLI number at the log site), and Footer CCLI license-number persistence. Auto-submit to a CCLI online API is **out of scope and deferred** (see bottom).

## File:line trace of the wired path

### Tools → CCLI Report → load via GetReportAsync(from,to)
- `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs:2426` `ToolCcliReport_Click` opens `CcliReportDialog`.
- `OperatorWindow.xaml.cs:2438` constructs the service in **app wiring** (no longer test-only): `_ccliReportService ??= new CcliReportService(_songUsageService);` (lazy field `_ccliReportService`, construct-from-dependency pattern matching PresenterIntegration's own service construction).
- `OperatorWindow.xaml.cs:2441` `dialog.QueryAsync = async (from, to) => ...` calls `reportService.GetReportAsync(from, to)` and projects each `CcliReportEntry` → `CcliReportItem { Title, Artist, CcliNumber, TimesPlayed, LastPlayed }` (CCLI Number carried through).
- `OperatorWindow.xaml.cs` initial load calls `dialog.LoadForRangeAsync(initialFrom, initialTo)` using the dialog's default From/To (last 3 months → today) — not stale `GetAllAsync()` data. The old date-ignoring `LoadCcliReportDataAsync` was removed.

### Date range honored (initial load + Generate)
- `WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml.cs:36` `GenerateButton_Click` validates From/To then calls `LoadForRangeAsync(from, to)`.
- `CcliReportDialog.xaml.cs:54` `LoadForRangeAsync` invokes the `QueryAsync(from, to)` callback → `CcliReportService.GetReportAsync(from, to)`. The grid is repopulated via `SetReportData`. The old "In production, this would query…" placeholder is gone.

### Dialog grid with CCLI Number
- `WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml` grid columns: Title / Artist / **CCLI Number** (`Binding="{Binding CcliNumber}"`) / Times Played / Last Played.
- `CcliReportDialog.xaml.cs` row model `CcliReportItem` now has `public string CcliNumber`.

### CSV export via ExportCsvAsync
- `CcliReportDialog.xaml.cs:75` `ExportCsvButton_Click` validates From/To, shows the SaveFileDialog, then calls the `ExportAsync(from, to, path)` callback.
- `OperatorWindow.xaml.cs:2456` `dialog.ExportAsync = (from, to, path) => reportService.ExportCsvAsync(from, to, path, _engine?.Settings?.CcliLicenseNumber);` — routes export through Core `CcliReportService.ExportCsvAsync`, which writes header `Song Title,Artist,CCLI Number,Play Count,Last Played`, honors the date range, and applies CSV escaping. The dialog's old hand-written weak CSV was removed.

### Song load → PresenterIntegration record-overload log with Artist/CcliNumber
- `WorshipPlayer.UI\Services\PresenterIntegration.cs:207` (in `LogSongUsageAsync`): `await SongUsageService.LogUsageAsync(SongUsageMapper.FromSong(song, serviceName, serviceDate));` — switched from the 4-arg overload to the `SongUsageRecord` overload.
- `WorshipPlayer.Core\Services\SongUsageMapper.cs:21` `FromSong` maps `SongTitle=song.Title`, `SongId=song.FolderPath`, `Artist=song.Artist`, `CcliNumber=song.Ccli` (verified real property names in `Song.cs`: `Artist` json `artist`, `Ccli` json `ccli`, `FolderPath` `[JsonIgnore]`). Leaves `Id` empty (service assigns a GUID) and does not pre-set `DatePlayed` (service stamps UtcNow). This is the testable extraction point in Core (the Tests project can't reference the WPF UI project).
- Backward-compatible with existing `song-usage.json`: same `SongUsageRecord` model, extra fields default to `""`.

### FooterSection load/save of the license number
- `WorshipPlayer.Presenter\Views\Settings\FooterSection.xaml.cs:19` `SetCcliLicenseNumber(string?)` loads into `CcliNumberTextBox`; read accessor `CcliLicenseNumber => CcliNumberTextBox.Text ?? ""`.
- `WorshipPlayer.Presenter\Views\Settings\SettingsWindow.xaml.cs:123` `GetFooterSection()` lazily creates the section and loads `PresenterSettings.CcliLicenseNumber` (mirrors `GetScreenConfigSection`). `"Footer" => GetFooterSection()` in `GetSection`.
- `SettingsWindow.xaml.cs:104` `SaveButton_Click` writes back: `PresenterSettings.CcliLicenseNumber = _footerSection.CcliLicenseNumber;` before `OnSaveRequested?.Invoke()`.

## What persists where
- CCLI license number persists on the flat `PresenterSettings.CcliLicenseNumber` (json `ccliLicenseNumber`), serialized to `presenter-settings.json` by the existing `OnSaveRequested → OperatorWindow._onSaveSettings → PresenterIntegration.SavePresenterSettings` pipeline. (`FooterSettings.CcliLicenseNumber` is not wired into `PresenterSettings`, so writing there would not persist — chosen `PresenterSettings` for correctness.)
- Song usage (incl. Artist + CcliNumber) persists to `song-usage.json` via `SongUsageService`.
- CSV export is written to the user-selected path; when a license number is set it is prefixed as a leading `CCLI License #,<value>` line (optional overload, below).

## CcliReportService construction in app wiring
Confirmed: `new CcliReportService(...)` now appears in `OperatorWindow.xaml.cs:2438` (production app wiring) in addition to the test project. It is no longer test-only.

## Optional CSV license header
Implemented as a non-breaking overload: `CcliReportService.ExportCsvAsync(from, to, outputPath, string? ccliLicenseNumber)` (`CcliReportService.cs:67`). The original 3-arg `ExportCsvAsync(from, to, outputPath)` now delegates to it with `null`, so its tested behavior/signature is unchanged. When a non-empty license number is supplied, a leading `CCLI License #,"<value>"` line precedes the column header.

## Tests added (net8.0 Tests project)
- `WorshipPlayer.Tests\SongUsageMappingTests.cs` (new):
  - `FromSong_CarriesArtistAndCcliNumber`, `FromSong_MapsSongIdToFolderPathAndServiceContext`, `FromSong_LeavesIdEmptyForServiceToAssign` — the log-site mapping carries Artist/CcliNumber into a `SongUsageRecord`.
  - `FromSong_LoggedThroughService_AggregatesWithCcliNumber` — end-to-end: logged record surfaces in the report with Artist + CCLI number.
  - `PresenterSettings_CcliLicenseNumber_RoundTripsThroughJson` — model-level persistence of the license number.
- `WorshipPlayer.Tests\CcliReportServiceTests.cs` (extended):
  - `ExportCsvAsync_RespectsDateRange_ExcludesOutOfRangePlays`, `GetReportAsync_RespectsDateRange_ExcludesOutOfRangePlays` — date range honored.
  - `ExportCsvAsync_WithLicenseNumber_WritesLicenseHeaderLine`, `ExportCsvAsync_WithoutLicenseNumber_OmitsLicenseHeaderLine` — CSV CCLI Number column + optional license header.

## Build / test results
- `dotnet build WorshipBacktrackPlayer.sln` → **Build succeeded, 0 errors, 4 warnings** (all 4 pre-existing: `MediaLibraryPanel._currentView` CS0414, `ChordChartWindow` CS8604, `WaveformEditorWindow._inReviewMode` CS0414). Exit code 0.
- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` → **total: 569, failed: 0, succeeded: 569, skipped: 0.** Exit code 0. (Includes the new CCLI mapping/report/settings tests.)

## Headless UI note
A headless WPF UI run is not possible in this environment, so the on-screen dialog/date-picker interaction was not exercised at runtime. Correctness is proven via the clean solution build (the XAML `CcliNumber` binding and all wiring compile), the passing unit tests against the real `CcliReportService` export/report path and the `SongUsageMapper` log-site mapping, and the file:line trace of the wired path above.

## Deferred: CCLI auto-submit (J4)
Auto-submit of usage to CCLI via an online API is **intentionally NOT implemented** and **no stub was added**. It is genuinely blocked: there is no CCLI reporting/SongSelect API endpoint, no authentication/credentials, and no networking/retry handling in this codebase. The shipped J4 capability is the manual CSV export (date-ranged, with the CCLI Number column and optional license header) plus accurate Artist/CCLI capture at play time. Auto-submit is deferred as a follow-up pending an external CCLI API + credentials.
