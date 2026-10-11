# Design Review — Presenter Offline Hardening

Reviewer: design-review subagent (fresh read, no authoring context).
Scope reviewed: `design.md` for the OWC Presenter offline-hardening task.
Method: every load-bearing claim in `design.md` was checked against the actual worktree source
at `.worktrees/presenter-offline-hardening`. Files read in full or grepped: `ServiceOrderManager.cs`,
`PresenterEngine.cs`, `StageDisplayServer.cs`, `OutputWindow.xaml.cs`, `PresenterIntegration.cs`,
`MultiTrackMixer.cs`, `OperatorWindow.xaml.cs`, `StageDisplayData.cs`, `PresenterSettings.cs`,
`WorshipPlayer.Tests.csproj`.

Overall: this is a strong, code-grounded design that has already absorbed two prior review passes.
The live-service path is traced with concrete file/function references that I independently
confirmed. The failure enumeration (F1–F12) is real and each item maps to a Priority-1 scope item.
The verification plan distinguishes unit-testable logic from human-on-hardware checks. The findings
below are the residual issues a fresh read surfaces; none overturn the core analysis, but two are
blocking because they would leave a coder guessing or produce a non-compiling test.

---

## Findings

### 1. [MEDIUM] F8 verification: where do `StageServerPortProbe` / `StageServerStartResult` live? The test cannot see them yet.
The F8 fix and verification plan correctly establish that `WorshipPlayer.Tests` is `net8.0`,
references only `WorshipPlayer.Core`, and its `<Compile Include>` set does **not** contain
`StageDisplayServer.cs` (I verified this against `WorshipPlayer.Tests.csproj` — the set is exactly
ThemeManager, MusicianViewRenderer, StageDisplayData, RemoteCommandHandler, PresenterEngine,
VideoLibrary, NdiOutputService, StubNdiOutput, TemplateService, AlertService, CountdownService).
The design then says the probe/result types make the bind decision "unit-testable in the
cross-platform `WorshipPlayer.Tests` project without adding `StageDisplayServer.cs`." But it never
states **which file** `StageServerPortProbe` and `StageServerStartResult` are declared in. If they
are declared inside `StageDisplayServer.cs` (the natural place, since `StartAsync` consults them),
the test still cannot compile against them — the exact problem the design set out to avoid. The
design is internally silent on this, so a coder could re-create the non-compiling situation.

Concrete fix: state explicitly that `StageServerPortProbe` (and the `StageServerStartResult`
record) live in their **own new source file(s)** that are added to the Tests project's
`<Compile Include>` set (e.g. `..\WorshipPlayer.Presenter\Services\StageServerPortProbe.cs` and
`StageServerStartResult.cs`, each `Link=`'d like the others), OR are placed in `WorshipPlayer.Core`
(which the test already references). Either removes the ambiguity; pick one in the design. Also note
that adding a new `<Compile Include>` entry is a change to the test project file — call that out so
the "no new warnings / 411 green" invariant is checked against the new file set.

### 2. [MEDIUM] F8 regression risk: the single-arg `StageDisplayServer` constructor is what production uses, so `StartAsync`'s new result path must not depend on the remote-handler constructor.
`PresenterIntegration` constructs the server with `new StageDisplayServer(_presenterSettings.StageServerPort)`
(verified) — the single-arg overload, which leaves `_remoteCommandHandler` and `_presenterSettings`
**null**. The design's F8 rewrite changes `StartAsync` and introduces `StageServerStartResult` /
`LastStartError`, and `StartStageServerAsync` is to "inspect the result." The design does not state
that the new start-result logic must work on the single-arg-constructed instance (the only one
production actually creates). If the new code reads `_presenterSettings` or `_remoteCommandHandler`
during start/observe, it will NRE in production even though a test that uses the 4-arg constructor
passes.

Concrete fix: add an explicit constraint to Fix F8: "The `StartAsync` → `StageServerStartResult`
path must not dereference `_presenterSettings` or `_remoteCommandHandler` (both may be null on the
single-arg constructor that `PresenterIntegration` uses). Add a unit test that calls `StartAsync`
on a server built with the single-arg constructor and asserts a clean `StageServerStartResult`
(no NRE)." This closes a gap the current tests would miss.

### 3. [MEDIUM] F5 verification is under-specified: the "shown chords use current pitch offset" assertion needs a named, extractable method and a defined relationship between pitch-shift sign and transpose direction.
I confirmed `OutputWindow.LoadSlideIntoBuffer` today does `_showChords ? slide.Content.Text :
_chordParser.StripChords(...)` and never calls `Transpose` — so F5's problem statement is correct.
The fix adds `OutputWindow.SetPitchShift(int)` and routes through `_chordParser.Transpose`. But the
verification plan says "extract the text-building into a testable method" without naming it or
committing to a signature, and it does not state the sign convention: does a +2 semitone pitch
shift transpose chords **up** by 2 (`Transpose(text, +2)`), and is capo/nashville applied before or
after the pitch offset on the congregation output? The engine already has `GetStageText(slide,
transposeSemitones)` which does exactly `Transpose(text, transposeSemitones)`; the design should say
whether the output reuses that or duplicates the logic, and in which order offsets compose.

Concrete fix: in Fix F5, name the extracted method and its signature (e.g.
`internal static string BuildDisplayText(Slide slide, bool showChords, int pitchSemitones,
ChordProParserService parser)` living where the Tests project can compile it, or reuse
`PresenterEngine.GetStageText`), and state the convention: "congregation output applies
`Transpose(text, +pitchSemitones)` only when `ShowChords` is true; pitch 0 returns text unchanged;
congregation output does not apply capo/nashville (those remain stage-only)." Then the unit test can
assert a concrete input/output pair (e.g. a `[C]` lyric at +2 renders `[D]`).

### 4. [NIT] Snapshot field-count mismatch between the F12 narrative and the actual broadcast.
Fix F12 says `BroadcastStageUpdate` "reads SEVEN live, mutable engine members." The actual
`PresenterIntegration.BroadcastStageUpdate` constructs `StageDisplayData` from ten reads
(`CurrentSlide.Content.Text`, `NextSlide.Content.Text`, `CurrentSlide.SectionName`,
`CurrentSong.Title`, `CurrentSong.OriginalKey` twice, `IsLive`, `IsBlacked`, `CurrentSlideIndex`,
`TotalSlides`). The torn-read concern is valid regardless, but the count is wrong and the fix should
list the real members so `SnapshotStageData()` covers all of them (notably `CurrentSong.Title` and
`OriginalKey`, which the "seven" framing omits).

Concrete fix: update the F12 member list to the ten actually read, and have `SnapshotStageData()`
derive `SongTitle`/`Key`/`OriginalKey` from `_currentSong` under the same lock.

### 5. [NIT] F1 auto-load-most-recent interacts with the `ServiceItemType.Song` auto-load path and should state the expected startup side effects.
Fix F1 adds "auto-load the most recent file in `ServiceOrders/` on startup." I confirmed that when
an item becomes current, `PresenterIntegration.OnServiceItemChanged` fires `SongLoadRequested` for
a `Song` item with `UseBacktrack`, and `ServiceOrderManager.LoadAsync` sets `CurrentItemIndex = 0`
and fires `ServiceOrderLoaded` (not `ItemChanged`). So auto-loading a service whose first item is a
backtrack song will — via the `ServiceOrderLoaded`→"set first item current" step the design adds —
trigger `ItemChanged`, which will fire `SongLoadRequested` and try to preload stems that this
worktree's `Songs/` folder does not contain (design Section 6 data note). The design should state
that startup auto-load must not auto-trigger a stem preload/crash, or must degrade silently.

Concrete fix: in Fix F1, specify that `ServiceOrderLoaded` handling sets the first item current but
that any resulting `SongLoadRequested`/stem load is wrapped by `MainWindow.LoadSong`'s existing
try/catch (already verified to degrade to a status message), and add a line to the F1 human check:
"on startup with the sample service and missing stems, the app loads the sidebar and shows a
non-fatal 'stems not found' status rather than erroring."

---

## Verified Assumptions (checked against source, found accurate)

1. **F1 — `ServiceOrderManager.LoadAsync` has no production caller, and `LoadService_Click`
   discards the chosen file.** Confirmed: a worktree grep for `LoadAsync` shows only the definition
   (plus unrelated `SettingsService.LoadAsync`); `OperatorWindow.LoadService_Click` opens an
   `OpenFileDialog` and on OK runs only the comment `// Engine will load and fire events`.
2. **F2/F3 — slide-source conflict is real.** `PresenterEngine.OnServiceItemChanged` sets
   `_currentSlides = e.NewItem.Slides` (index 0, fires `SlideChanged`); `LoadSong` →
   `GenerateSlidesFromSong` resets `_currentSlides = new()` and `continue`s on blank
   `section.Text`, then `LoadSong` fires `SlideChanged` for slide 0 again. The overwrite and the
   duplicate-emit are both present exactly as described, and the proposed `itemHasSlides` guard maps
   cleanly onto this code.
3. **F4 — no `SectionLooped` signal exists; both mixer loop branches are silent.** Confirmed:
   `SectionLooped` appears nowhere as an event; only the dead handlers
   `MidiPresenterSync.OnSectionLooped` and `PresenterEngine.OnSectionLooped` exist.
   `MultiTrackMixer.HandleSectionBoundary` (the `if (LoopEnabled && LoopInfinite)` branch) and
   `CheckSectionBoundary` (the identical fallback branch) both `SeekToMsWithVocalPickup` +
   `_loopIterations++` and raise no event. The requirement to raise in BOTH is correct and necessary.
4. **F5 — pitch shift never transposes congregation output.** Confirmed: `OutputWindow` has no
   `SetPitchShift`; `LoadSlideIntoBuffer` only strips-or-keeps chords and never calls `Transpose`.
   `PresenterIntegration.OnPitchShiftChanged` forwards to `MidiPresenterSync` only and does not
   re-broadcast stage data.
5. **F6/F7 — single-monitor default yields no output; no index clamping.** Confirmed: `OutputWindow`
   is created only by `ApplyDisplaySettings` when `settings.OutputDisplay >= 0 && GetOutputBounds()
   != null`; `PresenterSettings.OutputDisplay` and `StageDisplay` both default to `-1`;
   `OpenPresenterWindow` deliberately does not create output (VLC StackOverflow comment present).
   `GoFullScreen()` sets `Topmost = true`; `ExitFullScreen()` exists and sets `Topmost = false`,
   `WindowState = Normal`, `WindowStyle = SingleBorderWindow` — matching the fix's assumptions.
6. **F8 — fire-and-forget start swallows bind failures; `ListenAnyIP` is used.**
   Confirmed: `StageDisplayServer.StartAsync` does `builder.WebHost.ConfigureKestrel(o =>
   o.ListenAnyIP(_port))`, then `_serverTask = _app.RunAsync(); return Task.CompletedTask;`.
   `PresenterIntegration.StartStageServerAsync` wraps `await StartAsync()` in try/catch that cannot
   observe the background `_serverTask` failure. Port default 5055, `StageServerEnabled` default
   true. `ListenAnyIP` is inbound-only (no outbound), so it does not violate scope 7.
7. **F9 — three `BitmapImage` sites guarded only by `File.Exists`, no try/catch.** Confirmed in
   `OutputWindow`: `ApplyTheme` background, `ApplyTheme` logo, and `LoadSlideIntoBuffer` foreground.
8. **F11 — key table matches the required map and Esc currently = content black.** Confirmed in
   `OperatorWindow.Window_KeyDown`: Right/Space/PageDown=next, Left/PageUp=prev, Down/Up=item,
   B=black, F1=logo, Esc→`GoBlack()`, Enter=live, D1–D9=jump. So the F6 Esc re-route to
   `ExitFullScreen()` on the single-monitor branch is a genuine (and correctly flagged) behavior
   change.
9. **F12 — `StageDisplayData` is a mutable `{ get; set; }` DTO, not immutable.** Confirmed; the
   design's second-pass correction (fresh-instance-under-lock, not type immutability) is accurate.
10. **F8 testability — test project cannot reference the Presenter project or host Kestrel.**
    Confirmed: `WorshipPlayer.Tests.csproj` is `net8.0`, references only `WorshipPlayer.Core`, and
    its `<Compile Include>` set excludes `StageDisplayServer.cs`/`StageDisplayHtml.cs`/`RemoteHtml.cs`
    and has no ASP.NET Core hosting reference.
11. **Scope 7 (no network on live path).** Consistent with the design: the live path
    (OutputWindow + slides + ServiceOrder/Song load + section/pitch) makes no outbound calls; the
    only inbound serving is the LAN stage/remote server, which is scope-5 behavior.
12. **Windows/hardware items are flagged.** The design marks second-monitor placement, WASAPI
    audio + loop/section/pitch render-thread behavior, LibVLC video, and real phone/tablet LAN
    connections as human-on-hardware, which is appropriate; the build baseline (0 errors / 4
    warnings) and 411-test invariant are restated.

## Unverified / Wrong Assumptions

- **F12 "SEVEN members" (WRONG — minor).** The broadcast reads ten members, not seven (see Finding
  4). The conclusion (torn read possible) stands; the enumeration is inaccurate.
- **Build baseline "0 errors / 4 pre-existing warnings" and "411 tests green" (UNVERIFIED by this
  reviewer).** I did not run `dotnet build` or `dotnet test` in this review. The design asserts the
  baseline and I have no reason to doubt it, but it is not independently confirmed here. The
  invariant should be re-checked by the implementer after each change, especially after editing
  `WorshipPlayer.Tests.csproj` (Finding 1).
- **"The sample `2026-08-03_Sunday Morning.json` has lyric-bearing `items[].slides[]` with
  `sectionName` 'VERSE 1'/'CHORUS 1' and `timestamp_ms`" (UNVERIFIED by this reviewer).** The F2/F3
  decision rests on this data shape. I verified the code paths that consume it but did not open the
  sample service JSON to confirm the slide/section names and timestamps exist as described. The
  implementer should confirm the sample file matches before relying on the `itemHasSlides` guard and
  the `OrdinalIgnoreCase` section-name match for "Open The Heavens."
- **`StageServerPortProbe`/`StageServerStartResult` compile location (UNRESOLVED in the design).**
  See Finding 1 — the design does not say where these types live, so their testability claim is not
  yet verifiable.

---

## Verdict

Count of blocking findings: 3 MEDIUM (Findings 1, 2, 3), 2 NIT (Findings 4, 5).
Blocking (HIGH + MEDIUM) > 0 → **CHANGES_REQUESTED**.

The design is close. The three MEDIUM items are narrow and quick to resolve: name the compile
location of the F8 probe/result types (Finding 1), constrain the F8 start-result path against the
null-field single-arg constructor production actually uses (Finding 2), and pin the F5 extracted
method signature plus transpose sign/order convention (Finding 3). The two NITs tighten the F12
member list and the F1 startup auto-load side effects. Resolving these makes every P1 item both
unambiguous and verifiable.
