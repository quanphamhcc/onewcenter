# J4 — CCLI Song Usage Reporting + Auto-Submit: Gap Analysis

Repo: `owc-multitrack-feature-lyric-presenter` (main) · HEAD `72a94d7` · READ-ONLY investigation.
Sibling folder `...owc-multitrack-feature-lyric-presenter 2` was ignored per brief.

## Summary answer

The **data-capture and reporting backend for J4 is substantially DONE**: song plays are logged on load, persisted to a local JSON file, and there is a fully tested service (`CcliReportService`) that aggregates plays by song and exports a CCLI-shaped CSV. A WPF report dialog exists and is reachable from the operator window's Tools menu.

But there are **three real gaps**, in increasing severity:

1. **The production path does not use the tested `CcliReportService`.** The operator window and the dialog each re-implement their own grouping and a thinner CSV (Title/Artist/Times Played/Last Played) that omits the CCLI Number — the one column CCLI reporting actually needs. The well-tested `CcliReportService` (which *does* include CCLI Number) is instantiated only in tests, never wired into the UI.
2. **The CCLI license/account number is not stored.** The Settings → Footer section's "CCLI License Number" textbox is a static placeholder — `FooterSection.xaml.cs` contains only `InitializeComponent()`, with no load/save against `FooterSettings.CcliLicenseNumber` or `PresenterSettings.CcliLicenseNumber`. Likewise, per-song `CcliNumber`/`Artist` are never populated at the real log site, so they are blank in captured records today.
3. **"Auto-submit" is genuinely absent.** There is no CCLI online/API code anywhere — no SongSelect client, no `api.ccli` endpoint, no HTTP submission, no credential storage. Auto-submit is blocked on external CCLI API access that does not exist in this codebase and cannot be built from the current information.

**Feasible without external CCLI access:** wire the UI to the already-tested `CcliReportService` (gets CCLI Number into the export), populate `Artist`/`CcliNumber` at the log site, honor the dialog's date range, and make the Footer settings' CCLI license field actually persist. **Blocked on external credentials:** actual auto-submit to CCLI.

---

## Evidence

### 1. Song usage logging — how/when, fields, persistence

- **When:** usage is logged on song load, fire-and-forget.
  `WorshipPlayer.UI\Services\PresenterIntegration.cs:188` calls `_ = LogSongUsageAsync(song);` immediately after `PresenterEngine.OnSongLoaded(song)`.
- **What is captured at the real log site:** `PresenterIntegration.cs:200-205` builds the record from title + folder path + service name/date only:
  - `serviceName` ← `ServiceOrderManager.CurrentServiceOrder?.Name ?? "Unknown Service"` (`:204`)
  - `serviceDate` ← parsed from `CurrentServiceOrder?.Date` else `DateTime.Today` (`:205`)
  - call: `await SongUsageService.LogUsageAsync(song.Title, song.FolderPath, serviceName, serviceDate);` (`:205`)
  - This uses the 4-arg overload `SongUsageService.LogUsageAsync(string songTitle, string songId, string serviceName, DateTime serviceDate)` (`WorshipPlayer.Core\Services\SongUsageService.cs:49`), which **does not set `Artist` or `CcliNumber`** — so those fields are empty in production records even though the model and report support them.
- **Persistence:** local JSON file. `SongUsageService.SaveAsync()` serializes the full `_records` list with `WriteIndented` and a `JsonStringEnumConverter` to `_storagePath` (`SongUsageService.cs:142-152`). The path is `song-usage.json` in the app folder: `var usagePath = Path.Combine(appFolder, "song-usage.json");` (`PresenterIntegration.cs:105`). `LogUsageAsync` assigns a GUID `Id` if missing and **overwrites `DatePlayed = DateTime.UtcNow`** on every log (`SongUsageService.cs:38-45`).
- Query/read API: `GetByDateRangeAsync(from,to)` filters on `DatePlayed` inclusive and sorts descending (`SongUsageService.cs:71-78`); `GetAllAsync()` returns a copy (`:84-88`); there is also a record-level CSV `ExportToCsvAsync` with a wide header (`Song Title,Song ID,Artist,CCLI Number,Service Name,Service Date,Date Played`, `:98-116`).

### 2. Record shape — `SongUsageRecord`

`WorshipPlayer.Core\Models\SongUsageRecord.cs:10-38`. Fields: `Id` (GUID default), `SongTitle`, `SongId`, `ServiceName`, `ServiceDate`, `DatePlayed`, `CcliNumber`, `Artist`, `OrganizationId` — all JSON-annotated. So the model is capable of holding every field CCLI needs; the gap is that the production log site leaves `CcliNumber`/`Artist`/`OrganizationId` blank.

### 3. `CcliReportService` — formats and CCLI fitness

`WorshipPlayer.Core\Services\CcliReportService.cs`:
- `GetReportAsync(from,to)` (`:26-48`) pulls `GetByDateRangeAsync`, groups by `{SongTitle, SongId, CcliNumber, Artist}`, projects `PlayCount = g.Count()` and `LastPlayed = g.Max(DatePlayed)`, ordered by play count desc then title. Output type `CcliReportEntry` (`:84-120`) includes **CcliNumber**.
- `ExportCsvAsync(from,to,outputPath)` (`:56-73`) writes header **`Song Title,Artist,CCLI Number,Play Count,Last Played`**, quotes/escapes fields (`EscapeCsv`, doubles quotes, `:75-78`), creates the output dir if needed. **Only CSV** — no PDF, no aggregation beyond per-song play counts.
- **Fitness for manual CCLI reporting:** the columns (title, author/artist, CCLI song number, times used, date) are the right shape for a CCLI usage report. The two caveats are not in this service but upstream: (a) `CcliNumber`/`Artist` are blank unless the capture path is fixed (§1), and (b) this service is **not used by any production code** — only `WorshipPlayer.Tests\CcliReportServiceTests.cs:24` constructs it (`new CcliReportService(...)`); the repo-wide search for `new CcliReportService` returns that single test hit.

### 4. CCLI report UI — reachability and function

- **Tools → CCLI Report handler:** `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs:2423` `ToolCcliReport_Click(...)` — opens `new CcliReportDialog()` (`:2427`), sets owner, and if `_songUsageService != null` kicks off `_ = LoadCcliReportDataAsync(dialog)` (`:2431`), then `dialog.ShowDialog()`. (Historical "1835-1860" range is stale; real location ~2423-2440 at HEAD `72a94d7`.)
- **Data load:** `LoadCcliReportDataAsync` (`OperatorWindow.xaml.cs:2468-2494`) calls `_songUsageService.GetAllAsync()` — **ignores any date range** — groups **by `SongTitle` only**, projects `CcliReportItem { Title, Artist, TimesPlayed, LastPlayed }` (no CCLI Number), orders by `TimesPlayed` desc, and pushes into the dialog via `dialog.SetReportData(...)`.
- **Dialog:** `WorshipPlayer.Presenter\Views\Tools\CcliReportDialog.xaml` + `.xaml.cs`:
  - Grid columns are Title / Artist / Times Played / Last Played (`CcliReportDialog.xaml` DataGrid.Columns) — **no CCLI Number column**.
  - `GenerateButton_Click` (`.xaml.cs:25-49`) only toggles grid/empty-state visibility over whatever `ReportItems` were pre-loaded; its own comment says "In production, this would query the CcliReportService/SongUsageService" — i.e., the date pickers do **not** re-query; they are decorative. Default range is set to last 3 months in the ctor (`:19-21`) but unused for querying.
  - `ExportCsvButton_Click` (`:51-78`) writes its own CSV with header **`Title,Artist,Times Played,Last Played`** (no CCLI Number) via `SaveFileDialog`, independent of `CcliReportService`.
- **Verdict:** export is reachable and will produce a file, but the production export omits the CCLI Number and ignores the date range — it does not match the stronger, tested `CcliReportService` output.

### 5. Any CCLI online/API submission code? — NO

Repo-wide search for `SongSelect | api.ccli | HttpClient | auto-submit | AutoSubmit` (excluding `obj/`) found no CCLI networking:
- The only `HttpClient` is in `WorshipPlayer.UI\Views\SectionEditorWindow.xaml.cs:209-212`, an unrelated generic URL fetch for section/lyric content (User-Agent `WorshipPlayer/1.0`).
- `SongSelected` hits are a UI event on the song library panel (`SongLibraryPanel.xaml.cs:28`, `OperatorWindow.xaml.cs:2038`), **not** CCLI SongSelect.
- No endpoint constants, no auth/token handling, no submit call. **Auto-submit is genuinely absent.**

### 6. Footer/CCLI settings — license number storage

- Model support exists: `FooterSettings.CcliLicenseNumber` (`WorshipPlayer.Core\Models\FooterSettings.cs:10-11`) and `PresenterSettings.CcliLicenseNumber` + `CopyrightFormat="CCLI License #{0}"` (`WorshipPlayer.Core\Models\PresenterSettings.cs:138-145`). `SongUsageRecord.OrganizationId` and `FooterSettings.OrganizationId` also exist as account-scoping fields.
- **UI is a static placeholder:** `WorshipPlayer.Presenter\Views\Settings\FooterSection.xaml` has `CcliNumberTextBox` (`:27`) but `FooterSection.xaml.cs` contains only the constructor + `InitializeComponent()` — **no load from, and no save to**, `FooterSettings`/`PresenterSettings`. So a user can type a license number but it is never persisted, and nothing reads it for export/submit.

### 7. Tests — existing coverage

- `WorshipPlayer.Tests\SongUsageServiceTests.cs`: logging (record + 4-arg overload), GUID assignment, `DatePlayed` set, multi-record, date-range in/out/empty/ordering, record-level CSV (header, escaping, empty→header-only), file persistence. Solid coverage of `SongUsageService`.
- `WorshipPlayer.Tests\CcliReportServiceTests.cs`: empty range, no usage, single/multiple plays aggregation, grouping across songs, order-by-count-desc, includes Artist + CCLI Number, includes LastPlayed, CSV header `Song Title,Artist,CCLI Number,Play Count,Last Played`, CSV rows, comma/quote escaping, dir creation. Solid coverage of `CcliReportService`.
- **No tests exist for the production UI path** (`OperatorWindow.LoadCcliReportDataAsync`, `CcliReportDialog` export) — which is exactly the untested, weaker code that actually ships. No CCLI API/submit tests (nothing to test).

---

## Conclusions — DONE vs MISSING for J4

**DONE**
- Per-play capture on song load, persisted to `song-usage.json` (§1).
- Record model with all CCLI-relevant fields (§2).
- Aggregating report + CCLI-shaped CSV export (`CcliReportService`), well tested (§3, §7).
- A reachable Tools → CCLI Report dialog with grid + CSV export + save dialog (§4).

**MISSING / BROKEN**
- Production UI ignores the tested `CcliReportService`; its CSV omits **CCLI Number** and ignores the **date range** (§3, §4).
- `Artist`/`CcliNumber` not populated at the real log site (4-arg overload) — blank in captured data (§1, §2).
- CCLI license number has model fields but **no working Settings persistence** — the textbox is a dead placeholder (§6).
- **No CCLI online/API submission of any kind** — auto-submit absent (§5).

## What "auto-submit" would require that does NOT exist
- An external **CCLI API endpoint** for usage submission (SongSelect / CCLI reporting API). None referenced anywhere.
- **Authentication/credentials** (API key or OAuth token) and secure storage for them. None exists.
- A persisted **CCLI account/license number** to scope the submission. The field exists in the models but is never saved from the UI (§6).
- Networking + error/retry handling. The only `HttpClient` in the repo is unrelated (§5).
All of this is **blocked on external CCLI access and credentials** not available in this codebase.

## Recommendation (feasible J4 scope WITHOUT external CCLI API)

Target only the real, unblocked gap:
1. **Wire the UI to `CcliReportService`.** Replace the ad-hoc grouping/CSV in `OperatorWindow.LoadCcliReportDataAsync` (`OperatorWindow.xaml.cs:2468`) and `CcliReportDialog.ExportCsvButton_Click` (`CcliReportDialog.xaml.cs:51`) with `CcliReportService.GetReportAsync`/`ExportCsvAsync`, so the shipped export includes the **CCLI Number** column and respects the date range. Add a CCLI Number grid column in `CcliReportDialog.xaml`.
2. **Honor the date pickers.** Have `GenerateButton_Click` actually query by the selected `From`/`To` instead of showing pre-loaded data.
3. **Populate capture fields.** At the log site (`PresenterIntegration.cs:205`) pass the song's `Artist`/`CcliNumber` (switch to the record overload `LogUsageAsync(SongUsageRecord)`) so exports are meaningful.
4. **Make Footer settings persist the CCLI license number.** Implement load/save in `FooterSection.xaml.cs` against `FooterSettings.CcliLicenseNumber` (and/or `PresenterSettings.CcliLicenseNumber`), and surface it on the export as a header line so the generated report is self-sufficient for **manual** CCLI submission.
5. **Add tests** for the production path once it routes through `CcliReportService`.

Defer (blocked): actual auto-submit to CCLI — revisit only with a documented CCLI API, credentials, and account storage.

*Speculative labeling:* the brief's note that Settings sections are static placeholders is confirmed for the Footer/CCLI section (§6). I did not audit every other Settings section; that confirmation is scoped to `FooterSection`.
