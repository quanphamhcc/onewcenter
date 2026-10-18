# SQL Migration Surface Map (JSON → SQLite)

READ-ONLY investigation. Repo: `WorshipPlayer` (.NET 8 WPF). Actual HEAD at investigation time:
`9bd0eb4` (branch `main`, `origin/dev1`) — the brief referenced `5cf1ed8`, but the working tree
is `9bd0eb4`; everything below is cited against the current tree. The sibling folder
`owc-multitrack-feature-lyric-presenter 2` was ignored per instructions.

---

## 1. Summary answer

The app persists state across **10 distinct mechanisms**, all file-based JSON except the Bible
(already SQLite). Every store is constructed with an absolute path derived from
`AppDomain.CurrentDomain.BaseDirectory` (the app output folder) — **except** Songs/Setlists, whose
paths come from `settings.json` (`songsFolder` / `setlistsFolder`, default `"Songs"` / `"Setlists"`)
and may point anywhere the user picked at first launch.

Central construction happens in two files:
- `WorshipPlayer.UI\Services\PresenterIntegration.cs` constructor (lines ~71-123) — ServiceOrders,
  SongUsage, SongLibrary, Media, Alerts, Bible, Templates.
- `WorshipPlayer.UI\Views\MainWindow.xaml.cs` `MainWindow_Loaded` (lines 53-130) — Settings (AppSettings),
  SongManager (Songs/Setlists), TrainingData; plus a **second**, standalone presenter-settings
  reader/writer at lines 1505-1536.

The single reference pattern for the SQLite target is `BibleDatabaseService` +
`BibleSeedData` (`WorshipPlayer.Core\Services\Bible\`), using `Microsoft.Data.Sqlite` with
`Data Source=<path>` and `CREATE TABLE IF NOT EXISTS` schema SQL.

**No real user data exists on disk in the repo today** — `Songs/`, `ServiceOrders/`, and `Setlists/`
are all empty, and there is no `song-usage.json`, `Templates/`, `Media/`, or `bible.db` committed.
Only a root `settings.json` (AppSettings) is present. The runtime creates every store under the build
output folder (`bin/.../`) at first run. This means the migration can be designed primarily for the
**schema + code**, with import tooling validated against synthetic data (and the matching test-fixture
patterns), rather than a committed corpus.

The biggest structural mismatches for a relational move are: the **per-song folder layout**
(`Songs/<name>/song.json` with nested `sections[]`, `tracks[]`, `midiCues[]`, `chordChart`, and
inline `[chord]`/`{timestamp}` text — plus a 5-deep `song.json.N` backup rotation and a
`_song_registry.json` for out-of-tree songs); the **media sidecar files** (`<file>_metadata.json` next
to each copied asset); and **settings split across two files/models** (`settings.json`=AppSettings and
`presenter-settings.json`=PresenterSettings) with two independent writers for the latter.

---

## 2. Per-domain table

| # | Domain | Service + file | Current store (location + format) | Model type(s) | Public read/write API | Constructed where | Call-site count* | Backward-compat / serialization notes |
|---|--------|----------------|-----------------------------------|---------------|-----------------------|-------------------|------------------|----------------------------------------|
| 1 | Songs / lyrics | `SongManager` (`Core\Services\SongManager.cs`); `SongLibraryService` (`Core\Services\SongLibraryService.cs`); `SongPathResolver` (`Core\Services\SongPathResolver.cs`) | Per song: `<SongsFolder>/<folder>/song.json`; backups `song.json.1..5`; out-of-tree index `<SongsFolder>/_song_registry.json` (JSON `List<string>`). Also AI sidecars `_section_hints.json`, `_section_lyrics.json` written by `PythonAnalysisService.cs:83,91`. | `Song`, `Section`, `TrackInfo`, `LightingPreset`, `MidiCue`, `ChordChartData` (`Core\Models\Song.cs`, `ChordChartData.cs`); lib DTO `SongLibraryItem` (`Core\Models\Cloud\SongLibraryItem.cs`) | `SongManager`: `LoadAllSongsAsync`, `SaveSongAsync(Song)`, `RegisterSongPathAsync`, `RestoreBackupAsync`, `GetAvailableBackups`, `CreateSongFromFolder`, `LoadAllSetlistsAsync`, `SaveSetlistAsync`, `RemoveSong`. `SongLibraryService`: `GetAllAsync/SearchAsync/GetByIdAsync/FilterByKeyAsync/FilterByTagAsync/UpdateMetadataAsync/UpdateSlidesAsync/ImportChordProAsync/DeleteAsync` (`ISongLibraryService`) | `SongManager`: `MainWindow.xaml.cs:130` (paths from `settings.json`). `SongLibraryService`: `PresenterIntegration.cs:106` | `SongManager` ~95; `SongLibraryService` ~40 | `Song.FolderPath` is `[JsonIgnore]`, set at runtime from the dir. Additive fields: `Song.Ccli`, `Section.BackgroundImage` (nullable), `Section.TemplateId` (nullable). `WhenWritingNull` on serialize keeps old files loadable. `song.json.N` backup rotation on each save (`BackupSongJson`). |
| 2 | Service orders | `ServiceOrderManager` (`Core\Services\ServiceOrderManager.cs`) | `<AppFolder>/ServiceOrders/*.json`, one file per order; filename `"{Date}_{SanitizeFileName(Name)}.json"` | `ServiceOrder`, `ServiceItem`, `ServiceGroup`, `ServiceItemSettings`, `Slide` (`Core\Models\ServiceOrder.cs`, `ServiceGroup.cs`, `ServiceItemSettings.cs`, `Slide.cs`); scan DTO `ServiceFileInfo` | `LoadAsync(path)`, `SaveAsync(path?)`, `LoadAllAsync`, `ListServiceFilesAsync`, `GetFilePathFor`, `CreateNew`, `CreateFromSetlist`, `AddItem/RemoveItem/MoveItem`, group ops (`CreateGroup/RenameGroup/DeleteGroup/AssignItemToGroup/SetGroupCollapsed/GetUngroupedItemIds`), nav (`GoToItem/NextItem/PreviousItem/GetSongItems/GetNextSongItem`). Private: `GetDefaultFilePath`, `SanitizeFileName`. Events: `ItemChanged`, `ServiceOrderLoaded` | `PresenterIntegration.cs:79` (`Path.Combine(appFolder,"ServiceOrders")`, dir auto-created lines 76-78) | ~141 (widest) | `JsonIgnoreCondition.WhenWritingNull` + `JsonStringEnumConverter`. `ServiceOrder.StartTime` is nullable additive (date-only files load with no time). `Groups` reference item ids (single source of truth), not embedded items. Enum fields serialized as strings. |
| 3 | Templates | `TemplateService` (`Presenter\Services\TemplateService.cs`, `ITemplateService` in Core) | `<AppFolder>/Templates/*.json`, one file per custom template; filename = sanitized lowercase name. **3 built-ins are code-seeded, never written to disk** (`GetBuiltInTemplates`) | `SlideTemplate` (`Core\Models\SlideTemplate.cs`) | `GetAllAsync/GetByIdAsync/GetByCategoryAsync/CreateAsync/UpdateAsync/DeleteAsync/DuplicateAsync/GetBuiltInTemplatesAsync/ApplyToItemAsync`; static `GetBuiltInTemplates()` | `PresenterIntegration.cs:122` (`Path.Combine(appFolder,"Templates")`) | ~41 | Built-in ids are const strings (`builtin-default-centered`, `builtin-lower-third`, `builtin-trendy-line`); `IsBuiltIn` re-forced false on load and preserved on update; built-ins blocked from delete. Lives in **Presenter** project, not Core. `JsonStringEnumConverter`. |
| 4 | Media library | `MediaLibraryService` (`Core\Services\MediaLibraryService.cs`); `MediaLibraryFilter` (`Core\Services\MediaLibraryFilter.cs`) | `<AppFolder>/Media/` — copied asset files + a JSON **sidecar per asset**: `<assetpath>_metadata.json`. In-memory `_items` list cached after first `EnsureLoadedAsync` | `MediaLibraryItem` + `MediaLibraryType` (`Core\Models\MediaLibraryItem.cs`) | `ImportAsync(src,tags?)`, `GetAllAsync`, `SearchAsync`, `DeleteAsync(id,deleteFile)`, `GetByTagAsync`, `GetByTypeAsync`, `UpdateTagsAsync`, `GetLibraryPath` (`IMediaLibraryService`) | `PresenterIntegration.cs:109` (`Path.Combine(appFolder,"Media")`) | ~44 | Path-traversal validation (`ValidatePath`/`ValidateDestinationPath`). Import copies with `FileMode.CreateNew` + unique-name counter. `_loaded` cache means external file changes aren't seen until restart. `JsonStringEnumConverter`, `WriteIndented`. |
| 5 | Song usage / CCLI | `SongUsageService` (`Core\Services\SongUsageService.cs`); `SongUsageMapper` (`Core\Services\SongUsageMapper.cs`); `CcliReportService` (`Core\Services\CcliReportService.cs`) | `<AppFolder>/song-usage.json` — single JSON array of all records; full-file rewrite on every log | `SongUsageRecord` (`Core\Models\SongUsageRecord.cs`); report DTO `CcliReportEntry` | `SongUsageService`: `LogUsageAsync(record)`, `LogUsageAsync(title,id,service,date)`, `GetByDateRangeAsync`, `GetAllAsync`, `ExportToCsvAsync`. `CcliReportService`: `GetReportAsync`, `ExportCsvAsync` (3- and 4-arg, 4-arg prefixes CCLI license #). `SongUsageMapper.FromSong` | `SongUsageService`: `PresenterIntegration.cs:105`. `CcliReportService`: not constructed in `PresenterIntegration` (constructed at report call sites / tests) | `SongUsageService` ~30 | `DatePlayed` stamped at `UtcNow` on log; empty `Id` → GUID assigned. Append = load-all + add + rewrite whole file (`SaveAsync`). Additive `Artist`/`CcliNumber`/`OrganizationId` fields on the record. |
| 6 | Settings (app) | `SettingsService` (`Core\Services\SettingsService.cs`) | `<AppFolder>/settings.json` (also committed at **repo root**). Single JSON object | `AppSettings` (`Core\Models\AppSettings.cs`) | `LoadAsync`, `SaveAsync`, `Settings` property | `MainWindow.xaml.cs:57` (`Path.Combine(appFolder,"settings.json")`) | ~9 | Every field has explicit `[JsonPropertyName]` camelCase; enums (`OutputMode`,`JumpTiming`) use `JsonStringEnumConverter`. Holds `songsFolder`/`setlistsFolder` that drive domain #1's paths. Missing-file path writes defaults. |
| 6b | Settings (presenter) | **No dedicated service.** Two ad-hoc writers: `PresenterIntegration.SavePresenterSettings` (`PresenterIntegration.cs:414-422`) and `MainWindow.LoadPresenterSettings/SavePresenterSettings` (`MainWindow.xaml.cs:1505-1536`) | `<AppFolder>/presenter-settings.json`, single JSON object | `PresenterSettings` (`Core\Models\PresenterSettings.cs`) | Read: `MainWindow.LoadPresenterSettings()`. Write: both writers (`JsonSerializer.Serialize` + `File.WriteAllText`). No load in `PresenterIntegration` — it receives `PresenterSettings` injected via ctor | injected into `PresenterIntegration` ctor (param `presenterSettings`); file path built inline in both files | 2 writers | **Two independent writers to the same file**, different `JsonSerializerOptions` (read uses `PropertyNameCaseInsensitive`, writes use `WriteIndented`). Additive E5/J4 fields: `ScriptureShowReference`, `ScriptureShowTranslationName`, `CcliLicenseNumber` — all default-on-missing. Contains NDI/stage-server/remote/shortcut config too. |
| 7 | Bible (**already SQLite** — reference pattern) | `BibleDatabaseService` + `BibleSeedData` (`Core\Services\Bible\`) | `<AppFolder>/bible.db` SQLite. Tables `Translations`, `Books`, `Verses` (+ indexes `idx_verses_lookup`, `idx_verses_text`) | `BibleTranslation`, `BibleBook`, `BibleVerse`, `ScriptureReference` (`Core\Models\Bible\`) | `InitializeAsync`, `SeedKjvVersesAsync`, `SearchAsync`, `GetVerseAsync`, `GetVersesAsync(ref)`, `GetBooksAsync`, `GetTranslationsAsync`, `ParseReferenceAsync`, `ImportTranslationAsync(xml)` (`IBibleProvider`) | `PresenterIntegration.cs:119-121` (`Path.Combine(appFolder,"bible.db")`), seeded async in `InitializeBibleDatabaseAsync` | ~12 | Conn string `Data Source={path}`. New `SqliteConnection` per method (no shared/pooled connection kept). Schema via `CREATE TABLE IF NOT EXISTS`; seeds `INSERT OR IGNORE`. Only KJV/WEB translations + 4 chapters seeded (Gen1/Ps23/John3/Rom8). Tests call `SqliteConnection.ClearAllPools()` to release Windows file locks (`BibleDatabaseServiceTests.cs:28`). |
| 8 | Alerts | `AlertService` (`Presenter\Services\AlertService.cs`, `IAlertService`) | `<AppFolder>/alert-templates.json`, single JSON array | `AlertTemplate` (`Core\Models\AlertTemplate.cs`) | `ShowAlertAsync/DismissAlertAsync/SaveTemplateAsync/GetTemplatesAsync/DeleteTemplateAsync`; events `AlertShown/AlertDismissed` | `PresenterIntegration.cs:116` (`Path.Combine(appFolder,"alert-templates.json")`) | ~27 | `_loaded` cache; full-array rewrite on save; `JsonStringEnumConverter`. Display state (`CurrentAlert`,`IsAlertActive`) is in-memory only, not persisted. |
| 9 | Setlists (legacy) | `SongManager` (same service as #1) | `<SetlistsFolder>/*.json`, one file per setlist; filename = sanitized name | `Setlist` (`Core\Models\Setlist.cs`) | `LoadAllSetlistsAsync`, `SaveSetlistAsync`; migration helper `ServiceOrderManager.CreateFromSetlist` | `MainWindow.xaml.cs:130` (part of `SongManager`) | counted within #1 | Legacy format superseded by ServiceOrder; a migration path already exists (`CreateFromSetlist`). Folder empty on disk. |
| 10 | AI training data | `TrainingDataService` (`Core\Services\TrainingDataService.cs`) | User-chosen folder (default `E:\temp\OWClogs`, from `AppSettings.TrainingDataFolder`); one `*.json` per `TrainingRecord` | `TrainingRecord` | `SaveAsync(record)`, load-all (`Directory.GetFiles(..,"*.json")`) | `MainWindow.xaml.cs` (constructed from settings folder) | few | Out of band from the worship data model; a candidate to **leave as-is** (it is log/telemetry, not core user data, and lives outside the app folder). |

\* Call-site counts are rough `Select-String` hits on the type name across non-`bin/obj/.worktrees`
`.cs` files (includes declarations, usings-adjacent refs, and tests), so they over-count slightly;
use them for relative weighting, not exact totals.

### Not persistence (confirmed in-memory only, no disk store)
`CountdownService`, `DisplayManager`, `ThemeManager`, `StageDisplayServer`, `GlobalSearchService`,
`ExportService` (writes user-chosen export files only, not a managed store), `MidiPresenterSync`,
`PresenterEngine`, `NdiOutputService`/`StubNdiOutput`, `VideoBackgroundService`/`VideoPlaybackService`,
`LightingService`, MIDI command mappings (`MidiCommandMapping` model exists but is carried inside
`AppSettings`/`PresenterSettings` fields, not a separate file), `ScreenConfig`/`StageDisplayLayout`
models (present but not independently serialized to their own files at HEAD).

---

## 3. Actual data files on disk now

Directory listings (`Songs/`, `ServiceOrders/`, `Setlists/`) and a repo-wide scan show:

- `Songs/` — **empty** (no `<name>/song.json`, no `_song_registry.json`).
- `ServiceOrders/` — **empty**.
- `Setlists/` — **empty**.
- `settings.json` — **present at repo root** (AppSettings). Contents include
  `"songsFolder":"Songs"`, `"setlistsFolder":"Setlists"`, audio/MIDI/OSC config, and
  `"pythonPath":"C:\\AI\\Python3-13-13\\python.exe"`.
- No `song-usage.json`, `alert-templates.json`, `presenter-settings.json`, `Templates/`, `Media/`,
  or `bible.db` tracked in the repo (all created at runtime under the build output folder).

**Implication for migration:** there is effectively **no committed production corpus to import**.
Real data lives only in each developer/user's `bin/.../` output folder (and, for Songs/Setlists,
wherever the user pointed `settings.json`). The importer must therefore be written defensively
(scan the configured folders, tolerate absence) and can be verified against the **test fixtures**,
which already build each store under `Path.GetTempPath()` (see §5).

---

## 4. Dependency / risk assessment and phased ordering

### Entanglement & API-surface ranking (highest first)
1. **Service orders** — ~141 refs, the widest API (load/save/scan + nav + group ops + setlist
   migration) and the hub the `PresenterEngine`/operator UI drive off. Highest blast radius.
2. **Songs** — `SongManager` ~95 + `SongLibraryService` ~40. Entangled with the filesystem in ways
   no other domain is: per-folder layout, audio stems living beside `song.json`, backup rotation,
   and the out-of-tree registry. The hardest *structural* conversion.
3. **Media** — ~44, plus sidecar-file semantics and path-traversal guards to preserve.
4. **Templates** (~41) and **Alerts** (~27) — self-contained CRUD-over-a-folder/array; small models;
   low coupling.
5. **Song usage** (~30) — append-only log, trivial relational fit, feeds CCLI reporting.
6. **Settings** — small by count (~9) but split across two files/two models with **two writers** for
   presenter settings; the risk is correctness/consolidation, not volume.
7. **Bible** — already SQLite; the template, not a migration target.

### Suggested phased ordering (Bible is the pattern to copy)
A safe sequence moves low-coupling, append/CRUD stores first to prove the SQLite plumbing, then the
structurally hard ones last:

1. **Phase 0 — infrastructure:** a shared SQLite connection/db-path helper mirroring
   `BibleDatabaseService` (conn string `Data Source=`, `CREATE TABLE IF NOT EXISTS`, pool-clearing in
   tests). Decide one `app.db` vs per-domain `.db` files (not designed here).
2. **Phase 1 — Song usage** (append-only, smallest model, isolated writer) — proves schema + repo +
   import + test migration with the least coupling.
3. **Phase 2 — Alerts**, then **Templates** (both CRUD-over-folder, keep built-ins code-seeded).
4. **Phase 3 — Media** (relational rows + keep files on disk; drop the `_metadata.json` sidecars in
   favor of a `MediaItems` table).
5. **Phase 4 — Settings** (consolidate AppSettings + PresenterSettings; collapse the two presenter
   writers into one service first, then back it with a key/value or single-row table).
6. **Phase 5 — Service orders** (relational `ServiceOrder`/`ServiceItem`/`ServiceGroup`/`Slide`;
   preserve `ItemChanged`/`ServiceOrderLoaded` events and `GetFilePathFor`-style naming for export).
7. **Phase 6 — Songs/Setlists** (last; biggest structural change — see §5). Keep audio stems on disk;
   relationalize `song.json` metadata/sections/tracks/cues.

Each phase can keep tests green by swapping the store construction behind the existing public API and
pointing it at a temp `.db` the way the tests already point at temp folders/files.

---

## 5. Explicit "this will be hard" callouts

1. **Per-song folder layout vs relational rows (hardest).** `Songs/<name>/song.json` co-locates
   metadata, `sections[]`, `tracks[]`, `midiCues[]`, `lightingPresets[]`, and `chordChart` with the
   actual audio stem files in the same folder (`SongManager.CreateSongFromFolder` auto-detects
   `.mp3/.wav/...` siblings). The identity key is `Song.FolderPath` (`[JsonIgnore]`, derived from the
   dir), used all over via `SongPathResolver` normalization. A relational move must (a) keep stems on
   disk, (b) replace folder-path identity with a stable row id without breaking the ~135 Song/Lib
   references, and (c) reproduce `UpdateSlidesAsync`'s index-matched partial-field update semantics.
2. **The `song.json.N` backup rotation + `_song_registry.json`.** `BackupSongJson` keeps 5 rotating
   copies on every save; `_song_registry.json` tracks out-of-base-path songs. SQLite has no natural
   equivalent — the migration must either drop these (behavior change — would need user sign-off) or
   re-implement (row versioning table + a "known external songs" table).
3. **Media sidecar files.** Each asset has a sibling `<file>_metadata.json`; `EnsureLoadedAsync`
   globs `*_metadata.json`. Moving to a table means deciding migration/cleanup of existing sidecars
   and preserving the path-traversal validation and unique-name copy logic.
4. **Settings split + double writer.** `AppSettings` (`settings.json`) and `PresenterSettings`
   (`presenter-settings.json`) are separate models/files, and `presenter-settings.json` is written by
   **two** code paths with different serializer options (`PresenterIntegration.cs:414-422` and
   `MainWindow.xaml.cs:1522-1536`). Consolidate the writers before/with the DB move or risk divergent
   writes. Also note `settings.json` lives at the **repo root** (committed) as well as the runtime app
   folder — pick the authoritative location.
5. **Concurrent / full-file-rewrite save patterns.** `SongUsageService` and `AlertService` load the
   whole array, mutate, and rewrite the entire file on every change — no locking. Under SQLite these
   become single-row inserts/updates, which is strictly better, but any code assuming "save = whole
   file" must be reviewed. `ServiceOrderManager.SaveAsync` similarly rewrites a whole order file.
6. **Windows file-lock / connection-pooling.** The Bible tests already call
   `SqliteConnection.ClearAllPools()` (`BibleDatabaseServiceTests.cs:28`) to release the `.db` so
   temp files can be deleted on Windows. Every new SQLite store needs the same discipline in tests
   (temp `.db` + pool clear in teardown) or test cleanup will fail intermittently on Windows.
7. **Path source asymmetry.** Most stores anchor to `AppDomain.CurrentDomain.BaseDirectory`, but
   Songs/Setlists anchor to user-chosen paths in `settings.json` (which may be absolute and off-drive).
   A single `app.db` under the app folder would relocate song *metadata* away from the song *files* —
   a deliberate design choice to make (not decided here).
8. **Enum + additive-field compatibility.** All JSON uses `JsonStringEnumConverter` and
   `WhenWritingNull`; nullable additive fields (`Section.BackgroundImage/TemplateId`,
   `ServiceOrder.StartTime`, presenter scripture/CCLI toggles) default-on-missing today. The SQLite
   schema must store enums as their string names (to keep CSV/export parity) and allow NULLs so an
   importer can round-trip old files without data loss.

---

## 6. Test surface (keep green / adapt during migration)

All persistence tests already construct services against throwaway paths under
`Path.GetTempPath()`, so each phase can point the new SQLite store at a temp `.db` the same way:

- Songs / library: `SongLibraryServiceTests.cs`, `SongPathResolverTests.cs`, `ChordProImportTests.cs`
  (all `new SongManager(tempSongs, tempSetlists)` / `new SongUsageService(tempUsage)`).
- Service orders: `ServiceOrderPersistenceTests.cs`, `ServiceOrderManagerGroupTests.cs`,
  `ServiceListFilesTests.cs`, `ServiceDateNameTests.cs`, `DefensiveLoadTests.cs`,
  `AutoAdvanceSettingsPersistenceTests.cs`, and the `PresenterEngine*Tests` (construct
  `new ServiceOrderManager(tempDir)`).
- Templates: `TemplateServiceTests.cs`. Alerts: (covered via presenter tests). Media:
  `MediaLibraryServiceTests.cs`, `MediaLibraryFilterTests.cs`.
- Song usage / CCLI: `SongUsageServiceTests.cs`, `SongUsageMappingTests.cs`, `CcliReportServiceTests.cs`.
- Bible (reference): `BibleDatabaseServiceTests.cs` — the model for temp-`.db` + `ClearAllPools()` teardown.
- Settings persistence: `AutoAdvanceSettingsPersistenceTests.cs`, `PresenterSettingsStageServerTests.cs`.

Because every test already injects a temp path, the lowest-risk migration keeps each service's public
API identical and swaps only the backing store — the existing tests then validate behavior parity, and
only the setup line (temp folder → temp `.db`) changes per adapted test.

---

## 7. Conclusions / recommendations (map only — no schema proposed)

- Adopt `BibleDatabaseService`/`BibleSeedData` verbatim as the structural template (conn string,
  `CREATE TABLE IF NOT EXISTS`, `INSERT OR IGNORE` seeds, per-call connection, `ClearAllPools` in tests).
- Migrate in the §4 order (usage → alerts/templates → media → settings → service orders → songs) so
  the plumbing is proven on low-coupling stores before touching the ~141-ref ServiceOrder hub and the
  per-folder Song layout.
- Before touching settings, **consolidate the two `presenter-settings.json` writers** into one service.
- Keep binary/large files (audio stems, media assets, `bible.db` verse text) **on disk**; relationalize
  only the metadata/index that currently lives in JSON.
- Preserve all public method signatures and the `ItemChanged`/`ServiceOrderLoaded`/`AlertShown` events
  so UI/engine code is untouched; this also keeps the temp-path test suite green with minimal edits.
- Treat `TrainingDataService` as out of scope (telemetry/log, user-folder, not core worship data).
- Decide (not here): one `app.db` vs per-domain `.db`; where the DB lives relative to user-chosen
  Songs folders; and whether to retain song backup rotation / external-song registry semantics.
