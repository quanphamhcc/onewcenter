# Implementation Plan — SQL Migration Phase 6 (Songs + Setlists)

All work happens inside the worktree
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\sql-phase06`.
Step agents' cwd is the main repo, so use absolute paths and
`git -C <worktree>` for every file read/write/commit.

Baseline verified: `dotnet build WorshipBacktrackPlayer.sln` → 0 errors, 4 pre-existing warnings;
`dotnet test WorshipPlayer.Tests ... --filter Song|ChordPro` → 69 passed. HEAD `61af5f4`.

Build command (full): `dotnet build "<worktree>\WorshipBacktrackPlayer.sln" -c Debug`
Test command (targeted): `dotnet test "<worktree>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj" --filter "FullyQualifiedName~Song|FullyQualifiedName~ChordPro"`
Test command (full suite): `dotnet test "<worktree>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj"` (expect 607+ pass)

---

## Design decisions (grounded in code)

**D1 — Storage shape: MIRROR, not authoritative-DB.** Prefer the lower-risk Phase-5 mirror. Justification:
- `SongLibraryServiceTests.cs:255-262` and `:338-341` reload from disk via a *fresh* `SongManager`
  on the same folders and assert the reloaded `song.json` content (title/sections/chords/CCLI). The
  DB is never consulted on reload. JSON files must stay authoritative.
- `ChordProImportTests.cs:66-78` asserts `song.json` exists on disk and round-trips to a `Song`.
- `SongManagerDedupTests.LoadAllSongs_SongInFolderAndRegistry_LoadedOnce` (`SongPathResolverTests.cs:132-168`)
  asserts folder-scan + `_song_registry.json` dedup, reading only disk.
- `SongLibraryService` reads/searches/filters purely off the in-memory `_songManager.Songs` list
  (`SongLibraryService.cs:27-83`), so the mirror can be write-only and never alters read-path behavior.
  This is the SAME conclusion Phase 5 reached (`ServiceOrderManager.cs` remarks; approved in
  `.agents/tasks/sql-phase05/review.json`). **Deviation from the literal "drop JSON" goal is intentional
  and flagged**, exactly as Phase 5 did.

**D2 — Identity → key column.** `SongPathResolver.Normalize(folderPath)` (`SongPathResolver.cs:26-45`)
is the stable key for Songs, mirroring `ServiceOrderManager.NormalizePath` used as the `FilePath`
PRIMARY KEY. Setlists are keyed by the normalized setlist *file path* (one file per setlist,
`SongManager.SaveSetlistAsync` names it `SanitizeFileName(Name)+".json"`, `SongManager.cs:287-296`).
`Song.FolderPath` stays `[JsonIgnore]` (`Song.cs:88-90`); it is set at runtime from the dir and used
as the mirror key — the ~135 call sites and `SongPathResolver` are untouched.

**D3 — Backups + registry preserved as-is.** `BackupSongJson` rotation (`SongManager.cs:168-200`) and
`_song_registry.json` (`SongManager.cs:76-146`) stay exactly as they are on disk — the mirror does not
touch them. `RestoreBackupAsync`/`GetAvailableBackups` keep operating on on-disk backups
(`SongManager.cs:205-240`). Documented, no behavior change.

**D4 — Reuse `AppDatabase`; no second helper, no EF; `bible.db` separate.** New tables `Songs` and
`Setlists` in the shared `app.db` via `AppDatabase` (`AppDatabase.cs`), DDL `CREATE TABLE IF NOT EXISTS`
run on init, exactly like `SongUsageService`/`ServiceOrderManager`.

**D5 — Constructor db-path arg is OPTIONAL and defaulted** (`databasePath = null` → `AppDatabase.DefaultPath`),
so every existing 2-arg `new SongManager(songs, setlists)` call site keeps compiling unchanged
(`MainWindow.xaml.cs:131`, and the test fixtures that reload via `new SongManager(songs, setlists)`).

### Table schemas (both in `app.db`)

```sql
CREATE TABLE IF NOT EXISTS Songs (
    FolderPath   TEXT PRIMARY KEY,   -- SongPathResolver.Normalize(Song.FolderPath)
    Title        TEXT NOT NULL DEFAULT '',
    Artist       TEXT NOT NULL DEFAULT '',
    OriginalKey  TEXT NOT NULL DEFAULT '',
    Ccli         TEXT NOT NULL DEFAULT '',
    Themes       TEXT NOT NULL DEFAULT '',   -- raw Song.Themes string (comma-joined tags)
    Bpm          REAL NOT NULL DEFAULT 0,
    LastWriteUtc TEXT NOT NULL DEFAULT '',
    DataJson     TEXT NOT NULL                -- full Song graph via _jsonOptions
);

CREATE TABLE IF NOT EXISTS Setlists (
    FilePath     TEXT PRIMARY KEY,            -- SongPathResolver.Normalize(setlist file path)
    Name         TEXT NOT NULL DEFAULT '',
    Date         TEXT NOT NULL DEFAULT '',
    LastWriteUtc TEXT NOT NULL DEFAULT '',
    DataJson     TEXT NOT NULL                -- full Setlist graph via _jsonOptions
);
```

Promoted columns cover library listing/search/filter inputs (`SongLibraryService.MapToLibraryItem`,
`SearchAsync`, `FilterByKeyAsync`, `FilterByTagAsync`), but the live read path still uses the in-memory
list; promoted columns + `DataJson` exist for future DB-backed reads and for parity verification.

---

## Ordered items

- [ ] 1. Add the Songs mirror to `SongManager`: optional `databasePath` ctor arg + `AppDatabase` field,
      `CREATE TABLE IF NOT EXISTS Songs` DDL, `EnsureInitializedAsync` (schema + one-time import guard),
      and best-effort upsert helpers. Wire `SaveSongAsync` to upsert the saved song's row (keyed by
      `SongPathResolver.Normalize(song.FolderPath)`) AFTER the authoritative `song.json` write. Wire
      `LoadAllSongsAsync` to upsert+prune the mirror against the songs actually loaded (absorb removed
      folders), after the existing scan + registry load. All DB work wrapped in try/catch so a DB
      failure never changes file behavior or the `Songs` list. Backups + registry untouched.
      Files: `WorshipPlayer.Core/Services/SongManager.cs`
      Verify: `dotnet build "<worktree>\WorshipBacktrackPlayer.sln" -c Debug` → 0 errors; then
      `dotnet test ...WorshipPlayer.Tests... --filter "FullyQualifiedName~Song|FullyQualifiedName~ChordPro"`
      → all existing tests still pass (parity).

- [ ] 2. Add the one-time defensive Songs import: on first `EnsureInitializedAsync`, if the `Songs`
      table is empty, import each on-disk `<folder>/song.json` under the songs base path (and registry
      entries) as a mirror row — `INSERT OR IGNORE`, tolerate missing folder/malformed files, never
      throw; set `FolderPath` from the dir; JSON left on disk. Reuse the same scan logic shape as
      `LoadAllSongsAsync`. Non-duplicating via the empty-table guard (mirrors
      `ServiceOrderManager.ImportFilesIfNeededAsync`, `ServiceOrderManager.cs`).
      Files: `WorshipPlayer.Core/Services/SongManager.cs`
      Verify: same build + targeted test commands pass.

- [ ] 3. Add the Setlists mirror to `SongManager`: `CREATE TABLE IF NOT EXISTS Setlists` DDL, upsert on
      `SaveSetlistAsync` (keyed by normalized setlist file path) after the authoritative file write,
      upsert+prune on `LoadAllSetlistsAsync` after the scan, and a one-time defensive import of
      `<SetlistsFolder>/*.json` when the table is empty. Best-effort/never-throw. `CreateFromSetlist`
      (in `ServiceOrderManager`) is unaffected — it still consumes an in-memory `Setlist`.
      Files: `WorshipPlayer.Core/Services/SongManager.cs`
      Verify: same build + targeted test commands pass.

- [ ] 4. Wire construction sites to pass the shared `app.db` path. In `MainWindow.xaml.cs:131`, compute
      the app-folder `app.db` path (same `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.db")`
      the other stores use, see `PresenterIntegration.cs:85-86`) and pass it as the 3rd arg to
      `new SongManager(songsPath, setlistsPath, appDbPath)`. `SongLibraryService` needs no new arg —
      it holds the `SongManager` (`PresenterIntegration.cs:115`). Add a comment mirroring the Phase-5
      wording (JSON authoritative; app.db mirror).
      Files: `WorshipPlayer.UI/Views/MainWindow.xaml.cs`
      Verify: `dotnet build "<worktree>\WorshipBacktrackPlayer.sln" -c Debug` → 0 errors, 4 warnings.

- [ ] 5. Update existing song tests to inject a temp `app.db` and clear pools in teardown, keeping all
      current behavioral assertions. Pass `dbPath` (temp `app.db` under the test temp folder) as the 3rd
      `SongManager` arg in `SongLibraryServiceTests` (ctor + both reload managers at `:255`/`:338`),
      `SongPathResolverTests`' `SongManagerDedupTests`, and `ChordProImportTests`. Add
      `SqliteConnection.ClearAllPools()` in each affected test class's `Dispose` (SongPathResolver dedup
      and ChordPro currently lack it) so the temp `.db` can be deleted on Windows (pattern:
      `SongLibraryServiceTests.Dispose`).
      Files: `WorshipPlayer.Tests/SongLibraryServiceTests.cs`, `WorshipPlayer.Tests/SongPathResolverTests.cs`,
      `WorshipPlayer.Tests/ChordProImportTests.cs`
      Verify: targeted test command → same pass count as baseline (69) plus still green.

- [ ] 6. Add new import + FolderPath-contract tests. New file `SongManagerSqliteMirrorTests.cs`:
      (a) write two on-disk song folders (`song.json` with sections/tracks/metadata) + one setlist file,
      construct a `SongManager` against a FRESH temp `app.db`, call `LoadAllSongsAsync`/`LoadAllSetlistsAsync`,
      assert the `Songs`/`Setlists` tables each got one row per on-disk item preserving ids/paths and the
      full graph (query `DataJson`), and that a SECOND construct+load does NOT duplicate rows
      (non-duplicating import); (b) a FolderPath-contract test asserting the mirror row key equals
      `SongPathResolver.Normalize(song.FolderPath)` and that a save with a case/separator-variant path
      upserts the SAME row (no duplicate). Temp `app.db` + `SqliteConnection.ClearAllPools()` in `Dispose`.
      Files: `WorshipPlayer.Tests/SongManagerSqliteMirrorTests.cs` (new)
      Verify: targeted test command → new tests pass, existing stay green.

- [ ] 7. Cross-cutting verification: run the FULL test suite and full build.
      Verify: `dotnet build "<worktree>\WorshipBacktrackPlayer.sln" -c Debug` → 0 errors, 4 warnings;
      `dotnet test "<worktree>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj"` → all green (≥607 + new).

---

## Scope guardrails
- Touch ONLY `SongManager.cs`, `SongLibraryService.cs` (likely unchanged — reads stay in-memory),
  `SongPathResolver.cs` (likely unchanged — reused), `MainWindow.xaml.cs`, and the four test files.
- Do NOT change `Song`/`Section`/`Setlist` model shapes beyond what's already additive; keep JSON
  round-trippable (`WhenWritingNull`, `JsonStringEnumConverter`).
- Do NOT touch service-orders/settings/media/alerts/templates/usage code — already migrated.
- Keep audio stems and `song.json`/backups/registry/setlist files ON DISK.
