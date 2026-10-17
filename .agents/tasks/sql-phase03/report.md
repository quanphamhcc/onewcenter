# Completion Report — SQL Migration Phase 3 (Media library metadata → app.db)

## Summary

`MediaLibraryService` metadata is now persisted as rows in a `MediaItems` table in the shared
`app.db` (via the Phase-0 `AppDatabase` helper), migrated from the per-asset JSON sidecars
(`<assetpath>_metadata.json`). Asset binaries (images/videos/audio) stay on disk under the Media
library folder. The public `IMediaLibraryService` API and all observable behavior are preserved
byte-for-byte, and `MediaLibraryFilter` is untouched. `Microsoft.Data.Sqlite` only, no EF,
`bible.db` stays separate.

## MediaItems schema (columns vs blob, tag storage)

```sql
CREATE TABLE IF NOT EXISTS MediaItems (
    Id              TEXT PRIMARY KEY,
    Name            TEXT NOT NULL DEFAULT '',
    FilePath        TEXT NOT NULL DEFAULT '',
    MediaType       TEXT NOT NULL DEFAULT 'Image',
    Tags            TEXT NOT NULL DEFAULT '[]',
    OrganizationId  TEXT NOT NULL DEFAULT '',
    DataJson        TEXT NOT NULL
);
```
(`MediaLibraryService.cs:46-58`)

- **Promoted columns** (the fields named as queried/filtered): `Id`, `Name`, `FilePath`,
  `MediaType`, `Tags`, `OrganizationId`. Mirrors the `AlertService` schema shape
  (`WorshipPlayer.Presenter\Services\AlertService.cs:40-47`).
- **`MediaType`** is stored as its enum NAME (`"Image"` / `"Video"` / `"Audio"`) via
  `item.MediaType.ToString()`, consistent with `JsonStringEnumConverter`
  (`MediaLibraryItem.cs:45-46`) and the Phase-1/2 enums-as-strings rule
  (bind at `MediaLibraryService.cs:323`).
- **Tag storage**: the `Tags` column holds the JSON-array text of `List<string>` (e.g.
  `["worship","background"]`), produced with the same `JsonOptions` as `DataJson`
  (`MediaLibraryService.cs:324`). A JSON array (not a delimiter) was chosen so a tag that itself
  contains a delimiter character cannot corrupt parity with the exact `List<string>`. The column
  exists for completeness / future SQL-side querying; it is **not** the query source today —
  `GetByTagAsync` / `SearchAsync` / `MediaLibraryFilter` run over the in-memory `_items` list
  (deserialized from `DataJson`) so LINQ semantics are identical to the old sidecar design.
- **`DataJson` blob**: the whole `MediaLibraryItem` serialized, so remaining fields
  (`ThumbnailPath`, `FileSize`, `DateAdded`) round-trip losslessly without a column each.
  `GetAllAsync`/queries deserialize from this column.

## Import behavior + sidecar disposition

- Defensive one-time import `ImportSidecarsIfNeededAsync` (`MediaLibraryService.cs:349-408`):
  runs during `EnsureInitializedAsync` (`MediaLibraryService.cs:331-347`) under an `_initialized`
  guard, mirroring `AlertService.ImportLegacyJsonIfNeededAsync` (AlertService.cs:171-220).
- **Non-duplicating**: guarded by `SELECT COUNT(*) FROM MediaItems` — if the table is non-empty,
  it returns without importing (`MediaLibraryService.cs:360-366`). A second service instance
  against the same `app.db` therefore does not re-import.
- **Tolerant / never throws**: a missing Media folder returns early; each sidecar is read +
  deserialized in a per-file try/catch so a single malformed file is skipped and the rest continue;
  the whole body is wrapped in a swallowing try/catch (`MediaLibraryService.cs:349-407`).
- **Sidecar-file disposition (D4): LEAVE existing `*_metadata.json` files on disk; stop writing new
  ones.** Rationale: least-destructive (the task's stated preference). The empty-table guard means
  leaving sidecars cannot cause re-import; a stale sidecar is simply ignored going forward because
  `EnsureLoadedAsync` reads from the table, not from disk. New imports write a `MediaItems` row and
  NO sidecar (`SaveMetadataAsync`/`GetMetadataPath` removed). Asset binaries are never deleted by
  the import.

## file:line trace

- **AppDatabase** path + per-call connection factory + `EnsureDirectory`:
  `WorshipPlayer.Core\Services\AppDatabase.cs:47-51` (ctor), `:57` (`CreateConnection`),
  `:66-71` (`EnsureDirectory`), `:36-37` (`DefaultPath`).
- **MediaItems table init**: `MediaLibraryService.cs:46-58` (DDL const) →
  `EnsureInitializedAsync` (`MediaLibraryService.cs:328`) runs the `CREATE TABLE IF NOT EXISTS`,
  then the one-time sidecar import.
- **ImportAsync** (copy asset to disk + INSERT row): validation/copy unchanged
  (`MediaLibraryService.cs:91-120`), `CopyFileAsync` with `FileMode.CreateNew`
  (`MediaLibraryService.cs:290-295`), then `EnsureInitializedAsync()` +
  `UpsertAsync(item)` INSERT/upsert (`MediaLibraryService.cs:122-131`, upsert at
  `:303-320`).
- **GetAll / GetByTag / GetByType / Search** SELECT: `EnsureLoadedAsync` runs
  `SELECT DataJson FROM MediaItems` and fills `_items` (`MediaLibraryService.cs:411-440`);
  `GetAllAsync` (`:135-139`), `SearchAsync` (`:142-154`), `GetByTagAsync` (`:184-191`),
  `GetByTypeAsync` (`:194-199`) all read from `_items`.
- **DeleteAsync** (DELETE row + optional on-disk file delete):
  `MediaLibraryService.cs:157-180` — `DELETE FROM MediaItems WHERE Id=@Id` (`:166-173`), then
  `deleteFile && File.Exists` asset delete (`:176-177`).
- **UpdateTagsAsync**: sets tags on the cached item then `UpsertAsync`
  (`MediaLibraryService.cs:202-211`).
- **Construction site**: `WorshipPlayer.UI\Services\PresenterIntegration.cs:110-114` —
  `var mediaPath = Path.Combine(appFolder, "Media"); MediaLibraryService = new
  MediaLibraryService(mediaPath, appDbPath);` reusing the existing `appDbPath` local
  (declared at `PresenterIntegration.cs:106`); no second `app.db` path introduced.

## Constructor

`public MediaLibraryService(string libraryPath, string? databasePath = null)`
(`MediaLibraryService.cs:85-92`). Keeps the media-folder parameter (tests/callers inject a temp
media dir unchanged) and adds an optional db path; `null` defaults to `AppDatabase.DefaultPath`
(standard app-folder `app.db`). `PresenterIntegration` passes the already-resolved `appDbPath`.

## Build / test results

- `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` →
  **Build succeeded with 4 warning(s), 0 errors** (8.3s). The 4 warnings are pre-existing and
  unrelated to this change: `MediaLibraryPanel._currentView` (CS0414),
  `ChordChartWindow.BuildChordLine` (CS8604), `WaveformEditorWindow._inReviewMode` (CS0414), and the
  WPF temp-assembly copy of the first.
- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --nologo` →
  **total: 590, failed: 0, succeeded: 590, skipped: 0** (18.1s). All prior MediaLibrary behavioral
  tests pass (parity), plus the new tests:
  - `ImportsLegacySidecarsOnFirstUse_AndLeavesSidecarAndAssetOnDisk`
  - `ImportsLegacySidecars_SecondInstance_DoesNotDuplicate`
  - `ImportsLegacySidecars_MalformedFile_IsSkipped_DoesNotThrow`
  - `NewImports_DoNotWriteSidecarFiles`
  (The `Error loading service order: ...` console line is a tolerated log from a pre-existing,
  unrelated service-order test; it is not a test failure — the suite is fully green.)

## What was and was not verified

Verified:
- Full-solution build is clean (0 errors; only the 4 pre-existing warnings).
- Full test suite green (590/590), including parity for all existing MediaLibrary tests and the new
  sidecar-import + no-new-sidecar tests.
- Metadata persists in `MediaItems` in `app.db` via `AppDatabase`; asset binaries stay on disk
  (asserted by `NewImports_DoNotWriteSidecarFiles` and the sidecar test, which confirm the asset
  file exists while no `*_metadata.json` is written for new imports and existing sidecars are left
  on disk).
- One-time import is non-duplicating (second-instance test) and tolerant of malformed sidecars.
- `MediaLibraryFilter` is unchanged (no diff); `IMediaLibraryService` surface unchanged (no diff).
- Scope limited to the Media store + its construction site + its tests (git status: only
  `MediaLibraryService.cs`, `MediaLibraryServiceTests.cs`, `PresenterIntegration.cs` changed).
- `ClearAllPools()` is called in the test teardown (and in each new test's `finally`).

Not verified (out of scope / not runnable here):
- No end-to-end WPF runtime launch of the app (the UI project is `net8.0-windows`; it builds, but
  interactive run was not performed).
- No migration of settings / service-orders / songs (explicitly deferred to later phases).
- Real on-disk production `app.db` migration from an existing user's Media folder was not exercised
  beyond the automated sidecar-import tests.
