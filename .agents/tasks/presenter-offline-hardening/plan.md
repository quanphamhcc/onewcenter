# Implementation Plan — Presenter Offline Hardening (F1–F12, Priority-1)

Authoritative source: `design.md` (APPROVED, 5 clarifications committed). This plan implements EVERY
committed fix F1–F12. All paths are relative to the worktree
`.worktrees\presenter-offline-hardening`. Baseline: `dotnet build WorshipBacktrackPlayer.sln --nologo`
= 0 errors / 4 pre-existing warnings; `dotnet test WorshipPlayer.Tests --nologo` = 411 passing. After
the work: 0 errors, NO new warnings beyond the 4 pre-existing, all tests green (411 + new).

New tests live in `WorshipPlayer.Tests` (net8.0, references `WorshipPlayer.Core` only; selected
Presenter sources linked via `<Compile Include>`). Do NOT pull WPF or ASP.NET hosting into the Tests
project. `PresenterEngine.cs` is already linked into Tests; `StageServerPortProbe`/
`StageServerStartResult` go in `WorshipPlayer.Core` so the F8 tests compile with no csproj change.

Items are ordered by dependency. Each leaves the solution buildable.

---

- [ ] 1. (F8 types) Create `StageServerPortProbe` + `StageServerStartResult` in WorshipPlayer.Core.
      Add a new file declaring `public record StageServerStartResult(bool Started, string? Error)` and
      a static `public static class StageServerPortProbe` with
      `public static bool TryReserve(int port, out string? error)` that attempts a
      `System.Net.Sockets.TcpListener` bind on `IPAddress.Any:port` (mirrors `ListenAnyIP`), returns
      true + null error on success (and stops the listener), returns false + a non-empty message on
      `SocketException`/`IOException`. Pure BCL, cross-platform, no ASP.NET.
      Files: WorshipPlayer.Core/Services/Stage/StageServerPortProbe.cs (new)
      Verify: `dotnet build WorshipPlayer.Core --nologo` — 0 errors / no new warnings.

- [ ] 2. (F8 tests) Add StageServerPortProbe unit tests.
      Add tests: TryReserve on a port occupied by a test-owned `TcpListener` returns false + non-null
      error; TryReserve on a known-free ephemeral port returns true; and the mapping into
      `StageServerStartResult` yields `Started=false`+message on busy and `Started=true` on free. Use an
      ephemeral/caller-supplied port (never fixed 5055) so the test is not flaky.
      Files: WorshipPlayer.Tests/StageServerPortProbeTests.cs (new)
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — new tests pass; suite green.

- [ ] 3. (F4 producer) Add `SectionLooped(string sectionName)` event to MultiTrackMixer + AudioEngine.
      In `MultiTrackMixer` add `public event EventHandler<SectionLoopedEventArgs>? SectionLooped;` and
      raise it with payload `_song.Sections[_currentSectionIndex].Name` in BOTH infinite-loop branches:
      `HandleSectionBoundary` (after `SeekToMsWithVocalPickup(currentSection); _loopIterations++;`) and
      `CheckSectionBoundary` (identical branch). Define `SectionLoopedEventArgs { string SectionName; int SectionIndex }`
      (new args class near `SectionChangedEventArgs` in AudioEngine.cs). In `AudioEngine.LoadSong` and
      `ActivateSong`, subscribe `_mixer.SectionLooped` and re-raise an `AudioEngine.SectionLooped(string)`
      event (add `public event EventHandler<string>? SectionLooped;` — keep signature simple so
      PresenterIntegration can forward the name).
      Files: WorshipPlayer.Audio/Engine/MultiTrackMixer.cs, WorshipPlayer.Audio/Engine/AudioEngine.cs
      Verify: `dotnet build WorshipPlayer.Audio --nologo` — 0 errors / no new warnings.

- [ ] 4. (F2/F3) PresenterEngine.LoadSong: ServiceItem slides are the single source of truth.
      In `LoadSong(Song song)`: capture `bool itemHasSlides = _serviceOrderManager.CurrentItem?.Slides?.Count > 0;`
      FIRST. Always set `_currentSong = song; _pitchShiftSemitones = 0; _tempoPercent = 100.0;` and fire
      `SongLoaded`. Call `GenerateSlidesFromSong(song)` + reset index ONLY when `!itemHasSlides`. When
      `itemHasSlides`, leave `_currentSlides`/`_currentSlideIndex` untouched and do NOT fire `SlideChanged`
      from LoadSong (the slide-0 emit already came from `OnServiceItemChanged`). Keep `GenerateSlidesFromSong`
      intact as the fallback (fire `SlideChanged` once there). Section matching stays `OrdinalIgnoreCase`.
      Files: WorshipPlayer.Presenter/Services/PresenterEngine.cs
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — item-slide-precedence tests (item 5) pass.

- [ ] 5. (F2/F3 tests) Add slide-source precedence tests for PresenterEngine.
      Test A: a Song ServiceItem with pre-baked slides, then `OnSongLoaded` with an empty-text song →
      assert `_currentSlides` still equals the item slides AND no duplicate `SlideChanged` from LoadSong
      (count events; only the `OnServiceItemChanged` slide-0 emit). Test B: a library song WITH
      `sections[].text` and NO item slides → assert fallback generation produces slides and fires
      `SlideChanged` once. Assert slide `SectionName`s match `song.json` names `OrdinalIgnoreCase`.
      Files: WorshipPlayer.Tests/PresenterEngineSlideSourceTests.cs (new)
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — new tests pass.

- [ ] 6. (F4 consumer test) Add section-loop reset test.
      `OnSectionLooped("CHORUS 1")` on an engine whose current slides have advanced within/ past CHORUS 1
      → assert `CurrentSlideIndex` returns to that section's FIRST slide (reset, not advance); unmatched
      name → no change, no throw. (The existing `OnSectionLooped` reset path is activated end-to-end by
      the wiring in item 11.)
      Files: WorshipPlayer.Tests/PresenterEngineLoopResetTests.cs (new)
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — new tests pass.

- [ ] 7. (F5 test) Add chord-transpose-on-pitch test via PresenterEngine.GetStageText.
      Assert `GetStageText(slide, +2)` on a slide whose text contains `[C]` returns text containing `[D]`
      (positive = up a tone); `GetStageText(slide, 0)` returns the original text unchanged.
      Files: WorshipPlayer.Tests/PresenterEngineTransposeTests.cs (new)
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — new tests pass.

- [ ] 8. (F7) Add `ClampDisplayIndex` pure helper + test.
      Add `public static int ClampDisplayIndex(int savedIndex, int monitorCount)` to `DisplayManager`
      (static; pure): `savedIndex >= monitorCount` → -1; in-range preserved; -1 preserved; negative → -1.
      Files: WorshipPlayer.Presenter/Services/DisplayManager.cs; add
      `<Compile Include ... DisplayManager.cs Link="DisplayManager.cs" />` to WorshipPlayer.Tests.csproj
      ONLY if needed to reach it (DisplayManager has no WPF deps; confirm it compiles cross-platform —
      it uses only `WorshipPlayer.Core.Models`). Test file WorshipPlayer.Tests/ClampDisplayIndexTests.cs.
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — new tests pass; NO new warnings.

- [ ] 9. (F12) PresenterEngine.SnapshotStageData under a lock.
      Add `private readonly object _stateLock = new();`. Add
      `public StageDisplayData SnapshotStageData()` that, inside `lock (_stateLock)`, reads
      `_currentSlides`, `_currentSlideIndex`, `_currentSong` (incl. `.Title`/`.OriginalKey`), `_isLive`,
      `_isBlacked`, builds a brand-new `StageDisplayData` with all TEN derived values
      (CurrentText/NextText/SectionName/SongTitle/Key/OriginalKey/IsLive/IsBlacked/CurrentSlideIndex/
      TotalSlides) and returns it. Wrap the field writes in `SetSlideIndex`, `GenerateSlidesFromSong`
      (the `_currentSlides`/`_currentSlideIndex` assignment), `OnServiceItemChanged`, and `LoadSong`'s
      `_currentSong` write in the SAME `lock (_stateLock)`; raise `SlideChanged` OUTSIDE the lock.
      Note: `SnapshotStageData` returns the Presenter `StageDisplayData` type — this method lives in
      `PresenterEngine.cs` which is linked into Tests, and `StageDisplayData.cs` is also linked, so the
      Core→Presenter type direction is fine (both are Presenter types used from the engine).
      Files: WorshipPlayer.Presenter/Services/PresenterEngine.cs
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — snapshot test (item 10) passes.

- [ ] 10. (F12 test) Add SnapshotStageData consistency test.
      Drive a section-change → loop → position sequence against the engine and assert each
      `SnapshotStageData()` is internally consistent (CurrentText/SectionName match CurrentSlideIndex;
      SongTitle/Key/OriginalKey reflect the same song) and indices progress monotonically within a
      section. Plain lock, no UI thread.
      Files: WorshipPlayer.Tests/PresenterEngineSnapshotTests.cs (new)
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — new tests pass.

- [ ] 11. (F8 server) Rewrite StageDisplayServer.StartAsync to observe the bind deterministically.
      Change `StartAsync` to `public async Task<StageServerStartResult> StartAsync(CancellationToken ct = default)`.
      Keep `ListenAnyIP(_port)` (fixed port, no fallback). Replace fire-and-forget `_app.RunAsync()` with
      `await _app.StartAsync(ct)` inside a try/catch; on bind failure return `new StageServerStartResult(false, msg)`
      and set a new `public string? LastStartError { get; private set; }`; on success return
      `new StageServerStartResult(true, null)`. The start/observe path MUST NOT dereference
      `_presenterSettings`, `_remoteCommandHandler`, or `_presenterEngine` (null on the single-arg ctor
      production uses). Keep `_serverTask` semantics for StopAsync as needed (store the started app).
      Files: WorshipPlayer.Presenter/Services/StageDisplayServer.cs (uses the Core types from item 1)
      Verify: `dotnet build WorshipPlayer.Presenter --nologo` — 0 errors / no new warnings.

- [ ] 12. (F8 caller) PresenterIntegration.StartStageServerAsync inspects the result.
      `await StageDisplayServer.StartAsync()`; if `!result.Started`, surface a non-fatal operator-visible
      status ("Stage display server could not start on port 5055 — the service will run without the
      second-screen/remote display") and continue. Never crash; live path does not depend on the server.
      Files: WorshipPlayer.UI/Services/PresenterIntegration.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln --nologo` — 0 errors / no new warnings.

- [ ] 13. (F8 (c) safety test) Single-arg-constructor StartAsync returns clean result, no NRE.
      Prefer a Core-level seam if `StageDisplayServer.cs` cannot be reached from Tests without adding the
      ASP.NET hosting stack (which the no-new-warnings invariant forbids). If reachable without new
      warnings, add a test that `new StageDisplayServer(freePort)` + `await StartAsync()` returns
      `Started=true` and throws no `NullReferenceException`, then StopAsync. Otherwise assert the result
      mapping (already covered by item 2) and mark the live start human-on-hardware per design §4 F8.
      Files: WorshipPlayer.Tests/StageServerStartResultTests.cs (new, mapping-level) — no ASP.NET added.
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — tests pass; NO new warnings.

- [ ] 14. (F1) Wire a real ServiceOrder load + startup auto-load.
      Inject an async loader into `OperatorWindow.LoadService_Click` that calls
      `ServiceOrderManager.LoadAsync(path)`; null/exception → non-fatal MessageBox/status, no crash.
      Subscribe `ServiceOrderManager.ServiceOrderLoaded` in `PresenterIntegration.Initialize` to refresh
      the operator list and set the first item current (via existing `GoToItem(0)` path). On startup,
      auto-load the most recent file in `ServiceOrders/` if present. The resulting first-item-current may
      trigger a backtrack `SongLoadRequested`/stem preload that degrades SILENTLY through
      `MainWindow.LoadSong`'s existing try/catch (non-fatal "stems not found" status); the
      `ServiceOrderLoaded` handler adds NO throwing wrapper of its own.
      Files: WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs,
      WorshipPlayer.UI/Services/PresenterIntegration.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln --nologo` — 0 errors / no new warnings.

- [ ] 15. (F1 test) ServiceOrderManager.LoadAsync against the sample service order.
      Write the real `2026-08-03_Sunday Morning.json` content to a temp file (inline string, mirroring
      existing temp-folder test style) and assert `LoadAsync` yields 6 items, first item current, titles/
      types parsed; a malformed JSON file → returns null, no throw.
      Files: WorshipPlayer.Tests/ServiceOrderManagerLoadTests.cs (new)
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — new tests pass.

- [ ] 16. (F5 output) OutputWindow transposes shown chords on pitch change.
      Add `public void SetPitchShift(int semitones)` storing the offset and re-rendering the current slide.
      In `LoadSlideIntoBuffer`, when `_showChords` is true route text through
      `GetStageText(slide, +pitchSemitones)` (reuse the engine method; inject or pass the parser/offset)
      instead of a bare keep; when `_showChords` is false keep `StripChords` (pitch has no effect). Capo/
      Nashville stay stage-only. In `OperatorWindow.OnPitchShiftChanged`, forward the value to
      `_outputWindow.SetPitchShift(...)` and re-render current slide. In `PresenterIntegration`,
      re-broadcast stage data on pitch change (wire `PresenterEngine.PitchShiftChanged` →
      `BroadcastStageUpdate`). Pitch 0 → unchanged.
      Files: WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs,
      WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs,
      WorshipPlayer.UI/Services/PresenterIntegration.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln --nologo` — 0 errors / no new warnings.

- [ ] 17. (F6/F7) Single-monitor degrade + startup index clamping.
      On startup after `PopulateDisplayManager`, clamp `OutputDisplay`/`StageDisplay` via
      `DisplayManager.ClampDisplayIndex(saved, AvailableDisplays.Count)`. On Go-Live when
      `OutputDisplay < 0` OR only one monitor present, `PresenterIntegration` lazily creates the
      `OutputWindow`, calls `GoFullScreen()` then sets `Topmost = false` (add
      `GoFullScreen(bool topmost = true)` or explicit set), and registers it via
      `_operatorWindow.SetOutputWindow(_outputWindow)`. Reuse the window if created. Route single-monitor
      `Esc` in `OperatorWindow.Window_KeyDown` to `_outputWindow.ExitFullScreen()`; dual-monitor keeps
      `Esc` = content black. Wrap placement so a bad `DisplayInfo` falls back to primary, never throws.
      Files: WorshipPlayer.UI/Views/MainWindow.xaml.cs,
      WorshipPlayer.UI/Services/PresenterIntegration.cs,
      WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs,
      WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln --nologo` — 0 errors / no new warnings.

- [ ] 18. (F11 wiring) Forward the AudioEngine.SectionLooped through to the engine.
      In `PresenterIntegration.Initialize` subscribe `_audioEngine.SectionLooped` →
      `MidiPresenterSync.OnSectionLooped(name)` → existing `PresenterEngine.OnSectionLooped(name)`; unsub
      in `Dispose`. Confirm manual-advance keys (Space/Right/Left/B/Enter/Esc/1–9) drive the engine for a
      non-backtrack item (no key-table change beyond the F6 Esc branch). This activates the loop reset
      end-to-end.
      Files: WorshipPlayer.UI/Services/PresenterIntegration.cs
      Verify: `dotnet build WorshipBacktrackPlayer.sln --nologo` — 0 errors / no new warnings.

- [ ] 19. (F9/F10) Defensive load + render.
      Wrap all THREE `BitmapImage` sites in `OutputWindow` (`ApplyTheme` background, `ApplyTheme` logo,
      `LoadSlideIntoBuffer` foreground) in try/catch, skipping the image on failure (debug log). Resolve
      relative image paths against `AppDomain.CurrentDomain.BaseDirectory`; absolute paths as-is.
      Null-guard `Slide.Content`, `Song.Sections`, `Song.Tracks` on the consumption paths; `LoadAsync`
      callers handle null + `JsonException`. Add a normalization pass filling missing collections with
      empties so downstream never NREs.
      Files: WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs,
      WorshipPlayer.Presenter/Services/PresenterEngine.cs (defensive guards),
      WorshipPlayer.UI/Services/PresenterIntegration.cs (LoadAsync caller guard)
      Verify: `dotnet build WorshipBacktrackPlayer.sln --nologo` — 0 errors / no new warnings.

- [ ] 20. (F9/F10 test) Defensive-load unit tests.
      Malformed ServiceOrder → `LoadAsync` returns null, no throw. A song with null `Content`/`Sections`/
      `Tracks` → engine consumption (e.g., `GenerateSlidesFromSong`, `SnapshotStageData`) does not NRE.
      Files: WorshipPlayer.Tests/DefensiveLoadTests.cs (new)
      Verify: `dotnet test WorshipPlayer.Tests --nologo` — new tests pass.

- [ ] 21. Final verification.
      Run `dotnet build WorshipBacktrackPlayer.sln --nologo` (0 errors, exactly the 4 pre-existing
      warnings, none new) and `dotnet test WorshipPlayer.Tests --nologo` (all green, 411 + new tests).
      Fix any regressions before closing.
      Files: (none — verification only)
      Verify: both commands succeed per the invariants above.

## Human-on-hardware checks (cannot be verified in CI; from design §4)
Second-monitor placement/full-screen; audio render-thread loop/section/pitch behavior with real stems;
LibVLC video items; phone/tablet reaching `/stage` and `/remote` over LAN; port 5055 already-taken
operator message + service still runs; single-arg server `StartAsync` with no NRE at runtime.
