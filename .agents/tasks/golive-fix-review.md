# GO LIVE single-monitor fix: hide-while-live + dispose-on-close

The change fixes two linked single-monitor defects that fired when the operator closed the full-screen OutputWindow while LIVE: GO LIVE could no longer reopen the output, and the orphaned window's LibVLC motion-background loop leaked native decode memory (184MB → ~2GB). It applies the report's recommended Fix 1b + Fix 2 combination: GO LIVE/Enter now always invoke the single-monitor lazy-create hook before the engine-state guard; OutputWindow intercepts a close while LIVE and hides instead of destroying (gated by a host-settable `SuppressDestroyWhileLive`); and a new `OnClosed` disposes both video services, stops the alert timer, and unsubscribes PlaybackEnded on a genuine close. PresenterIntegration drives the flag true on the single-monitor GO LIVE path and false on reconfigure/None/Dispose, and OperatorWindow stops OutputCaptureService on close so NDI can't run against an orphan.

Watch for: nothing blocking. The flag lifecycle, lazy-creation preservation, and dual-monitor/Esc/B semantics all hold up against the report. (confirmed)

**Verdict**: APPROVED

## High-level view

The reopen fix moves `_onGoLive?.Invoke()` ahead of the `IsBlacked || !IsLive` guard in both `GoLive_Click` and the `Key.Enter` branch, while `_engine.GoLive()` stays behind that guard and `_outputWindow?.Unblack()` runs unconditionally afterward. Because `IsLive` stays true after the first GO LIVE, the old placement meant the hook was never reached again; running it first lets `HandleGoLiveSingleMonitor` recreate or re-show the closed window and refresh the operator's `_outputWindow` reference via `SetOutputWindow`.

The leak/orphan fix uses hide-while-live. `OutputWindow.OnClosing` cancels the close and hides when `SuppressDestroyWhileLive` is set, so the single instance and its two LibVLC services survive and GO LIVE just re-shows them. On a genuine (non-live) close or app shutdown the flag is false, the window closes for real, and `OnClosed` disposes both video services (which clears `_shouldLoop` and unsubscribes `EndReached`), stops the alert timer, and unsubscribes PlaybackEnded.

The flag is owned by the host. `HandleGoLiveSingleMonitor` sets it true; `ApplyDisplaySettings` (dual-monitor and None paths) and `Dispose` set it false before closing. That keeps dual-monitor close semantics and app-shutdown disposal intact while only the single-monitor live path gets hide-on-close.

Lazy creation is preserved: `OutputWindow` is still constructed only inside `ApplyDisplaySettings` (on a settings change to a configured display) and `HandleGoLiveSingleMonitor` (on GO LIVE), never eagerly at app open. Esc still reclaims the screen on single-monitor full-screen and keeps its content-black meaning on dual; B still blacks. Neither path was touched.

<details>
<summary>Issues (0)</summary>

No blocking or non-blocking action items. The implementation matches the report's recommended fix.

</details>

<details>
<summary>Details</summary>

## Reopen path: hook before the guard

Both entry points now call `_onGoLive?.Invoke()` unconditionally, then keep `_engine.GoLive()` behind `IsBlacked || !IsLive`, then call `_outputWindow?.Unblack()`. This is exactly the report's Fix 1a shape layered under Fix 1b. The key behavioral correction is that after the first GO LIVE the engine sits at `IsLive=true, IsBlacked=false`, so the old guard-gated hook was unreachable on a second press; the window the user closed never came back. Running the hook first means `HandleGoLiveSingleMonitor` re-shows the hidden instance (or recreates it if it was genuinely gone) and pushes the fresh reference back into the operator window through `SetOutputWindow`, so slides forward to a live target rather than a dead one.

The `Key.Enter` branch is restructured to `if (_engine != null) { _onGoLive?.Invoke(); if (IsBlacked || !IsLive) GoLive(); Unblack(); }`, matching the button. The earlier null-conditional `_engine?.GoLive()` is now a plain call inside the guarded block, which is safe because the outer `_engine != null` check dominates it.

## Hide-while-live via SuppressDestroyWhileLive

`OnClosing` cancels and hides when the flag is set; otherwise it defers to base. Because the close is cancelled, the `Closed += (s,e) => _outputWindow = null` handler the host attached does not fire, so the host keeps its single instance and GO LIVE re-shows the same window with its LibVLC services already initialized — no re-init churn on reopen, and RAM stays bounded.

The flag defaults to false (auto-property, no initializer), so a window created and shown via a dual-monitor `ApplyDisplaySettings` destroys normally on close exactly as before. `ApplyDisplaySettings` also sets it false explicitly on both the configured-display branch and the None teardown branch, and `Dispose` sets it false before `Close()`. So the only state in which a close hides is single-monitor GO LIVE, and every genuine-teardown path resets it so `OnClosed` disposal actually runs.

## Teardown on genuine close

`OnClosed` stops `_alertTimer`, unsubscribes `OnVideoPlaybackEnded`, and disposes both video services. Confirmed in `VideoBackgroundService.Dispose()`: it calls `StopBackground()` which sets `_shouldLoop = false` and removes the `EndReached` handler before stopping, then disposes the MediaPlayer and LibVLC. That severs the `OnMediaEndReached` self-restart loop described in the report — the loop early-returns on `!_shouldLoop` and the handler is detached — so the native decoder and per-restart `Media` allocations stop. This is the direct remedy for the multi-GB climb.

## NDI capture on orphaned output

`OperatorWindow`'s cleanup now calls `_outputCaptureService.Stop()` before unsubscribing `OnFrameCaptured`, so the up-to-60-FPS `RenderTargetBitmap` capture timer can't keep running against an output that's going away. This closes the report's secondary (NDI-only) leak contributor.

## Dual-monitor and Esc/B preserved

Esc and B handling in `Window_KeyDown` are unchanged: Esc still reclaims the screen via `ExitFullScreen` on a single-monitor full-screen output and otherwise keeps content-black; B still calls `GoBlack()`. The dual-monitor path runs through `ApplyDisplaySettings`, which now only adds the `SuppressDestroyWhileLive = false` assignment and leaves placement/show logic intact. No other topology behavior moved.

## Scope and verification

The diff touches three files and 87 insertions, all on the two reported defects — no unrelated refactors. The commit message records `dotnet build WorshipBacktrackPlayer.sln` at 0 errors with the four pre-existing warnings, and `dotnet test WorshipPlayer.Tests` at 432 passed / 0 failed. Per the review mandate this evidence is accepted as recorded; no suite was re-run. There are no automated tests exercising WPF window close/hide lifecycle, which is inherent to the UI layer and not a regression introduced here.

</details>

<details>
<summary>File map</summary>

- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — hook moved before guard in GO LIVE click and Enter; Stop() OutputCaptureService on cleanup.
- `WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs` — new `SuppressDestroyWhileLive` flag, `OnClosing` hide-while-live, `OnClosed` dispose/stop/unsubscribe.
- `WorshipPlayer.UI/Services/PresenterIntegration.cs` — set flag true on single-monitor GO LIVE; false on configured/None reconfigure and on Dispose.

Full diff: `git show HEAD`.

</details>
