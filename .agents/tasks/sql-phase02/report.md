# Completion Report — SQL Phase 2 (Alerts + Templates → SQLite app.db)

Migrated the two custom-template stores (`AlertService`, `TemplateService`) from JSON files
onto the shared `app.db`, reusing the Phase-0 `AppDatabase` helper and copying the Phase-1
`SongUsageService` shape. No second DB helper, no EF, `bible.db` untouched. Media / settings /
service-orders / songs were not touched.

Branch: `feature/sql-phase02-alerts-templates` (worktree `.worktrees/sql-phase02`).

---

## Design decision: no Core-side store extraction

Followed the plan's Decision 1 — persistence stays inside `AlertService` and `TemplateService`.
Both live in `WorshipPlayer.Presenter`, which already references Core, so each service does
`new AppDatabase(dbPath)` directly, exactly like `SongUsageService`. The Tests project compiles
both services as linked sources on net8.0 and references `Microsoft.Data.Sqlite` 8.0.0, so no new
project/package wiring was needed. Extraction would add indirection for zero benefit and risk
changing the public API that must stay identical.

Models (`AlertTemplate`, `SlideTemplate`) were NOT changed — both remain JSON-round-trippable,
which the `DataJson` blob depends on.

---

## Table schemas (columns vs JSON-blob)

Rule applied: enums stored as string names; fields the public API queries/filters on are promoted
to real columns; everything else rides in a single `DataJson` text column that is the FULL
`JsonSerializer.Serialize(model, JsonOptions)` document. The JSON blob is the source of truth on
read (the row is reconstructed by deserializing `DataJson`); promoted columns are a denormalized
index kept in sync on every write.

### `AlertTemplates`
```sql
CREATE TABLE IF NOT EXISTS AlertTemplates (
    Id              TEXT PRIMARY KEY,
    Name            TEXT NOT NULL DEFAULT '',
    OrganizationId  TEXT NOT NULL DEFAULT '',
    DataJson        TEXT NOT NULL
);
```
- `Id` — column (PK): identity / upsert + delete key.
- `Name` — column + in blob: human-facing, promoted for symmetry/future name queries.
- `OrganizationId` — column + in blob: tenancy key, promoted for filtering.
- `Text, FontSize, FontFamily, FontColor, HasShadow, ShadowColor, HasOutline, OutlineColor,
  OutlineWidth, ShowOnStageDisplay` — blob only: pure styling payload, never queried.

### `SlideTemplates` (CUSTOM rows only — built-ins are never rows)
```sql
CREATE TABLE IF NOT EXISTS SlideTemplates (
    Id               TEXT PRIMARY KEY,
    Name             TEXT NOT NULL DEFAULT '',
    TemplateCategory TEXT NOT NULL DEFAULT 'General',
    OrganizationId   TEXT NOT NULL DEFAULT '',
    DataJson         TEXT NOT NULL
);
```
- `Id` — column (PK): identity / update + delete key.
- `Name` — column + in blob: shown in UI, promoted for lookups.
- `TemplateCategory` — column (enum string name, e.g. `"Scripture"`) + in blob: the one real
  filter field (`GetByCategoryAsync`). The column stores `TemplateCategory.ToString()`, matching
  the blob's `JsonStringEnumConverter` output, so column and blob never disagree.
- `OrganizationId` — column + in blob: tenancy key.
- `IsBuiltIn` — blob only, and ALWAYS re-forced `false` when a row is read (custom rows are by
  definition not built-in).
- All other styling/formatting/margin fields — blob only. Enum fields
  (`HorizontalAlignment`/`VerticalAlignment`/`TextTransform`/`BorderStyle`) serialize as string
  names inside the blob via `JsonStringEnumConverter`.

`JsonOptions` (`WriteIndented = true`, `JsonStringEnumConverter`) is reused verbatim from the old
services, so enum-as-string parity holds for both the blob and the legacy import.

---

## Import behavior (defensive, one-time, non-duplicating)

Both stores copy `SongUsageService.ImportLegacyJsonIfNeededAsync` structure: schema-init +
import run once per process behind an `_initialized` guard, inside one open connection.

- **Empty-table guard** → non-duplicating: `SELECT COUNT(*)`; if `> 0`, return without importing.
- **Tolerant**: missing file/folder → return; the whole import is wrapped in `try/catch` that
  swallows (never throws); the table is left empty on failure. Templates additionally has a
  per-file `try/catch` so one malformed `*.json` is skipped without aborting the rest.
- **Alerts**: legacy source is the sibling `alert-templates.json` (single JSON array of
  `AlertTemplate`). Each imported template gets a GUID only if `Id` is empty; `INSERT OR IGNORE`
  inside a transaction preserves historical ids.
- **Templates**: legacy source is the sibling `Templates` folder of `*.json` (one custom template
  per file). The folder is NOT created on import. Any file whose id is a built-in id
  (`builtin-default-centered` / `builtin-lower-third` / `builtin-trendy-line`) is skipped so
  built-ins are never imported as rows; `IsBuiltIn` is forced `false`; `INSERT OR IGNORE`.

---

## Built-in handling (Templates)

Preserved exactly: `GetBuiltInTemplates()` (static) remains the only source of the 3 built-ins and
is byte-for-byte unchanged, including the three const ids. Built-ins are NEVER rows and NEVER
imported.

- `GetAllAsync` returns the code-seeded built-ins FIRST, then custom rows, deduped by id (a custom
  whose id already exists among built-ins is skipped — so a masquerading built-in id cannot create
  a duplicate and the code-seeded built-in wins). `IsBuiltIn` forced `false` on every custom row.
- `GetByIdAsync` / `GetByCategoryAsync` operate over that merged set.
- `CreateAsync` / `DuplicateAsync` force `IsBuiltIn = false` before insert; never write built-ins.
- `UpdateAsync`: a built-in id → preserve the built-in flag and do NOT write to the DB; a custom
  row → `UPDATE`; neither built-in nor an existing row (`rows-affected == 0`) → throw
  `InvalidOperationException` (preserves the old "not found → throw").
- `DeleteAsync`: a built-in id → return `false` without touching the DB; else delete and return
  rows-affected > 0.
- `GetBuiltInTemplatesAsync` returns the 3 code-seeded built-ins.

Alerts have no built-ins.

---

## file:line trace

### Alerts store — `WorshipPlayer.Presenter/Services/AlertService.cs`
- Reuse `AppDatabase`: ctor `AlertService(string databasePath)` at `:69`
  (`_database = new AppDatabase(databasePath)`; sibling legacy `alert-templates.json` derived there).
- Table init DDL `CreateSchemaSql` (AlertTemplates) at `:39`; run in `EnsureInitializedAsync`
  at `:160` (schema exec at `:171`, import at `:175`, `_initialized` guard at the top).
- Create/Update (upsert): `SaveTemplateAsync` at `:95` (`INSERT ... ON CONFLICT(Id) DO UPDATE`;
  GUID-if-empty just before binding).
- Delete: `DeleteTemplateAsync` at `:143` (`DELETE ... WHERE Id = @Id`).
- GetAll SELECT: `GetTemplatesAsync` at `:120` (`SELECT DataJson`, then deserialize each row).
- One-time import: `ImportLegacyJsonIfNeededAsync` at `:186`.
- Display state (unchanged, in-memory): `ShowAlertAsync`, `DismissAlertAsync`,
  `CurrentAlert`/`IsAlertActive` properties, events `AlertShown`/`AlertDismissed`.
- Construction against app.db: `WorshipPlayer.UI/Services/PresenterIntegration.cs:118`
  (`AlertService = new AlertService(appDbPath)`, reusing `appDbPath` computed at `:106`).

### Templates store — `WorshipPlayer.Presenter/Services/TemplateService.cs`
- Reuse `AppDatabase`: ctor `TemplateService(string databasePath)` at `:67`
  (`_database = new AppDatabase(databasePath)`; sibling `Templates` folder derived there).
- Table init DDL `CreateSchemaSql` (SlideTemplates) at `:56`; run in `EnsureInitializedAsync`
  at `:334` (schema exec just below, import at `:349`, `_initialized` guard at the top).
- Create: `CreateAsync` at `:108` (`INSERT`; `IsBuiltIn=false` before binding).
- Update: `UpdateAsync` at `:131` (built-in short-circuit near `:144`; `UPDATE`; not-found throw
  when rows-affected == 0).
- Delete: `DeleteAsync` at `:165` (built-in → false near `:181`; else `DELETE` + rows-affected).
- Duplicate: `DuplicateAsync` at `:185` (deep copy → `CreateAsync` at `:201`).
- GetAll SELECT + built-in merge: `GetAllAsync` at `:75` (built-ins first via
  `GetBuiltInTemplates()` at `:81`, custom rows deduped at `:84`); custom-row reader
  `ReadCustomTemplatesAsync` at `:307` (`SELECT DataJson`, `IsBuiltIn=false` per row).
- Static built-ins (unchanged): `GetBuiltInTemplates()` at `:228`; const ids near `:44`;
  `BuiltInIds` set just below.
- One-time import (built-ins excluded): `ImportLegacyTemplatesIfNeededAsync` at `:361`
  (built-in id skip inside the per-file loop).
- Construction against app.db: `WorshipPlayer.UI/Services/PresenterIntegration.cs:127`
  (`TemplateService = new TemplateService(appDbPath)`), still typed `ITemplateService`.

(Line numbers are from the committed files; small drift is possible if the files are re-formatted.)

---

## Build / test results

- `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded, 0 errors, 4 warnings**
  (all 4 pre-existing and unrelated: `MediaLibraryPanel._currentView` CS0414,
  `ChordChartWindow` CS8604, `WaveformEditorWindow._inReviewMode` CS0414 — the last two counted
  as 2 UI warnings).
- `dotnet test WorshipPlayer.Tests` → **total: 586, succeeded: 586, failed: 0, skipped: 0**.
  - All prior `TemplateServiceTests` behavioral assertions pass (parity); the old
    `CreateAsync_SavesAsFile` file-system check was replaced by `CreateAsync_PersistsToDatabase`
    (fresh service reads the row back from app.db).
  - New import/parity tests pass: `TemplateServiceTests.Import_LegacyTemplateFolder_*`,
    `GetAllAsync_EmptyStore_ReturnsExactlyThreeBuiltInsNoCustom`,
    `Import_LegacyFileWithBuiltInId_IsNotImportedAsDuplicate`,
    `Import_MalformedLegacyFile_DoesNotThrowAndYieldsOnlyBuiltIns`; new `AlertServiceTests`
    (CRUD/upsert/delete/no-op/persistence + in-memory show/dismiss events + legacy import
    once/no-duplicate + malformed-safe).
  - The `Error loading service order: 't' is an invalid start of a property name` console line is
    expected output from a pre-existing unrelated malformed-input test, not a failure.
- `git diff --stat` scope (plus untracked new test file + task docs): exactly
  `AlertService.cs`, `TemplateService.cs`, `TemplateServiceTests.cs`, `PresenterIntegration.cs`,
  new `AlertServiceTests.cs`, `.agents/tasks/sql-phase02/`. No media/settings/service-orders/songs.

---

## Verified vs not verified

Verified:
- Full solution builds (0 errors) and the full test suite is green (586/586) on this Windows host.
- The two construction sites in `PresenterIntegration` now build both services against the shared
  `appDbPath`, consistent with `SongUsageService`.
- Import is non-duplicating across a second service instance; built-ins are not imported/duplicated;
  malformed legacy input does not throw (covered by tests).
- Public `IAlertService` / `ITemplateService` surfaces and the static `GetBuiltInTemplates()` are
  unchanged (interfaces compared; implementations match signatures).

Not verified:
- No runtime/manual UI smoke test of the WPF app (headless host; the UI project builds but was not
  launched).
- No migration against a real production corpus — only synthetic fixtures (no data corpus is
  committed; consistent with Phase 1).
- `GetTemplatesAsync` / `GetAllAsync` row ordering is not asserted (no `ORDER BY`); matches the
  prior loose file-enumeration order. Flagged in the plan as a potential follow-up if a
  deterministic order is wanted.
