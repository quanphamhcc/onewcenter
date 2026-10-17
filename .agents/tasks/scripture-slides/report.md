# Completion Report — Scripture Slides (E3 auto-split + E5 display options)

Branch: `feature/scripture-slides`
Worktree: `.worktrees/scripture-slides`
Iteration: first (no `review.json` present)

## Summary

Implemented E3 (scripture auto-split into slides via the Core generator) and E5
(show/hide Reference and show/hide Translation-name toggles) in the reachable
`ScriptureBrowserPanel`. The panel now delegates ALL slide creation to the single Core
splitter (`ScriptureSlideGenerator`) and sends the generated slides to the live output
through the engine. Two additive, backward-compatible toggle defaults persist via
`PresenterSettings`.

## False premises found (stated per instructions)

1. **"The panel splits internally" — FALSE.** `ScriptureBrowserPanel.PerformLookup()`
   did NOT split or call any generator. It joined verses into one flat string shown in
   `ResultsText`. There was no ad-hoc splitter to remove in the reachable panel; there
   was no generator call at all. (The ad-hoc splitter lives only in the orphaned
   `ScriptureSlidePreview.xaml.cs`, which is NOT reachable from `OperatorWindow` and was
   left untouched.)

2. **"The output path the panel already uses" — DID NOT EXIST.** The old
   `AddToServiceRequested` event was declared and invoked but **never subscribed**
   anywhere in the solution. Scripture reached neither a service nor the live output.
   This task therefore ADDED the minimal output path, mirroring the Songs-tab standalone
   pattern (`OnSongSelected` → `PresenterEngine.LoadStandaloneSong` → `SlideChanged` →
   `OutputWindow.ShowSlide`). The new path is `PresentSlidesRequested` →
   `PresenterEngine.LoadScriptureSlides` → `SlideChanged` → `OutputWindow.ShowSlide`.

3. **AC3 persistence assumption — PARTLY FALSE for the Presenter module.** The plan
   assumed `SettingsService`/`AppSettings` would be reachable from the panel's
   construction site. They are NOT: `OperatorWindow` is created by
   `PresenterIntegration` and receives only `PresenterEngine` (carrying
   `PresenterSettings`) plus a set of domain services — no `AppSettings` and no
   `SettingsService` instance. `AppSettings`/`SettingsService` live in the
   `WorshipPlayer.UI` layer. The clean, additive, module-appropriate slot is
   `PresenterSettings` (reachable via `_engine.Settings`, already persisted by the
   existing `SavePresenterSettings` → `presenter-settings.json` callback). See
   Persistence section below.

## Where/how defaults persist (AC3)

Persisted in `PresenterSettings` (NOT `AppSettings`), because that is the settings model
reachable from the panel's construction site in the Presenter module and it already has a
save path. Two additive, backward-compatible bool properties were added:

- `scriptureShowReference` (default `true`)
- `scriptureShowTranslationName` (default `true`)

Both are plain `[JsonPropertyName]` auto-properties: a missing JSON key deserializes to
the C# default, so this is backward-compatible with existing `presenter-settings.json`
files. The panel seeds its checkboxes from these on `Loaded` and writes them back in
`Options_Changed`. When `Settings` is null (e.g. a bare panel with no engine), the panel
falls back to session-only checkbox state (both default to checked) — guarded by null
checks. `LinesPerSlide` is intentionally NOT persisted (kept at the generator default of
4) to stay strictly scoped to E5's reference/translation toggles.

Note: the panel exposes this as a `PresenterSettings? Settings` property rather than the
`SettingsService? SettingsService` named in the plan, for the reason in false-premise #3.
Fire-and-forget disk writes are not performed by the panel; the existing
`SavePresenterSettings` callback owns persistence of `PresenterSettings` to disk (invoked
through the normal settings-save flow), and the in-memory defaults are updated live so a
save captures them.

## Full file:line trace (reference input → generator → slides → output)

1. **Reference input.** Operator types into `ReferenceTextBox` and clicks "Look Up" (or
   Enter) →
   `WorshipPlayer.Presenter/Views/Scripture/ScriptureBrowserPanel.xaml.cs`
   `LookUp_Click` / `ReferenceTextBox_KeyDown` → `PerformLookup()` (L84).

2. **Resolve verses.** `PerformLookup()` calls
   `IBibleProvider.ParseReferenceAsync(reference, translation)` → `ScriptureReference`
   and `IBibleProvider.GetVersesAsync(parsedRef)` → `List<BibleVerse>`.
   The parsed reference + verses are retained in `_currentReference` / `_currentVerses`.

3. **Generator = the ONE splitter.** `RegenerateSlides()` (L145-162) builds
   `ScriptureSlideGenerator.SlideGenerationOptions` with
   `ShowReference = ShowReferenceCheckBox.IsChecked == true` and
   `ShowTranslationName = ShowTranslationCheckBox.IsChecked == true`, then calls
   `_generator.GenerateSlides(_currentVerses, _currentReference, options)` and stores the
   result in `_currentSlides`. `_generator` is `new ScriptureSlideGenerator()` (field,
   L22). The panel contains NO splitting/flattening logic of its own anymore — the old
   `displayText` join is kept ONLY as the human-readable in-panel preview (`ResultsText`),
   not as slide content.
   Core splitter:
   `WorshipPlayer.Core/Services/ScriptureSlideGenerator.cs` `GenerateSlides` (L55) →
   `CreateSlide` builds `Content.Subtitle` honoring ShowReference/ShowTranslationName
   (L159-186).

4. **Present.** Operator clicks "Add to Service" →
   `AddToService_Click` (L184) raises `PresentSlidesRequested` with `_currentSlides`.

5. **Engine load.**
   `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`:
   in the `"Scripture"` case of `SwitchToTab` the panel is created once, assigned
   `Settings = _engine?.Settings`, and subscribed
   `PresentSlidesRequested += OnScripturePresentRequested`. `OnScripturePresentRequested`
   calls `_engine?.LoadScriptureSlides(slides)`.
   `WorshipPlayer.Presenter/Services/PresenterEngine.cs` `LoadScriptureSlides` sets
   `_currentSlides` / `_currentSlideIndex` under `_stateLock` and fires one `SlideChanged`
   for slide 0 (mirrors `LoadStandaloneSong`).

6. **Output.** OperatorWindow's existing `_engine.SlideChanged += OnSlideChanged` handler
   forwards to `OutputWindow.ShowSlide`. Operator navigates with GO LIVE / arrow keys
   exactly as for songs. ONE splitter (the Core generator) feeds output.

Toggle round-trip: `ShowReferenceCheckBox` / `ShowTranslationCheckBox`
`Checked`/`Unchecked` → `Options_Changed` (L168) → updates `PresenterSettings` defaults +
calls `RegenerateSlides()` so toggling changes generated slide content immediately.

## Build & test results (run in the worktree)

- `dotnet build WorshipBacktrackPlayer.sln` → **Build succeeded. 0 Error(s), 4 Warning(s)**
  (the 4 warnings are pre-existing: `MediaLibraryPanel._currentView`,
  `ChordChartWindow` CS8604, `WaveformEditorWindow._inReviewMode` — unrelated to this
  change).
- `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj` → **Passed! Failed: 0,
  Passed: 550, Skipped: 0, Total: 550.**
- Scripture + engine subset (`FullyQualifiedName~ScriptureSlideGenerator |
  ~PresenterEngineScripture`) → **Passed: 23, Failed: 0** (18 pre-existing generator
  tests + 3 new generator tests + 2 new engine tests).

## Tests added (AC6)

Extended `WorshipPlayer.Tests/ScriptureSlideGeneratorTests.cs` (not duplicated):
- `GenerateSlides_LongPassage_AutoSplitsIntoExactSlideCount` — 12 one-line verses,
  `LinesPerSlide=2`, `MaxCharsPerLine=80` → **exactly 6 slides** (exact-count assertion).
- `GenerateSlides_ShowReferenceToggle_ChangesSubtitle` — ShowReference true vs false on
  the same multi-verse input: reference present when true, absent when false, subtitles
  differ.
- `GenerateSlides_ShowTranslationNameToggle_ChangesSubtitle` — ShowTranslationName true vs
  false: translation id present when true, absent when false, subtitles differ.

New `WorshipPlayer.Tests/PresenterEngineScriptureTests.cs`:
- `LoadScriptureSlides_SetsSlidesSelectsFirst_AndFiresSlideChangedOnce` — 3 slides →
  `TotalSlides==3`, `CurrentSlideIndex==0`, `CurrentSlide` is the first, SlideChanged
  fired once with slide 0.
- `LoadScriptureSlides_EmptyList_SelectsNothing_AndFiresNoSlideChanged` — empty list →
  index -1, no slide, no SlideChanged.

## What WAS verified

- Clean full-solution build on Windows (0 errors, 4 pre-existing warnings).
- All 550 unit tests pass, including the 5 new scripture/engine tests.
- The reachable panel now uses only the Core `ScriptureSlideGenerator`; no ad-hoc
  splitter remains in the reachable path (grep confirms the old `ScriptureLookupResult`
  type and `AddToServiceRequested` event are fully removed with no remaining references).
- `ScriptureSlidePreview.xaml(.cs)` left untouched; songs/templates/media/service-builder
  not modified. Bible DB not seeded (E2 out of scope).

## What was NOT verified (WPF desktop constraint)

- **Visual slide rendering was not exercised headless.** `WorshipPlayer.Presenter` is a
  `net8.0-windows` WPF project and `OperatorWindow`/`OutputWindow` require an interactive
  WPF message loop; a headless UI run is not possible in this environment. Correctness of
  the wired path is proven via: clean full-solution build (XAML + code-behind compile),
  passing generator tests (slide content/count), the engine test for `LoadScriptureSlides`
  (`PresenterEngine.cs` is compiled into the test project and unit-tested without WPF), and
  the file:line trace above. The actual pixels drawn by `OutputWindow.ShowSlide` were not
  observed in this run.
