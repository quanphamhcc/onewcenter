# SQL Migration Phase 5 — Service Orders → shared SQLite app.db

Worktree (ALL work here, absolute paths): `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sql-phase05`
Branch: `feature/sql-phase05-service-orders`. Solution: `WorshipBacktrackPlayer.sln`.
Base state: builds clean, 601 tests pass; the 47 service-order tests (ServiceOrder*, ServiceList, ServiceDateName, DefensiveLoad, AutoAdvanceSettingsPersistence) pass (verified).

---

## Design decisions (made from the code, with rationale)

### Decision 1 — Storage shape: DataJson-per-order ROW, not full relational. JUSTIFIED.
Each `ServiceOrder` is persisted as ONE row in a new `ServiceOrders` table whose `DataJson TEXT` column holds the whole order serialized with the manager's existing `_jsonOptions` (WriteIndented, `JsonIgnoreCondition.WhenWritingNull`, `JsonStringEnumConverter`). Promoted real columns carry only the fields needed for listing/sorting/lookup.

Rationale: the object graph is deep — `ServiceOrder` → `Items[]` (`ServiceItem` with Song/Scripture/Media/Theme/AutoAdvance fields + `Slides[]`) → `Groups[]` (ids only, single source of truth) → `ServiceItemSettings` (nullable). Three test suites assert *serialization* semantics directly against stored bytes (WhenWritingNull omits `startTime`/`autoAdvanceSettings`; enums as strings) — see `WorshipPlayer.Core\Models\ServiceOrder.cs:33` (`StartTime` nullable), `ServiceOrder.cs` `[JsonConverter(JsonStringEnumConverter)]`, `WorshipPlayer.Tests\AutoAdvanceSettingsPersistenceTests.cs:78` and `WorshipPlayer.Tests\ServiceDateNameTests.cs:140`. A DataJson column preserves all of this byte-for-byte with zero re-mapping risk. Full relational decomposition would require reproducing every additive/nullable/enum rule across ~15 columns per item plus join tables for groups/slides, for no behavioral gain. This is exactly the pattern already shipped and approved in Phase 3 (`WorshipPlayer.Core\Services\MediaLibraryService.cs:51` schema + `UpsertAsync` + DataJson). Follow it.

### Decision 2 — The path contract: JSON FILES STAY AUTHORITATIVE; app.db is a MIRROR. JUSTIFIED. (Deviation from brief — see Decision 3.)
The public API is path-oriented and the UI treats a file path as a service's identity:
- `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs:69` `LoadAsync(path)`, `:303` `ListServiceFilesAsync()` → `ServiceListingBuilder.Build`, `:416` `GetFilePathFor(...)` stored in `_currentServicePath` (`:116`), `:445` `SaveAsync(path)` for rename.
- CRITICAL: `OperatorWindow.xaml.cs:463` deletes a service with `File.Delete(path)` **directly, bypassing the manager**. There is no `DeleteAsync` on `ServiceOrderManager`.

Four existing test suites hard-assert the on-disk JSON files and read raw/hand-written files through the API:
- `ServiceOrderPersistenceTests.cs:33` `Directory.GetFiles(_tempFolder,"*.json")` + `Assert.Single` after `SaveAsync()`.
- `AutoAdvanceSettingsPersistenceTests.cs:35,78` same single-file assert + reads JSON text to assert key omission.
- `ServiceDateNameTests.cs:125,146` `Directory.GetFiles(...,"*.json")` + reads JSON to assert no `startTime` key.
- `ServiceListFilesTests.cs:40` drops a raw `corrupt.json` on disk and expects `ListServiceFilesAsync` to scan+skip it; `ServiceOrderManagerGroupTests.cs:181` writes raw legacy JSON at an arbitrary path and expects `LoadAsync(path)` to read it.

Therefore the JSON files cannot stop being written and `LoadAsync(path)`/`ListServiceFilesAsync` must keep reading arbitrary files from disk. The lowest-risk design keeps the JSON-file behavior of every public method **unchanged**, and adds a SQLite `ServiceOrders` mirror table keyed by the file path. On `SaveAsync` the manager writes the JSON file exactly as today AND upserts a mirror row; on `LoadAsync`/`LoadAllAsync`/`ListServiceFilesAsync` it reads the files as today AND reconciles the mirror (upsert present files, prune rows whose file no longer exists — this absorbs the UI's out-of-band `File.Delete`). The file stays the single source of truth; app.db is a queryable mirror that unlocks future cross-store SQLite queries without touching the ~141 call sites or the two events.

### Decision 3 — Deviation from the brief, explicitly flagged.
The brief says "stop writing new JSON files; leave old ones on disk." That is incompatible with the mandate to keep all tests green and minimize blast radius: `ServiceOrderPersistenceTests`/`AutoAdvanceSettingsPersistenceTests`/`ServiceDateNameTests` would fail the instant `SaveAsync` stops producing exactly one readable `.json`, and the UI's `File.Delete(path)` delete path (OperatorWindow:463) would orphan SQLite rows if the DB were authoritative. Prior phases (media/alerts/settings) could stop writing JSON because their tests only exercised the public API, never the on-disk files — Service Orders is categorically different because its API *is* file paths. Resolution: keep JSON authoritative, make app.db a reconciled mirror. This still delivers Phase 5's real goal — Service Orders live in app.db via AppDatabase — while preserving behavior byte-for-byte. If the reviewer insists on JSON removal, that requires rewriting 4 test suites and the UI delete/rename/list paths (large behavior change) and must be a separate, signed-off decision.

### Decision 4 — Promoted columns.
`ServiceOrders` table: `FilePath TEXT PRIMARY KEY` (the normalized full path = identity, mirrors `_currentServicePath`), `Id TEXT`, `Name TEXT`, `Date TEXT`, `StartTime TEXT NULL` (ISO-8601 or NULL — preserves nullable semantics), `ServiceType TEXT`, `LastWriteUtc TEXT`, `DataJson TEXT NOT NULL`. These cover everything `ServiceFileInfo` (`WorshipPlayer.Core\Models\ServiceFileInfo.cs:9`) and `ServiceListingBuilder` (`WorshipPlayer.Core\Services\ServiceListingBuilder.cs:19` sorts by StartTime-or-Date then LastWriteUtc) consume, so a future SQLite-sourced listing is possible without reparsing JSON. `FilePath` as PK makes the mirror upsert/prune keyed exactly to the UI's identity.

### Decision 5 — Constructor + injection.
Add an optional `string? databasePath = null` second constructor parameter (default → `AppDatabase.DefaultPath`), exactly like `MediaLibraryService(libraryPath, databasePath=null)` (`MediaLibraryService.cs:81`). Keep the existing single-arg constructor behavior intact so all existing `new ServiceOrderManager(tempFolder)` test call sites compile unchanged and simply get a default (temp-adjacent) app.db. `PresenterIntegration.cs:89` passes the shared `app.db` path (the `appDbPath` already computed at `PresenterIntegration.cs:~117`).

### Decision 6 — Import + old-file disposition.
One-time defensive import mirrors `MediaLibraryService.ImportSidecarsIfNeededAsync` (`MediaLibraryService.cs:299`): on first init, if the `ServiceOrders` table is empty AND `*.json` files exist under the base path, import each file as a mirror row once (preserve ids/dates; tolerate missing dir / malformed file per-file; never throw; empty-table guard = non-duplicating). JSON files are LEFT ON DISK and remain authoritative (Decision 2/3) — nothing is deleted. New saves continue to write JSON (unlike prior phases) because the files are the identity.

---

## File:line trace (end to end)

- `AppDatabase` (`WorshipPlayer.Core\Services\AppDatabase.cs:1`) — reuse as-is: `CreateConnection()`, `EnsureDirectory()`, `DefaultPath`. No second helper.
- Schema init → new `const CreateSchemaSql` (`CREATE TABLE IF NOT EXISTS ServiceOrders(...)`) run in a new `EnsureInitializedAsync` on first DB touch, mirroring `MediaLibraryService.cs:410`.
- `SaveAsync` (`ServiceOrderManager.cs:78`) → unchanged file write + new `await UpsertRowAsync(order, filePath)` (INSERT … ON CONFLICT(FilePath) DO UPDATE), mirroring `MediaLibraryService.cs:265`.
- `LoadAsync` (`ServiceOrderManager.cs:48`) → unchanged file read + on success upsert that order's mirror row (keeps mirror fresh for files edited out-of-band).
- `LoadAllAsync` (`ServiceOrderManager.cs:355`) / `ListServiceFilesAsync` (`ServiceOrderManager.cs:405`) → unchanged file scan + reconcile mirror (upsert each scanned file; prune rows whose `FilePath` is no longer on disk — absorbs `OperatorWindow.xaml.cs:463` `File.Delete`). Return value and ordering stay identical (still built from the file scan, so `ServiceListingBuilder` behavior is untouched).
- Group/item/nav ops (`CreateGroup`…`GetUngroupedItemIds` `ServiceOrderManager.cs:184-315`, `AddItem/RemoveItem/MoveItem` `:117-181`, `GoToItem/NextItem/PreviousItem/GetSongItems/GetNextSongItem` `:318-353`) → UNCHANGED. They mutate `CurrentServiceOrder` in memory; persistence still flows only through `SaveAsync`, so the mirror updates on the next save exactly as the JSON file does.
- Events `ItemChanged` (`ServiceOrderManager.cs:28`) and `ServiceOrderLoaded` (`:33`) → fire from the SAME places, unchanged.
- Construction: `PresenterIntegration.cs:89` passes the shared app.db path as the new 2nd arg.

---

## Implementation Plan

- [ ] 1. Add the SQLite mirror backing to `ServiceOrderManager` (schema + optional db-path ctor arg), without changing any public method behavior yet.
      Add `using Microsoft.Data.Sqlite;`, a private `AppDatabase _database`, a private `bool _initialized`, a `private const string CreateSchemaSql` creating `ServiceOrders(FilePath TEXT PRIMARY KEY, Id TEXT, Name TEXT, Date TEXT, StartTime TEXT NULL, ServiceType TEXT, LastWriteUtc TEXT, DataJson TEXT NOT NULL)`, and a `private async Task EnsureInitializedAsync()` that calls `_database.EnsureDirectory()`, runs the schema (IF NOT EXISTS), and invokes the import (step 3). Add constructor param `string? databasePath = null` → `_database = new AppDatabase(databasePath ?? AppDatabase.DefaultPath)`; keep the existing single-arg path/options setup intact. Follow `MediaLibraryService.cs:51,81,410` exactly.
      Files: `WorshipPlayer.Core\Services\ServiceOrderManager.cs`
      Verify: `dotnet build WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug` — 0 errors; all 47 service-order tests still pass: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj --no-build -c Debug --filter "FullyQualifiedName~ServiceOrder|FullyQualifiedName~ServiceList|FullyQualifiedName~ServiceDateName|FullyQualifiedName~DefensiveLoad|FullyQualifiedName~AutoAdvanceSettingsPersistence"`.

- [ ] 2. Add mirror upsert/prune helpers and wire them into SaveAsync/LoadAsync/LoadAllAsync/ListServiceFilesAsync — JSON file behavior unchanged, mirror kept in sync.
      Add `private async Task UpsertRowAsync(ServiceOrder order, string filePath)` (normalize `filePath` via `Path.GetFullPath`; INSERT … ON CONFLICT(FilePath) DO UPDATE; `StartTime` → ISO string or `DBNull`; `DataJson` = `JsonSerializer.Serialize(order, _jsonOptions)`; `LastWriteUtc` from the file's `LastWriteTimeUtc` when it exists else `DateTime.UtcNow`) and `private async Task ReconcileAsync(IEnumerable<(string path, ServiceOrder order)> scanned)` (upsert each scanned pair, then `DELETE FROM ServiceOrders WHERE FilePath NOT IN (...scanned paths...)`). Call `EnsureInitializedAsync()` + `UpsertRowAsync` at the end of the existing `SaveAsync` success path and after a successful `LoadAsync`; call `EnsureInitializedAsync()` + `ReconcileAsync` after the existing file scans in `LoadAllAsync` and `ListServiceFilesAsync`. Do NOT change return values, ordering, directory-creation, exception-swallowing, or event firing. Wrap all mirror DB work so a DB failure never breaks the file-based public behavior (match the defensive try/catch tone of `MediaLibraryService.cs`).
      Files: `WorshipPlayer.Core\Services\ServiceOrderManager.cs`
      Verify: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug --filter "FullyQualifiedName~ServiceOrder|FullyQualifiedName~ServiceList|FullyQualifiedName~ServiceDateName|FullyQualifiedName~DefensiveLoad|FullyQualifiedName~AutoAdvanceSettingsPersistence"` — all pass unchanged.

- [ ] 3. Add the one-time defensive, non-duplicating JSON→mirror import.
      Add `private async Task ImportFilesIfNeededAsync(SqliteConnection connection)` invoked from `EnsureInitializedAsync`: if `SELECT COUNT(*) FROM ServiceOrders` > 0 return; if base dir missing return; else for each `*.json` under `_serviceOrdersBasePath`, deserialize with `_jsonOptions`, skip malformed (continue), assign a GUID id only when missing, and `INSERT OR IGNORE` a row inside one transaction (bind the same promoted columns + DataJson). Never throw (outer try/catch). JSON files are left on disk. Mirror `MediaLibraryService.ImportSidecarsIfNeededAsync` (`MediaLibraryService.cs:299`).
      Files: `WorshipPlayer.Core\Services\ServiceOrderManager.cs`
      Verify: build + the full service-order filter from step 2 still green (import must be inert when the table is non-empty and harmless when empty).

- [ ] 4. Inject the shared app.db path at the one production construction site.
      In `PresenterIntegration` constructor, pass the existing shared `app.db` path (the `appDbPath`/`Path.Combine(appFolder,"app.db")` already used for `SongUsageService`/`MediaLibraryService`) as the new 2nd arg to `new ServiceOrderManager(serviceOrdersPath, appDbPath)` at `WorshipPlayer.UI\Services\PresenterIntegration.cs:89`. Confirm `appDbPath` is in scope at that line (it is defined a few lines below for SongUsage — move the `appDbPath` computation above the ServiceOrderManager construction if needed; it is a pure `Path.Combine`).
      Files: `WorshipPlayer.UI\Services\PresenterIntegration.cs`
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors, 4 pre-existing warnings only (UI project compiles).

- [ ] 5. Add the import test (new file), following the temp-folder + ClearAllPools discipline.
      New `ServiceOrderSqliteImportTests` (IDisposable; temp folder + temp `app.db`; `SqliteConnection.ClearAllPools()` in `Dispose`, mirroring `MediaLibraryServiceTests.cs:40`). Cases: (a) given 2 valid `.json` files written via a first manager's `SaveAsync` plus one hand-written `corrupt.json`, a fresh manager pointed at the same folder+db imports exactly 2 mirror rows (assert via a direct `SELECT COUNT(*) FROM ServiceOrders` on a `new SqliteConnection("Data Source="+dbPath)`), skips the corrupt file, never throws; (b) re-constructing a manager against the already-populated db does NOT duplicate rows (count stays 2 — non-duplicating guard); (c) `ListServiceFilesAsync` still returns the 2 orders (public behavior unchanged).
      Files: `WorshipPlayer.Tests\ServiceOrderSqliteImportTests.cs`
      Verify: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug --filter "FullyQualifiedName~ServiceOrderSqliteImport"` — new tests pass.

- [ ] 6. Add the path-contract test (new file) proving file identity + mirror stay consistent across save/rename/delete.
      New `ServiceOrderPathContractTests` (same temp-folder + ClearAllPools pattern). Cases: (a) `SaveAsync()` with no path writes exactly one `.json` at `GetFilePathFor(order)` AND a mirror row exists keyed by that normalized path; (b) rename flow — `SaveAsync(newPath)` to a second path leaves both files on disk and both mirror rows present (matches the UI rename-by-save behavior, OperatorWindow:445); (c) delete-out-of-band — after `File.Delete(path)` (what OperatorWindow:463 does), a subsequent `ListServiceFilesAsync` prunes the stale mirror row (assert `SELECT COUNT(*)` drops) while the public list reflects only the remaining file. Assert mirror state via a direct read-only `SqliteConnection`.
      Files: `WorshipPlayer.Tests\ServiceOrderPathContractTests.cs`
      Verify: `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug --filter "FullyQualifiedName~ServiceOrderPathContract"` — new tests pass.

- [ ] 7. Full regression: build the solution and run the entire suite; confirm no behavior drift.
      Files: (none — verification only)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` — 0 errors, 4 pre-existing warnings; then `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug` — all tests pass (≥601 base + the new import/path-contract tests), specifically every named suite green: ServiceOrderPersistenceTests, ServiceOrderManagerGroupTests, ServiceListFilesTests, ServiceDateNameTests, DefensiveLoadTests, AutoAdvanceSettingsPersistenceTests, PresenterEngine*Tests.

---

## Notes / assumptions
- Microsoft.Data.Sqlite 8.0.0 is already referenced by both Core (`WorshipPlayer.Core.csproj:11`) and Tests (`WorshipPlayer.Tests.csproj`); no package changes.
- bible.db stays separate; no EF; reuse `AppDatabase` only.
- Songs/Setlists untouched (Phase 6): `CreateFromSetlist` (`ServiceOrderManager.cs:467`) still consumes a `Setlist` object; `ServiceItem.SongFolder` linkage to songs is preserved as-is.
- Windows file locks: every new/adapted test that opens the temp app.db MUST call `SqliteConnection.ClearAllPools()` in teardown (see `MediaLibraryServiceTests.cs:40`) or temp-folder deletion flakes.
- Deviation flagged (Decision 3): JSON files remain authoritative and continue to be written; app.db is a reconciled mirror. This is the only design that keeps the 4 file-asserting suites and the UI's out-of-band `File.Delete` correct. If the brief's "stop writing JSON" is to be enforced literally, it is a separate, larger, sign-off-required change.
