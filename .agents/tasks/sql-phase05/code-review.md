# Service Orders mirrored into shared SQLite app.db (Phase 5)

Phase 5 moves Service Orders into the shared `app.db` without disturbing the path-oriented public contract the operator UI and PresenterEngine drive off. Rather than making SQLite authoritative, the change keeps the on-disk JSON files as the source of truth and adds a reconciled `ServiceOrders` mirror table (`FilePath` PK + promoted listing columns + a `DataJson` blob) that `SaveAsync`/`LoadAsync` upsert and `LoadAllAsync`/`ListServiceFilesAsync` reconcile (upsert present, prune vanished). A one-time, empty-table-guarded import seeds the mirror from existing JSON. All DB work is best-effort wrapped so a database failure can never alter the file-based behavior or the `ItemChanged`/`ServiceOrderLoaded` events.

Watch for: the deliberate deviation from the brief's literal "stop writing JSON files" — JSON stays authoritative and app.db is a mirror (confirmed, well-justified, matches plan Decisions 2/3). This is the only behavioral note; everything else verifies clean.

**Verdict**: APPROVED

## High-level view

The storage shape is DataJson-per-order: one row per service, promoted columns (`Id`, `Name`, `Date`, `StartTime` nullable, `ServiceType`, `LastWriteUtc`) carry listing/sort data and the whole order round-trips through `DataJson` serialized with the manager's existing `_jsonOptions`. This preserves the nullable/additive/enum-as-string serialization rules byte-for-byte with zero re-mapping risk and matches the Phase 3 MediaLibraryService pattern.

The public API is untouched: every method (`LoadAsync(path)`, `SaveAsync(path?)`, `LoadAllAsync`, `ListServiceFilesAsync`, `GetFilePathFor`, `CreateNew`, `CreateFromSetlist`, add/remove/move, the group ops, the nav ops, private `GetDefaultFilePath`/`SanitizeFileName`) keeps its exact signature and return value. The only signature change is an additive optional second constructor parameter (`string? databasePath = null`), so existing single-arg call sites compile and behave unchanged.

The mirror is keyed by `Path.GetFullPath(filePath)`, matching the UI's notion that a file path is a service's identity — including the UI's out-of-band `File.Delete(path)`, which the reconcile prune absorbs on the next list. Events fire from the same places with the same ordering; the load-time upsert runs before `ServiceOrderLoaded` and is swallow-wrapped so a DB fault can't suppress or reorder it.

The import is one-time (empty-table guard), tolerant of a missing folder and malformed files (skips and continues), never throws, and leaves JSON on disk. Scope is strictly Phase 5: `ServiceOrderManager`, the single `PresenterIntegration` construction site, and two new test files. Setlists are not migrated and model shapes are unchanged beyond the additive storage already present.

The one deviation from the brief is intentional and flagged in the plan: JSON files stay authoritative and continue to be written. The brief's literal "stop writing JSON" is incompatible with the four file-asserting test suites and the UI's direct `File.Delete`, so the coder kept JSON authoritative and made app.db a mirror. Phase 5's real goal — Service Orders living in app.db via `AppDatabase` — is delivered.

<details>
<summary>Issues (1)</summary>

1. **Brief deviation: JSON remains authoritative (informational)** — The brief's literal "stop writing JSON files" was not implemented; JSON stays the source of truth and app.db is a reconciled mirror. This is a sound, explicitly-flagged design decision (plan Decisions 2/3) driven by the file-asserting test suites and the UI's out-of-band `File.Delete`. No action required unless a product decision later mandates DB-as-source-of-truth, which is a separate signed-off change.

</details>

<details>
<summary>Details</summary>

### Storage shape and lossless round-trip

Each order is one `ServiceOrders` row: `FilePath TEXT PRIMARY KEY`, promoted `Id/Name/Date/StartTime(NULL)/ServiceType/LastWriteUtc`, and `DataJson TEXT NOT NULL` holding the full order serialized with the manager's `_jsonOptions` (`WriteIndented`, `JsonIgnoreCondition.WhenWritingNull`, `JsonStringEnumConverter`). Because the mirror serializes with the same options the JSON file uses, the deep graph — `Items[]` with Song/Scripture/Media/Theme/AutoAdvance fields and `Slides[]`, `Groups[]` holding id references only, nullable `AutoAdvanceSettings`, nullable `StartTime`, enums-as-strings, `WhenWritingNull` omission — round-trips losslessly with no re-mapping surface (confirmed: `ServiceOrder.cs` shows `StartTime` as `DateTime?` and the enums carry `[JsonConverter(typeof(JsonStringEnumConverter))]`; `BindOrder` writes `DataJson = JsonSerializer.Serialize(order, _jsonOptions)`). `StartTime` is additionally promoted to its own column as an ISO-8601 round-trip string or `DBNull`, preserving the nullable semantics in the promoted copy as well (confirmed in `BindOrder`).

The promoted columns cover everything `ServiceFileInfo` and `ServiceListingBuilder` consume, so a future SQLite-sourced listing is possible without reparsing JSON — but the current listing path still builds its return value purely from the file scan, so sort/order behavior is untouched (confirmed: `ListServiceFilesAsync` adds to `files` from the scan and returns `files`; the reconcile does not feed the return value).

### Path identity contract preserved

The mirror key is `NormalizePath(filePath)` → `Path.GetFullPath`, so one JSON file maps to one row by normalized path, matching `_currentServicePath`/`GetFilePathFor` identity (confirmed). `GetFilePathFor` still delegates to the private `GetDefaultFilePath`, `LoadAsync(path)` still reads an arbitrary path, `SaveAsync(path?)` still defaults via `GetDefaultFilePath`, and `ListServiceFilesAsync` still scans and returns `ServiceFileInfo` in scan order (confirmed). The path-contract test exercises default-path save (one file + one row), rename-by-save (both files + both rows), and out-of-band `File.Delete` (pruned from mirror on next list) — the three UI flows that depend on path identity.

### Event semantics

In `LoadAsync` the mirror upsert (`UpsertRowSafeAsync`) runs before `ServiceOrderLoaded?.Invoke` and is wrapped in its own try/catch, so a DB fault is swallowed inside the helper and never reaches `LoadAsync`'s own catch — it cannot be misreported as a load failure, and it cannot suppress or reorder the event (confirmed by reading the method body). `ItemChanged` and the navigation/group/add/remove/move operations are byte-for-byte unchanged; they mutate `CurrentServiceOrder` in memory and persistence still flows only through `SaveAsync` (confirmed: those method bodies are outside the diff hunks and their signatures are intact).

### One-time import

`ImportFilesIfNeededAsync` runs from `EnsureInitializedAsync` (guarded by `_initialized`, so once per process). It returns early if the folder is missing, returns early if `SELECT COUNT(*) FROM ServiceOrders > 0` (the non-duplicating empty-table guard), skips a malformed file via per-file try/catch and continues, assigns a GUID id only when missing (preserving historical ids), inserts with `INSERT OR IGNORE` inside one transaction, and the whole method is wrapped in an outer try/catch so it never throws. JSON files are left on disk (confirmed by reading the method). The import test proves exactly-2-of-3 import with one corrupt file skipped, non-duplication on reconstruct, and empty-folder-never-throws.

### Failure mode: fail-safe toward the file

Every mirror entry point (`UpsertRowSafeAsync`, `ReconcileSafeAsync`, `ImportFilesIfNeededAsync`) swallows all exceptions by design, so the mirror can silently drift from the files if the DB is persistently unavailable. That is the correct trade-off here — the JSON files are authoritative and the mirror is reconciled on the next list/save — and it is what keeps the parity suites green. Worth being aware of: a mirror that fails to write leaves no log breadcrumb (the catch blocks are empty), so a chronically broken mirror would be invisible until someone queries app.db directly. Not blocking for Phase 5, since nothing reads the mirror for behavior yet.

### Prune query is parameterized

`ReconcileSafeAsync` builds the `DELETE … WHERE FilePath NOT IN (...)` prune with per-path `@keep{i}` parameters rather than string interpolation, and special-cases the empty-scan case to `DELETE FROM ServiceOrders` (confirmed). No injection surface from file paths, and the empty case correctly clears the mirror when the folder has no files.

### Construction and scope

`PresenterIntegration` computes `appDbPath = Path.Combine(appFolder, "app.db")` above the manager construction and passes it as the new second argument; the duplicate later `appDbPath` declaration was removed and the sibling stores reuse the same path (confirmed in diff). The constructor's new parameter defaults to `AppDatabase.DefaultPath`, so no other call site needs changes. `AppDatabase` is reused as-is (`CreateConnection`/`EnsureDirectory`/`DefaultPath`) with no second DB helper and no EF; bible.db is untouched (confirmed by reading `AppDatabase.cs` and the diff). `CreateFromSetlist(Setlist, List<Song>)` still consumes a `Setlist` object — Setlists are not migrated and songs are untouched (confirmed). The `git diff main` touches only `ServiceOrderManager.cs`, `PresenterIntegration.cs`, the two new test files, and the task docs.

### Build and test evidence (read from report/commit, not re-run)

The report records `dotnet build WorshipBacktrackPlayer.sln -c Debug --no-incremental` → 0 errors, 4 warnings (the named pre-existing ones), and `dotnet test WorshipPlayer.Tests` → 607 passed / 0 failed (601 base + 6 new: 3 import + 3 path-contract), with the service-order filter at 53 and the PresenterEngine filter at 27. Per the review instructions the suites were not re-run; the recorded evidence is specific (exact counts, named suites) and consistent with the diff (two new test files with 3 tests each; no production warning-introducing patterns). New tests inject a temp app.db and call `SqliteConnection.ClearAllPools()` in teardown, matching the required Windows file-lock discipline (confirmed by reading both test files).

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Core/Services/ServiceOrderManager.cs` — additive optional db-path ctor arg; schema const; `EnsureInitializedAsync`; best-effort upsert/reconcile/import helpers; `SaveAsync`/`LoadAsync`/`LoadAllAsync`/`ListServiceFilesAsync` wired to the mirror. Public API and events unchanged.
- `WorshipPlayer.UI/Services/PresenterIntegration.cs` — `appDbPath` hoisted above construction and passed as the manager's 2nd arg; duplicate declaration removed.
- `WorshipPlayer.Tests/ServiceOrderSqliteImportTests.cs` — one-time non-duplicating import, corrupt-skip, empty-folder-never-throws.
- `WorshipPlayer.Tests/ServiceOrderPathContractTests.cs` — default-path save, rename-by-save, out-of-band delete prune.
- `.agents/tasks/sql-phase05/plan.md`, `report.md` — task docs.

Full diff: `git -C .worktrees/sql-phase05 diff main`

</details>
