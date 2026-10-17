# Alerts and Templates stores migrated from JSON to SQLite app.db

Phase 2 of the JSON->SQLite migration moves the two custom-template stores — `AlertService` and `TemplateService` — off their JSON backing (an `alert-templates.json` file and a `Templates/*.json` folder) onto rows in the shared `app.db`, reusing the Phase-0 `AppDatabase` connection factory and copying the Phase-1 `SongUsageService` shape (per-call connection, `_initialized` guard, defensive one-time legacy import). No second DB helper was introduced, no EF, `bible.db` is left alone. The three built-in slide templates stay code-seeded and are never written as rows. Both public interfaces (`IAlertService`, `ITemplateService`) and both models are byte-for-byte unchanged, so the API surface is preserved. Watch for: nothing blocking — the diff is tightly scoped, build/test evidence is recorded in the commit (0 errors, 586 tests green), and the merge/import/built-in-protection logic is faithful to the prior behavior (confirmed).

**Verdict**: APPROVED

## High-level view

Persistence stays inside the two Presenter-side services; each now holds an `AppDatabase` built from an injected db path and owns a `CREATE TABLE IF NOT EXISTS` DDL that runs under an `_initialized` guard — the same self-initializing, store-local pattern `AppDatabase` was designed for. Enum-and-styling payload rides in a single `DataJson` blob (full `JsonSerializer.Serialize` of the model); only the query/filter fields (`Id`, `Name`, `OrganizationId`, and `TemplateCategory` for slides) are promoted to columns as a denormalized index. The blob is the source of truth on read.

The built-in slide templates are the one invariant that must survive the migration intact, and it does: `GetBuiltInTemplates()` is unchanged, the three const ids are unchanged, built-ins are merged ahead of custom rows and dedupe-win over any masquerading custom row, they are blocked from update (no DB write, flag preserved) and delete (returns false), and the legacy importer explicitly skips any file carrying a built-in id. Every custom row has `IsBuiltIn` forced false on read.

The legacy import for both stores is non-duplicating (empty-table COUNT guard), tolerant (missing file/folder returns, malformed input is swallowed, per-file try/catch for the folder case), and never throws — matching the old `EnsureLoadedAsync` catch semantics. Both construction sites in `PresenterIntegration` now pass the shared `appDbPath`, so existing `alert-templates.json` and `Templates/` data sitting beside `app.db` migrate on first run.

Scope is strictly Phase 2: the diff touches only the two services, the two test files, the one integration wiring file, and the task docs. Media, settings, service-orders, songs, and `bible.db` are untouched.

<details>
<summary>Issues (0)</summary>

No blocking or non-blocking findings. One informational note (not an action item): `AlertTemplate.Id` defaults to a fresh GUID rather than empty string, so the `SaveTemplateAsync` GUID-if-empty branch only fires when a caller explicitly passes `Id = ""`. This is faithful to the pre-migration behavior and is correctly exercised by the tests.

</details>

<details>
<summary>Details</summary>

### Public API parity (verification 2)

The interface files `IAlertService.cs` and `ITemplateService.cs` and the model files `AlertTemplate.cs` / `SlideTemplate.cs` show an empty diff against `main` (confirmed — `git diff main...feature/... -- <those four paths>` returns nothing). The implementations match every required member:

- `AlertService` (`WorshipPlayer.Presenter/Services/AlertService.cs`): `ShowAlertAsync` (:79), `DismissAlertAsync` (:88), `SaveTemplateAsync` (:97), `GetTemplatesAsync` (:120), `DeleteTemplateAsync` (:143), events `AlertShown` (:51) / `AlertDismissed` (:54), and `CurrentAlert` (:66) / `IsAlertActive` (:61) kept as in-memory auto-properties set only by `ShowAlertAsync`/`DismissAlertAsync` — never read from or written to the DB (confirmed).
- `TemplateService` (`WorshipPlayer.Presenter/Services/TemplateService.cs`): `GetAllAsync` (:75), `GetByIdAsync` (:95), `GetByCategoryAsync` (:102), `CreateAsync` (:110), `UpdateAsync` (:131), `DeleteAsync` (:165), `DuplicateAsync` (:188), `GetBuiltInTemplatesAsync` (:212), `ApplyToItemAsync` (:220), and static `GetBuiltInTemplates()` (:235) (confirmed).

Alert display state being in-memory is the explicit requirement and it holds: there is no column, no serialization, and no read path for `CurrentAlert`/`IsAlertActive`.

### Shared app.db, no second helper (verification 3)

Both services take a `string databasePath` and construct `new AppDatabase(databasePath)` (`AlertService.cs:71`, `TemplateService.cs:69`), the same factory `SongUsageService` uses. No new DB helper class was added — grep of the diff shows only `AlertService.cs`, `TemplateService.cs`, the two test files, `PresenterIntegration.cs`, and task docs changed. `Microsoft.Data.Sqlite` is the only data dependency (both files `using Microsoft.Data.Sqlite;`), no EF. `BibleDatabaseService`/`bible.db` is untouched — `PresenterIntegration.cs` still constructs it from its own `biblePath` (unchanged line in the diff) (confirmed).

Each store owns its own DDL (`AlertService.cs:41`, `TemplateService.cs:52`) run idempotently under `EnsureInitializedAsync`, exactly the "stores share app.db, each self-initializes with IF NOT EXISTS" contract documented in `AppDatabase.cs` (confirmed).

### Built-in slide templates stay code-seeded and protected (verification 4)

`GetBuiltInTemplates()` (`TemplateService.cs:235-299`) is the sole source of the three built-ins, unchanged, with the three const ids `builtin-default-centered` / `builtin-lower-third` / `builtin-trendy-line` (`:44-46`) and a `BuiltInIds` set (`:48`). Protection is enforced on every path:

- `GetAllAsync` (:75) seeds built-ins first, then appends custom rows only if their id is not already present (`ids.Add(custom.Id)` at :87), so a custom row carrying a built-in id can never create a duplicate and the code-seeded built-in wins.
- `CreateAsync` (:118) and `DuplicateAsync` (:206) force `IsBuiltIn = false` before insert.
- `UpdateAsync` short-circuits on a built-in id (`:138`), re-asserts `IsBuiltIn = true`, and returns without any DB write.
- `DeleteAsync` returns `false` for a built-in id without touching the DB (`:172`).
- The importer skips any file whose id is a built-in id (`:388`).
- `ReadCustomTemplatesAsync` forces `IsBuiltIn = false` on every row (`:324`).

Built-ins are therefore never rows and never imported (confirmed).

### Column-vs-blob schema and round-trip (verification 9)

Schemas match the report exactly. `AlertTemplates` promotes `Id`/`Name`/`OrganizationId` and stores the rest in `DataJson` (`AlertService.cs:41-49`); `SlideTemplates` additionally promotes `TemplateCategory` as the one real filter field for `GetByCategoryAsync` (`TemplateService.cs:52-60`). `BindTemplate` writes `DataJson = JsonSerializer.Serialize(template, JsonOptions)` — the full model — in both services (`AlertService.cs:230`, `TemplateService.cs:410`), and reads reconstruct the model from `DataJson` alone (`AlertService.cs:134`, `TemplateService.cs:321`), so the promoted columns are a pure index and the blob is the source of truth, as the report claims. `TemplateCategory` column stores `.ToString()` (`:407`), matching the blob's `JsonStringEnumConverter` output. `JsonOptions` (`WriteIndented`, `JsonStringEnumConverter`) is reused verbatim in both services, so enum-as-string parity holds for blob and import (confirmed).

### Defensive one-time import (verification 5)

Both importers follow `SongUsageService.ImportLegacyJsonIfNeededAsync`: outer `try/catch` that swallows (never throws), early return on missing source, `SELECT COUNT(*)` empty-table guard that makes re-import a no-op, GUID-if-empty on legacy rows, and `INSERT OR IGNORE` inside a transaction (`AlertService.cs:192-241`, `TemplateService.cs:367-401`). The templates importer additionally has a per-file `try/catch` so one malformed `*.json` is skipped without aborting the rest (`:384-398`), and never creates the `Templates` folder. Both are invoked from `EnsureInitializedAsync` after schema creation, once per process via the `_initialized` guard (confirmed).

### Construction wiring (verification 6)

`PresenterIntegration.cs` diff replaces the two JSON-path locals with the shared `appDbPath` (computed at :106 for `SongUsageService`): `AlertService = new AlertService(appDbPath)` and `TemplateService = new TemplateService(appDbPath)`, with the now-unused `alertTemplatesPath`/`templatesPath` locals removed. Because the legacy `alert-templates.json` and `Templates/` live in `appFolder` beside `app.db`, and each service derives its legacy source as a sibling of the db file (`AlertService.cs:73`, `TemplateService.cs:70`), existing data migrates on first run (confirmed). The ctors take a required path rather than a defaulted one, but the "test-injectable path, production passes app.db" intent is met the same way `AppDatabase`/`SongUsageService` do it.

### Test coverage (verification 7)

`TemplateServiceTests` now injects `_dbPath = <temp>/app.db`, calls `SqliteConnection.ClearAllPools()` at the top of `Dispose`, and wraps the folder delete in try/catch — mirroring `SongUsageServiceTests`. The old `CreateAsync_SavesAsFile` filesystem assertion was replaced by `CreateAsync_PersistsToDatabase`, which reopens a fresh service against the same `app.db` and reads the row back; all prior behavioral assertions (built-ins present, CRUD, category filter, duplicate, static built-ins) are retained unchanged (confirmed). New tests cover one-time non-duplicating folder import, empty store returns exactly 3 built-ins, a legacy file with a built-in id is not duplicated (and the code-seeded version wins — asserts FontSize 60 not 123), and a malformed file yields only built-ins without throwing. `AlertServiceTests` is new and covers GUID-if-empty, id preservation, full styling round-trip through `DataJson`, upsert-in-place with no duplicate, delete, absent-id no-op, cross-instance persistence, in-memory show/dismiss state and events, import-once/no-reimport, and malformed-safe import.

Not tested: no runtime/manual WPF UI smoke test (headless host; disclosed in the report); no migration against a real production corpus, only synthetic fixtures (consistent with Phase 1); `GetTemplatesAsync`/`GetAllAsync` row ordering is not asserted (no `ORDER BY`), matching the prior loose file-enumeration order. These are disclosed limitations, not gaps introduced by this change.

### Build/test evidence (verification 1)

Not re-run per the review contract. The commit message for `3a10fcb` records `dotnet build WorshipBacktrackPlayer.sln -c Debug => 0 errors, 4 pre-existing warnings` and `dotnet test => 586 passed, 0 failed, 0 skipped`, and the report records the same with the four warnings named (two in `MediaLibraryPanel`/`WaveformEditorWindow` CS0414, one `ChordChartWindow` CS8604 counted as two UI warnings — all pre-existing and unrelated to this diff). Evidence is present in both the commit and the report, so no spot-check is warranted (confirmed present).

### Scope (verification 8)

`git diff --stat main...feature/...` shows exactly seven paths: the two services, the two test files, `PresenterIntegration.cs`, and the two task docs. No media, settings, service-orders, songs, or `bible.db` code is touched, and there is no uncommitted work. Scope is strictly Phase 2 (confirmed).

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Presenter/Services/AlertService.cs` — re-backed onto `AlertTemplates` in app.db (upsert/select/delete + one-time import); display state and events unchanged.
- `WorshipPlayer.Presenter/Services/TemplateService.cs` — re-backed custom rows onto `SlideTemplates` in app.db; built-ins stay code-seeded, merged, and protected; one-time folder import excludes built-in ids.
- `WorshipPlayer.UI/Services/PresenterIntegration.cs` — both construction sites now pass the shared `appDbPath`.
- `WorshipPlayer.Tests/TemplateServiceTests.cs` — temp app.db injection, ClearAllPools teardown, file-check replaced with DB-persistence check, new import/built-in tests.
- `WorshipPlayer.Tests/AlertServiceTests.cs` — new: CRUD/upsert/delete/no-op/persistence + in-memory events + import once/no-dup/malformed-safe.
- `.agents/tasks/sql-phase02/plan.md`, `report.md` — task docs.

Full diff: `git -C .worktrees/sql-phase02 diff main...feature/sql-phase02-alerts-templates`

</details>
