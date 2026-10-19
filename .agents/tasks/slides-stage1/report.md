# Completion Report — Slides Module STAGE 1 (Slides Library)

Branch: `feature/slides-library` (committed inside the worktree; NOT merged/pushed).

## Summary

Added a new top-nav **Slides** tab and library to the .NET 8 WPF Presenter, backed by a new
`SlidePresentation` model and a SQLite-backed `SlidePresentationService` writing to a new
`SlidePresentations` table in the shared `app.db`. Everything is additive — no existing table,
data file, or feature was modified; `app.db` was only extended.

## Model / schema

- `WorshipPlayer.Core\Models\SlidePresentation.cs` — two JSON-serializable types in one file
  (`SlidePresentation` + `PresentationSlide`), explicit `[JsonPropertyName]` on every member.
  - `SlidePresentation`: `Id` (GUID default), `Name`, `CreatedAt`/`UpdatedAt` (UtcNow),
    `Slides` (`List<PresentationSlide>`), nullable `ThumbnailPath` (Stage-2 forward-compat).
  - `PresentationSlide`: `Id` (GUID default), `Order`, `Text`, nullable `BackgroundImage` /
    `BackgroundColor`.
  - A NEW type (not the runtime `Slide`) so library/editor concerns stay decoupled from
    presenter-runtime fields. Forward-compatible for Stage 2 (editor fills `Slides` + backgrounds)
    and Stage 3 (service item references the presentation by `Id`) with no model rewrite.
- Schema (one `CREATE TABLE IF NOT EXISTS SlidePresentations`): `Id TEXT PRIMARY KEY`,
  `Name TEXT NOT NULL DEFAULT ''`, `SlideCount INTEGER NOT NULL DEFAULT 0`, `CreatedAt TEXT NOT NULL`,
  `UpdatedAt TEXT NOT NULL`, `DataJson TEXT NOT NULL`; plus
  `CREATE INDEX IF NOT EXISTS idx_slidepresentations_updatedat`. Promoted columns back the listing;
  the whole object round-trips through `DataJson`. Microsoft.Data.Sqlite only, parameterized SQL,
  no EF, no second db helper.

## Service API

`WorshipPlayer.Core\Services\Interfaces\ISlidePresentationService.cs` +
`WorshipPlayer.Core\Services\SlidePresentationService.cs`
(ctor `SlidePresentationService(string? databasePath = null)` → `new AppDatabase(path ?? DefaultPath)`):

- `GetAllAsync()` — `SELECT DataJson … ORDER BY UpdatedAt DESC`, per-row try/catch skip of a
  malformed row.
- `GetByIdAsync(id)` — null if no row.
- `CreateAsync(name)` — GUID Id, trimmed name (blank/whitespace → "New Presentation"),
  `CreatedAt = UpdatedAt = UtcNow`, empty `Slides`; functional.
- `UpdateAsync(p)` — stamps `UpdatedAt = UtcNow`, re-derives `SlideCount`, upsert
  (`ON CONFLICT(Id) DO UPDATE`).
- `DeleteAsync(id)` — no-op if absent (no throw); functional.
- `DuplicateAsync(id)` — throws `KeyNotFoundException` if missing; else JSON deep-clone, new Id for
  the presentation AND each slide, `" (Copy)"` name, upsert; functional.

The service owns the `SlideCount` / `UpdatedAt` invariant (always re-derived/stamped before upsert).
Mirrors `MediaLibraryService` (DataJson + promoted columns + upsert) and `SongUsageService`
(ISO-8601-UTC `FormatDate`, ORDER BY date DESC, index, `_initialized` guard). No legacy-import
branch (Slides is brand new).

## Nav wiring

- `WorshipPlayer.UI\Services\PresenterIntegration.cs` — new
  `public ISlidePresentationService SlidePresentationService { get; }`, constructed after
  `TemplateService` with the shared `appDbPath`, and appended as the trailing arg to
  `_operatorWindow.SetServices(...)`.
- `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs` — added
  `using WorshipPlayer.Presenter.Views.Slides;`, fields `_slidePresentationService` +
  `_slidesLibraryPanel`, a trailing `SetServices` param, `TabSlides_Click → SwitchToTab("Slides")`,
  a `"Slides"` case (lazy-creates `SlidesLibraryPanel`, assigns the service, subscribes the two
  stub events, shows `SlidesPanel`, `SetTabActive(TabSlides)`), plus the `TabSlides`
  hide/reset-style lines. The sidebar-visible-when-service-loaded behavior is unchanged (every
  tab still calls `SetSidebarVisible(...)`).
- `WorshipPlayer.Presenter\Views\OperatorWindow.xaml` — `TabSlides` button inserted between
  `TabSongs` and `TabScripture` (order: Live / Services / Songs / Slides / Scripture / Media /
  Tools), inline-styled (NO StaticResource — OperatorWindow stays StaticResource-free); a
  `SlidesPanel` + `SlidesPanelContent` host beside `SongsPanel`/`MediaPanel`.

## Library UI

- `WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml(.cs)` — a `UserControl` that MERGES
  `OwcDesignSystem.xaml` (StaticResource allowed). Wide **Search Slides** box + blue **+ Add
  Slides** button (`OwcBlueButton`) on top, a simple A-Z/Date **Sort** control, an empty-state, and
  a `WrapPanel` `ItemsControl` card grid. Each card: a thumbnail-placeholder tile (dark box, muted
  monitor glyph), the Name, a sub-line `"N slides · Modified <relative time>"`
  (via `RelativeTimeFormatter`), and a ⋮ overflow button whose menu has **Add to current service /
  Edit / Duplicate / Delete**. `SlidePresentationListItem` display model defined in the `.cs`.
- `WorshipPlayer.Presenter\Views\Slides\SlideNameDialog.xaml(.cs)` — small modal name dialog
  (merges the theme): single `OwcTextBox` pre-filled "New Presentation" (select-all on load), OK
  (`OwcBlueButton`, disabled when blank) / Cancel; exposes `PresentationName`.

## Functional vs. stage-2/3 stub

| Capability | Status |
|---|---|
| Slides tab + library card grid | ✅ functional |
| Model + `SlidePresentations` table in shared `app.db` | ✅ functional |
| Service CRUD + Duplicate | ✅ functional |
| Search + A-Z/Date sort + empty state | ✅ functional |
| + Add Slides (name dialog → CreateAsync) | ✅ functional |
| ⋮ Duplicate / ⋮ Delete | ✅ functional |
| ⋮ Edit | ⛔ STAGE 2 stub — raises `EditPresentationRequested`; host shows `ShowComingSoon` info notice |
| ⋮ Add to current service | ⛔ STAGE 3 stub — raises `AddPresentationToServiceRequested`; host shows `ShowComingSoon` info notice |
| Real card thumbnails | ⛔ STAGE 2 (placeholder tile in Stage 1) |

## File:line trace

- Model: `WorshipPlayer.Core\Models\SlidePresentation.cs:16` (`SlidePresentation`), `:49` (`PresentationSlide`).
- Interface: `WorshipPlayer.Core\Services\Interfaces\ISlidePresentationService.cs:11`.
- Service: `WorshipPlayer.Core\Services\SlidePresentationService.cs:40` (schema), `:122` (`CreateAsync`), `:166` (`DuplicateAsync`), `:198` (`UpsertAsync`), `:247` (`FormatDate`).
- DI: `WorshipPlayer.UI\Services\PresenterIntegration.cs` — property (`SlidePresentationService`), ctor construction after `TemplateService`, and the `SetServices(...)` trailing arg.
- OperatorWindow code-behind: `using …Views.Slides`; `_slidePresentationService` / `_slidesLibraryPanel` fields; `SetServices` trailing param + assignment; `TabSlides_Click`; `SwitchToTab` hide/reset lines + `"Slides"` case; `ShowComingSoon` + `OnEditPresentationRequested` (STAGE 2 STUB) + `OnAddPresentationToServiceRequested` (STAGE 3 STUB) next to `ShowToolError`.
- OperatorWindow XAML: `TabSlides` button between `TabSongs` and `TabScripture`; `SlidesPanel` + `SlidesPanelContent` host.
- Panel: `WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml.cs` — `AddSlides_Click` (functional), `DuplicateMenu_Click` (functional), `DeleteMenu_Click` (functional), `EditMenu_Click` (stub event), `AddToServiceMenu_Click` (stub event), `SearchTextBox_TextChanged` / `SortAZ_Click` / `SortDate_Click`; `SlidePresentationListItem` at the bottom.
- Dialog: `WorshipPlayer.Presenter\Views\Slides\SlideNameDialog.xaml.cs`.
- Tests: `WorshipPlayer.Tests\SlidePresentationServiceTests.cs`.

## Build / test results

- `dotnet build …\WorshipBacktrackPlayer.sln` → **0 errors, 4 pre-existing warnings** (details in `verification.md`).
- `dotnet test …\WorshipBacktrackPlayer.sln` → **Passed: 636, Failed: 0, Skipped: 0** (624 prior + 12 new Slides cases).

## Click-test checklist (manual, WPF)

1. Open the Presenter operator window → a **Slides** tab appears between **Songs** and **Scripture**.
2. Click **Slides** → the library panel renders (empty-state message when there are none).
3. **+ Add Slides** → name dialog (pre-filled "New Presentation", OK disabled when blank) → OK →
   a card appears with "0 slides · Modified just now".
4. Type in **Search Slides** → the card grid filters by name; clearing it shows all.
5. **Sort** A-Z / Date reorders the displayed cards.
6. ⋮ → **Duplicate** → a "… (Copy)" card appears.
7. ⋮ → **Delete** → confirm Yes → the card disappears.
8. ⋮ → **Edit** → an informational "coming in a later update" notice (not an error dialog).
9. ⋮ → **Add to current service** → an informational "coming in a later update" notice.
10. Songs / Services / Media / Scripture / Live tabs, the left sidebar, and the Tools menu still work.
