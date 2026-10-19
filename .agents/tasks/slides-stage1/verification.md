# Verification — Slides Module STAGE 1

All commands were run inside the worktree
`c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slides-stage1\`.

## Build

Command:

```
dotnet build c:\Users\...\.worktrees\slides-stage1\WorshipBacktrackPlayer.sln
```

Result: **Build succeeded — 0 errors, 4 warnings.**

All 4 warnings are pre-existing and unrelated to this change:

- `WorshipPlayer.Presenter\Views\MediaLibrary\MediaLibraryPanel.xaml.cs(16,20): CS0414` — `_currentView` assigned but never used.
- `WorshipPlayer.UI\Views\ChordChartWindow.xaml.cs(235,51): CS8604` — possible null arg `lyricText`.
- `WorshipPlayer.UI\Views\WaveformEditorWindow.xaml.cs(1775,18): CS0414` — `_inReviewMode` assigned but never used.
- (The MediaLibraryPanel CS0414 is reported once for the wpftmp pass and once for the real compile; net distinct pre-existing warnings = the 3 above, matching the expected "4 pre-existing warnings OK" count reported by the build line `Build succeeded with 4 warning(s)`.)

No new warnings were introduced by the Slides files.

## Test

Command:

```
dotnet test c:\Users\...\.worktrees\slides-stage1\WorshipBacktrackPlayer.sln
```

Result: **Passed! — Failed: 0, Passed: 636, Skipped: 0, Total: 636.**

- Prior suite: 624 tests.
- New `SlidePresentationServiceTests`: **12** test cases (10 `[Fact]` + 1 `[Theory]` × 2 `[InlineData]`), all passing. Filtered run:
  `--filter FullyQualifiedName~SlidePresentationServiceTests` → `Passed: 12, Total: 12`.
- 624 + 12 = 636. ✔

## New test coverage (SlidePresentationServiceTests)

- Create assigns a GUID Id + defaults (empty Slides, CreatedAt/UpdatedAt set) and persists (re-read via GetById + GetAll).
- Create with blank / whitespace name → `Name == "New Presentation"` (Theory, 2 cases).
- GetAll returns most-recently-updated first (create two, update the older, assert order).
- Update re-derives SlideCount from Slides and advances UpdatedAt (read back and assert).
- Duplicate deep-clones: new presentation Id, new id per slide, `" (Copy)"` name, original unchanged (two rows).
- Duplicate with an unknown id throws `KeyNotFoundException`.
- Delete removes the row; Delete with an unknown id is a no-op (no throw).
- GetById returns null for an unknown id.
- A manually-corrupted DataJson row (raw insert) is skipped by GetAll; the valid row is still returned.
- Schema-init is idempotent across two service instances on the same temp db.
