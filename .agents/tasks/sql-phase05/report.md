# SQL Migration Phase 5 — Service Orders → shared SQLite app.db — Completion Report

Branch: `feature/sql-phase05-service-orders`
Worktree: `.worktrees/sql-phase05`
Solution: `WorshipBacktrackPlayer.sln`
Iteration: FIRST (no `review.json` present).

---

## 1. Chosen storage shape + justification

**DataJson-per-order ROW, not full relational.** Each `ServiceOrder` is persisted as ONE row in a
new `ServiceOrders` table whose `DataJson TEXT` column holds the whole order serialized with the
manager's existing `_jsonOptions` (`WriteIndented`, `JsonIgnoreCondition.WhenWritingNull`,
`JsonStringEnumConverter`). Promoted real columns carry only the fields needed for
listing/sorting/lookup.

Justification: the object graph is deep — `ServiceOrder` → `Items[]` (`ServiceItem` with
Song/Scripture/Media/Theme/AutoAdvance fields + `Slides[]`) → `Groups[]` (ids only, single source
of truth) → `ServiceItemSettings` (nullable). Three suites assert *serialization* semantics
directly against stored bytes (`WhenWritingNull` omits `startTime`/`autoAdvanceSettings`; enums as
strings). A DataJson column preserves all of this byte-for-byte with zero re-mapping risk, exactly
the pattern already shipped and approved in Phase 3 (`MediaLibraryService`). Full relational
decomposition would reproduce every additive/nullable/enum rule across ~15 columns per item plus
join tables for groups/slides, for no behavioral gain.

## 2. Promoted columns

`ServiceOrders` table (`ServiceOrderManager.cs`, `CreateSchemaSql`):

| Column         | Type              | Purpose |
|----------------|-------------------|---------|
| `FilePath`     | TEXT PRIMARY KEY  | Normalized full path = identity (mirrors `_currentServicePath`); the upsert/prune key |
| `Id`           | TEXT              | Order id (preserved on import) |
| `Name`         | TEXT              | `ServiceFileInfo` / listing display |
| `Date`         | TEXT              | Listing sort key (stored date) |
| `StartTime`    | TEXT NULL         | ISO-8601 round-trip string or NULL — preserves nullable semantics |
| `ServiceType`  | TEXT              | Listing display / filter |
| `LastWriteUtc` | TEXT              | ISO-8601; from the file's `LastWriteTimeUtc` when present else `UtcNow` |
| `DataJson`     | TEXT NOT NULL     | Whole order serialized with `_jsonOptions` — lossless round-trip |

These cover everything `ServiceFileInfo` and `ServiceListingBuilder` consume (sorts by
StartTime-or-Date then LastWriteUtc), so a future SQLite-sourced listing is possible without
reparsing JSON.

## 3. How the path contract maps to rows

The public API is path-oriented and the UI treats a file path as a service's identity
(`GetFilePathFor` → `_currentServicePath`; `LoadAsync(path)`; `SaveAsync(path?)`;
`ListServiceFilesAsync`; and an out-of-band `File.Delete(path)` for deletes at
`OperatorWindow.xaml.cs:463`). The mirror is keyed by `Path.GetFullPath(filePath)` (see
`NormalizePath`), so one JSON file ↔ one `ServiceOrders` row by normalized path. The JSON files
remain **authoritative**; app.db is a reconciled **mirror**:

- `SaveAsync` writes the JSON file exactly as before, then upserts the row keyed by that path.
- `LoadAsync(path)` reads the file exactly as before, then refreshes that order's row (keeps the
  mirror fresh for files edited out-of-band), then fires `ServiceOrderLoaded`.
- `LoadAllAsync` / `ListServiceFilesAsync` scan files exactly as before (same return value and
  ordering), then upsert each scanned file and prune rows whose `FilePath` is no longer on disk —
  absorbing the UI's out-of-band `File.Delete`.

### Deviation from the brief (explicitly flagged, per plan Decision 2/3)

The brief says "stop writing new JSON files." That is incompatible with keeping all tests green and
minimizing blast radius: four suites hard-assert the on-disk `.json`
(`ServiceOrderPersistenceTests`, `AutoAdvanceSettingsPersistenceTests`, `ServiceDateNameTests`,
`ServiceListFilesTests`) and the UI deletes via `File.Delete(path)` directly. Service Orders are
categorically different from prior phases because the API *is* file paths. Resolution per the
authoritative plan: JSON stays authoritative and continues to be written; app.db is a reconciled
mirror. This still delivers Phase 5's goal (Service Orders live in app.db via `AppDatabase`) while
preserving behavior byte-for-byte. Literal JSON removal would require rewriting 4 test suites and
the UI delete/rename/list paths and must be a separate, signed-off change.

## 4. Import behavior + old-file disposition

`ImportFilesIfNeededAsync` (invoked once per process from `EnsureInitializedAsync`, mirroring
`MediaLibraryService.ImportSidecarsIfNeededAsync`):

- Runs only if the `ServiceOrders` table is empty (non-duplicating guard) AND `*.json` files exist
  under the base path.
- Deserializes each file with `_jsonOptions`; skips a single malformed file and continues.
- Assigns a GUID id only when missing; preserves historical ids/dates.
- `INSERT OR IGNORE` inside one transaction.
- Tolerates a missing folder / missing / malformed files; **never throws** (outer try/catch).
- **JSON files are LEFT ON DISK and remain authoritative — nothing is deleted.** New saves continue
  to write JSON (unlike prior phases) because the files are the identity.

## 5. Event-preservation evidence

`ItemChanged` and `ServiceOrderLoaded` fire from the SAME places, unchanged. In `LoadAsync`, the
mirror upsert runs **before** `ServiceOrderLoaded?.Invoke(...)` and is wrapped in a best-effort
`UpsertRowSafeAsync` (swallows any DB failure), so a mirror failure can never suppress or reorder
the event. All mirror DB work (`UpsertRowSafeAsync`, `ReconcileSafeAsync`) is try/catch-wrapped so
the file-based public behavior and both events are byte-for-byte unaffected. Parity is proven by
the full existing suite staying green, including `PresenterEngine*Tests` (27) which subscribe to
these events, and the behavioral service-order suites (navigation/group/persistence) which assert
the event-driven `CurrentItem`/index transitions.

## 6. File:line trace (end to end)

- `AppDatabase` (`WorshipPlayer.Core/Services/AppDatabase.cs`) — reused as-is: `CreateConnection()`,
  `EnsureDirectory()`, `DefaultPath`. No second helper, no EF, bible.db untouched.
- Schema init → `const CreateSchemaSql` + `EnsureInitializedAsync()`
  (`ServiceOrderManager.cs`) run on first DB touch (`CREATE TABLE IF NOT EXISTS ServiceOrders(...)`).
- `SaveAsync` → unchanged file write + `UpsertRowSafeAsync(CurrentServiceOrder, filePath)`.
- `LoadAsync` → unchanged file read + `UpsertRowSafeAsync(serviceOrder, filePath)` **before**
  `ServiceOrderLoaded?.Invoke`.
- `LoadAllAsync` / `ListServiceFilesAsync` → unchanged file scan + `ReconcileSafeAsync(scanned)`
  (upsert each scanned file; prune rows whose `FilePath` is no longer on disk). Return value and
  ordering unchanged.
- `UpsertRowAsync` → `INSERT … ON CONFLICT(FilePath) DO UPDATE`; `BindOrder` binds promoted columns
  + `DataJson`; `StartTime` → ISO string or `DBNull`.
- `ImportFilesIfNeededAsync` → one-time, empty-table-guarded, never-throwing JSON import.
- Group/item/nav ops (`CreateGroup…GetUngroupedItemIds`, `AddItem/RemoveItem/MoveItem`,
  `GoToItem/NextItem/PreviousItem/GetSongItems/GetNextSongItem`) → UNCHANGED; mutate
  `CurrentServiceOrder` in memory; persistence still flows only through `SaveAsync`.
- Events `ItemChanged` / `ServiceOrderLoaded` → fire from the same places, unchanged.
- Construction: `PresenterIntegration.cs` computes `appDbPath = Path.Combine(appFolder,"app.db")`
  **above** the manager construction and passes it as the new 2nd arg:
  `new ServiceOrderManager(serviceOrdersPath, appDbPath)`. The duplicate later `appDbPath`
  declaration was removed; the sibling stores (SongUsage/Media/Alert/Template) reuse the same path.

## 7. Setlists / scope

Setlists NOT migrated (Phase 6). `CreateFromSetlist` still consumes a `Setlist` object and songs
still load from their own store. The `ServiceOrder/ServiceItem/ServiceGroup/ServiceItemSettings/
Slide` model shapes are unchanged (additive storage only; still JSON-round-trippable). Scope was
limited to `ServiceOrderManager`, its one production construction site (`PresenterIntegration`),
and the tests.

## 8. Constructor + injection

Added optional `string? databasePath = null` 2nd ctor param → `new AppDatabase(databasePath ??
AppDatabase.DefaultPath)`. The existing single-arg constructor behavior is intact, so every existing
`new ServiceOrderManager(tempFolder)` call site compiles unchanged and gets a default app.db. Tests
inject a temp app.db via the 2nd arg.

## 9. Build / test results (recorded)

- `dotnet build WorshipBacktrackPlayer.sln -c Debug --no-incremental` → **0 Error(s), 4 Warning(s)**
  (the 4 pre-existing warnings: `MediaLibraryPanel._currentView`, `ChordChartWindow` CS8604 null
  arg, `WaveformEditorWindow._inReviewMode`). Matches the acceptance criterion exactly.
- `dotnet test WorshipPlayer.Tests` (full suite) → **Passed! Failed: 0, Passed: 607, Skipped: 0,
  Total: 607.** Base was 601; +6 new (3 import + 3 path-contract).
- Service-order + related filter (`ServiceOrder|ServiceList|ServiceDateName|DefensiveLoad|
  AutoAdvanceSettingsPersistence`) → **53 passed** (47 pre-existing parity + 6 new).
- `PresenterEngine` filter → **27 passed** (event-driven parity).

### New tests added

- `WorshipPlayer.Tests/ServiceOrderSqliteImportTests.cs` — one-time non-duplicating import
  preserving items/groups/settings; skips corrupt; re-construct does not duplicate; empty folder
  never throws / leaves table empty.
- `WorshipPlayer.Tests/ServiceOrderPathContractTests.cs` — `GetFilePathFor`/`LoadAsync(path)`
  round-trip after migration; save-with-default-path writes one file + mirror row;
  rename-by-save leaves both files + both rows; out-of-band `File.Delete` is pruned from the mirror
  on the next list.

All new tests inject a temp app.db and call `SqliteConnection.ClearAllPools()` in teardown.

## 10. Verified vs not verified

Verified: solution builds (0 errors / 4 pre-existing warnings); full 607-test suite passes
including every named parity suite (`ServiceOrderPersistenceTests`, `ServiceOrderManagerGroupTests`,
`ServiceListFilesTests`, `ServiceDateNameTests`, `DefensiveLoadTests`,
`AutoAdvanceSettingsPersistenceTests`, `PresenterEngine*Tests`) plus the new import/path-contract
tests; the DataJson round-trip preserves items/groups/per-item settings; the mirror upsert/prune and
one-time import behave per spec; `PresenterIntegration` injects the shared app.db path and compiles.

Not verified: no runtime/manual UI run of the WPF app (headless CI environment); NDI/stage-display
and other UI-only paths beyond what the automated suite exercises; literal "stop writing JSON"
behavior was intentionally NOT implemented (see Deviation in §3) and therefore not verified.
