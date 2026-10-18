# SQLite mirror for Songs and Setlists (Phase 6)

SongManager now keeps a reconciled SQLite mirror of the Songs and Setlists stores in the shared `app.db` via `AppDatabase`, following the Phase-5 `ServiceOrderManager` MIRROR pattern. On-disk `song.json` / setlist `*.json` files, `song.json.N` backups, and `_song_registry.json` stay authoritative and are written/read exactly as before; the mirror is populated after the authoritative writes and reconciled against the in-memory lists on load. The constructor gains an optional `databasePath` arg so existing 2-arg call sites keep compiling, and MainWindow passes the single shared `app.db` path. All DB work is best-effort try/catch-swallow so a mirror failure never changes file behavior or the in-memory `Songs`/`Setlists` lists.

Watch for: mirror identity uses `COLLATE NOCASE` on the PK columns (confirmed) to match the app-wide `OrdinalIgnoreCase` FolderPath identity — a deliberate, well-reasoned deviation from the plan's plain `TEXT PRIMARY KEY`, documented in FEAT-002 findings. No blocking concerns found.

**Verdict**: APPROVED

## High-level view

The design copies the established mirror pattern verbatim: a `DataJson` column holding the full object graph serialized with the manager's own `_jsonOptions`, plus promoted columns (Title/Artist/Key/CCLI/Themes/BPM for songs, Name/Date for setlists) for future listing/search, keyed by `SongPathResolver.Normalize` of the folder/file path. Writes mirror after the authoritative JSON write; loads reconcile (upsert present, prune vanished via parameterized `NOT IN`); first init runs an empty-table-guarded one-time import that leaves JSON on disk.

The FolderPath identity contract is honored. `SongPathResolver.Normalize` canonicalizes separators and trailing slashes but does not fold Windows drive-letter case, and the app compares with `OrdinalIgnoreCase`. A default case-sensitive SQLite PK would therefore split `C:\x` and `C:\X\` into two rows. The implementer caught this (it initially produced 2 rows) and declared the mirror PK columns `COLLATE NOCASE`, confined to the internal mirror schema — authoritative JSON and in-memory lists are untouched. The new `MirrorKey_IsNormalizedFolderPath_UpsertNotDuplicate` test locks this behavior in.

The read/edit surface is unchanged. `SongLibraryService` is not in the diff; listing/search/filter and `UpdateMetadataAsync` / `UpdateSlidesAsync` (index-matched partial update) / `ImportChordProAsync` / `DeleteAsync` all still operate on the in-memory `Songs` list and are preserved byte-for-byte. `ServiceOrderManager.CreateFromSetlist` is untouched; it consumes an in-memory `Setlist`.

Scope is tight: SongManager + MainWindow wiring + three updated test fixtures + one new test file. Models are not reshaped, no EF, Microsoft.Data.Sqlite only, `bible.db` stays separate, stems stay on disk. Build is clean (0 errors) and the full suite is green (609 passed, 0 failed, 0 skipped).

<details>
<summary>Issues (0)</summary>

No blocking or non-blocking changes required.

</details>

<details>
<summary>Details</summary>

### Mirror topology and write ordering

`SaveSongAsync` calls `UpsertSongRowSafeAsync(song)` strictly after `File.WriteAllTextAsync(songJsonPath, json)` and after `BackupSongJson`, so the backup rotation and the authoritative write are untouched and run first. `SaveSetlistAsync` likewise upserts only after the setlist `.json` is written. `LoadAllSongsAsync` reconciles against `Songs` after the folder scan and `LoadSongsFromRegistryAsync`, so the mirror reflects exactly the deduped in-memory list; `LoadAllSetlistsAsync` reconciles against the scanned `(path, setlist)` pairs. The in-memory list contents and ordering come only from the scan — the reconcile does not touch them (confirmed).

`RemoveSong` deliberately adds no mirror call; the removed row is pruned on the next `LoadAllSongsAsync` reconcile, matching the Phase-5 out-of-band-delete absorption (confirmed against FEAT-001 step 10).

### FolderPath identity via COLLATE NOCASE

The one deviation from the written plan: `CreateSongsSchemaSql` and `CreateSetlistsSchemaSql` declare `FolderPath TEXT PRIMARY KEY COLLATE NOCASE` / `FilePath TEXT PRIMARY KEY COLLATE NOCASE` rather than a plain PK (confirmed). This is required for correctness, not cosmetic: `Normalize` leaves drive-letter case intact, and a case-sensitive PK produced duplicate rows for case/separator variants of the same path, violating the acceptance criterion's FolderPath identity contract and the app-wide `OrdinalIgnoreCase` identity. The change is confined to the mirror schema; authoritative JSON and in-memory lists are unaffected. The deviation is documented in FEAT-002 findings and covered by a dedicated test.

### One-time import, non-duplicating

`ImportSongsIfNeededAsync` returns early unless `_songsBasePath` exists and `SELECT COUNT(*) FROM Songs` is 0, then imports base-path subdirs and `_song_registry.json`-referenced external folders via `INSERT OR IGNORE`, deduping within the pass with an `OrdinalIgnoreCase` key set. Per-file parsing and the whole method body are try/catch-swallowed, so a malformed or locked file is skipped and the import never throws. `ImportSetlistsIfNeededAsync` mirrors this for setlist files. The empty-table guard plus `INSERT OR IGNORE` make a second load non-duplicating — asserted by `ImportsOnDiskSongsAndSetlists_IntoMirror_NonDuplicating`, which also round-trips a Song's section and track out of `DataJson` (confirmed).

### Failure isolation

Every public-facing mirror entry point (`UpsertSongRowSafeAsync`, `ReconcileSongsSafeAsync`, `UpsertSetlistRowSafeAsync`, `ReconcileSetlistsSafeAsync`) wraps `EnsureInitializedAsync` + the DB work in try/catch and swallows. This is fail-open by design: a bad/unwritable db path or a lock cannot surface out of `SaveSongAsync` / `LoadAllSongsAsync` or alter the in-memory list. The JSON layer is the source of truth, so the fail-open posture is appropriate here (confirmed against FEAT-001 acceptance criterion 4).

### Test seams and cleanup

The three existing fixtures (`SongLibraryServiceTests`, `SongManagerDedupTests`, `ChordProImportTests`) inject a temp `app.db` as the 3rd ctor arg — including both reload managers in `SongLibraryServiceTests` — and call `SqliteConnection.ClearAllPools()` in teardown before deleting the temp folder, the mandatory Windows file-lock-release pattern. The new `SongManagerSqliteMirrorTests` follows the same teardown. All prior assertions are preserved; the suite runs 609 passed / 0 failed / 0 skipped (607 baseline + 2 new).

Not tested: a forced DB-failure path (e.g. unwritable db) asserting that `SaveSongAsync`/`LoadAllSongsAsync` still succeed and leave the in-memory list intact — the fail-open behavior is implemented and reasoned about but not exercised by an explicit test. Not a blocker given the try/catch structure, but worth noting as a coverage gap. (confidence: confirmed)

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Core/Services/SongManager.cs` — Songs + Setlists mirror helpers, schema DDL, one-time import, reconcile, ctor `databasePath` arg.
- `WorshipPlayer.UI/Views/MainWindow.xaml.cs` — pass shared `app.db` path as 3rd SongManager arg.
- `WorshipPlayer.Tests/SongLibraryServiceTests.cs` — inject temp app.db into ctor + both reload managers.
- `WorshipPlayer.Tests/SongPathResolverTests.cs` — SongManagerDedupTests inject temp app.db + ClearAllPools.
- `WorshipPlayer.Tests/ChordProImportTests.cs` — inject temp app.db + ClearAllPools.
- `WorshipPlayer.Tests/SongManagerSqliteMirrorTests.cs` — new import + FolderPath-contract tests.

Full diff: `git -C <worktree> diff 61af5f4`

</details>
