# Implementation Plan — SQL Migration Phase 4: Settings (AppSettings + PresenterSettings) → app.db

Scope: consolidate the two PresenterSettings writers into ONE service, then back BOTH
settings stores (AppSettings via `SettingsService`, PresenterSettings via the new
`PresenterSettingsService`) with the shared SQLite `app.db`, reusing the Phase-0 `AppDatabase`
helper. Do NOT touch service-orders or songs (separate future phases).

Everything below is cited against the worktree
`c:\Users\Quang\...\.worktrees\sql-phase04`. Baseline (`dotnet build WorshipBacktrackPlayer.sln`)
builds clean at planning time: 0 errors, 4 pre-existing warnings.

---

## Verified current state (file:line, worktree)

- **AppSettings store** — `WorshipPlayer.Core\Services\SettingsService.cs:16` ctor takes
  `settingsPath`; `LoadAsync` (`:31`) reads/deserializes, falls back to `new AppSettings()` on
  missing/malformed; `SaveAsync` (`:56`) serializes with
  `WriteIndented = true` + `JsonStringEnumConverter` (`:21-25`). `Settings` property (`:11`).
  Model `WorshipPlayer.Core\Models\AppSettings.cs` — camelCase `[JsonPropertyName]` on every
  field; enums `OutputMode`/`JumpTiming` carry `[JsonConverter(typeof(JsonStringEnumConverter))]`
  on the enum type itself (`AppSettings.cs:148,159`), so enums round-trip as strings even without
  a converter in options. Holds `SongsFolder`/`SetlistsFolder` (`AppSettings.cs:33,36`) — these
  stay folder-path VALUES, only the containing settings object moves to SQLite.
- **Construction** — `WorshipPlayer.UI\Views\MainWindow.xaml.cs:55-60`:
  `settingsPath = Path.Combine(appFolder, "settings.json")`; `new SettingsService(settingsPath)`;
  `await _settingsService.LoadAsync()`; `_settings = _settingsService.Settings`. `SaveAsync` is
  called at `:72` and `:111` (folder-prompt flows).
- **PresenterSettings store** — NO dedicated service today. Model
  `WorshipPlayer.Core\Models\PresenterSettings.cs` — all fields are int/bool/string/double (NO
  enum fields); additive E5/J4 fields present and default-on-missing: `ScriptureShowReference`
  (`:118`, default true), `ScriptureShowTranslationName` (`:127`, default true),
  `CcliLicenseNumber` (`:135`, default ""); plus NDI (`:196-221`), stage-server
  (`:72-78`), remote (`:160-189`), shortcut (`:140-157`) config.
- **TWO writers to `presenter-settings.json` with DIFFERENT options (the known trap):**
  1. `WorshipPlayer.UI\Services\PresenterIntegration.cs:420-438` `SavePresenterSettings(settings)`
     — serializes with `WriteIndented = true` only (NO enum converter, NO case-insensitive), then
     calls `ApplyDisplaySettings(settings)` (`:436`). This is the LIVE writer: wired as the save
     callback passed to `OperatorWindow.Initialize(...)` at `PresenterIntegration.cs:252`.
  2. `WorshipPlayer.UI\Views\MainWindow.xaml.cs:1529-1535` static `SavePresenterSettings` —
     serializes with `WriteIndented = true` only. VERIFIED UNUSED: a worktree-wide grep for
     `SavePresenterSettings` finds only its definition at `MainWindow.xaml.cs:1529` with NO call
     site (the only live writer is PresenterIntegration's). It is dead code.
- **The reader** — `MainWindow.xaml.cs:1512-1527` static `LoadPresenterSettings()` reads
  `presenter-settings.json` with `PropertyNameCaseInsensitive = true` (NO enum converter), returns
  `new PresenterSettings()` on missing/malformed. Called once at `MainWindow.xaml.cs:175`:
  `var presenterSettings = LoadPresenterSettings();` then injected into `PresenterIntegration`
  ctor (`:176`). `GetPresenterSettingsPath()` at `:1506-1510`.
- **PresenterIntegration** receives `PresenterSettings` by ctor param (`PresenterIntegration.cs:72`,
  stored `_presenterSettings` field `:30`); it does NOT read the file itself.

### Serializer-options reconciliation (the trap, resolved)
The three code paths disagree only on flags that are observationally irrelevant for THIS model:
- No `PresenterSettings` field is an enum, so the absence of `JsonStringEnumConverter` in the two
  writers changes nothing on disk today.
- The reader's `PropertyNameCaseInsensitive = true` matters only if a key's casing differed from
  the camelCase `[JsonPropertyName]`; the writers emit exactly those camelCase names, so round-trip
  already matches.
**Decision:** the single canonical `JsonSerializerOptions` for `PresenterSettingsService` will be
`{ WriteIndented = true, Converters = { new JsonStringEnumConverter() }, PropertyNameCaseInsensitive = true }`
— identical in shape to `SettingsService`'s options (`SettingsService.cs:21-25`) plus
`PropertyNameCaseInsensitive = true` to preserve the reader's leniency for any hand-edited legacy
file. Rationale: this is a strict superset of all three existing behaviors (keeps indented output,
keeps case-insensitive reads, and adds enums-as-strings parity for future-proofing per the phase
contract), so no existing on-disk file becomes unreadable and the double-writer hazard collapses to
one well-defined path. Enums-as-strings also matches the established Phase-1/2/3 service convention
(`SongUsageService.cs:34-38`, `MediaLibraryService.cs:40-44`).

### Backing-store shape (decision, documented)
Each settings object is ONE logical record. Per the phase contract, store it as a single-row table
with the whole object serialized to a JSON text column — do NOT promote individual columns:
```
CREATE TABLE IF NOT EXISTS AppSettings       (Id INTEGER PRIMARY KEY CHECK (Id = 1), DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS PresenterSettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), DataJson TEXT NOT NULL);
```
Rationale: settings are read/written as a whole object through `Settings`/`LoadAsync`/`SaveAsync`;
there are no per-field queries, so column promotion adds schema churn with zero query benefit.
Enums-as-strings parity lives INSIDE the serialized JSON (enum types already carry the converter,
and the canonical options add it), so values round-trip. Both tables live in the shared `app.db`;
`CREATE TABLE IF NOT EXISTS` makes schema-init order-independent across stores
(`AppDatabase.cs` remarks). `bible.db` stays separate.

### Legacy JSON disposition (decision, documented)
Mirror Phase 1-3: defensive, non-duplicating, one-time import. On first use, if the legacy JSON
(`settings.json` / `presenter-settings.json`) sits beside `app.db` AND the store's table row is
absent, import it once into row `Id=1` (tolerate missing/malformed → fall back to defaults; NEVER
throw — matches `SongUsageService.ImportLegacyJsonIfNeededAsync` `SongUsageService.cs:210-262`).
After migration, STOP writing the JSON files (saves go to SQLite only) and LEAVE existing JSON files
on disk untouched (least-destructive; a user can delete them). The runtime store is the app-folder
path (`AppDomain.CurrentDomain.BaseDirectory`); the committed repo-root `settings.json` is NOT data
and is never touched by the services (services only know the path the caller passes).

---

## Plan

- [ ] 1. Introduce `PresenterSettingsService` in Core (file-backed first — no SQLite yet) that owns
      load+save of `PresenterSettings`, mirroring `SettingsService`. Public API:
      ctor `PresenterSettingsService(string settingsPath)`, `Settings` get-property,
      `Task LoadAsync()`, `Task SaveAsync()`. Use the canonical `JsonSerializerOptions` decided
      above. `LoadAsync`: read+deserialize if file exists, fall back to `new PresenterSettings()` on
      missing/malformed (match `MainWindow.LoadPresenterSettings` leniency); do NOT auto-write on
      missing (presenter settings had no auto-create, unlike `SettingsService.LoadAsync`). `SaveAsync`:
      create parent dir, serialize, write file.
      Files: `WorshipPlayer.Core\Services\PresenterSettingsService.cs` (new).
      Verify: `dotnet build WorshipBacktrackPlayer.sln` succeeds (0 errors).

- [ ] 2. Route BOTH presenter-settings writers and the reader through the one service.
      In `MainWindow.xaml.cs`: construct a `PresenterSettingsService` against
      `Path.Combine(appFolder,"presenter-settings.json")` where `LoadPresenterSettings()` is called
      (`:175`), `await LoadAsync()`, pass `.Settings` into `PresenterIntegration` (`:176`), and hold
      the service in a field so later saves reuse it. Delete the dead static `SavePresenterSettings`
      (`:1529-1535`) and the now-unused `LoadPresenterSettings`/`GetPresenterSettingsPath`
      (`:1506-1527`) OR reduce them to thin delegations to the service — prefer DELETION since the
      static writer is verified unused and the loader is replaced. In `PresenterIntegration.cs`:
      change the ctor to accept the `PresenterSettingsService` (or a save delegate backed by it) so
      its `SavePresenterSettings` (`:420-438`) delegates persistence to
      `PresenterSettingsService.SaveAsync()` while KEEPING the `ApplyDisplaySettings(settings)` call
      (`:436`) and the `OperatorWindow` save-callback wiring (`:252`). Simplest cohesive choice:
      inject `PresenterSettingsService` into `PresenterIntegration` ctor (alongside the existing
      `PresenterSettings` param, which stays the in-memory instance the engine mutates), and have the
      private `SavePresenterSettings` set `service.Settings`-equivalent state and call `SaveAsync()`.
      Files: `WorshipPlayer.UI\Views\MainWindow.xaml.cs`,
      `WorshipPlayer.UI\Services\PresenterIntegration.cs`.
      Verify: `dotnet build WorshipBacktrackPlayer.sln` succeeds; a new test (item 4) proves a single
      consistent save path.

- [ ] 3. Add unit tests for the consolidated file-backed `PresenterSettingsService` (parity + single
      path), following the temp-folder pattern. Cover: defaults when no file; round-trip save→reload
      preserving additive E5/J4 fields (`ScriptureShowReference`, `ScriptureShowTranslationName`,
      `CcliLicenseNumber`) and NDI/stage-server/remote/shortcut fields; malformed file → defaults, no
      throw; and a test asserting there is exactly ONE save path (construct via the service, save,
      reload via the service, values match — i.e. no divergent-writer drift).
      Files: `WorshipPlayer.Tests\PresenterSettingsServiceTests.cs` (new).
      Verify: `dotnet test WorshipBacktrackPlayer.sln --filter FullyQualifiedName~PresenterSettingsServiceTests`
      — all new tests pass.

- [ ] 4. Back `SettingsService` with `app.db`. Change ctor to
      `SettingsService(string? databasePath = null)` (default `AppDatabase.DefaultPath`), construct a
      `AppDatabase`, add `const string CreateSchemaSql` for the `AppSettings(Id=1, DataJson TEXT)`
      table, and an `EnsureInitializedAsync` guard (one-time schema-init + legacy import) exactly like
      `SongUsageService.cs:181-205`. `LoadAsync`: ensure-init, then
      `SELECT DataJson FROM AppSettings WHERE Id=1`; if present deserialize into `Settings`, else keep
      defaults. `SaveAsync`: `INSERT ... ON CONFLICT(Id) DO UPDATE` (or `INSERT OR REPLACE`) the single
      row with serialized `Settings`. Compute the legacy `settings.json` path as the sibling of the db
      file (`Path.Combine(dir,"settings.json")`), import once when the row is absent (tolerate
      missing/malformed, never throw). Preserve the `Settings` property and `LoadAsync`/`SaveAsync`
      signatures. Keep the auto-create-on-missing behavior by writing defaults to the row when neither
      row nor legacy file exists.
      Files: `WorshipPlayer.Core\Services\SettingsService.cs`.
      Verify: `dotnet build` succeeds; existing + new SettingsService tests pass (items 6-7).

- [ ] 5. Back `PresenterSettingsService` with `app.db` the same way: ctor
      `PresenterSettingsService(string? databasePath = null)` (default `AppDatabase.DefaultPath`),
      `PresenterSettings(Id=1, DataJson TEXT)` table, `EnsureInitializedAsync` schema-init + one-time
      import of sibling `presenter-settings.json` (tolerant, non-duplicating via empty-row guard),
      `LoadAsync` SELECT row / `SaveAsync` upsert row. Keep public API from item 1. Then update the
      construction sites from item 2 to pass the shared `appDbPath`
      (`Path.Combine(appFolder,"app.db")`, the same value `PresenterIntegration.cs:112` already uses
      for the sibling SQLite services) instead of the JSON path.
      Files: `WorshipPlayer.Core\Services\PresenterSettingsService.cs`,
      `WorshipPlayer.UI\Views\MainWindow.xaml.cs`,
      `WorshipPlayer.UI\Services\PresenterIntegration.cs`.
      Verify: `dotnet build WorshipBacktrackPlayer.sln` succeeds (0 errors).

- [ ] 6. Update `PresenterSettingsServiceTests` (item 3) to inject a temp `app.db` and clear SQLite
      pools in teardown (mirror `SongUsageServiceTests.cs:33-48` `SqliteConnection.ClearAllPools()`).
      Add import tests: write a legacy `presenter-settings.json` beside a fresh temp `app.db`, construct
      the service, assert it imported once (values present) and that a SECOND instance against the same
      db does NOT duplicate; assert additive E5/J4 fields survive the import; assert a malformed legacy
      file yields defaults without throwing.
      Files: `WorshipPlayer.Tests\PresenterSettingsServiceTests.cs`.
      Verify: `dotnet test WorshipBacktrackPlayer.sln --filter FullyQualifiedName~PresenterSettingsServiceTests`
      — all pass.

- [ ] 7. Add `SettingsService` SQLite tests (there is NO existing SettingsService test today — note:
      `AutoAdvanceSettingsPersistenceTests.cs` tests `ServiceOrderManager`, NOT SettingsService, and
      `PresenterSettingsStageServerTests.cs` tests only the model's serialization; neither constructs a
      settings store, so "existing settings tests" are really the model/round-trip tests, which keep
      passing unchanged). New tests inject a temp `app.db`, clear pools in teardown, and cover:
      defaults when no row/file; save→reload round-trip through a fresh instance on the same db;
      one-time non-duplicating import of a legacy `settings.json` (values incl. `SongsFolder`/
      `SetlistsFolder` survive); malformed legacy file → defaults, no throw; enum fields
      (`OutputMode`/`JumpTiming`) round-trip as strings.
      Files: `WorshipPlayer.Tests\SettingsServiceTests.cs` (new).
      Verify: `dotnet test WorshipBacktrackPlayer.sln --filter FullyQualifiedName~SettingsServiceTests`
      — all pass.

- [ ] 8. Full verification: build the whole solution and run the complete test suite to confirm
      parity (the model round-trip tests, all Phase-1/2/3 SQLite tests, and the new settings tests all
      pass) and that nothing in service-orders/songs was disturbed.
      Files: none (verification only).
      Verify: `dotnet build WorshipBacktrackPlayer.sln` → 0 errors; `dotnet test WorshipBacktrackPlayer.sln`
      → all tests pass.

---

## Out of scope (do NOT touch)
Service-orders (`ServiceOrderManager`), songs/setlists (`SongManager`, `SongLibraryService`),
`TrainingDataService`, media/templates/alerts/song-usage/bible stores (already migrated in Phases
1-3 or explicitly deferred). Do NOT change `AppSettings`/`PresenterSettings` model shapes beyond
additive needs; keep them JSON-round-trippable. Do NOT introduce a second db helper — reuse
`AppDatabase`. Do NOT touch the committed repo-root `settings.json` as data.
