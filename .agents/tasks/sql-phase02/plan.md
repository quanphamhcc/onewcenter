# Implementation Plan — SQL Phase 2 (Alerts + Templates → SQLite app.db)

Migrate exactly two custom-template stores from JSON files to rows in the shared
`app.db`, reusing the Phase-0 `AppDatabase` helper and copying the Phase-1
`SongUsageService` shape. Do NOT touch media, settings, service-orders, or songs.

All paths below are inside the worktree:
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sql-phase02`
(abbreviated `<WT>` from here on). Use absolute paths; relative paths land in the
parent workspace.

---

## STOP CONTRACT (shared by implementer and reviewer)

The downstream implement-and-review loop's gate file is:
`<WT>\.agents\tasks\sql-phase02\review.json`

The loop stops when that file's top-level `"verdict"` field equals `"APPROVED"`
(string, exact). Any other value (e.g. `"CHANGES_REQUESTED"`) means keep iterating.
The reviewer writes this file every iteration; the implementer must not hand-edit it.

---

## Decisions (made here, with rationale)

1. **Keep persistence logic inside `AlertService` and `TemplateService`; do NOT extract
   a Core-side store.** Both services live in `WorshipPlayer.Presenter`
   (`<WT>\WorshipPlayer.Presenter\Services\AlertService.cs:12`,
   `<WT>\WorshipPlayer.Presenter\Services\TemplateService.cs:15`), and the Presenter
   project already references Core
   (`<WT>\WorshipPlayer.Presenter\WorshipPlayer.Presenter.csproj:27-28`), so each service
   can `new AppDatabase(dbPath)` directly — exactly as `SongUsageService` does in Core
   (`<WT>\WorshipPlayer.Core\Services\SongUsageService.cs:63`). Core has
   `Microsoft.Data.Sqlite` 8.0.0 (`<WT>\WorshipPlayer.Core\WorshipPlayer.Core.csproj:11`),
   and the Tests project compiles both services as linked sources on net8.0
   (`<WT>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj:36-37`) and also references
   `Microsoft.Data.Sqlite` 8.0.0 (`:14`), so no new project/package wiring is needed.
   Extraction would add indirection for zero benefit and risk changing the public API the
   task says must stay identical. Keep it simple.

2. **One shared `app.db`, two new tables: `AlertTemplates` and `SlideTemplates`.** Reuse
   `AppDatabase` (`<WT>\WorshipPlayer.Core\Services\AppDatabase.cs:40`); each store owns its
   own `CREATE TABLE IF NOT EXISTS` DDL and runs it on init, which is safe across stores
   sharing `app.db` (`AppDatabase.cs:14-21` documents this). `bible.db` stays separate
   (do not touch `BibleDatabaseService`). Microsoft.Data.Sqlite only, no EF.

3. **Enums stored as string names; the "long tail" of styling fields stored as a single
   JSON-blob text column; query/filter fields promoted to real columns.** Rationale:
   `SlideTemplate` has ~30 fields and `AlertTemplate` ~12 — mapping every one to a column
   is noisy and brittle against future additive fields, and the models already round-trip
   through `System.Text.Json` with `JsonStringEnumConverter`
   (`SlideTemplate.cs:1`, enums at `SlideTheme.cs:209-226`, `SlideTemplate.cs:149-182`;
   `AlertTemplate.cs` is a flat POCO). Only the fields the public API actually queries/filters
   on become columns (see schemas). The rest ride in a `DataJson` column that is the FULL
   `JsonSerializer.Serialize(model, JsonOptions)` document (the same options the current
   services use), so the row is reconstructed by deserializing `DataJson` and does NOT
   require the promoted columns to round-trip — the columns are a denormalized index, the
   JSON blob is the source of truth. This keeps models JSON-round-trippable and unchanged
   (no model-shape edits).

4. **Built-in slide templates stay code-seeded, never rows.** `GetBuiltInTemplates()`
   (`TemplateService.cs:154-233`) remains the only source of the 3 built-ins; they are NOT
   written to `SlideTemplates` and NOT imported from legacy JSON. `GetAllAsync` merges
   code-seeded built-ins with custom rows (see item 3 of plan). Alerts have no built-ins.

5. **`JsonOptions` reused verbatim.** Both services already define
   `new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }`
   (`AlertService.cs:18-22`, `TemplateService.cs:23-27`). Keep that instance; `DataJson`
   is produced/consumed with it so enum-as-string parity holds for both the blob and the
   legacy import.

---

## Proposed table schemas

### `AlertTemplates` (model: `<WT>\WorshipPlayer.Core\Models\AlertTemplate.cs`)

```sql
CREATE TABLE IF NOT EXISTS AlertTemplates (
    Id              TEXT PRIMARY KEY,
    Name            TEXT NOT NULL DEFAULT '',
    OrganizationId  TEXT NOT NULL DEFAULT '',
    DataJson        TEXT NOT NULL
);
```

Column vs blob decision for every `AlertTemplate` field:

| Field (AlertTemplate.cs) | Storage | Reason |
|---|---|---|
| `Id` (`:9-10`) | column `Id` (PK) | identity; used by `DeleteTemplateAsync`/`SaveTemplateAsync` upsert key (`AlertService.cs:71-85,95-101`) |
| `Name` (`:12-13`) | column `Name` + in blob | human-facing; promoted so a future name query is a real column (parity with SlideTemplates) |
| `OrganizationId` (`:52-53`) | column `OrganizationId` + in blob | tenancy key; promoted for symmetry/filtering |
| `Text`, `FontSize`, `FontFamily`, `FontColor`, `HasShadow`, `ShadowColor`, `HasOutline`, `OutlineColor`, `OutlineWidth`, `ShowOnStageDisplay` (`:15-49`) | blob only (`DataJson`) | pure styling payload; never queried/filtered by the API |

`DataJson` = `JsonSerializer.Serialize(template, JsonOptions)` of the whole `AlertTemplate`
(so every field, including the promoted ones, round-trips from the blob). `Name`/`OrganizationId`
columns are a denormalized copy kept in sync on every write.

### `SlideTemplates` (model: `<WT>\WorshipPlayer.Core\Models\SlideTemplate.cs`)

```sql
CREATE TABLE IF NOT EXISTS SlideTemplates (
    Id              TEXT PRIMARY KEY,
    Name            TEXT NOT NULL DEFAULT '',
    TemplateCategory TEXT NOT NULL DEFAULT 'General',
    OrganizationId  TEXT NOT NULL DEFAULT '',
    DataJson        TEXT NOT NULL
);
```

Only CUSTOM templates are rows here. Built-ins are never stored (Decision 4).

Column vs blob decision for `SlideTemplate` fields:

| Field (SlideTemplate.cs) | Storage | Reason |
|---|---|---|
| `Id` (`:11-12`) | column `Id` (PK) | identity; `GetByIdAsync`/`DeleteAsync`/`UpdateAsync` key (`TemplateService.cs:46-50,88-105,110-128`) |
| `Name` (`:14-15`) | column `Name` + in blob | shown in UI; former file name was derived from it; promote for lookups |
| `TemplateCategory` (`:177-178`) | column `TemplateCategory` (enum name text) + in blob | `GetByCategoryAsync` filters on it (`TemplateService.cs:53-57`) — the one real query field |
| `OrganizationId` (`:181-182`) | column `OrganizationId` + in blob | tenancy key; promote for filtering parity |
| `IsBuiltIn` (`:173-174`) | blob only, and ALWAYS re-forced `false` on read of a row | custom rows are by definition not built-in; see item 3 for enforcement. Not a column because every row's value is identically false |
| all other fields: `BaseThemeId`, `FontFamily`, `FontSize`, `FontWeight`, `FontColor`, `FontItalic`, `HorizontalAlignment`, `VerticalAlignment`, `HasShadow`, `ShadowColor`, `ShadowBlur`, `HasOutline`, `OutlineColor`, `OutlineWidth`, `TextTransform`, `LetterSpacing`, `LineHeight`, `LineBackgroundEnabled`, `LineBackgroundColor`, `LineBackgroundOpacity`, `BorderStyle`, `BorderColor`, `BorderWidth`, `MarginTop/Left/Right/Bottom` (`:19-170`) | blob only (`DataJson`) | styling payload; never queried/filtered. Enum fields (`HorizontalAlignment`/`VerticalAlignment`/`TextTransform`/`BorderStyle`) serialize as string names via `JsonStringEnumConverter` inside the blob |

`TemplateCategory` column stores the enum's string name (e.g. `"Scripture"`), matching the
blob's `JsonStringEnumConverter` output, so the column and blob never disagree.

---

## Parameterized SQL shapes (both stores)

Model the shapes on `SongUsageService` (`SongUsageService.cs:76-90,113-137,221-235`): per-call
connection from `AppDatabase.CreateConnection()`, `await OpenAsync()`, `AddWithValue` params,
schema-init + one-time import behind an `_initialized` guard
(`SongUsageService.cs:181-199`).

### AlertTemplates

- **Upsert (SaveTemplateAsync)** — replaces the current find-or-add + full-array rewrite
  (`AlertService.cs:68-86`):
  ```sql
  INSERT INTO AlertTemplates (Id, Name, OrganizationId, DataJson)
  VALUES (@Id, @Name, @OrganizationId, @DataJson)
  ON CONFLICT(Id) DO UPDATE SET
      Name = excluded.Name,
      OrganizationId = excluded.OrganizationId,
      DataJson = excluded.DataJson;
  ```
  Keep the existing pre-write behavior from `SaveTemplateAsync`: assign a GUID when
  `Id` is empty (`AlertService.cs:73-74`) BEFORE binding, so returned template carries the id.
- **SelectAll (GetTemplatesAsync)** (`AlertService.cs:89-93`):
  `SELECT DataJson FROM AlertTemplates;` → deserialize each `DataJson` into `AlertTemplate`
  with `JsonOptions`. (Order is not asserted by any test; preserve insertion-ish order with
  no ORDER BY, matching the old list order which was file order.)
- **Delete (DeleteTemplateAsync)** (`AlertService.cs:96-104`):
  `DELETE FROM AlertTemplates WHERE Id = @Id;` — no-op when absent, matching the old
  "FirstOrDefault == null → return" (`AlertService.cs:100-101`).

### SlideTemplates

- **Insert (CreateAsync / DuplicateAsync)** — replaces `SaveTemplateToFileAsync`
  (`TemplateService.cs:62-74,131-149,254-262`). Force `IsBuiltIn=false` first
  (`TemplateService.cs:69,145`), assign GUID if empty (`:66-67,141`):
  ```sql
  INSERT INTO SlideTemplates (Id, Name, TemplateCategory, OrganizationId, DataJson)
  VALUES (@Id, @Name, @TemplateCategory, @OrganizationId, @DataJson);
  ```
- **Update (UpdateAsync)** — only when NOT built-in (`TemplateService.cs:77-96`). Built-ins
  must be blocked from overwrite (see item 3). For a custom row:
  ```sql
  UPDATE SlideTemplates SET
      Name = @Name, TemplateCategory = @TemplateCategory,
      OrganizationId = @OrganizationId, DataJson = @DataJson
  WHERE Id = @Id;
  ```
  Preserve the "not found → throw InvalidOperationException" semantics
  (`TemplateService.cs:84-85`), where "found" = built-in-by-id OR a row exists.
- **SelectAll custom rows** (used by GetAllAsync merge, GetByIdAsync, GetByCategoryAsync):
  `SELECT DataJson FROM SlideTemplates;` → deserialize, force `IsBuiltIn=false` on each.
- **Delete (DeleteAsync)** (`TemplateService.cs:99-120`): if the id is a built-in → return
  `false` without touching the DB (`:107-108`); else `DELETE FROM SlideTemplates WHERE Id=@Id;`
  and return whether a row existed (`ExecuteNonQueryAsync` rows-affected > 0), matching the
  old "FirstOrDefault==null → false, else remove → true" (`:104-118`).

---

## GetAllAsync merge + IsBuiltIn / delete-overwrite protection (Templates)

Preserve the exact behavior currently in `EnsureLoadedAsync` + the CRUD guards
(`TemplateService.cs:235-273`, and `:69,82-83,107-108,145`):

1. `GetAllAsync` returns code-seeded built-ins FIRST, then custom rows. Build a fresh list
   each call: `GetBuiltInTemplates()` (always, `:239`) + all `SlideTemplates` rows with
   `IsBuiltIn` forced `false`. Dedupe by `Id` the same way the old loader did
   (`:258` skips a custom whose id already exists among built-ins). Return a copy
   (`:42-43` returned `new List<SlideTemplate>(...)`).
2. `GetByIdAsync` / `GetByCategoryAsync` operate over that merged set (built-ins + custom),
   matching `:46-57`.
3. `CreateAsync` / `DuplicateAsync` force `IsBuiltIn=false` before insert (`:69,145`); never
   write built-ins.
4. `UpdateAsync`: resolve the target; if it is a built-in (id ∈ built-in ids, or the resolved
   target has `IsBuiltIn==true`), preserve the built-in flag and do NOT write to the DB
   (mirrors `:88-95`, which only calls save when `!isBuiltIn`); if it is a custom row, UPDATE
   it; if neither built-in nor an existing row → throw `InvalidOperationException` (`:84-85`).
5. `DeleteAsync`: built-in id → `false` (`:107-108`); otherwise delete the row and return
   rows-affected > 0.
6. `GetBuiltInTemplatesAsync` returns the 3 code-seeded built-ins (`:135-139`) — unchanged,
   no DB read required beyond the merge.

No `_templates` in-memory list is required anymore, but a one-time `EnsureInitializedAsync`
(schema + import, `_initialized` guard) replaces `_loaded`/`EnsureLoadedAsync`. Each public
method calls `EnsureInitializedAsync` first (as every method already calls `EnsureLoadedAsync`).

---

## Defensive one-time import (per store)

Copy `ImportLegacyJsonIfNeededAsync` exactly (`SongUsageService.cs:206-258`): empty-table
guard makes it non-duplicating; tolerate missing/malformed; never throw (swallow in a
`try/catch`, leave table empty on failure). Run it from `EnsureInitializedAsync` after
schema creation, inside the same open connection, once per process via `_initialized`.

### Alerts import

- Legacy path = the `templatesFilePath` the ctor currently receives
  (`AlertService.cs:44-47`), i.e. `<AppFolder>/alert-templates.json`. Since the ctor now
  takes a db path, derive the legacy JSON as a sibling named `alert-templates.json`
  (same technique as `SongUsageService.cs:65-66` deriving `song-usage.json`).
- Guard: `SELECT COUNT(*) FROM AlertTemplates`; if > 0 return.
- Read file (skip if absent), `JsonSerializer.Deserialize<List<AlertTemplate>>(json, JsonOptions)`
  (old shape, single array — `AlertService.cs:112-113`); if null/empty return.
- For each: assign GUID if `Id` empty; `INSERT OR IGNORE` into `AlertTemplates` inside a
  transaction (preserve historical ids; no re-stamp — alerts have no timestamp field anyway).
- Any exception → swallow (matches old `EnsureLoadedAsync` catch, `AlertService.cs:114-117`).

### Templates import

- Legacy source = the `<AppFolder>/Templates` FOLDER of `*.json` files
  (`TemplateService.cs:243-261`), one custom template per file. Derive it as a sibling folder
  named `Templates` next to the db file (same derivation technique as above).
- Guard: `SELECT COUNT(*) FROM SlideTemplates`; if > 0 return.
- If the folder is absent → return (no import; do NOT create it — the old loader created it
  only as a save target, `:245-249`).
- For each `*.json`: `JsonSerializer.Deserialize<SlideTemplate>(json, JsonOptions)`; skip null;
  **skip any whose id is a built-in id** (`builtin-default-centered` / `builtin-lower-third` /
  `builtin-trendy-line`, `TemplateService.cs:30-32`) so built-ins are never imported as rows;
  force `IsBuiltIn=false`; `INSERT OR IGNORE`. Per-file try/catch skips invalid files (mirrors
  `:263-266`), and the whole import is wrapped so it never throws.

---

## PresenterIntegration.cs edits (both construction sites)

File: `<WT>\WorshipPlayer.UI\Services\PresenterIntegration.cs`. `appFolder` is set at
`:74`; `app.db` path is already computed at `:106` (`var appDbPath = Path.Combine(appFolder, "app.db")`).

1. **Alerts** — currently lines **116-118**:
   ```csharp
   var alertTemplatesPath = Path.Combine(appFolder, "alert-templates.json");
   AlertService = new AlertService(alertTemplatesPath);
   ```
   Change to construct against `app.db` (reuse `appDbPath` from `:106`):
   ```csharp
   AlertService = new AlertService(appDbPath);
   ```
   Remove the now-unused `alertTemplatesPath` local. The legacy `alert-templates.json` sitting
   beside `app.db` in `appFolder` is exactly where the one-time importer looks, so existing
   data migrates on first run.

2. **Templates** — currently lines **124-126**:
   ```csharp
   var templatesPath = Path.Combine(appFolder, "Templates");
   TemplateService = new TemplateService(templatesPath);
   ```
   Change to:
   ```csharp
   TemplateService = new TemplateService(appDbPath);
   ```
   Remove the now-unused `templatesPath` local. The legacy `Templates\*.json` folder beside
   `app.db` in `appFolder` is where the importer looks. `TemplateService` remains typed as
   `ITemplateService` (property at `:50`) — the interface is unchanged.

Both now share the single `appDbPath` already used by `SongUsageService` (`:106-108`),
consistent with how `BibleDatabaseService` is constructed from a path (`:122-123`).

---

## Implementation items (ordered by dependency)

- [ ] 1. Re-back `AlertService` onto `app.db`, preserving the full `IAlertService` surface and
      in-memory display state.
      Change the ctor to accept a db path and build `_database = new AppDatabase(dbPath)` plus a
      derived sibling `alert-templates.json` legacy path; add `CreateSchemaSql` for
      `AlertTemplates`, `_initialized` guard, `EnsureInitializedAsync` (schema + import),
      `ImportLegacyJsonIfNeededAsync`. Reimplement `SaveTemplateAsync` (upsert), `GetTemplatesAsync`
      (select+deserialize), `DeleteTemplateAsync` (delete) against SQLite. Leave `ShowAlertAsync`,
      `DismissAlertAsync`, `AlertShown`/`AlertDismissed`, `CurrentAlert`, `IsAlertActive` EXACTLY as
      they are (`AlertService.cs:49-66,27-42`) — display state stays in-memory, not persisted.
      Follow the `SongUsageService` structure verbatim.
      Files: `<WT>\WorshipPlayer.Presenter\Services\AlertService.cs`
      Verify: `dotnet build <WT>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` succeeds
      (compiles the linked AlertService source on net8.0).

- [ ] 2. Re-back `TemplateService` onto `app.db`, preserving the full `ITemplateService` surface,
      the static `GetBuiltInTemplates()`, built-in merge, and delete/overwrite protection.
      Change the ctor to accept a db path (`AppDatabase` + sibling `Templates` legacy folder path);
      add `CreateSchemaSql` for `SlideTemplates`, `_initialized` guard, `EnsureInitializedAsync`
      (schema + folder import that EXCLUDES built-in ids), `ImportLegacyTemplatesIfNeededAsync`.
      Reimplement `GetAllAsync` (built-ins + custom rows, dedupe by id, `IsBuiltIn` forced false on
      rows), `GetByIdAsync`, `GetByCategoryAsync`, `CreateAsync`, `UpdateAsync` (built-in → no DB
      write but preserve flag; custom → UPDATE; neither → throw), `DeleteAsync` (built-in → false;
      else delete + rows-affected), `DuplicateAsync`, `GetBuiltInTemplatesAsync`, `ApplyToItemAsync`
      against SQLite. Keep `GetBuiltInTemplates()` (`:154-233`) and the three const ids byte-for-byte.
      Files: `<WT>\WorshipPlayer.Presenter\Services\TemplateService.cs`
      Verify: `dotnet build <WT>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` succeeds.

- [ ] 3. Point both construction sites at `app.db` in `PresenterIntegration`.
      Apply the two edits in the "PresenterIntegration.cs edits" section: Alerts (current lines
      116-118) and Templates (current lines 124-126), reusing `appDbPath` from line 106 and
      removing the two now-unused locals.
      Files: `<WT>\WorshipPlayer.UI\Services\PresenterIntegration.cs`
      Verify: `dotnet build <WT>\WorshipBacktrackPlayer.sln -c Debug` succeeds (UI is net8.0-windows;
      builds on this Windows host).

- [ ] 4. Adapt `TemplateServiceTests` to inject a temp `app.db` and clear pools in teardown,
      preserving every existing behavioral assertion.
      Change setup from `new TemplateService(_tempFolder)` to a temp-folder + `_dbPath =
      Path.Combine(_tempFolder,"app.db")` + `new TemplateService(_dbPath)` (mirror
      `SongUsageServiceTests.cs:21-31`). Add `SqliteConnection.ClearAllPools()` at the top of
      `Dispose` and wrap the folder delete in try/catch (`SongUsageServiceTests.cs:33-49`). The
      `CreateAsync_SavesAsFile` test (`TemplateServiceTests.cs:89-101`) asserts a `*.json` file was
      written — replace it with a DB-persistence check: after `CreateAsync`, a fresh
      `new TemplateService(_dbPath)` `GetAllAsync()` contains the template (mirrors
      `SongUsageServiceTests.cs:224-240`). Keep all other assertions (built-ins present, CRUD,
      category filter, duplicate, static built-ins) unchanged.
      Files: `<WT>\WorshipPlayer.Tests\TemplateServiceTests.cs`
      Verify: `dotnet test <WT>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --filter
      "FullyQualifiedName~TemplateServiceTests"` — all pass.

- [ ] 5. Add new `AlertServiceTests` covering the SQLite-backed template persistence and parity.
      New xUnit class following `SongUsageServiceTests` discipline (temp folder, `_dbPath` =
      `<temp>\app.db`, `new AlertService(_dbPath)`, `ClearAllPools()` in `Dispose`). Cover:
      SaveTemplateAsync assigns a GUID when Id empty and returns it; GetTemplatesAsync round-trips
      saved templates (all styling fields survive via DataJson); SaveTemplateAsync with an existing
      Id updates in place (no duplicate); DeleteTemplateAsync removes; DeleteTemplateAsync on an
      absent id is a no-op; persistence across a fresh service instance against the same app.db;
      ShowAlertAsync/DismissAlertAsync toggle `IsAlertActive`/`CurrentAlert` and raise
      `AlertShown`/`AlertDismissed` (in-memory, unchanged). `AlertService.cs` is already linked into
      the Tests project (`WorshipPlayer.Tests.csproj:37`), so no csproj change is needed.
      Files: `<WT>\WorshipPlayer.Tests\AlertServiceTests.cs` (new)
      Verify: `dotnet test ... --filter "FullyQualifiedName~AlertServiceTests"` — all pass.

- [ ] 6. Add one-time-import and built-ins-not-duplicated tests for both stores.
      In `TemplateServiceTests`: (a) write a legacy `<temp>\Templates\*.json` custom template BEFORE
      first use; a fresh service imports it once and a second instance does NOT duplicate it (mirror
      `SongUsageServiceTests.cs:244-299`); (b) a built-ins-not-duplicated test: `GetAllAsync` returns
      exactly 3 built-ins and no custom rows when the DB/folder is empty, AND placing a legacy
      template file whose id is a built-in id (`builtin-default-centered`) does NOT add a duplicate
      row (built-ins excluded from import); (c) a malformed legacy file does not throw and yields
      only built-ins. In `AlertServiceTests`: write a legacy `<temp>\alert-templates.json` array,
      assert import-once + no re-import on a second instance, and a malformed file does not throw and
      yields an empty template list.
      Files: `<WT>\WorshipPlayer.Tests\TemplateServiceTests.cs`,
      `<WT>\WorshipPlayer.Tests\AlertServiceTests.cs`
      Verify: `dotnet test ... --filter "FullyQualifiedName~TemplateServiceTests|FullyQualifiedName~AlertServiceTests"`
      — all pass.

- [ ] 7. Full build + full test suite (integration gate for the whole phase).
      Confirm nothing else regressed (song usage, bible, service orders, etc. untouched).
      Files: none (verification only).
      Verify: `dotnet build <WT>\WorshipBacktrackPlayer.sln -c Debug` succeeds, then
      `dotnet test <WT>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug` — all tests pass
      (baseline before this phase: TemplateServiceTests 20 + SongUsageServiceTests 15 green).

---

## Guardrails for the implementer

- Microsoft.Data.Sqlite ONLY; no EF; do NOT add a second db helper — reuse `AppDatabase`.
- Do NOT touch `bible.db` / `BibleDatabaseService`, nor media/settings/service-orders/songs.
- Do NOT change `SlideTemplate` / `AlertTemplate` model shapes (they must stay
  JSON-round-trippable; `DataJson` depends on it).
- Keep the public APIs of `IAlertService` and `ITemplateService` byte-for-byte, including
  events, in-memory alert display state, and the static `GetBuiltInTemplates()`.
- Built-ins remain code-seeded, never rows, never imported.
- Commit locally on the branch; never push.
- If satisfying any finding would change what the user sees or how the system behaves, stop and
  raise it (send_message, severity "warning") rather than making the change silently.

## Assumptions / gaps

- No real data corpus is committed (surface doc §3); the importer is validated against synthetic
  fixtures in tests, consistent with Phase 1.
- `GetTemplatesAsync`/`GetAllAsync` ordering is not asserted by any current test; no `ORDER BY` is
  specified, preserving the prior file-enumeration order loosely. If the reviewer wants a
  deterministic order, add `ORDER BY Name` — flagged, not pre-decided.
