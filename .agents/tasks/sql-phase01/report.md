# SQLite Migration — Phase 0 + Phase 1 (Song usage store) — Completion Report

Branch: `feature/sql-phase01-usage`
Worktree: `.worktrees\sql-phase01`
Iteration: first (no `review.json` present at start).

## Scope

Phase 0 (shared SQLite infrastructure) + Phase 1 (migrate the Song usage store from
`song-usage.json` to SQLite `app.db`). No other store (alerts, templates, media, settings,
service orders, songs) was touched. The Bible database (`bible.db`) stays SEPARATE.

## Phase-0 infrastructure design

New file: `WorshipPlayer.Core\Services\AppDatabase.cs` (namespace `WorshipPlayer.Core.Services`).

A deliberately thin connection/path factory — NO EF Core, NO repository framework — mirroring the
`BibleDatabaseService` style (connection string `Data Source=<path>`, a new `SqliteConnection` per
call, no shared/pooled connection kept open).

Members:
- `const string DefaultFileName = "app.db"`.
- `static string DefaultPath` → `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.db")` —
  the single shared user-data database under the app folder, alongside `bible.db`.
- `AppDatabase(string databasePath)` — stores `DatabasePath` and builds
  `_connectionString = $"Data Source={databasePath}"` (identical shape to `BibleDatabaseService`).
  Tests inject a temp path; production passes `DefaultPath`.
- `string DatabasePath { get; }`.
- `SqliteConnection CreateConnection()` → `new SqliteConnection(_connectionString)` (caller opens
  and disposes, per-call, exactly like each Bible method).
- `void EnsureDirectory()` — mkdir the parent dir if missing; safe to call repeatedly.

**How future phases register/own their table(s):** each store keeps its own
`CREATE TABLE IF NOT EXISTS` DDL and runs it against a connection from
`AppDatabase.CreateConnection()` during its own init. Because every DDL statement is
`IF NOT EXISTS`, multiple stores sharing one `app.db` can each run their schema-init idempotently
and in any order with no conflict — this satisfies "schema-init is safe to call repeatedly and from
multiple stores sharing app.db." No central table registry is needed for Phase 0/1.

**Test injectability:** `AppDatabase` is constructable with any path, and `SongUsageService` takes
a db path, so tests point both at a temp `app.db` and never touch the real app folder.

## SongUsage table schema (owned by SongUsageService)

```sql
CREATE TABLE IF NOT EXISTS SongUsage (
    Id             TEXT PRIMARY KEY,
    SongTitle      TEXT NOT NULL DEFAULT '',
    SongId         TEXT NOT NULL DEFAULT '',
    ServiceName    TEXT NOT NULL DEFAULT '',
    ServiceDate    TEXT NOT NULL,            -- ISO-8601 round-trip ("o"), normalized to UTC
    DatePlayed     TEXT NOT NULL,            -- ISO-8601 round-trip ("o"), normalized to UTC
    CcliNumber     TEXT NOT NULL DEFAULT '',
    Artist         TEXT NOT NULL DEFAULT '',
    OrganizationId TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_songusage_dateplayed ON SongUsage(DatePlayed);
```

Column order mirrors `SongUsageRecord` fields. Dates are stored as ISO-8601 round-trip (`"o"`) text,
**normalized to UTC first** so every stored/compared string has the same `...Z` shape. Lexicographic
ordering of those strings is chronological, so the text range filter in `GetByDateRangeAsync` and the
`ORDER BY DatePlayed DESC` reproduce the former in-memory `DateTime` comparisons regardless of each
input's `Kind`. `Unspecified` dates (e.g. `new DateTime(2020,1,1)` used in tests) are stamped UTC
without converting ticks — this matches the old code, which did a raw `DateTime` compare (tick-based,
Kind-ignorant). Read back with `DateTime.Parse(..., InvariantCulture, DateTimeStyles.RoundtripKind)`.
CSV date formatting (`yyyy-MM-dd` / `yyyy-MM-dd HH:mm:ss`) is unchanged.

## Public API parity (byte-for-byte signatures preserved)

- `LogUsageAsync(SongUsageRecord)` — empty `Id` → `Guid.NewGuid().ToString()`; `DatePlayed =
  DateTime.UtcNow` stamped on every log; then a single-row parameterized `INSERT`.
- `LogUsageAsync(string title, string songId, string serviceName, DateTime serviceDate)` — builds a
  record and delegates to the record overload (unchanged).
- `GetByDateRangeAsync(from, to)` — `SELECT ... WHERE DatePlayed >= @from AND DatePlayed <= @to
  ORDER BY DatePlayed DESC` (inclusive, descending).
- `GetAllAsync()` — `SELECT ... FROM SongUsage` (returns all).
- `ExportToCsvAsync(from, to, outputPath)` — unchanged logic; still reads via `GetByDateRangeAsync`
  and writes the exact same header/row format.

Constructor signature preserved as `SongUsageService(string databasePath)` — same single-string
shape it already had (previously a json path, now an app.db path), so no call site changed shape.

## Defensive one-time JSON → SQLite import

In `EnsureInitializedAsync` (guarded by a `_initialized` flag, runs once per instance): after the
`CREATE TABLE IF NOT EXISTS` DDL, `ImportLegacyJsonIfNeededAsync` runs. It:
1. Returns immediately if the sibling `song-usage.json` (derived from the db path's directory) does
   not exist.
2. Returns if `SELECT COUNT(*) FROM SongUsage > 0` — the empty-table guard makes the import
   **non-duplicating**: once rows exist, a later construction skips import entirely.
3. Deserializes with the SAME `JsonSerializerOptions` the old code used (`WriteIndented`,
   `JsonStringEnumConverter`), inserts each record AS-IS inside a transaction using `INSERT OR
   IGNORE` (preserving historical `Id` and `DatePlayed`; assigns a GUID only if `Id` is empty so the
   PRIMARY KEY is satisfied).
4. The whole read/parse/insert is wrapped in try/catch that SWALLOWS any exception — a missing or
   malformed/locked legacy file NEVER throws (matches the old `EnsureLoadedAsync` catch). The legacy
   JSON file is left on disk untouched (migration is reversible).

## File:line trace

- app.db path helper: `WorshipPlayer.Core\Services\AppDatabase.cs`
  (`DefaultPath` ~:34; ctor + `_connectionString` ~:44-48; `CreateConnection` ~:58;
  `EnsureDirectory` ~:65-70).
- SongUsageService table init: `WorshipPlayer.Core\Services\SongUsageService.cs`
  `EnsureInitializedAsync` (runs `CreateSchemaSql`, then the import) ~:178-199;
  schema DDL constant `CreateSchemaSql` ~:37-50.
- LogUsageAsync INSERT: `SongUsageService.cs` ~:70-90 (GUID-on-empty + `DatePlayed = UtcNow`, then
  single-row parameterized INSERT).
- GetByDateRangeAsync SELECT: `SongUsageService.cs` ~:116-135 (inclusive range, `ORDER BY DatePlayed
  DESC`).
- GetAllAsync SELECT: `SongUsageService.cs` ~:140-153.
- Import path: `SongUsageService.cs` `ImportLegacyJsonIfNeededAsync` ~:206-250.
- CcliReportService still reads correctly: `WorshipPlayer.Core\Services\CcliReportService.cs:28`
  calls `_usageService.GetByDateRangeAsync(...)` — UNCHANGED file (verified).
- SongUsageMapper: `WorshipPlayer.Core\Services\SongUsageMapper.cs` `FromSong` — UNCHANGED file
  (verified; still leaves `Id` empty and does not set `DatePlayed`, relying on the service stamping).
- PresenterIntegration now constructs against app.db:
  `WorshipPlayer.UI\Services\PresenterIntegration.cs` ~:104-107
  (`var appDbPath = Path.Combine(appFolder, "app.db"); SongUsageService = new
  SongUsageService(appDbPath);`), consistent with the Bible construction at ~:119-120
  (`Path.Combine(appFolder, "bible.db")`). The following line
  `SongLibraryService = new SongLibraryService(_songManager, SongUsageService)` and all ordering are
  intact.

## CcliReportService / SongUsageMapper — zero changes

Both only call the public `SongUsageService` API and the `SongUsageRecord` model. They were read and
confirmed to need ZERO edits; neither file appears in the git diff.

## Tests

- `SongUsageServiceTests.cs`: setup now creates a temp folder + `app.db` and constructs
  `new SongUsageService(_dbPath)`. `Dispose` calls `SqliteConnection.ClearAllPools()` BEFORE deleting
  the temp folder, wrapped in try/catch(IOException) — mirrors `BibleDatabaseServiceTests`. All
  existing behavioral assertions kept unchanged (parity proof). The old
  `LogUsageAsync_PersistsToFile` (asserted JSON file contents) is replaced by
  `LogUsageAsync_PersistsAcrossServiceInstances`, which re-opens a NEW service against the same
  `app.db` and reads the record back (DB-backed persistence). Added
  `Import_LegacyJson_ImportsOnceWithoutRestamping` (writes a legacy `song-usage.json`, asserts
  records import with Id/DatePlayed preserved, then a SECOND service instance against the same
  `app.db` sees no duplicates) and `Import_MalformedLegacyJson_DoesNotThrowAndYieldsEmptyStore`.
- `CcliReportServiceTests.cs`: setup switched to a temp `app.db`; `ClearAllPools()` added to teardown.
  All assertions unchanged.
- `SongUsageMappingTests.cs`: the one service-backed test switched to a temp `app.db`;
  `ClearAllPools()` added to the `finally`. Mapping assertions unchanged.
- `SongLibraryServiceTests.cs`: this test ALSO constructs a `SongUsageService` (not named in the
  plan but in-scope as "any test that constructs SongUsageService"). Switched its usage path to a
  temp `app.db` and added `ClearAllPools()` to teardown; without this, its `Dispose` failed with an
  `IOException` ("file in use") once the store logged a row through SQLite. Its own assertions are
  unchanged.
- `ChordProImportTests.cs`: left unchanged — it constructs a `SongUsageService` but never logs
  usage, so no SQLite connection is opened and no file lock is held; it passes as-is and its
  `Dispose` already swallows cleanup exceptions.

## Build / test results (recorded for the reviewer)

Commands run in the worktree root:

- `dotnet build WorshipBacktrackPlayer.sln` →
  **Build succeeded. 4 Warning(s), 0 Error(s).** The 4 warnings are the pre-existing baseline
  (MediaLibraryPanel `_currentView` CS0414 — appears for both the normal and the WPF-temp project,
  ChordChartWindow CS8604, WaveformEditorWindow `_inReviewMode` CS0414). No new warnings introduced.
- `dotnet test WorshipBacktrackPlayer.sln` →
  **Passed! Failed: 0, Passed: 571, Skipped: 0, Total: 571.**
  Baseline was ~569; the delta is the net of the new import/persistence tests added to
  SongUsageServiceTests minus the replaced file-contents test. All previously-passing song-usage,
  CCLI, mapping, and song-library tests still pass (parity), plus the two new import tests and the
  reworked persistence test.

(Interim note: the first full test run surfaced one failure —
`SongLibraryServiceTests.RecordUsageAsync_StoresRecord` — whose Dispose could not delete the temp
`usage.json` because SQLite still held the handle. Fixed by adding `ClearAllPools()` to that test's
teardown; the re-run is the green result above.)

## Scope confirmation (git diff)

`git status --short` in the worktree shows exactly:
- `?? WorshipPlayer.Core/Services/AppDatabase.cs` (new, Phase-0 helper)
- `M  WorshipPlayer.Core/Services/SongUsageService.cs` (Phase-1 backing store)
- `M  WorshipPlayer.UI/Services/PresenterIntegration.cs` (construction site)
- `M  WorshipPlayer.Tests/SongUsageServiceTests.cs`
- `M  WorshipPlayer.Tests/CcliReportServiceTests.cs`
- `M  WorshipPlayer.Tests/SongUsageMappingTests.cs`
- `M  WorshipPlayer.Tests/SongLibraryServiceTests.cs`
- `?? .agents/tasks/sql-phase01/` (task docs + this report)

No alerts/templates/media/settings/service-orders/songs store files were modified.
`CcliReportService.cs` and `SongUsageMapper.cs` are NOT in the diff.

## ClearAllPools in test teardown — confirmed

`SqliteConnection.ClearAllPools()` is called in the teardown of every test class that constructs a
SQLite-backed `SongUsageService` and actually opens a connection: `SongUsageServiceTests`,
`CcliReportServiceTests`, `SongLibraryServiceTests`, and (in the service-backed test's `finally`)
`SongUsageMappingTests`.

## Verified vs not verified

Verified: full solution build (0 errors, 4 pre-existing warnings); full `dotnet test` suite (571
pass, 0 fail) including the new import/persistence tests; git diff scope; that CcliReportService and
SongUsageMapper are unchanged; that the public API signatures and GUID/UtcNow behaviors are preserved.

Not verified: runtime behavior of the live WPF app (no GUI launch) — the construction change at
`PresenterIntegration.cs` is covered only by compilation plus the behavioral test parity; a real
first-run JSON→app.db import against an actual deployed `song-usage.json` in the app output folder was
not executed (the repo has no committed corpus — the import path is covered by the synthetic import
tests instead).
