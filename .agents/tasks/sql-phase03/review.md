# Media-library metadata moved from JSON sidecars to app.db (Phase 3)

`MediaLibraryService` metadata now lives as rows in a `MediaItems` table inside the shared `app.db`, replacing the per-asset `*_metadata.json` sidecars. Asset binaries still get copied onto disk under the Media folder exactly as before; only the metadata backing store changed. The public `IMediaLibraryService` surface is byte-for-byte unchanged, `MediaLibraryFilter` is untouched, and a defensive one-time importer folds any pre-existing sidecars into the table on first use. The change reuses the Phase-0 `AppDatabase` helper and the `AlertService` upsert/import shape rather than inventing new infrastructure.

Watch for: nothing blocking. The `Tags` column is written as indented multi-line JSON and is intentionally not the query source (confirmed, cosmetic). Existing sidecar files are deliberately left on disk (confirmed, documented as the least-destructive choice).

**Verdict**: APPROVED

## High-level view

The design keeps the in-memory `_items` cache and only swaps its source — sidecar globbing becomes a `SELECT DataJson FROM MediaItems`. Because Search/GetByTag/GetByType/`MediaLibraryFilter` all run over `_items` with the same LINQ, query semantics are preserved without having to replicate them in SQL. Promoted columns (`Id`, `Name`, `FilePath`, `MediaType`, `Tags`, `OrganizationId`) exist for future SQL-side querying; today they are write-only mirrors of the `DataJson` blob.

Persistence goes through the shared `AppDatabase` (a thin per-call connection factory over `Data Source=<path>`), so there is no second db helper and `bible.db` stays separate. Only `Microsoft.Data.Sqlite` is used; no EF anywhere in the diff.

The constructor gained an optional `databasePath` (defaulting to `AppDatabase.DefaultPath`) while keeping the media-folder parameter, so tests inject a temp db and `PresenterIntegration` passes the already-resolved `appDbPath` it uses for the sibling services. The one-time sidecar importer is empty-table-guarded, per-file tolerant, wrapped in a swallowing catch, and leaves both sidecars and assets on disk. New imports write a row and no sidecar.

<details>
<summary>Issues (2)</summary>

1. **Tags column stored as indented JSON (non-blocking)** — `BindItem` serializes `Tags` with the shared `WriteIndented = true` options, so the column holds multi-line JSON. Harmless because the column is never read back (queries use `DataJson` → `_items`), but a compact serialization would be tidier if the column ever becomes a query source.
2. **Sidecars intentionally orphaned on disk (non-blocking, by design)** — legacy `*_metadata.json` files are left in place after import. Documented as the least-destructive choice and made safe by the empty-table guard; worth a future cleanup pass only if stale files on disk become a concern.

</details>

<details>
<summary>Details</summary>

### Cache source swap preserves query parity

The former design loaded `_items` by globbing `*_metadata.json` and deserializing each; the new `EnsureLoadedAsync` runs `SELECT DataJson FROM MediaItems`, deserializes each row, and skips unparseable rows in a per-row catch — the same tolerance the old loader had for bad files. Every consumer (`GetAllAsync`, `SearchAsync`, `GetByTagAsync`, `GetByTypeAsync`, and `MediaLibraryFilter`) still reads from `_items`, so the LINQ comparison semantics (`OrdinalIgnoreCase` name/tag contains, tag `Contains` with `OrdinalIgnoreCase`, type equality) are unchanged. Keeping the query layer in memory is what makes the "byte-for-byte behavior" claim hold without reimplementing filters in SQL. Confirmed by reading `MediaLibraryService.cs` lines 135–211 and 411–440.

`ImportAsync` adds to `_items` and upserts the row without setting `_loaded`; a later `GetAllAsync` triggers `EnsureLoadedAsync`, which clears and reloads `_items` from the table. The just-imported row is in the table (via `UpsertAsync`), so it survives the reload — cache coherence holds. This matches the pre-change pattern where `ImportAsync` added to the list and wrote a sidecar without forcing a reload. Confirmed.

### Schema and the write-only promoted columns

```sql
CREATE TABLE IF NOT EXISTS MediaItems (
    Id TEXT PRIMARY KEY, Name TEXT, FilePath TEXT,
    MediaType TEXT, Tags TEXT, OrganizationId TEXT, DataJson TEXT NOT NULL
);
```

`MediaType` is bound via `item.MediaType.ToString()` (enum name), consistent with the `JsonStringEnumConverter` used for `DataJson`, so the column and the blob agree. The promoted columns are populated on every upsert but never selected — reads come only from `DataJson`. One consequence: the `Tags` column carries the shared `WriteIndented` formatting (multi-line JSON). Harmless today since nothing reads it, but it would want a compact serialization if the column ever becomes a query source. Confirmed at `BindItem` lines 322–330 and `UpsertAsync` lines 303–320.

### Shared app.db, no second helper, no EF

`AppDatabase` is a path + per-call `SqliteConnection` factory with an idempotent `EnsureDirectory`, mirroring `BibleDatabaseService`'s connection shape; each store owns its own `CREATE TABLE IF NOT EXISTS`. `MediaLibraryService` constructs one `AppDatabase` from the passed path (or `DefaultPath`) and runs its own DDL in `EnsureInitializedAsync`. The diff contains no `DbContext`, `EntityFramework`, or `bible.db` wiring — the only `bible.db` token in the branch is report prose stating it stays separate. `Microsoft.Data.Sqlite` is already a Core package reference (8.0.0) from Phase 0. Confirmed.

### One-time sidecar importer

`ImportSidecarsIfNeededAsync` runs inside `EnsureInitializedAsync` (itself `_initialized`-guarded, once per process) after the schema DDL. It returns early if the Media folder is absent, counts existing rows and bails if the table is non-empty (the non-duplicating guard), then imports each `*_metadata.json` inside a transaction. A per-file try/catch skips a single malformed sidecar and continues; the whole body is wrapped in a swallowing catch so a locked folder or bad file never throws. Historical ids are preserved, with a GUID assigned only when missing, and `INSERT OR IGNORE` avoids clobbering. Existing sidecars and asset binaries are never touched. This mirrors `AlertService.ImportLegacyJsonIfNeededAsync`, which I confirmed exists with the same empty-table-guard + swallowing-catch structure. Confirmed at lines 349–408.

The second-instance non-duplication is real: the empty-table `COUNT(*)` guard means a fresh `MediaLibraryService` over the same `app.db` with the sidecar still present skips import. Confirmed, and exercised by `ImportsLegacySidecars_SecondInstance_DoesNotDuplicate`.

### Delete semantics preserved

`DeleteAsync` removes the item from `_items`, issues `DELETE FROM MediaItems WHERE Id=@Id`, then deletes the on-disk asset only when `deleteFile` is true and the file exists. The old code deleted the sidecar unconditionally and the asset only on the flag — the row delete now stands in for the unconditional sidecar delete, and the `deleteFile`-gated asset delete is unchanged. Confirmed at lines 157–180.

### Unchanged copy/validation path

`ImportAsync` validation (empty/null-byte/`..` rejection via `ValidatePath`), destination containment (`ValidateDestinationPath` with `Path.GetFullPath` + `StartsWith`), unique-name generation (`GetUniqueFilePath` counter loop), `CopyFileAsync` with `FileMode.CreateNew`, `SanitizeFileName`, and `GetLibraryPath` are all byte-for-byte identical to the base — only the metadata persistence call changed from `SaveMetadataAsync` to `UpsertAsync`. `MediaLibraryFilter` and `IMediaLibraryService` show no diff. Confirmed via `--name-only` (only the three source files + report changed).

### Construction site

`PresenterIntegration` now calls `new MediaLibraryService(mediaPath, appDbPath)`, reusing the `appDbPath` local already passed to `SongUsageService`/`AlertService` — no new `app.db` path is introduced. Confirmed at `PresenterIntegration.cs:110–114`.

### Test coverage

Tests inject a temp media dir plus a temp `app.db` and call `SqliteConnection.ClearAllPools()` in teardown and in each new test's `finally`, so the temp db unlocks for deletion on Windows (with a best-effort IOException swallow). The four new tests cover first-use import (asserting the sidecar and asset remain on disk), second-instance non-duplication, malformed-sidecar skip-without-throw, and no-new-sidecar on fresh import. Existing parity tests were updated only to pass the temp db path.

Not tested: SQL-side querying of the promoted columns (none exists yet, so there is nothing to test); real on-disk production migration from an existing user's Media folder beyond the automated sidecar tests; interactive WPF runtime launch. These align with the report's "not verified" list and are out of Phase-3 scope.

### Build/test evidence

Per the commit and report: `dotnet build WorshipBacktrackPlayer.sln -c Debug` succeeded with 0 errors and 4 pre-existing warnings (named: `MediaLibraryPanel._currentView` CS0414, `ChordChartWindow.BuildChordLine` CS8604, `WaveformEditorWindow._inReviewMode` CS0414, plus the WPF temp-assembly copy of the first). `dotnet test` reported 590/590 passing including the four new sidecar tests. The warnings are unrelated to the three files in this diff. The report's schema, import behavior, file:line trace, and construction-site description all match the code I read, and the cited `AlertService` mirror was spot-checked and confirmed. Evidence accepted; suites not re-run per instructions.

### Scope

Strictly Phase 3: the Media store, its construction site, and its tests. No settings/service-orders/songs migration appears in the diff. An untracked `plan.md` sits in the task directory but is not part of the change. Confirmed.

</details>

<details>
<summary>Files changed</summary>

- `WorshipPlayer.Core/Services/MediaLibraryService.cs` — metadata persistence moved to `MediaItems` in app.db; schema DDL, upsert, one-time sidecar import, SQLite-backed cache load; optional `databasePath` ctor param.
- `WorshipPlayer.UI/Services/PresenterIntegration.cs` — construction site passes the shared `appDbPath`.
- `WorshipPlayer.Tests/MediaLibraryServiceTests.cs` — temp app.db injection, `ClearAllPools` teardown, four new sidecar-import/no-sidecar tests; parity tests retained.
- `.agents/tasks/sql-phase03/report.md` — completion report.

Full diff: `git -C .worktrees\sql-phase03 diff main...feature/sql-phase03-media`

</details>
