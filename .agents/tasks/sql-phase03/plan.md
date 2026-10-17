# Implementation Plan — SQL Migration Phase 3 (Media library metadata → app.db)

## Goal

Re-back `MediaLibraryService` metadata from per-asset JSON sidecars (`<assetpath>_metadata.json`)
onto a `MediaItems` table in the shared `app.db`, reusing the `AppDatabase` helper. Asset binaries
stay on disk under the Media folder. The public `IMediaLibraryService` API and all observable
behavior stay byte-for-byte identical, and `MediaLibraryFilter` is untouched. Scope is strictly the
Media store + its construction site + its tests. Do NOT migrate settings / service-orders / songs.

## Stop contract

The implement-and-review loop stops when
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sql-phase03\.agents\tasks\sql-phase03\review.json`
has top-level `"verdict" == "APPROVED"`.

All work happens INSIDE the worktree
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sql-phase03`.
Step agents' cwd is the repo root
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter`,
but every file read/written/cited for this task lives under the worktree above. Commit locally on
the worktree's branch; never push.

---

## Context grounded in the worktree (cited)

Reference pattern — the established "re-back a JSON store onto app.db" shape to copy exactly:

- `WorshipPlayer.Core\Services\AppDatabase.cs` — the shared `app.db` path + per-call
  `SqliteConnection` factory + `EnsureDirectory`. `AppDatabase(string databasePath)` ctor
  (AppDatabase.cs:47-51), `CreateConnection()` (AppDatabase.cs:57), `EnsureDirectory()`
  (AppDatabase.cs:66-71). REUSE; do NOT add a second db helper.
- `WorshipPlayer.Core\Services\SongUsageService.cs` (Phase 1) — ctor takes a db path and derives a
  sibling legacy JSON path (SongUsageService.cs:60-65); `EnsureInitializedAsync` runs
  `CREATE TABLE IF NOT EXISTS` then the one-time import under an `_initialized` guard
  (SongUsageService.cs:178-198); `ImportLegacyJsonIfNeededAsync` is empty-table-guarded, tolerant,
  never throws (SongUsageService.cs:206-253).
- `WorshipPlayer.Presenter\Services\AlertService.cs` (Phase 2) — the closest analog: real columns for
  queried fields + a `DataJson` TEXT blob for the whole object (schema AlertService.cs:40-47),
  enum-as-string via `JsonStringEnumConverter` + `WriteIndented` (AlertService.cs:33-37), `ON
  CONFLICT(Id) DO UPDATE` upsert (AlertService.cs:106-115), `GetTemplatesAsync` deserializes the blob
  (AlertService.cs:123-144), defensive empty-table-guarded import (AlertService.cs:171-220).
- Bible test discipline: tests call `SqliteConnection.ClearAllPools()` in teardown so Windows
  releases the temp `.db` lock — see `AlertServiceTests.Dispose()`
  (WorshipPlayer.Tests\AlertServiceTests.cs:38-52).
- Survey, Media domain #4: `.agents\tasks\sql-migration-surface.md` (row 4 of §2; §5 callout 3).

Store being migrated:

- `WorshipPlayer.Core\Services\MediaLibraryService.cs` (implements `IMediaLibraryService`).
  Current ctor takes `libraryPath` only and creates the folder (MediaLibraryService.cs:43-49).
  In-memory `_items` + `_loaded` cache (MediaLibraryService.cs:16-17). `EnsureLoadedAsync` globs
  `*_metadata.json` and deserializes into `_items`, skipping malformed files
  (MediaLibraryService.cs:261-290). `SaveMetadataAsync` writes a sidecar
  (MediaLibraryService.cs:245-250); `GetMetadataPath` = `filePath + "_metadata.json"`
  (MediaLibraryService.cs:252-255).
- Public API (must stay identical) — `IMediaLibraryService`
  (WorshipPlayer.Core\Services\Interfaces\IMediaLibraryService.cs:12-66):
  `ImportAsync(src, tags?)`, `GetAllAsync`, `SearchAsync(query)`, `DeleteAsync(id, deleteFile)`,
  `GetByTagAsync(tag)`, `GetByTypeAsync(type)`, `UpdateTagsAsync(id, tags)`, `GetLibraryPath()`.
- Behaviors to preserve exactly:
  - Path-traversal guards `ValidatePath` (MediaLibraryService.cs:170-182) and
    `ValidateDestinationPath` (MediaLibraryService.cs:187-194), both called before any copy.
  - Unique-name copy: `GetUniqueFilePath` + counter (MediaLibraryService.cs:207-227) and
    `CopyFileAsync` with `FileMode.CreateNew` (MediaLibraryService.cs:238-243).
  - `ImportAsync` sets `Name = GetFileNameWithoutExtension(sanitizedName)`, `MediaType` from
    extension, `FileSize` from the copied file, `DateAdded = UtcNow`, `Id = Guid` and returns the
    item (MediaLibraryService.cs:52-91).
  - `SearchAsync` empty/whitespace query returns all; otherwise case-insensitive name OR tag
    substring (MediaLibraryService.cs:97-110).
  - `GetByTagAsync` case-insensitive exact membership (MediaLibraryService.cs:131-137).
  - `GetByTypeAsync` type equality (MediaLibraryService.cs:140-145).
  - `DeleteAsync` removes from the store and, only when `deleteFile == true`, deletes the on-disk
    asset (MediaLibraryService.cs:113-128).
  - `GetLibraryPath()` returns the configured folder (MediaLibraryService.cs:167).
- `MediaLibraryFilter` composes type AND tag AND search in memory over a `MediaLibraryItem` list and
  must keep working unchanged (MediaLibraryFilter.cs:34-61). It depends ONLY on
  `MediaLibraryItem` fields, so it is untouched by this migration.
- Model `WorshipPlayer.Core\Models\MediaLibraryItem.cs`: fields `Id, Name, FilePath, MediaType
  (enum, JsonStringEnumConverter), ThumbnailPath, Tags (List<string>), FileSize, DateAdded,
  OrganizationId` (MediaLibraryItem.cs:10-39). Enum `MediaLibraryType { Image, Video, Audio }`
  (MediaLibraryItem.cs:45-50). Model is NOT modified.
- Construction site: `WorshipPlayer.UI\Services\PresenterIntegration.cs`. `appDbPath` is already
  resolved at PresenterIntegration.cs:106 (`Path.Combine(appFolder, "app.db")`) and reused for
  `SongUsageService` (line 107), `AlertService` (line 118), `TemplateService` (line 127). Media is
  built at `var mediaPath = Path.Combine(appFolder, "Media"); MediaLibraryService = new
  MediaLibraryService(mediaPath);` (PresenterIntegration.cs:110-111).

Project facts:

- `WorshipPlayer.Core.csproj` already references `Microsoft.Data.Sqlite` 8.0.0
  (WorshipPlayer.Core.csproj:11) — so `MediaLibraryService` STAYS in Core; no csproj change needed.
- `WorshipPlayer.Tests.csproj` references Core directly (WorshipPlayer.Tests.csproj:23) and already
  references `Microsoft.Data.Sqlite` 8.0.0 (line 11); `MediaLibraryServiceTests.cs` and
  `MediaLibraryFilterTests.cs` already exist in that project.
- Baseline verified green in this worktree before planning: `dotnet build
  WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` → 0 warnings / 0 errors; the 33 `MediaLibrary*`
  tests pass.

---

## Design decisions (made here, with rationale)

### D1 — `MediaItems` schema: promoted columns + `DataJson` blob

```sql
CREATE TABLE IF NOT EXISTS MediaItems (
    Id             TEXT PRIMARY KEY,
    Name           TEXT NOT NULL DEFAULT '',
    FilePath       TEXT NOT NULL DEFAULT '',
    MediaType      TEXT NOT NULL DEFAULT 'Image',
    Tags           TEXT NOT NULL DEFAULT '[]',
    OrganizationId TEXT NOT NULL DEFAULT '',
    DataJson       TEXT NOT NULL
);
```

Rationale (mirrors `AlertService` schema AlertService.cs:40-47): promote the fields the task names as
queried/filtered — `Id`, `Name`, `FilePath`, `MediaType`, `Tags`, `OrganizationId` — to real columns,
and keep the whole `MediaLibraryItem` serialized in `DataJson` so any remaining fields
(`ThumbnailPath`, `FileSize`, `DateAdded`) round-trip losslessly without a column each. `MediaType`
is stored as its enum NAME ("Image"/"Video"/"Audio"), consistent with `JsonStringEnumConverter`
(MediaLibraryItem.cs:45-46) and the Phase-1/2 enums-as-strings rule.

### D2 — Tag storage: `Tags` column holds the JSON array text (same `JsonStringEnumConverter`+indent options)

Store tags as the JSON serialization of `List<string>` (e.g. `["worship","background"]`) in the
`Tags` column. Rationale: the current filtering is all in-memory LINQ over `item.Tags`
(`GetByTagAsync` MediaLibraryService.cs:131-137, `SearchAsync` MediaLibraryService.cs:97-110,
`MediaLibraryFilter` MediaLibraryService.cs → MediaLibraryFilter.cs:46-61). This plan keeps that
in-memory model (see D3), so the `Tags` column exists for completeness/debuggability and future
SQL-side querying, while the authoritative tag list for every query is the deserialized
`MediaLibraryItem.Tags` from `DataJson`. A JSON array (not a delimiter) avoids any ambiguity when a
tag itself contains a delimiter character, so parity with the exact `List<string>` is guaranteed.
`Tags` and `DataJson` are produced from the same object with the same `JsonOptions` already present
in the service (MediaLibraryService.cs:21-25), so they never diverge.

### D3 — Keep the in-memory `_items` cache; `GetAllAsync` loads it from `MediaItems`

Preserve the existing `_items` list + `_loaded` flag design (MediaLibraryService.cs:16-17) so that
`SearchAsync`, `GetByTagAsync`, `GetByTypeAsync` and `MediaLibraryFilter` keep operating over the
exact same in-memory `List<MediaLibraryItem>` with byte-for-byte identical LINQ semantics. Only the
SOURCE of that list changes: `EnsureLoadedAsync` reads rows from `MediaItems` (deserializing
`DataJson`) instead of globbing `*_metadata.json`. This is the lowest-risk way to guarantee query
parity — the only code that touches SQLite is load + the per-item upsert/delete.

### D4 — Sidecar-file disposition: LEAVE existing sidecars on disk; stop writing new ones

After the one-time import, do NOT delete existing `*_metadata.json` sidecar files, and never delete
asset binaries. Rationale: least-destructive (the task's stated preference). The import is
empty-table-guarded so leaving the sidecars cannot cause re-import or duplication; a stale sidecar is
simply ignored going forward because `EnsureLoadedAsync` reads from the table, not from disk. New
imports write a `MediaItems` row and NO sidecar (`SaveMetadataAsync`'s sidecar write is removed).

### D5 — Constructor signature

`public MediaLibraryService(string libraryPath, string? databasePath = null)`.

Rationale: keep the existing library-folder parameter (callers/tests inject a temp media dir
unchanged) and ADD an optional db path. When `databasePath` is null, default to
`<libraryParentOrAppFolder>/app.db`. To match the other migrated services' "the caller passes the
path" convention and the single-`app.db` architecture, PresenterIntegration passes the already-
resolved `appDbPath` explicitly (see item 4), so the null-default path is only a fallback for callers
that don't supply one. Default resolution: `databasePath ?? Path.Combine(AppDatabase.DefaultPath's
folder…)` — concretely use `AppDatabase.DefaultPath` (AppDatabase.cs:36-37) as the default so the
fallback lands on the standard app-folder `app.db`, consistent with Phase 0/1/2.

---

## Plan items

- [ ] 1. Rewrite `MediaLibraryService` to back metadata on `MediaItems` in `app.db` via `AppDatabase`,
      keeping the public API, behaviors, and the in-memory `_items`/`_loaded` cache identical.
      Changes, following `AlertService`/`SongUsageService` exactly:
      - Ctor → `MediaLibraryService(string libraryPath, string? databasePath = null)` (D5). Keep
        creating the library folder (MediaLibraryService.cs:46-48). Set
        `_database = new AppDatabase(databasePath ?? AppDatabase.DefaultPath)`. The media folder path
        stays `_libraryPath`.
      - Add `private bool _initialized;` and `EnsureInitializedAsync()` that calls
        `_database.EnsureDirectory()`, runs the D1 `CREATE TABLE IF NOT EXISTS MediaItems` DDL
        (idempotent), then `ImportSidecarsIfNeededAsync(connection)`, guarded by `_initialized`
        (copy the shape of SongUsageService.cs:178-198 / AlertService.cs:149-169).
      - `ImportAsync`: unchanged validation/copy/item-construction (MediaLibraryService.cs:54-88);
        then `await EnsureInitializedAsync()`, add to `_items`, and `await UpsertAsync(item)` INSTEAD
        OF `SaveMetadataAsync`. (Call `EnsureInitializedAsync` at the top so the table exists.)
      - `UpsertAsync(MediaLibraryItem)`: open a connection, `INSERT INTO MediaItems (Id, Name,
        FilePath, MediaType, Tags, OrganizationId, DataJson) VALUES (@Id,@Name,@FilePath,@MediaType,
        @Tags,@OrganizationId,@DataJson) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name,
        FilePath=excluded.FilePath, MediaType=excluded.MediaType, Tags=excluded.Tags,
        OrganizationId=excluded.OrganizationId, DataJson=excluded.DataJson` (mirror
        AlertService.cs:106-115). Bind: `@MediaType = item.MediaType.ToString()`,
        `@Tags = JsonSerializer.Serialize(item.Tags, JsonOptions)`,
        `@DataJson = JsonSerializer.Serialize(item, JsonOptions)`, others from the item.
      - `GetAllAsync`: `await EnsureLoadedAsync(); return new List<MediaLibraryItem>(_items);`
        (unchanged signature/behavior, MediaLibraryService.cs:94-98).
      - `SearchAsync`, `GetByTagAsync`, `GetByTypeAsync`: unchanged bodies — they already run over
        `_items` after `EnsureLoadedAsync` (MediaLibraryService.cs:101-145). Keep as-is (D3).
      - `DeleteAsync(id, deleteFile)`: `await EnsureLoadedAsync()`, find+remove from `_items`
        (unchanged), REPLACE the sidecar delete (MediaLibraryService.cs:122-124) with a
        `DELETE FROM MediaItems WHERE Id=@Id` command, and KEEP the `deleteFile && File.Exists` asset
        delete (MediaLibraryService.cs:127) verbatim.
      - `UpdateTagsAsync(id, tags)`: `await EnsureLoadedAsync()`, set `item.Tags = tags` on the cached
        item (unchanged), then `await UpsertAsync(item)` instead of `SaveMetadataAsync`
        (MediaLibraryService.cs:148-158).
      - `GetLibraryPath()`: unchanged (MediaLibraryService.cs:167).
      - `EnsureLoadedAsync()`: `await EnsureInitializedAsync()` first; then clear `_items`,
        `SELECT DataJson FROM MediaItems`, deserialize each row into a `MediaLibraryItem` (skip
        nulls), add to `_items`, set `_loaded = true`. Remove the `*_metadata.json` globbing
        (MediaLibraryService.cs:273-287). Keep the try/catch-per-row tolerance (deserialize failures
        are skipped, matching the old "skip invalid metadata files" behavior
        MediaLibraryService.cs:283-285).
      - `ImportSidecarsIfNeededAsync(SqliteConnection)` (D4): defensive one-time import. Wrap the
        whole body in try/catch that swallows everything (never throws — mirror
        AlertService.cs:171-220). Steps: if `!Directory.Exists(_libraryPath)` return; run
        `SELECT COUNT(*) FROM MediaItems` and return if > 0 (empty-table guard → non-duplicating);
        glob `Directory.GetFiles(_libraryPath, "*_metadata.json")`; for each, try to read +
        `JsonSerializer.Deserialize<MediaLibraryItem>` (skip the single file on failure, continue);
        for each valid item assign `Id = Guid` if empty, and `INSERT OR IGNORE INTO MediaItems (...)`
        inside one transaction (mirror AlertService.cs:196-216). Do NOT delete sidecar files and do
        NOT touch asset binaries.
      - Remove the now-unused `SaveMetadataAsync` (MediaLibraryService.cs:245-250) and `GetMetadataPath`
        (MediaLibraryService.cs:252-255). Add `using Microsoft.Data.Sqlite;` and
        `using WorshipPlayer.Core;` namespace already in place.
      Files: `WorshipPlayer.Core\Services\MediaLibraryService.cs`
      Verify: `dotnet build WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug --nologo` →
      Build succeeded, 0 errors (warning count no worse than baseline 0).

- [ ] 2. Update `MediaLibraryServiceTests` to inject a temp `app.db` alongside the temp media dir and
      clear SQLite pools in teardown, so existing behavioral assertions still pass (parity).
      - Add fields `_dbPath` and construct `_service = new MediaLibraryService(_tempFolder, _dbPath)`
        where `_dbPath = Path.Combine(_tempFolder, "app.db")`
        (current ctor call MediaLibraryServiceTests.cs:20). The temp media dir stays `_tempFolder`.
      - In `Dispose()` (MediaLibraryServiceTests.cs:24-27) call `SqliteConnection.ClearAllPools()`
        BEFORE deleting the temp folder, and wrap the delete in a try/catch(IOException) — copy the
        shape of `AlertServiceTests.Dispose()` (AlertServiceTests.cs:38-52). Add
        `using Microsoft.Data.Sqlite;`.
      - `Constructor_CreatesLibraryFolder` (MediaLibraryServiceTests.cs:30-43): update the inner
        `new MediaLibraryService(folder)` — the second arg is optional, so this still compiles, but
        pass a temp db under `folder` for cleanliness, and ClearAllPools in its finally.
      - All other test bodies are unchanged (they assert API/behavior parity).
      Files: `WorshipPlayer.Tests\MediaLibraryServiceTests.cs`
      Verify: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --filter
      "FullyQualifiedName~MediaLibraryServiceTests" --nologo` → all existing tests pass (parity;
      baseline had 20 in this class as part of the 33 MediaLibrary total).

- [ ] 3. Add a sidecar-import test proving the defensive one-time migration from
      `*_metadata.json` files into `MediaItems`.
      - New `[Fact]` in `MediaLibraryServiceTests`, e.g. `ImportsLegacySidecarsOnFirstUse`: in a
        fresh temp folder, hand-write one or two `<asset>_metadata.json` files whose JSON matches a
        serialized `MediaLibraryItem` (use the service's `JsonOptions` shape: camelCase property
        names per MediaLibraryItem.cs attributes, `mediaType` as a string). Also drop a matching
        (empty) asset file so `DeleteAsync(deleteFile:true)` semantics could be exercised if desired.
        Construct a NEW `MediaLibraryService(folder, dbPath)` against a fresh `app.db`, call
        `GetAllAsync()`, and assert the sidecar items are present (count + a name/tag). Then assert
        the sidecar `.json` files still EXIST on disk (D4 — not deleted).
      - Add a second assertion/`[Fact]` for non-duplication: call `GetAllAsync()` again on a second
        fresh service against the SAME `app.db` with the SAME sidecars present, and assert the count
        did not grow (empty-table guard prevents re-import).
      - Add a tolerance `[Fact]` (optional but recommended): write a malformed `*_metadata.json`
        (invalid JSON) alongside a valid one; assert `GetAllAsync()` returns the valid item and does
        not throw.
      Files: `WorshipPlayer.Tests\MediaLibraryServiceTests.cs`
      Verify: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --filter
      "FullyQualifiedName~MediaLibraryServiceTests" --nologo` → the new sidecar-import test(s) pass
      along with all existing ones.

- [ ] 4. Update the construction site to pass the already-resolved `appDbPath` into
      `MediaLibraryService`.
      - In `PresenterIntegration` ctor, change
        `MediaLibraryService = new MediaLibraryService(mediaPath);`
        (PresenterIntegration.cs:111) to
        `MediaLibraryService = new MediaLibraryService(mediaPath, appDbPath);`
        REUSING the `appDbPath` local already declared at PresenterIntegration.cs:106 (do NOT
        introduce a second `app.db` path). Keep `var mediaPath = Path.Combine(appFolder, "Media");`
        (line 110) unchanged — the service still needs the Media FOLDER for asset files. Add a brief
        comment matching the sibling services' comments (lines 105-106, 117-118, 126-127) noting
        media metadata now lives in app.db while assets stay on disk.
      Files: `WorshipPlayer.UI\Services\PresenterIntegration.cs`
      Verify: `dotnet build WorshipPlayer.UI\WorshipPlayer.UI.csproj -c Debug --nologo` → Build
      succeeded, 0 errors. (This is a net8.0-windows WPF project; builds on Windows.)

- [ ] 5. Full-solution build + full test run to confirm nothing else regressed and the build stays
      clean.
      Files: none (verification only)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo` → Build succeeded, 0 errors
      and warning count no worse than the stated baseline (0 errors, pre-existing warnings only);
      then `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj --nologo` → all tests pass
      (the full suite stays green, including the new sidecar-import test).

---

## Acceptance criteria (restated, must all hold)

1. Media metadata persists in a `MediaItems` table in `app.db` via `AppDatabase`; asset files stay on
   disk; no sidecar JSON is written for new imports (item 1).
2. `MediaLibraryService` public API + behaviors (import copy / unique-name, path-traversal guards,
   `DeleteAsync` deleteFile semantics, `GetLibraryPath`, tag/type/search queries) unchanged;
   `MediaLibraryFilter` unchanged (items 1-3, verified by parity tests).
3. Defensive one-time non-duplicating import from existing `*_metadata.json` sidecars — tolerates
   absence/malformed, never throws, does not delete assets, leaves sidecar files in place (items 1 &
   3; D4).
4. Ctor takes media folder (as today) + optional db path (default app.db); PresenterIntegration
   updated to pass the existing `appDbPath` (items 1 & 4; D5).
5. `Microsoft.Data.Sqlite` only, reuse `AppDatabase`, no EF, `bible.db` separate, asset binaries on
   disk (item 1; Core already references the package).
6. Build stays clean; scope limited to the Media store + its construction site + its tests; no
   settings/service-orders/songs touched (item 5).
7. Tests inject a temp app.db + temp media dir with `ClearAllPools` in teardown; existing behavioral
   assertions still pass; a sidecar-import test is added (items 2 & 3).

## Notes / assumptions

- Phase 2 (Alerts + Templates) IS present in this worktree under `WorshipPlayer.Presenter\Services\`
  (not under Core) — `AlertService.cs:27` / `TemplateService.cs:28` — matching "Phase 2 DONE". The
  task text's "Phase-2 AlertService/TemplateService changes" are followed from those files.
- `MediaLibraryService` stays in `WorshipPlayer.Core` (unlike Alert/Template which live in Presenter):
  Core already references `Microsoft.Data.Sqlite` (WorshipPlayer.Core.csproj:11) and the model +
  interface + filter are already in Core, so no project move is needed or desirable.
- The `Tags` promoted column is written for completeness/future SQL querying but is NOT the query
  source today — queries run over the in-memory `_items` list for guaranteed LINQ parity (D2/D3).
  This is a deliberate, documented choice; if a reviewer wants SQL-side tag filtering it would be a
  separate, behavior-visible change and must not be made silently.
