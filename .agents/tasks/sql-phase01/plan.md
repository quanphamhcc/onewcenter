# Implementation Plan — SQLite migration Phase 0 + Phase 1 (Song usage store)

All paths below are inside the worktree:
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sql-phase01`
Step agents run with cwd = main repo, so use absolute paths and
`git -C "<worktree>"` for any git operation. IGNORE the sibling folder
`owc-multitrack-feature-lyric-presenter 2`.

## Context verified by exploration (cite file:line)

- Microsoft.Data.Sqlite 8.0.0 is already referenced in Core and Tests — no new package needed:
  `WorshipPlayer.Core\WorshipPlayer.Core.csproj:9` and `WorshipPlayer.Tests\WorshipPlayer.Tests.csproj:12`.
- Reference SQLite pattern = `WorshipPlayer.Core\Services\Bible\BibleDatabaseService.cs`
  (ctor takes a db path, builds `_connectionString = $"Data Source={databasePath}"` at
  `BibleDatabaseService.cs:17-23`; `InitializeAsync` creates the dir, opens a per-call
  `SqliteConnection`, runs `CREATE TABLE IF NOT EXISTS` DDL at `BibleDatabaseService.cs:28-55`).
  Schema DDL text lives in `BibleSeedData.GenerateSchemaSQL()` (`BibleSeedData.cs:14-52`).
- Test teardown pattern = `WorshipPlayer.Tests\BibleDatabaseServiceTests.cs:25-43`: call
  `SqliteConnection.ClearAllPools()` first, then best-effort `File.Delete` wrapped in a
  try/catch(IOException) so Windows file locks never fail cleanup.
- `SongUsageService` current JSON implementation: `WorshipPlayer.Core\Services\SongUsageService.cs`.
  Public API to preserve byte-for-byte: `LogUsageAsync(SongUsageRecord)` (`:36`),
  `LogUsageAsync(string,string,string,DateTime)` (`:52`), `GetByDateRangeAsync(from,to)` (`:72`),
  `GetAllAsync()` (`:84`), `ExportToCsvAsync(from,to,outputPath)` (`:96`).
  Behavior to preserve: empty `Id` → `Guid.NewGuid().ToString()` on log (`:41-42`);
  `record.DatePlayed = DateTime.UtcNow` stamped on every log (`:44`);
  `GetByDateRangeAsync` filters on **DatePlayed** inclusive `>= from && <= to` and orders
  **descending by DatePlayed** (`:76-79`); `GetAllAsync` returns all records (`:84-88`);
  `ExportToCsvAsync` reads via `GetByDateRangeAsync` and writes the exact header/row format at
  `SongUsageService.cs:102-110` — this method stays as-is (it only calls the public API).
- Model `WorshipPlayer.Core\Models\SongUsageRecord.cs`: `Id` string (`:11`), `SongTitle` (`:14`),
  `SongId` (`:17`), `ServiceName` (`:20`), `ServiceDate` DateTime (`:23`), `DatePlayed` DateTime (`:26`),
  `CcliNumber` (`:29`), `Artist` (`:32`), `OrganizationId` (`:35`). All strings default `""`.
- Consumers that MUST keep working with ZERO changes (they only touch the public API):
  `WorshipPlayer.Core\Services\CcliReportService.cs` (calls `GetByDateRangeAsync` at `:28`) and
  `WorshipPlayer.Core\Services\SongUsageMapper.cs` (`FromSong` builds a record only, `:22-37`;
  leaves `Id` empty and does not set `DatePlayed`, relying on the service's stamping behavior).
- Construction site to edit: `WorshipPlayer.UI\Services\PresenterIntegration.cs:104-105`
  (`var usagePath = Path.Combine(appFolder, "song-usage.json"); SongUsageService = new SongUsageService(usagePath);`).
  `appFolder = AppDomain.CurrentDomain.BaseDirectory` is defined at `PresenterIntegration.cs:81`.
  Bible is constructed the same way at `PresenterIntegration.cs:119-120`
  (`var biblePath = Path.Combine(appFolder, "bible.db"); BibleDatabaseService = new BibleDatabaseService(biblePath);`).
  NOTE: `SongLibraryService = new SongLibraryService(_songManager, SongUsageService)` at
  `PresenterIntegration.cs:106` consumes the `SongUsageService` instance — keep that line and
  ordering intact.
- Existing tests to adapt / extend:
  `WorshipPlayer.Tests\SongUsageServiceTests.cs` (constructs `new SongUsageService(_storagePath)`
  with a temp `song-usage.json` at `:17-23`, deletes the temp folder in `Dispose` at `:25-29`;
  the last test `LogUsageAsync_PersistsToFile` at `:217-227` asserts the JSON file contents — this
  one must change). `WorshipPlayer.Tests\SongUsageMappingTests.cs` and
  `WorshipPlayer.Tests\CcliReportServiceTests.cs` also construct `SongUsageService` against temp
  paths and must keep passing.
- Build command (README `:72-73`): `dotnet build` on `WorshipBacktrackPlayer.sln`.
  Test command: `dotnet test` (no custom runner; xunit). Baseline: 0 errors, 4 pre-existing
  warnings, ~569 tests pass.

## Architecture decisions (confirmed by user — recorded here, not re-decided)

- ONE shared SQLite file `app.db` under `AppDomain.CurrentDomain.BaseDirectory` for all user-data
  stores. Bible stays in its SEPARATE `bible.db`. Rationale: user directive; keeps Bible's large
  seeded corpus independent of churn in user-data stores.
- Microsoft.Data.Sqlite directly, NO EF Core; mirror `BibleDatabaseService` style
  (`Data Source=`, `CREATE TABLE IF NOT EXISTS`, per-call connection, `ClearAllPools()` in tests).
- Thin helper only — a connection/path factory plus per-store schema init. No repository framework.
- Dates stored as ISO-8601 UTC text (round-trip format `"o"`), parsed back with
  `DateTime.Parse(..., CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)`. Rationale:
  preserves full precision and UTC kind so `GetByDateRangeAsync`'s `DatePlayed` comparisons and the
  CCLI report's `g.Max(DatePlayed)` behave exactly as the in-memory JSON version did, and keeps the
  CSV date formatting (`yyyy-MM-dd` / `yyyy-MM-dd HH:mm:ss`) unchanged.

## Phase-0 helper design

Class `AppDatabase` in `WorshipPlayer.Core\Services\AppDatabase.cs`, namespace
`WorshipPlayer.Core.Services`. Responsibilities: own the `app.db` path and hand out connections;
nothing store-specific lives here.

- `public const string DefaultFileName = "app.db";`
- `public static string DefaultPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFileName);`
- `public AppDatabase(string databasePath)` — stores `DatabasePath` and
  `_connectionString = $"Data Source={databasePath}"` (identical shape to `BibleDatabaseService:22`).
- `public string DatabasePath { get; }`
- `public SqliteConnection CreateConnection() => new SqliteConnection(_connectionString);`
  (caller opens/disposes, exactly like each Bible method does per-call).
- `public void EnsureDirectory()` — mkdir the parent dir if missing (mirrors
  `BibleDatabaseService.InitializeAsync:30-32`); safe to call repeatedly.

How a future phase registers/owns its table: each store keeps its own `CREATE TABLE IF NOT EXISTS`
DDL and runs it against a connection from `AppDatabase.CreateConnection()` during its own init.
Because every DDL statement is `IF NOT EXISTS`, multiple stores sharing one `app.db` can each run
their init idempotently and in any order with no conflict — this satisfies "safe to call repeatedly
and from multiple stores." No central table registry is needed for Phase 0/1; a store simply calls
`EnsureInitializedAsync()` on itself (see Phase 1).

How a test injects a path: construct `new SongUsageService(dbPath)` with
`Path.Combine(tempFolder, "app.db")` (same injection seam the service already exposes). Phase-0
`AppDatabase` is likewise constructable with any path, so tests never touch the real app folder.

## SongUsage table schema (owned by SongUsageService)

```sql
CREATE TABLE IF NOT EXISTS SongUsage (
    Id             TEXT PRIMARY KEY,
    SongTitle      TEXT NOT NULL DEFAULT '',
    SongId         TEXT NOT NULL DEFAULT '',
    ServiceName    TEXT NOT NULL DEFAULT '',
    ServiceDate    TEXT NOT NULL,            -- ISO-8601 round-trip ("o"), UTC
    DatePlayed     TEXT NOT NULL,            -- ISO-8601 round-trip ("o"), UTC
    CcliNumber     TEXT NOT NULL DEFAULT '',
    Artist         TEXT NOT NULL DEFAULT '',
    OrganizationId TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_songusage_dateplayed ON SongUsage(DatePlayed);
```

Column order mirrors `SongUsageRecord` fields. `idx_songusage_dateplayed` backs the
`GetByDateRangeAsync` range scan. Store `DateTime` with `value.ToString("o", CultureInfo.InvariantCulture)`;
read with `DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)`.

## SQL command shapes (parameterized — never string-interpolate values)

- LogUsage (single-row INSERT after the GUID/UtcNow stamping that stays in C#):
  ```sql
  INSERT INTO SongUsage
    (Id, SongTitle, SongId, ServiceName, ServiceDate, DatePlayed, CcliNumber, Artist, OrganizationId)
  VALUES (@Id, @SongTitle, @SongId, @ServiceName, @ServiceDate, @DatePlayed, @CcliNumber, @Artist, @OrganizationId);
  ```
- GetByDateRange (DatePlayed inclusive, descending — matches `SongUsageService.cs:76-79`):
  ```sql
  SELECT Id, SongTitle, SongId, ServiceName, ServiceDate, DatePlayed, CcliNumber, Artist, OrganizationId
  FROM SongUsage
  WHERE DatePlayed >= @from AND DatePlayed <= @to
  ORDER BY DatePlayed DESC;
  ```
  Pass `@from`/`@to` as the same `"o"` ISO-8601 text; lexicographic comparison of round-trip UTC
  strings is chronological, so the text range filter matches the former `DateTime` comparison.
- GetAll (matches `GetAllAsync` returning all records; no ordering guaranteed by the old API, but
  add `ORDER BY DatePlayed DESC` for determinism — the old version returned insertion order; the
  existing tests only assert `.Count`, so descending is safe):
  ```sql
  SELECT Id, SongTitle, SongId, ServiceName, ServiceDate, DatePlayed, CcliNumber, Artist, OrganizationId
  FROM SongUsage;
  ```
  Keep GetAll unordered (no ORDER BY) to match "returns all" semantics; map each row to a
  `SongUsageRecord`.

## Defensive one-time JSON → SQLite import

On first use (inside `EnsureInitializedAsync`, after `CREATE TABLE IF NOT EXISTS`), run the import
exactly once per process via a `_initialized` guard:

1. Compute the legacy JSON path. Keep a private `_legacyJsonPath` the ctor derives from the db path:
   `Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "song-usage.json")`. This makes the importer
   testable — a temp `app.db` and a temp `song-usage.json` sit in the same temp folder.
2. If `SELECT COUNT(*) FROM SongUsage == 0` AND `File.Exists(_legacyJsonPath)`:
   - Read + deserialize with the SAME `JsonSerializerOptions` the old code used
     (`WriteIndented`, `JsonStringEnumConverter`) — reuse the existing options block.
   - Wrap the whole read/parse/insert in try/catch and SWALLOW any exception (malformed/locked file
     must NEVER throw — matches the old `EnsureLoadedAsync` catch at `SongUsageService.cs:124-131`).
   - Insert each imported record AS-IS (do NOT re-stamp `DatePlayed`, do NOT reassign non-empty
     `Id`; preserve the historical values). If a record has an empty `Id`, assign a GUID so the
     PRIMARY KEY is satisfied. Use `INSERT OR IGNORE` on the batch so a duplicate `Id` can't throw.
3. The empty-table guard guarantees the import is non-duplicating: once rows exist, a later run
   finds `COUNT > 0` and skips the import entirely. The legacy JSON file is left on disk untouched
   (no delete) so the migration is reversible.

Document this behavior in the implementation report.

## Plan items

- [ ] 1. Add the Phase-0 `AppDatabase` helper.
      What: create `AppDatabase` with `DefaultFileName`, `DefaultPath`, ctor(dbPath),
      `DatabasePath`, `CreateConnection()`, `EnsureDirectory()` as specified above. Mirror
      `BibleDatabaseService.cs:17-23,30-32` for the connection-string and dir-creation shape.
      Files: `WorshipPlayer.Core\Services\AppDatabase.cs` (new).
      Verify: `dotnet build WorshipBacktrackPlayer.sln` from the worktree root — 0 errors
      (pre-existing 4 warnings OK).

- [ ] 2. Re-back `SongUsageService` with the `SongUsage` table in `app.db`, preserving the public
      API and all behavior.
      What: change the ctor to `SongUsageService(string databasePath)` where `databasePath` defaults
      conceptually to `<appFolder>/app.db` (callers pass the path; see item 4). Build an `AppDatabase`
      from it, derive `_legacyJsonPath` as a sibling `song-usage.json`. Add private
      `EnsureInitializedAsync()` that: creates the dir, runs the `CREATE TABLE IF NOT EXISTS` +
      index DDL, then runs the one-time defensive JSON import (empty-table guard). Rewrite
      `LogUsageAsync(record)` to keep the empty-Id→GUID and `DatePlayed=UtcNow` stamping, then do the
      single-row parameterized INSERT. Keep `LogUsageAsync(string,...)` delegating to the record
      overload unchanged. Rewrite `GetByDateRangeAsync` and `GetAllAsync` as the SELECTs above,
      mapping rows back to `SongUsageRecord` (parse dates with RoundtripKind). Leave `ExportToCsvAsync`
      logic untouched (it calls `GetByDateRangeAsync`). Remove the JSON `_records`/`_loaded`/`SaveAsync`
      machinery. Keep the class in namespace `WorshipPlayer.Core.Services`.
      Files: `WorshipPlayer.Core\Services\SongUsageService.cs` (modify).
      Verify: `dotnet build WorshipBacktrackPlayer.sln` — 0 errors. (Tests run in item 5/6.)

- [ ] 3. Confirm no changes needed in CcliReportService / SongUsageMapper.
      What: read both and confirm they only call the public `SongUsageService` API and the
      `SongUsageRecord` model — expect ZERO edits. If a signature mismatch surfaces, STOP and treat
      as a plan gap rather than changing these files.
      Files: none (verification only) — `WorshipPlayer.Core\Services\CcliReportService.cs`,
      `WorshipPlayer.Core\Services\SongUsageMapper.cs`.
      Verify: `dotnet build WorshipBacktrackPlayer.sln` still 0 errors with items 1-2 in place.

- [ ] 4. Point the construction site at `app.db`.
      What: at `PresenterIntegration.cs:104-105`, replace the two lines with an `app.db` path and
      the new ctor, consistent with the Bible construction at `:119-120`. Suggested:
      `var appDbPath = Path.Combine(appFolder, "app.db"); SongUsageService = new SongUsageService(appDbPath);`
      Keep line `:106` (`SongLibraryService = new SongLibraryService(_songManager, SongUsageService)`)
      and all ordering intact. `appFolder` is already in scope from `:81`.
      Files: `WorshipPlayer.UI\Services\PresenterIntegration.cs` (modify ~104-105).
      Verify: `dotnet build WorshipBacktrackPlayer.sln` — 0 errors.

- [ ] 5. Adapt existing SongUsage tests to inject a temp `app.db` and clear pools in teardown.
      What: in `SongUsageServiceTests.cs`, change setup to create a temp folder and
      `_dbPath = Path.Combine(_tempFolder, "app.db")`, construct `new SongUsageService(_dbPath)`.
      Add `using Microsoft.Data.Sqlite;` and in `Dispose` call `SqliteConnection.ClearAllPools()`
      BEFORE deleting the temp folder, wrapping the delete in try/catch(IOException) — mirror
      `BibleDatabaseServiceTests.cs:25-43`. Replace the file-contents assertion in
      `LogUsageAsync_PersistsToFile` (`:217-227`) with a persistence check that re-opens a NEW
      `SongUsageService` against the same `_dbPath` and asserts the record is read back (DB-backed
      persistence, not JSON file contents). Confirm `SongUsageMappingTests.cs` and
      `CcliReportServiceTests.cs` still construct the service correctly against a temp `app.db`
      (update their setup paths the same way if they pass a `song-usage.json` path).
      Files: `WorshipPlayer.Tests\SongUsageServiceTests.cs` (modify), and if needed
      `WorshipPlayer.Tests\SongUsageMappingTests.cs`, `WorshipPlayer.Tests\CcliReportServiceTests.cs`.
      Verify: `dotnet test WorshipBacktrackPlayer.sln --filter "FullyQualifiedName~SongUsage|FullyQualifiedName~CcliReport"`
      — all these tests pass.

- [ ] 6. Add a non-duplicating import test.
      What: add a test in `SongUsageServiceTests.cs` that writes a legacy `song-usage.json` (one or
      two records with known `Id`, `SongTitle`, and a fixed `DatePlayed`) into the temp folder BEFORE
      first service use, constructs `SongUsageService` against the sibling `app.db`, asserts
      `GetAllAsync` returns the imported record(s) with values preserved (Id and DatePlayed NOT
      re-stamped), then constructs a SECOND service instance against the same `app.db` and asserts
      the count is unchanged (import ran once, no duplicates). Add a second test: a malformed
      `song-usage.json` must not throw and yields an empty store.
      Files: `WorshipPlayer.Tests\SongUsageServiceTests.cs` (modify).
      Verify: `dotnet test WorshipBacktrackPlayer.sln --filter "FullyQualifiedName~SongUsage"`
      — new import tests pass.

- [ ] 7. Full build + full test run to confirm no regression.
      What: run the whole solution build and test suite; confirm the baseline (0 errors, 4
      pre-existing warnings, ~569 tests) holds with the migrated store.
      Files: none.
      Verify: `dotnet build WorshipBacktrackPlayer.sln` (0 errors) then
      `dotnet test WorshipBacktrackPlayer.sln` — all tests pass (count should remain ~569 plus the
      new import tests).

## Scope confirmation

In scope ONLY: Phase-0 `AppDatabase` helper, `SongUsageService` backing store, its construction
site in `PresenterIntegration.cs`, and its tests. NO other store (alerts, templates, media,
settings, service orders, songs/setlists, bible) is touched. Bible stays in its own `bible.db`.
Large/binary files remain on disk. CcliReportService and SongUsageMapper remain unchanged.

## Gaps / assumptions

- Assumption: `app.db` lives beside `bible.db` under `AppDomain.CurrentDomain.BaseDirectory` — the
  user confirmed one shared `app.db` in the app folder.
- Assumption: legacy `song-usage.json` for import is the sibling of `app.db` in the same folder. In
  production both are under the app folder; in tests both are under the temp folder. If a real
  deployment had `song-usage.json` elsewhere, that corpus would not auto-import — acceptable because
  the survey confirms no committed corpus and runtime data lives in the app folder.
- Assumption: `GetAllAsync` ordering is unspecified by the old contract and existing tests assert
  only `.Count`, so leaving it unordered is safe.
