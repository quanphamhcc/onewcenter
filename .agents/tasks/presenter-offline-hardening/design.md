# Presenter Offline Hardening — Investigation & Design

Goal: make one Windows machine run a full worship service end-to-end, OFFLINE, with no crashes.
Priority: correctness and real reliability of the live path, NOT new features.

This document is the result of reading the actual code in the worktree
`.worktrees/presenter-offline-hardening` (solution `WorshipBacktrackPlayer.sln`). Baseline
verified in this environment: `dotnet build` succeeds with **0 errors / 4 pre-existing warnings**
(the same 4 warnings quoted in the task). This is a Windows host, so the WPF projects build and
most runtime checks are possible here; items that need a real second monitor, real audio hardware,
or a real phone/tablet are flagged as **human-on-hardware** below.

Stack is LOCKED by the existing solution and is NOT changed by this work:
.NET 8, C#, WPF (`net8.0-windows` for Audio/Presenter/UI), xUnit tests, Kestrel/ASP.NET Core
minimal API for the stage server, `Microsoft.Data.Sqlite` for the Bible DB, NAudio/WASAPI for audio.
`WorshipPlayer.Core` and `WorshipPlayer.Tests` are cross-platform; `EnableWindowsTargeting` is set
on Presenter.

---

## 1. The live-service code path AS IT ACTUALLY IS

File references are to the worktree. Line-level behavior was read, not assumed.

### 1.1 Bootstrap
- `WorshipPlayer.UI/Views/MainWindow.xaml.cs` `MainWindow_Loaded`:
  - Loads `AppSettings` via `SettingsService`.
  - Creates `PresenterIntegration` (`WorshipPlayer.UI/Services/PresenterIntegration.cs`) and calls
    `Initialize()`, then `PopulateDisplayManager(...)`, `StartStageServerAsync()`,
    `OpenPresenterWindow()`.
- `PresenterIntegration` constructor wires the object graph:
  - `ServiceOrderManager` rooted at `AppDomain.BaseDirectory/ServiceOrders`.
  - `PresenterEngine = new PresenterEngine(ServiceOrderManager, presenterSettings)`.
  - `MidiPresenterSync = new MidiPresenterSync(PresenterEngine, midiCueService, presenterSettings)`.
  - `DisplayManager`, `StageDisplayServer(presenterSettings.StageServerPort)` (port 5055 default),
    `ThemeManager`, library/alert/countdown/usage services, and
    `BibleDatabaseService(BaseDirectory/bible.db)`.
- `PresenterIntegration.Initialize()`:
  - `MidiPresenterSync.Activate()` (subscribes to `MidiCueService.CueTriggered`).
  - Subscribes to `AudioEngine.SectionChanged`, `AudioEngine.PositionChanged`,
    `AudioEngine.PlaybackStopped`.
  - Subscribes `ServiceOrderManager.ItemChanged` → `OnServiceItemChanged`.
  - Subscribes `PresenterEngine.SlideChanged` → `OnPresenterSlideChanged` (stage broadcast).
  - Fires `InitializeBibleDatabaseAsync()` (schema + seed KJV) fire-and-forget.

### 1.2 Loading a service + songs
- `ServiceOrderManager.LoadAsync(path)` (`WorshipPlayer.Core/Services/ServiceOrderManager.cs`)
  deserializes a `ServiceOrder`, sets `CurrentServiceOrder`, `CurrentItemIndex = 0`, and fires
  `ServiceOrderLoaded`. **No production caller invokes `LoadAsync`** (verified by searching the whole
  worktree: only `WorshipPlayer.Tests/RemoteCommandHandlerTests.cs` and the Add-Item dialog use
  `CreateNew`). The sample `ServiceOrders/2026-08-03_Sunday Morning.json` is therefore never loaded
  by the running app.
- `OperatorWindow.LoadService_Click` (`WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`) opens
  an `OpenFileDialog` and then does nothing with the result — the comment literally says
  "Engine will load and fire events," but nothing loads it.
- Songs are discovered by `SongManager` from the `Songs/` folder and the registry
  `Songs/_song_registry.json`. **In this worktree's `Songs/` folder** only
  `Songs/Open The Heavens/song.json` exists; the stems (`01_Click.mp3` … `17_LeadVocal.mp3`)
  referenced by that song.json are **not present** in the song folder (there are loose mp3s at the
  repo root instead). `Songs/TheBlessing/` and `_song_registry.json` referenced in the task are
  absent **from this worktree's `Songs/` folder** (they exist in the main checkout; this is a
  worktree-scoped observation, not a repo-wide one).

### 1.3 Live playback → events → slides
- Operator (or MainWindow) loads a song: `MainWindow.LoadSong(song)` preloads tracks
  (`AudioEngine.PreloadSong` on a background thread), activates WASAPI, then calls
  `_presenterIntegration.OnSongLoaded(song)`.
- `PresenterIntegration.OnSongLoaded` → `MidiPresenterSync.OnSongLoaded` → `PresenterEngine.OnSongLoaded`
  → `PresenterEngine.LoadSong` → `GenerateSlidesFromSong`.
- During playback the audio engine is the conductor:
  - `MultiTrackMixer` fires `SectionChanged` from `JumpToSection` (sequential advance and explicit
    jump) and `PositionChanged` periodically.
  - `AudioEngine` re-raises both to `PresenterIntegration`.
  - `OnAudioEngineSectionChanged` → `MidiPresenterSync.OnBacktrackSectionChanged(name)` →
    `PresenterEngine.OnSectionChanged(name)` → finds first slide whose `SectionName` matches → fires
    `SlideChanged`.
  - `OnAudioEnginePositionChanged` → `MidiPresenterSync.OnPlaybackPositionUpdate` →
    `PresenterEngine.OnPlaybackPositionUpdate` → timestamp-driven slide advance.
  - `OnAudioEnginePlaybackStopped` → `MidiPresenterSync.OnSongEnded` → `PresenterEngine.OnSongEnded`
    → advance to next item if `AutoAdvanceOnSongEnd`.
- `PresenterEngine.SlideChanged` is consumed in two places:
  - `OperatorWindow.OnSlideChanged` (via `Dispatcher.Invoke`) updates previews and calls
    `_outputWindow.ShowSlide(...)`.
  - `PresenterIntegration.OnPresenterSlideChanged` → `BroadcastStageUpdate()` → async
    `StageDisplayServer.BroadcastSlideUpdate`.

### 1.4 Output + stage display
- `OutputWindow` (`WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs`) is the congregation screen.
  It is created ONLY by `PresenterIntegration.ApplyDisplaySettings` when `settings.OutputDisplay >= 0`
  and a matching `DisplayInfo` exists. `OpenPresenterWindow` deliberately does NOT create it (comment
  cites a prior StackOverflow with the VLC `VideoView`). Default `PresenterSettings.OutputDisplay = -1`
  and `StageDisplay = -1`.
- Monitor list is populated by `MainWindow.PopulateDisplayManager` → `GetAllMonitors()` (Win32
  `EnumDisplayMonitors` P/Invoke, no WinForms). On a one-monitor machine it returns exactly one
  `DisplayInfo`.
- `StageDisplayServer` (`WorshipPlayer.Presenter/Services/StageDisplayServer.cs`) hosts Kestrel on
  `ListenAnyIP(port)` and serves `/stage`, `/ws/stage`, `/remote`, `/ws/remote`. Started by
  `StartStageServerAsync` only if `StageServerEnabled` (default true).

### 1.5 Scripture / Bible (offline)
- `BibleDatabaseService` (`WorshipPlayer.Core/Services/Bible/BibleDatabaseService.cs`) is pure local
  SQLite (`Data Source=bible.db`). `InitializeAsync` creates schema + seeds translations/books;
  `SeedKjvVersesAsync` seeds a **small sample set only** (Genesis 1, Psalms 23, John 3, Romans 8).
  No HTTP anywhere. The scripture ServiceItem in the sample service carries its own `scriptureText`
  in its pre-baked slides, so showing that item does not depend on the DB; only the live scripture
  *browser/lookup* reads the DB.
- Whole-worktree search for `HttpClient|http://|https://|SongSelect|GetAsync` finds network use ONLY
  in `SectionEditorWindow` (ChordPro URL import — not on the live path) and `PythonAnalysisService`
  (model/dependency setup — not on the live path). The live service path is network-free.

---

## 2. Concrete failure points (enumerated), tagged by P1 scope item

Scope items: (1) load service/songs/lyrics, (2) backtrack→slide live flow incl. loop reset + pitch,
(3) manual advance, (4) output/stage on 2nd monitor + single-monitor degrade, (5) stage/remote web
server offline + graceful failure, (6) offline scripture, (7) no network on live path.

### F1 — No way to actually LOAD a ServiceOrder in the live app  — [scope 1]
`ServiceOrderManager.LoadAsync` is never called in production; `LoadService_Click` discards the
chosen file. Result: the real `2026-08-03_Sunday Morning.json` cannot be driven by the operator; the
service order sidebar and `PresenterEngine.CurrentServiceOrder` stay empty unless items are built by
hand. This blocks the entire P1-item-1.

### F2 — Song lyrics are NOT in song.json; slide generation yields ZERO slides  — [scope 1,2]
`PresenterEngine.GenerateSlidesFromSong` iterates `song.Sections[].Text` and `continue`s when the
text is blank. The real `Songs/Open The Heavens/song.json` sections contain only timing/loop/lighting
fields — **no `text`, no inline `[chords]`, no `{timestamps}`, no `chordChart`**. So for the real data,
`LoadSong` produces an empty slide list. Lyrics for the service actually live in the ServiceOrder's
pre-baked `items[].slides[]` (e.g. s002/s003/s004 with `sectionName` "VERSE 1"/"CHORUS 1" and
`timestamp_ms`). The design in `.tasks/presenter-module-v2.md` assumes lyrics come from
`sections[].text`; the real content model is "slides live on the ServiceItem." These two sources
currently fight each other (see F3).

### F3 — Song-item slide source conflict / ordering race  — [scope 1,2]
When a Song item becomes current, `PresenterEngine.OnServiceItemChanged` sets
`_currentSlides = e.NewItem.Slides` (the correct, lyric-bearing slides). But the backtrack load path
also calls `OnSongLoaded`→`LoadSong`→`GenerateSlidesFromSong`, which **overwrites** `_currentSlides`
with slides regenerated from `song.json` (empty for real data, per F2). Depending on event order the
operator can end up with zero slides even though the ServiceItem had good ones. The two code paths
have no defined precedence.

### F4 — Section LOOP never reaches the Presenter (loop does not reset slides)  — [scope 2]
`MultiTrackMixer.HandleSectionBoundary` handles an infinite loop by calling
`SeekToMsWithVocalPickup` + `_loopIterations++` and fires **no event at all** (neither `SectionChanged`
nor any loop event). `AudioEngine` exposes only `PositionChanged`, `SectionChanged`, `PlaybackStopped`
— there is **no `SectionLooped` event**. `MidiPresenterSync.OnSectionLooped` and
`PresenterEngine.OnSectionLooped` exist but are dead code with no caller. Net effect: when a section
loops, slides do nothing (they only re-trigger incidentally via timestamp rewind, which is unreliable).
The requirement "loop must RESET to section start, not advance" is unmet.

### F5 — Real-time pitch shift during a song never transposes the display  — [scope 2]
`AudioEngine` has no pitch-change event. `PresenterIntegration.OnPitchShiftChanged` is only called
from `MainWindow` key-shift buttons (`_presenterIntegration?.OnPitchShiftChanged(_pitchSemitones)`),
and it forwards to `MidiPresenterSync.OnPitchShiftChanged` → `PresenterEngine.OnPitchShiftChanged`
(updates `_pitchSemitones`, fires `PitchShiftChanged`). But the **congregation `OutputWindow` renders
`slide.Content.Text` and only strips/keeps chords** (`LoadSlideIntoBuffer`) — it never calls
`GetStageText`/`Transpose`, so even when chords are shown they are NOT transposed by the pitch shift.
The stage web display personalizes by capo/nashville but is not re-pushed on pitch change either. So
"pitch shift must transpose displayed chords" is unmet on the live output.

### F6 — Single-monitor + default settings = no congregation output ever appears  — [scope 4]
`OutputWindow` is created only when `OutputDisplay >= 0`. Default is `-1`. With one monitor and no
Display Settings interaction, "GO LIVE" updates operator state but there is no output window to show
anything. On a single-monitor machine the only sane behavior (output full-screens over the operator,
or operator explicitly chooses "present on this screen") is not reachable without the settings dialog,
and even then index 0 is the operator's own monitor. Not a crash, but a silent "nothing on screen"
failure for the target one-machine scenario.

### F7 — Output placement can crash / mis-place with a stale or out-of-range display index  — [scope 4]
`DisplayManager.GetOutputBounds()` guards the index, returning null when out of range (good). But if
`OutputDisplay`/`StageDisplay` were saved as 1 on a two-monitor rig and the service is run later on
one monitor, `OutputDisplay >= 0` is true while `GetOutputBounds()` returns null, so
`ApplyDisplaySettings` takes the "None" branch and closes/omits output — again silent-no-output. There
is no clamping of saved indices to the currently available monitor count.

### F8 — Stage server port conflict / bind failure can be swallowed or crash  — [scope 5]
`StageDisplayServer.StartAsync` builds Kestrel with `ListenAnyIP(_port)` and starts via
`_app.RunAsync()` returning `Task.CompletedTask` immediately. A bind failure (port 5055 already in
use, or blocked) surfaces on the **background `_serverTask`, not on `StartAsync`**, so the
`try/catch` in `StartStageServerAsync` does NOT catch it — the exception is unobserved. There is no
user-visible error and no port fallback. `ListenAnyIP(_port)` binds all interfaces, which is the
CORRECT behavior for scope 5 (a phone/tablet on the church LAN must reach `/stage`). Binding all
interfaces makes NO outbound connection and therefore does not violate scope 7 (scope 7 forbids
OUTBOUND network on the live path, not inbound LAN serving). Requirement: observe the bind result
deterministically, fail gracefully with a clear operator-visible message, never crash. See Fix F8
for the committed listen configuration.

### F9 — Missing media files on slides can throw during render  — [scope 1,4]
`OutputWindow.LoadSlideIntoBuffer` and `ApplyTheme` guard image paths with `File.Exists` before
`new BitmapImage(new Uri(path))` (good). BUT the sample service references
`Resources/giving-qr-code.png` (offering slide) and theme logo/background paths; if a path exists but
is an unreadable/corrupt file, or a `foregroundImage` is a relative path that `File.Exists` resolves
against the wrong working directory, `BitmapImage` can throw and is **not** wrapped in try/catch here.
Video items rely on LibVLC (`VideoBackgroundService`/`VideoPlaybackService`); if LibVLC native libs
are absent, `Initialize` may leave `LibVLCInstance` null (handled defensively) but `ShowVideo` on a
Video item is only partially wired. Audio stems missing (F2 reality) is already handled by
`MainWindow.LoadSong`'s try/catch (shows a status message, does not crash) — verified.

### F10 — Malformed / partial song.json or ServiceOrder can throw on load  — [scope 1]
`ServiceOrderManager.LoadAsync` wraps deserialize in the method but a malformed file returns null or
throws `JsonException`; callers (once F1 is fixed) must handle null and exceptions. `SongManager`
loading a song with a missing/!valid field (e.g., no `sections`, null `tracks`) must not throw.
Several Presenter consumers dereference `song.OriginalKey`, `song.Title`, `CurrentSlide.Content.Text`
assuming non-null; `Content` is a nested object that could be null in a hand-edited JSON.

### F11 — Manual-advance mode is wired but depends on F1/F3 to have slides  — [scope 3]
Keyboard handling in `OperatorWindow.Window_KeyDown` covers Space/Right/PageDown=next,
Left/PageUp=prev, Down/Up=item, B=black, Enter=live, Esc=black, F1=logo, D1–D9=jump — **the key
TABLE (which keys are handled → which action) matches the required map in full and is unchanged.**
The ONLY behavioral change to a key is the single-monitor `Esc`: today `Window_KeyDown` routes `Esc`
to `_engine?.GoBlack()` (content black), and Fix F6 re-routes `Esc` on the single-monitor branch to
`OutputWindow.ExitFullScreen()` so the operator reclaims the screen; on a dual-monitor rig `Esc`
keeps its current meaning (content black against the dedicated output monitor). No other remap is
needed. The real gaps are otherwise downstream, not in the key table: (a) manual advance only works
once the current item actually has slides, which is blocked by F1/F3 for the real service; and (b)
Enter/Esc rely on `_outputWindow`, which is null in the
single-monitor default (F6). `_isManualAdvanceMode` is set from `!item.UseBacktrack` but is otherwise
unused (no behavioral difference) — not a defect, just noting it carries no behavior today.

### F12 — Audio-thread events mutate UI-affecting state without a consistent marshaling contract — [scope 2]
`AudioEngine.SectionChanged`/`PositionChanged` are raised from the NAudio render thread.
`PresenterEngine.OnSectionChanged`/`OnPlaybackPositionUpdate` mutate `_currentSlideIndex` and raise
`SlideChanged` on that same thread. `OperatorWindow` marshals with `Dispatcher.Invoke` (safe), but
`PresenterIntegration.OnPresenterSlideChanged` reads engine state and serializes on the audio thread,
and `BroadcastSlideUpdate` is `async void`-ish fire-and-forget. This is mostly working today but is a
latent source of intermittent races/odd ordering during a live service; it needs an explicit, tested
contract rather than being left implicit.

---

## 3. Proposed fix approach (behavior-preserving unless noted)

Guiding rule: do not change what the operator/congregation sees unless the current behavior is a bug.
Prefer Core-level, unit-testable changes; keep WPF glue thin.

### Fix F1 — Wire a real ServiceOrder load
Make `OperatorWindow.LoadService_Click` call an injected async loader that calls
`ServiceOrderManager.LoadAsync(path)`; subscribe `ServiceOrderManager.ServiceOrderLoaded` in
`PresenterIntegration.Initialize` to refresh the operator list and set the first item current. Also
auto-load the most recent file in `ServiceOrders/` on startup if present (so the sample service drives
the app). Null/exception from `LoadAsync` shows a non-fatal `MessageBox`/status, never crashes.
Behavior change is additive (a feature that was intended but missing), inside the stated scope item 1.

**Startup auto-load × backtrack stem preload (committed, closes review Finding 5).** Setting the
first item current on `ServiceOrderLoaded` fires `OnServiceItemChanged`; if that first item is a
backtrack `Song`, the chain raises `SongLoadRequested` → `MainWindow.LoadSong`, which preloads the
stems. In this worktree's `Songs/` folder those stems are absent (data note, Section 6), so the
preload WILL fail — and that failure **MUST degrade silently, not error on startup.** The committed
behavior: the resulting `SongLoadRequested`/stem-preload runs through `MainWindow.LoadSong`'s
**existing `try/catch`** (verified: it catches the missing-stem failure, shows a non-fatal
"stems not found" status message, and does not crash — see F9). The `ServiceOrderLoaded` handler
does NOT add its own throwing path around the song load; it relies on that existing guard. Net: on
startup with the sample service and missing stems, the app fills the service-order sidebar, sets the
first item current, and shows the non-fatal "stems not found" status — it does NOT throw or crash on
startup. (This is the auto-load counterpart to the operator-initiated Load Service path; both share
`MainWindow.LoadSong`'s guard.)

### Fix F2 + F3 — Single, committed slide source with a precise precedence guard
**Decision (committed, not an either/or): the ServiceItem's pre-baked `slides[]` is the SINGLE
source of truth for a Song item's lyrics. `song.json` supplies section/loop/pitch TIMING only.
`GenerateSlidesFromSong` is a FALLBACK used ONLY when the ServiceItem carries zero slides.**

Rationale: the shipped data has lyrics on the ServiceItem (`items[].slides[]` with `sectionName`
"VERSE 1"/"CHORUS 1" and `timestamp_ms`), and `Songs/Open The Heavens/song.json` has NO section
`text` at all, so `GenerateSlidesFromSong` yields zero slides for the real service. The
"ServiceItem slides are authoritative" branch is the only one that works for the shipped data, and
it avoids inventing lyric content. The alternative — authoring lyrics into `song.json →
sections[].text` — is explicitly **OUT OF SCOPE** for this hardening pass (that would be a data
change, not a reliability fix); it is recorded in Section 5 so no one re-opens it.

Precise implementation (stated in code terms so there is no ambiguity):
- In `PresenterEngine.LoadSong(Song song)`, FIRST capture the guard:
  `bool itemHasSlides = _serviceOrderManager.CurrentItem?.Slides?.Count > 0;`
- ALWAYS set the song/pitch/section context regardless of the guard:
  `_currentSong = song; _pitchShiftSemitones = 0; _tempoPercent = 100.0;` and fire `SongLoaded` as
  today (that event only carries the `Song`, it does not touch slides).
- Call `GenerateSlidesFromSong(song)` and reset `_currentSlideIndex` **ONLY when `!itemHasSlides`**.
  When `itemHasSlides` is true, `LoadSong` leaves `_currentSlides` and `_currentSlideIndex`
  untouched (they were already set by `OnServiceItemChanged` to `e.NewItem.Slides` with index 0).
- `LoadSong` must **NOT** fire `SlideChanged` when it kept the existing item slides. Today `LoadSong`
  always re-fires `SlideChanged` for slide 0; after this change it fires `SlideChanged` only in the
  fallback branch (`!itemHasSlides` and generation produced slides). This prevents a duplicate
  slide-0 emit that would fight the emit already raised by `OnServiceItemChanged`.
- Keep `GenerateSlidesFromSong` intact as the fallback for library/ChordPro-imported songs that DO
  carry `sections[].text`, so no existing behavior for those is lost.
- Section matching (`OnSectionChanged`, and loop reset in F4) keys on the slide `SectionName`
  (already present on the ServiceItem slides, e.g. "VERSE 1"), compared `OrdinalIgnoreCase` against
  the `song.json` section name. The sample data's names match, so the chain works.

This also resolves the ordering race: regardless of whether `OnServiceItemChanged` or
`OnSongLoaded`→`LoadSong` runs first, `LoadSong` can no longer blow away populated item slides.

### Fix F4 — Introduce a SectionLooped signal end-to-end
- Add an event on `MultiTrackMixer` (`SectionLooped`) raised at the exact point the mixer seeks back
  for an infinite loop. There are TWO such points and the event MUST be raised in BOTH, or looping
  that happens via the fallback path will still not reset slides:
  1. `HandleSectionBoundary` — the `if (LoopEnabled && LoopInfinite)` branch (after
     `SeekToMsWithVocalPickup(currentSection); _loopIterations++;`).
  2. `CheckSectionBoundary` (the safety-net fallback) — the identical `if (LoopEnabled &&
     LoopInfinite)` branch.
- **Payload MUST carry the section NAME, not just the index:**
  `Song.Sections[_currentSectionIndex].Name` (both branches have `_song.Sections[_currentSectionIndex]`
  / `currentSection` in scope). The index may be included for diagnostics, but the Presenter path
  keys on the NAME.
- Re-raise it on `AudioEngine` as `SectionLooped(string sectionName)`.
- `PresenterIntegration.Initialize` subscribes it → `MidiPresenterSync.OnSectionLooped(name)` →
  existing `PresenterEngine.OnSectionLooped(name)`, which matches `_currentSlides` by `SectionName`
  using `OrdinalIgnoreCase` and `SetSlideIndex`es to that section's FIRST slide (reset, not advance).
- Failure mode: an unmatched section name is a **debug-log + no-op** (no slide change, no crash).
This activates the already-written-but-dead reset path.

### Fix F5 — Transpose displayed chords on pitch change
- Define the display contract: congregation output transposes chords by the current pitch-shift
  semitones **when chords are shown**; when chords are hidden, pitch shift has no visible effect
  (chords are stripped anyway). Stage/musician view already transposes via capo/nashville; extend it
  to also apply the live pitch-shift offset.

**Committed extracted method + convention (closes review Finding 3).** Do NOT invent a new method —
**reuse the existing `PresenterEngine.GetStageText(Slide slide, int transposeSemitones)`** (verified
present at `WorshipPlayer.Presenter/Services/PresenterEngine.cs`; it delegates to
`ChordProParserService.Transpose` and short-circuits to the untouched text when
`transposeSemitones == 0`). `PresenterEngine.cs` is compiled into `WorshipPlayer.Tests` via its
`<Compile Include ... Link="PresenterEngine.cs" />` entry (verified), so a unit test can call it
directly with no new csproj change. This is the single text-building method for the congregation
"chords shown" render path.

- **Transpose sign / compose convention (committed):** congregation output calls
  `GetStageText(slide, +pitchSemitones)` **ONLY when `ShowChords` is true**. A `+N`-semitone pitch
  shift transposes chords UP by `N` (positive = up). When `pitchSemitones == 0`, `GetStageText`
  returns the slide text unchanged (the method already short-circuits), so there is no visible change
  at pitch 0. When `ShowChords` is false, the output uses `GetCongregationText` (chords stripped) and
  the pitch offset has no effect. **Capo and Nashville remain stage/musician-view-only** — they are
  NOT composed into the congregation output; the congregation path applies the live pitch offset
  alone. (So on congregation output there is exactly one transform in play: `Transpose(text,
  +pitchSemitones)` gated by `ShowChords`.)

- Implementation: `OutputWindow` gains a `SetPitchShift(int semitones)` that stores the offset and
  re-renders the current slide; when `ShowChords` is true its render path routes the slide text
  through `GetStageText(slide, +pitchSemitones)` (reusing the engine method above) instead of a bare
  strip, so shown chords reflect the live key. `OperatorWindow.OnPitchShiftChanged` forwards the
  value to `_outputWindow` and triggers a re-render of the current slide. `PresenterIntegration`
  re-broadcasts stage data on pitch change so the web stage display updates.
- Behavior preserved when pitch is 0 (`GetStageText` returns text unchanged) and when chords are off.

### Fix F6 + F7 — Single-monitor degrade + index clamping
**Index clamping (F7):** On startup, after `PopulateDisplayManager`, **clamp** `OutputDisplay` and
`StageDisplay` to `[-1, monitorCount-1]`. If a saved index exceeds the available monitor count
(e.g. index 1 restored on a one-monitor machine), reset it to `-1` (unassigned) rather than leaving
it `>= 0` with null bounds, which currently produces the silent "None" no-output branch. Clamping
is a pure function (`ClampDisplayIndex(saved, monitorCount)`) and is unit-tested.

**Single-monitor Live UX (F6) — committed, single behavior:** when the operator presses Enter (Go
Live) and there is NO separate output monitor configured (`OutputDisplay < 0` OR only one monitor
is present), the output presents **full-screen on the sole monitor with `Topmost = false`**. The
existing `OutputWindow.GoFullScreen()` sets `Topmost = true`, which would permanently cover the
operator — so this path MUST set `Topmost = false` after going full-screen (either a parameter on
`GoFullScreen(bool topmost = true)` or an explicit `_outputWindow.Topmost = false` on the
single-monitor branch). With `Topmost = false`, Alt-Tab and the operator window's own activation
bring the command center back to the front; the operator drives slides from there while the
projector/clone shows the output.

- **`Esc` semantics on single monitor:** `Esc` must call `OutputWindow.ExitFullScreen()` (which
  already exists: it sets `Topmost = false`, `WindowState = Normal`, `WindowStyle =
  SingleBorderWindow`) so the operator fully reclaims the screen — NOT merely `ShowBlack()`, which
  only blanks content while leaving a borderless top window in place. `B` remains content-black
  (toggle) and does not change window chrome. (On a dual-monitor rig `Esc`/`B` keep their current
  meaning against the dedicated output monitor.)
- **Ownership of lazy creation:** `PresenterIntegration` owns creating the lazy `OutputWindow` on
  Go-Live for the single-monitor case (it already owns `_outputWindow`, `ApplyDisplaySettings`, and
  the VLC-StackOverflow avoidance comment). The newly created window MUST be registered through the
  SAME wiring `ApplyDisplaySettings` uses — `_operatorWindow?.SetOutputWindow(_outputWindow)` — so
  that `OperatorWindow.OnSlideChanged` forwards slides to it. Reuse the window if already created.
- Never create output eagerly at window open (keeps the VLC-StackOverflow avoidance from the
  current comment).
- All window placement is wrapped so a bad `DisplayInfo` cannot throw; fall back to the primary
  monitor.

### Fix F8 — Stage server binds on the LAN and fails gracefully (committed, single behavior)
**Decision 1 — listen target:** keep `ListenAnyIP(_port)`. Scope 5 requires LAN clients
(phone/tablet) to reach `/stage`, which a loopback-only bind would break; `ListenAnyIP` makes no
outbound connection, so it does not violate scope 7. The "bind loopback" language from the failure
note is **dropped**. (The machine's LAN IPv4 for the operator-facing URL/QR is discovered the same
way the current `/stage` QR already does; no change to that.)

**Decision 2 — port strategy:** keep the port **FIXED at `StageServerPort` (5055 default); do NOT
auto-fall-back to another port.** Rationale for a single offline machine: a stable URL/QR the
musicians already know beats silently moving the port. On a bind conflict the server is simply not
started and the service continues without it.

**Decision 3 — deterministic failure observation.** Replace the fire-and-forget start. Today
`StartAsync` does `_serverTask = _app.RunAsync(); return Task.CompletedTask;`, so a Kestrel bind
`SocketException`/`IOException` lands on `_serverTask` and the `try/catch` in
`StartStageServerAsync` never sees it. Change `StartAsync` to `await _app.StartAsync(ct)` inside a
`try/catch` (NOT `RunAsync`), so a bind failure is observed on the awaited call. `StartAsync`
returns a result the caller can act on. **Committed shape (so the verification plan is executable —
see Finding 1): make the bind outcome an explicit, extractable value type rather than relying on
hosting the server to observe it.** `StartAsync` returns a small `StageServerStartResult` record
(`bool Started`, `string? Error`) and also sets a `LastStartError` property; it never throws out of
the normal start path (the `try/catch` converts a bind exception into `Started = false` + the
message). `PresenterIntegration.StartStageServerAsync` inspects the result and turns a failure into
a **non-fatal, operator-visible status message** ("Stage display server could not start on port
5055 — the service will run without the second-screen/remote display") and continues. The live
congregation path (OutputWindow + slides) does NOT depend on the server starting.

**Testability note (ties to Finding 1):** the port-conflict decision ("is this port bindable?") is
factored into a tiny pure helper that does NOT require hosting Kestrel — e.g. a static
`StageServerPortProbe.TryReserve(int port, out string? error)` that attempts a `TcpListener` bind
on the loopback+any-IP target and reports success/failure. `StartAsync` consults it (or catches the
Kestrel bind exception and maps it to the same `StageServerStartResult`), but the DECISION logic is
unit-testable without a WebApplication.

**Decision 4 — WHERE `StageServerPortProbe` and `StageServerStartResult` live (committed, closes
review Finding 1).** Both types live in **`WorshipPlayer.Core`** (new source file(s), e.g.
`WorshipPlayer.Core/Services/Stage/StageServerPortProbe.cs` declaring the static probe and the
`StageServerStartResult` record `record StageServerStartResult(bool Started, string? Error)`). They
are NOT declared inside `StageDisplayServer.cs`. `StageDisplayServer` *consumes* both (it references
`WorshipPlayer.Core` already via the Presenter project), and `StartAsync` returns
`StageServerStartResult` and sets `LastStartError`. This placement is required because
`WorshipPlayer.Tests` references `WorshipPlayer.Core` directly (verified in
`WorshipPlayer.Tests.csproj`: single `<ProjectReference>` to `..\WorshipPlayer.Core\…`) but does
NOT compile `StageDisplayServer.cs` — its `<Compile Include>` set links only `ThemeManager`,
`MusicianViewRenderer`, `StageDisplayData`, `RemoteCommandHandler`, `PresenterEngine`,
`VideoLibrary`, `NdiOutputService`, `StubNdiOutput`, `TemplateService`, `AlertService`,
`CountdownService` (verified). Putting the two types in Core lets the F8 unit test see them WITHOUT
adding `StageDisplayServer.cs`/`StageDisplayHtml.cs`/`RemoteHtml.cs` or the ASP.NET Core hosting
stack to the Tests project.
- **csproj / warnings invariant:** because the types land in `WorshipPlayer.Core` (already
  referenced), the F8 test needs **no new `<Compile Include>` entry** in `WorshipPlayer.Tests.csproj`
  — the existing `<ProjectReference>` to Core is sufficient. If any `<Compile Include>` or csproj
  change is nonetheless introduced, it MUST keep the build at **0 new warnings (beyond the 4
  pre-existing)** and leave the full test suite green (411 + any new tests). The probe uses plain
  BCL `System.Net.Sockets.TcpListener`, which is cross-platform and adds no new warnings.

**Decision 5 — the `StartAsync` → `StageServerStartResult` path MUST survive the single-arg
constructor (committed, closes review Finding 2).** Production builds the server via
`new StageDisplayServer(StageServerPort)` (the single-arg constructor
`StageDisplayServer(int port = 5055, ChordProParserService? chordProParser = null)` — verified),
which leaves `_presenterEngine`, `_presenterSettings`, and `_remoteCommandHandler` null (they are
only set by the 4-arg constructor used in tests). Therefore the rewritten
`StartAsync`/`StageServerStartResult` path MUST NOT dereference `_presenterSettings` or
`_remoteCommandHandler` (nor `_presenterEngine`) on the start/observe path. The bind decision and
result mapping depend only on `_port`, the probe, and the Kestrel start outcome — none of the
nullable collaborator fields. (The remote endpoints already guard on `RemoteEnabled`/null and refuse
when the handler is absent, so the single-arg instance simply serves `/stage` without `/remote`.)

- Keep offline behavior: no outbound calls; serving `/stage` and `/remote` over the LAN is
  inbound-only. `RemoteEnabled` stays default-false (remote endpoints already refuse when disabled).

### Fix F9 + F10 — Defensive load + render
- Wrap **every** `BitmapImage` construction in `OutputWindow` in try/catch and skip the image on
  failure (log debug). There are three such sites today, all guarded only by `File.Exists`, none by
  try/catch: `ApplyTheme` background (`new BitmapImage(new Uri(theme.BackgroundImage))`), `ApplyTheme`
  logo (`new BitmapImage(new Uri(theme.LogoPath))`), and `LoadSlideIntoBuffer` foreground
  (`new BitmapImage(new Uri(slide.Content.ForegroundImage))`).
- **Named base for relative paths:** resolve any relative slide/theme image path against
  `AppDomain.CurrentDomain.BaseDirectory` — the same root `PresenterIntegration` already uses for
  `ServiceOrders`, `bible.db`, and `Media`. (The sample offering slide uses the relative
  `Resources/giving-qr-code.png`.) Absolute paths are used as-is. Stated once: the base is
  `AppDomain.CurrentDomain.BaseDirectory`, not the CWD and not the service-order file's directory.
- Harden `ServiceOrder`/`Song` consumption: null-guard `Slide.Content`, `Song.Sections`,
  `Song.Tracks`; `LoadAsync` callers handle null + `JsonException` with a user message. Add a
  normalization pass that fills missing collections with empties so downstream code never NREs.
- Confirm (already true) that `MainWindow.LoadSong` keeps its try/catch so missing stems degrade to a
  status message, not a crash.

### Fix F11 — Make manual mode coherent
- Once F1/F3 give items real slides, verify Space/Right/Left/B/Enter/Esc/1-9 drive the engine for a
  non-backtrack item. Ensure Enter/Esc work when `_outputWindow` is created lazily on Live (F6). No
  mapping change needed beyond confirming Esc=black and number-key jump, which already match.

### Fix F12 — Explicit, lock-based snapshot for backtrack→stage broadcast
Problem being pinned down: `BroadcastStageUpdate` runs on the NAudio render thread and reads TEN
live, mutable engine members in sequence (verified against `PresenterIntegration.BroadcastStageUpdate`):
`CurrentSlide.Content.Text` (→ `CurrentText`), `NextSlide.Content.Text` (→ `NextText`),
`CurrentSlide.SectionName` (→ `SectionName`), `CurrentSong.Title` (→ `SongTitle`),
`CurrentSong.OriginalKey` (read TWICE — once → `Key`, once → `OriginalKey`), `IsLive`, `IsBlacked`,
`CurrentSlideIndex`, and `TotalSlides` — while `SetSlideIndex`/`OnSectionChanged` on the same thread
(or a UI command on another) can mutate `_currentSlideIndex`, `_currentSlides`, and `_currentSong`
between reads. A plain struct copy still tears (index can advance between reading `CurrentSlide` and
`CurrentSlideIndex`; `_currentSong` can be swapped between the `Title` and `OriginalKey` reads).
"Snapshot atomically" must be made concrete.

**Committed mechanism (approach a, made precise):** keep `PresenterEngine` thread-agnostic, but give
it a single method that builds the snapshot UNDER A LOCK:

- Add `private readonly object _stateLock = new();` to `PresenterEngine`.
- Add `public StageDisplayData SnapshotStageData()` that, **inside `lock (_stateLock)`**, reads
  `_currentSlides`, `_currentSlideIndex`, `_currentSong`, `_isLive`, `_isBlacked` — **including
  `_currentSong.Title` and `_currentSong.OriginalKey` read under the SAME lock** (so `SongTitle`,
  `Key`, and `OriginalKey` cannot tear against a `_currentSong` swap) — and derives all TEN values:
  `CurrentText`/`NextText`/`SectionName`/`SongTitle`/`Key`/`OriginalKey`/`IsLive`/`IsBlacked`/
  `CurrentSlideIndex`/`TotalSlides` into **a freshly-constructed `StageDisplayData` snapshot — a brand-new instance
  populated entirely inside the lock and never mutated after the lock is released** — so the result
  is internally consistent (no torn index/text). (Note: `StageDisplayData.cs` is a plain mutable
  `{ get; set; }` DTO, so the correctness guarantee comes from building a fresh instance under the
  lock and not touching it afterward, NOT from any type-level immutability. Converting its setters to
  `init;` is an optional, non-required extra and would touch the serialization call sites, so it is
  not part of this fix.)
- The writers that mutate the snapshotted state — `SetSlideIndex`, `GenerateSlidesFromSong`'s
  assignment of `_currentSlides`/`_currentSlideIndex`, `OnServiceItemChanged`'s assignment, and
  `LoadSong`'s assignment of `_currentSong` (which `SongTitle`/`Key`/`OriginalKey` now read under
  the lock) — take the SAME `lock (_stateLock)` around the field writes. The `SlideChanged` event is raised OUTSIDE the lock
  (after releasing) to avoid holding the lock across subscriber callbacks.
- `PresenterIntegration.BroadcastStageUpdate` becomes: `var data = PresenterEngine.SnapshotStageData();
  _ = StageDisplayServer.BroadcastSlideUpdate(data);` — one locked read producing a fresh snapshot
  instance, then the existing async send of that snapshot (which no one mutates after it leaves the
  lock). No engine fields are read on the audio thread outside the lock.

This keeps the engine free of any UI/dispatcher dependency (so it stays unit-testable with no UI
thread) while removing the torn-read window. Testability: a Core/engine-level test drives a
sequence (section change → loop → position) and asserts each `SnapshotStageData()` is monotonic and
internally consistent (the text matches the index); because the mechanism is a plain lock with no UI
thread, this is a deterministic unit test.

---

## 4. Verification plan (per item)

Unit tests live in `WorshipPlayer.Tests` (cross-platform, xUnit) and must keep the existing 411
passing. Prefer engine/Core tests; UI/hardware items are explicit human checks.

- F1: Unit test `ServiceOrderManager.LoadAsync` against the real
  `ServiceOrders/2026-08-03_Sunday Morning.json` (copy into test content) → asserts 6 items, first
  item current, titles/types parsed. **Startup auto-load degrade (closes Finding 5):** human check —
  launch the app with the sample service present and the backtrack stems ABSENT (this worktree's
  state); confirm the service-order sidebar fills, the first item is set current, and a non-fatal
  "stems not found" status appears (via `MainWindow.LoadSong`'s existing try/catch) with NO startup
  error/crash. The `ServiceOrderLoaded` handler adds no throwing wrapper of its own around the song
  load. Human: launch app, Load Service, confirm sidebar fills.
- F2/F3: Unit test `PresenterEngine` with a Song ServiceItem that has pre-baked slides, then invoke
  `OnSongLoaded` with an empty-text `song.json` → assert (a) `_currentSlides` is still the item
  slides (not emptied), AND (b) **no duplicate `SlideChanged` fired** by `LoadSong` in the
  item-slides case (count the events; the only slide-0 emit comes from `OnServiceItemChanged`).
  Second test: a library song WITH `sections[].text` and NO item slides → assert fallback generation
  still produces slides and fires `SlideChanged` once. Assert slide `SectionName`s match the
  `song.json` section names for "Open The Heavens" (`OrdinalIgnoreCase`).
- F4: Unit test: raise the new mixer/engine loop signal for section "CHORUS 1" → assert
  `CurrentSlideIndex` returns to that section's FIRST slide and does not advance. Audio-thread
  behavior of the actual mixer loop is **human-on-hardware** (play the song, enable loop, watch slides
  reset).
- F5: Unit test `ChordProParserService.Transpose` already covers chord math; add an engine-level test
  (closes Finding 3) calling the committed, already-compilable method
  `PresenterEngine.GetStageText(slide, transposeSemitones)` directly (it is linked into the Tests
  project via `PresenterEngine.cs`): assert a slide whose text contains `[C]` returns `[D]` at
  `transposeSemitones = +2` (positive = up), and returns the ORIGINAL text unchanged at
  `transposeSemitones = 0`. This pins the sign and the pitch-0 no-op convention at the non-UI layer
  without any new csproj change. Human-on-hardware: shift pitch live, confirm projector chords change
  (and the lyric letters are unaffected) when `ShowChords` is on, and that pitch shift has no visible
  effect when `ShowChords` is off.
- F6/F7: Unit test the clamping function `ClampDisplayIndex(savedIndex, monitorCount)` (pure logic:
  index ≥ monitorCount → -1; in-range preserved; -1 preserved). Human-on-hardware: run on a
  one-monitor machine, GO LIVE, confirm full-screen output appears with `Topmost = false` so Alt-Tab
  returns to the operator, and `Esc` calls `ExitFullScreen()` (operator reclaims the screen, window
  chrome restored) while `B` only content-blacks; run with a saved index of 1 on one monitor and
  confirm it is clamped to -1 (no silent no-output branch).
- F8: **Unit test the extracted bind-outcome contract, NOT two live Kestrel servers.**
  `WorshipPlayer.Tests` is `net8.0` and cannot reference the `net8.0-windows` Presenter project; it
  compiles selected Presenter sources via `<Compile Include>`, and `StageDisplayServer.cs` (plus
  `StageDisplayHtml.cs`/`RemoteHtml.cs` and the ASP.NET Core hosting stack) are **not** in that set
  (verified in `WorshipPlayer.Tests.csproj`), so a drop-in two-server test will not compile.
  **Compile location (closes Finding 1):** because `StageServerPortProbe` and `StageServerStartResult`
  live in `WorshipPlayer.Core` (Fix F8 Decision 4) and the Tests project already has a
  `<ProjectReference>` to Core, these tests need **no new `<Compile Include>` entry** and add no new
  warnings. Instead:
  (a) occupy a port with a plain `System.Net.Sockets.TcpListener`, then assert
  `StageServerPortProbe.TryReserve(thatPort, out error)` returns `false` with a non-null `error`, and
  returns `true` for a known-free ephemeral port; and (b) assert the mapping into
  `StageServerStartResult` yields `Started = false` + a non-empty message on the busy-port path and
  `Started = true` on the free-port path. `TcpListener`/`StageServerPortProbe` are plain BCL/Core
  types, so this adds no Presenter/ASP.NET dependency to the test project and introduces no new
  warnings. The probe uses an ephemeral/caller-supplied port in the test (never the fixed production
  5055) so the test is not flaky when 5055 is busy on the build box; the fixed-port decision applies
  only to production start.
  **(c) Single-arg-constructor safety (closes Finding 2):** unit test that constructs the server via
  the single-arg `new StageDisplayServer(freePort)` (leaving `_presenterSettings`/
  `_remoteCommandHandler`/`_presenterEngine` null, as production does) and calls `StartAsync` →
  asserts it returns a clean `StageServerStartResult` (`Started = true` on a free port) with **no
  `NullReferenceException`**, proving the start/observe path does not dereference the null
  collaborators. NOTE: this (c) test, unlike (a)/(b), does touch `StageDisplayServer` itself; it is
  marked **human-on-hardware** if `StageDisplayServer.cs` cannot be reached from the Tests project
  without adding it (and the ASP.NET hosting stack) to `<Compile Include>` — which the no-new-warnings
  invariant forbids. The purely non-hosting assertion (no NRE on the null-collaborator start path) is
  the required outcome; the implementation step chooses whether to assert it via a Core-level seam or
  the human-on-hardware start.
  **Human-on-hardware:** start the app with port 5055 already taken, confirm the clear operator
  status message and that the service still runs without the stage/remote display (this exercises the
  real `StageDisplayServer.StartAsync` → `await _app.StartAsync` → `StageServerStartResult` path end
  to end, which the unit test does not host); and start the app normally (single-arg-constructed
  server) and confirm no NRE/crash on `StartAsync`.
- F9/F10: Unit tests for the JSON normalization/null-guards (malformed ServiceOrder → no throw,
  returns null + message path; song with null `Content`/`Sections`/`Tracks` → no NRE). Human: point a
  slide at a missing image, confirm render continues.
- F11: Unit test engine navigation (next/prev/jump/go-live/black) for a non-backtrack item. Human:
  keyboard walkthrough on the single machine.
- F12: Engine-level test driving a section→loop→position sequence against
  `PresenterEngine.SnapshotStageData()`, asserting each snapshot is internally consistent across all
  TEN members (the `CurrentText`/`SectionName` match the `CurrentSlideIndex`, and
  `SongTitle`/`Key`/`OriginalKey` all reflect the same `_currentSong` captured under the lock) and
  that indices progress monotonically within a section — all with no UI thread, since the mechanism
  is a plain `lock`. Human: watch for
  flicker/wrong-slide during a full run.
- Global: `dotnet build` must stay 0 errors / 4 warnings; full `dotnet test` must stay green
  (currently 411 passing). Any new tests add to that count.

**Items that ONLY the user can verify (Windows + real hardware, cannot be confirmed in this
environment):** actual second-monitor placement and full-screen output; audio playback and the audio
render-thread loop/section/pitch behavior (WASAPI + stems present); LibVLC video items; a real phone/
tablet connecting to `/stage` and `/remote` over the church LAN; projector color/scaling. The design
flags each above.

---

## 5. OUT OF SCOPE (restated — do NOT design or build)

Cloud sync; user accounts; CCLI SongSelect online import; Planning Center / ProPresenter import;
AirPlay/Miracast; billing. Also explicitly NOT in this hardening pass: adding new presenter features,
redesigning the UI, NDI real output (stub stays), or adding any network dependency to the live path.
Free CCLI tier only: offline Bible DB, no online SongSelect, no new outbound network calls.

Explicitly out of scope for F2/F3 (resolved, do not re-open): authoring lyrics into `song.json →
sections[].text` to feed `GenerateSlidesFromSong`. The committed model is "ServiceItem `slides[]`
are the single source of truth; `song.json` is timing only." Changing the lyric authoring model is
a data/content decision, not a reliability fix, and is not part of this hardening pass.

Also out of scope for F8 (resolved): auto port-fallback to an alternate port. The port is fixed at
`StageServerPort` (5055 default); on conflict the server is skipped with a clear message.

---

## 6. Decisions locked in this pass + data notes

Formerly-open questions are now resolved (see the review findings they close):
- **F2/F3 lyric source (closed):** ServiceItem `slides[]` is the single source of truth;
  `GenerateSlidesFromSong` is fallback only when the item has zero slides; `song.json` supplies
  timing only. The "author lyrics into song.json" alternative is out of scope (Section 5).
- **F8 port strategy (closed):** fixed port 5055, no fallback; graceful skip + operator message on
  conflict, observed via `await _app.StartAsync` (not fire-and-forget `RunAsync`), surfaced as a
  `StageServerStartResult` (`bool Started`, `string? Error`) the caller inspects. The bind decision
  is factored into a pure, host-free `StageServerPortProbe.TryReserve(port, out error)` so it is
  unit-testable in the cross-platform `WorshipPlayer.Tests` project without adding
  `StageDisplayServer.cs` / the ASP.NET Core hosting stack to that project's `<Compile Include>` set;
  the two-live-Kestrel conflict is a human-on-hardware check.
- **F8 bind target (closed):** keep `ListenAnyIP(_port)` so LAN clients reach `/stage`; this is
  inbound-only and does not violate scope 7's no-outbound rule.
- **F6 single-monitor UX (closed):** full-screen on the sole monitor with `Topmost = false`; `Esc` →
  `ExitFullScreen()` so the operator reclaims the screen; `PresenterIntegration` owns the lazy
  window creation and re-wires it via `SetOutputWindow`.
- **F12 threading (closed):** `PresenterEngine.SnapshotStageData()` builds **a freshly-constructed
  `StageDisplayData` snapshot (populated entirely inside the lock, never mutated after release)**
  under `_stateLock`; writers take the same lock; events raised outside the lock. (`StageDisplayData`
  itself is a mutable DTO; the guarantee is "new instance built under the lock," not type
  immutability.)

Data notes (worktree-scoped):
- `Songs/TheBlessing/`, `_song_registry.json`, and the actual mp3 stems named in
  `Open The Heavens/song.json` are absent from **this worktree's `Songs/` folder** (they exist in the
  main checkout). End-to-end audio verification requires the user to supply these on the service
  machine; the design must not assume they exist in CI.

---

## 7. Responses to design review (revision pass)

Review verdict was CHANGES_REQUESTED (6 MEDIUM, 4 NIT). All findings addressed:

- **Finding 1 (MEDIUM, F2/F3 either/or) — ADDRESSED.** Fix F2+F3 now commits unconditionally to
  "ServiceItem `slides[]` is the single source of truth; `GenerateSlidesFromSong` is fallback only."
  The "author lyrics into song.json" alternative is moved to an explicit OUT-OF-SCOPE note
  (Section 5) and the closed-decision list (Section 6).
- **Finding 2 (MEDIUM, F2/F3 precedence not precise) — ADDRESSED.** Fix F2+F3 now states the exact
  guard (`itemHasSlides = CurrentItem?.Slides?.Count > 0`), that `_currentSong`/pitch/section are set
  always, that `GenerateSlidesFromSong` + index reset run only when `!itemHasSlides`, and that
  `LoadSong` must not fire `SlideChanged` when it keeps item slides. The verification plan adds the
  "item slides survive `OnSongLoaded` with empty-text song, no duplicate `SlideChanged`" test.
- **Finding 3 (MEDIUM, F8 port fallback vs fixed) — ADDRESSED.** Fix F8 commits to a FIXED port
  (no fallback), with deterministic failure observation via `await _app.StartAsync` and a non-fatal
  operator message. Recorded as closed in Sections 5 and 6.
- **Finding 4 (MEDIUM, F8 bind contradiction) — ADDRESSED.** Both Section 2 F8 and Fix F8 now keep
  `ListenAnyIP(_port)` (LAN clients require it; inbound-only, so scope 7 is not violated) and the
  "bind loopback" language is dropped.
- **Finding 5 (MEDIUM, F6 single-monitor under-specified) — ADDRESSED.** Fix F6+F7 now specifies
  the single-monitor UX (full-screen with `Topmost = false`), that `PresenterIntegration` owns lazy
  window creation and re-wires via `SetOutputWindow`, and that `Esc` calls `ExitFullScreen()` (which
  already exists in `OutputWindow`) so the operator reclaims the screen.
- **Finding 6 (MEDIUM, F12 snapshot not concrete) — ADDRESSED.** Fix F12 now defines
  `PresenterEngine.SnapshotStageData()` building a freshly-constructed `StageDisplayData` snapshot
  (populated entirely inside `_stateLock`, never mutated after release) under `_stateLock`, with
  writers taking the same lock and events raised outside it, plus the ordering/consistency test.
- **Finding 7 (NIT, F4 payload name + both branches) — ADDRESSED.** Fix F4 now requires the
  `SectionLooped` payload to carry `Song.Sections[_currentSectionIndex].Name`, name-matching
  `OrdinalIgnoreCase`, unmatched = debug-log no-op, and the event raised in BOTH
  `HandleSectionBoundary` and `CheckSectionBoundary` loop branches.
- **Finding 8 (NIT, F9/F10 base dir) — ADDRESSED.** Fix F9+F10 now names
  `AppDomain.CurrentDomain.BaseDirectory` as the base and lists the three `BitmapImage` sites to wrap
  in try/catch.
- **Finding 9 (NIT, F11 "EXCEPT") — ADDRESSED.** Section 2 F11 reworded to "the mapping matches the
  required map in full; no remap needed," keeping the real downstream points (slides via F1/F3,
  Enter/Esc depend on `_outputWindow`, tying to Finding 5).
- **Finding 10 (NIT, worktree-scoped data note) — ADDRESSED.** Section 1.2 and Section 6 now say
  "in this worktree's `Songs/` folder" and note the files exist in the main checkout.

### Second review pass (verdict CHANGES_REQUESTED — 1 MEDIUM, 2 NIT) — all addressed

- **Finding 1 (MEDIUM, F8 verification not executable as written) — ADDRESSED (Option A).** Verified
  against `WorshipPlayer.Tests.csproj`: it is `net8.0`, references only `WorshipPlayer.Core`, and its
  `<Compile Include>` set links `ThemeManager`, `MusicianViewRenderer`, `StageDisplayData`,
  `RemoteCommandHandler`, `PresenterEngine`, `VideoLibrary`, `NdiOutputService`, `StubNdiOutput`,
  `TemplateService`, `AlertService`, `CountdownService` — `StageDisplayServer.cs`,
  `StageDisplayHtml.cs`, and `RemoteHtml.cs` are NOT present, and the project has no ASP.NET Core
  hosting reference, so a two-live-Kestrel test cannot compile as drop-in. Chose the review's
  preferred Option A: Fix F8 now factors the bind decision into a host-free
  `StageServerPortProbe.TryReserve(port, out error)` and a `StageServerStartResult` value type; the
  F8 verification plan unit-tests the probe + result mapping (occupy a port with a `TcpListener`,
  assert `TryReserve` fails with a message, assert free-port success) using an ephemeral/caller-
  supplied port so the test is not flaky when 5055 is busy, and explicitly marks the real two-server
  port-conflict run as **human-on-hardware**. No Presenter/ASP.NET source is added to the Tests
  project, preserving the "no new warnings / 411 green" invariant. The design no longer presents a
  two-live-server test as drop-in. (Sections "Fix F8", "4. Verification plan → F8", "6".)
- **Finding 2 (NIT, F12 "immutable" mislabeled) — ADDRESSED.** Verified `StageDisplayData.cs` is a
  plain mutable `{ get; set; }` DTO. Replaced "immutable `StageDisplayData`" everywhere (Fix F12,
  the `BroadcastStageUpdate` becomes-line, Section 6, and the prior-pass Finding 6 response) with "a
  freshly-constructed `StageDisplayData` snapshot populated entirely inside the lock and never
  mutated after release," and added an explicit note that the correctness rests on the fresh-instance
  construction, not on type immutability (with `init;` setters noted as an optional, non-required
  extra that would touch serialization call sites).
- **Finding 3 (NIT, F11 "no remap needed" vs F6 Esc change) — ADDRESSED.** Section 2 F11 now states
  the key TABLE is complete and unchanged, and calls out the one behavioral change explicitly: `Esc`
  on the single-monitor branch is re-routed by Fix F6 from `GoBlack()` (current `Window_KeyDown`
  behavior, verified) to `ExitFullScreen()`, while the dual-monitor rig keeps `Esc` = content black.

### Third review pass (verdict CHANGES_REQUESTED — 3 MEDIUM, 2 NIT) — all 5 resolved (design APPROVED)

All five open items were specification clarifications, not design errors. Each is resolved as a
committed decision edited into the relevant Fix section and the verification plan; verified against
the real worktree source before committing.

- **Finding 1 (MEDIUM, F8 — compile location of `StageServerPortProbe`/`StageServerStartResult`) —
  RESOLVED.** Fix F8 "Decision 4" commits both types to **`WorshipPlayer.Core`** (not inside
  `StageDisplayServer.cs`); `StageDisplayServer` consumes them. Verified `WorshipPlayer.Tests.csproj`
  has a single `<ProjectReference>` to `WorshipPlayer.Core` and its `<Compile Include>` set does NOT
  include `StageDisplayServer.cs`, so the Core placement makes the F8 test compile with **no new
  `<Compile Include>` entry**; any csproj change must keep 0 new warnings (beyond the 4 pre-existing)
  and all tests green. (Fix F8 Decision 4; Verification plan F8 compile-location note.)
- **Finding 2 (MEDIUM, F8 — null `_presenterSettings`/`_remoteCommandHandler` on single-arg ctor) —
  RESOLVED.** Fix F8 "Decision 5" forbids the `StartAsync` → `StageServerStartResult` path from
  dereferencing `_presenterSettings`, `_remoteCommandHandler`, or `_presenterEngine` (all null on the
  single-arg `new StageDisplayServer(port)` production uses — verified the ctor
  `StageDisplayServer(int port = 5055, …)`). Verification plan F8 (c) adds a unit test that calls
  `StartAsync` on a single-arg-constructed server and asserts a clean `StageServerStartResult` with
  no `NullReferenceException`.
- **Finding 3 (MEDIUM, F5 — named extractable transpose method + convention) — RESOLVED.** Fix F5
  commits to reusing the existing, Tests-compilable
  `PresenterEngine.GetStageText(Slide slide, int transposeSemitones)` (verified present and linked
  into the Tests project). Convention committed: congregation output applies
  `Transpose(text, +pitchSemitones)` ONLY when `ShowChords` is true; `+N` = up; pitch 0 returns text
  unchanged; capo/nashville stay stage-only. Verification plan F5 adds the concrete assertion
  `[C]` at `+2` → `[D]` and `0` → unchanged.
- **Finding 4 (NIT, F12 — snapshot member count) — RESOLVED.** Fix F12 now lists the TEN members
  actually read by `BroadcastStageUpdate` (verified): `CurrentSlide.Content.Text`,
  `NextSlide.Content.Text`, `CurrentSlide.SectionName`, `CurrentSong.Title`, `CurrentSong.OriginalKey`
  (read twice — `Key` and `OriginalKey`), `IsLive`, `IsBlacked`, `CurrentSlideIndex`, `TotalSlides`.
  `SnapshotStageData()` reads `_currentSong.Title`/`_currentSong.OriginalKey` under the SAME
  `_stateLock`, and `LoadSong`'s `_currentSong` write takes that lock.
- **Finding 5 (NIT, F1 — startup auto-load × backtrack stem preload) — RESOLVED.** Fix F1 commits
  that `ServiceOrderLoaded` setting the first item current may trigger a backtrack
  `SongLoadRequested`/stem preload, which degrades SILENTLY through `MainWindow.LoadSong`'s existing
  try/catch (non-fatal "stems not found" status) and does NOT error on startup; the handler adds no
  throwing wrapper of its own. Verification plan F1 adds the human startup-degrade check.
