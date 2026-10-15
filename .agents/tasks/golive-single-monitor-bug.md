# GO LIVE single-monitor bug — root cause & fix

## Summary (answer first)

Two independent defects combine into the reported symptom. Both are triggered by **closing the OutputWindow (clicking its [X]) while the service is LIVE** on a single-monitor machine.

1. **Cannot reopen the output.** `GoLive_Click` / Enter only calls the single-monitor "lazy create + full-screen" hook when `IsBlacked || !IsLive`. After GO LIVE the engine state is `IsLive = true, IsBlacked = false`, so pressing GO LIVE / LIVE again fails that guard and `HandleGoLiveSingleMonitor()` is never invoked — the window that the user closed is never recreated. The recreation logic itself is fine; it is simply never reached. Also, `OperatorWindow._outputWindow` is left as a stale/dead reference because nothing refreshes it after the window closes.

2. **~2 GB RAM growth.** `OutputWindow` has **no `Closed`/`OnClosed` handler**. When the user closes the window, its two `LibVLC` instances (`VideoBackgroundService` + `VideoPlaybackService`) are **never disposed**, and if a motion-background video was playing, its `EndReached` self-restart loop keeps re-creating `Media` objects and decoding frames forever on a now-invisible, dereferenced window. The native VLC decoder plus the per-restart `Media` allocations are the most likely source of the multi-GB climb. (A closed-but-undisposed `OutputCaptureService`/NDI `RenderTargetBitmap` loop would do the same, but that only runs if NDI was toggled on.)

---

## Evidence

### Problem 1 — output cannot be reopened while LIVE

`WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`, `GoLive_Click`:

```csharp
private void GoLive_Click(object sender, RoutedEventArgs e)
{
    if (_engine == null) return;
    if (_engine.IsBlacked || !_engine.IsLive)   // <-- guard
    {
        _onGoLive?.Invoke();                     // lazy create + full-screen
        _engine.GoLive();
        _outputWindow?.Unblack();
    }
}
```

The Enter-key path in `Window_KeyDown` (`case Key.Enter:`) has the identical guard `_engine?.IsBlacked == true || !(_engine?.IsLive ?? false)`.

- `PresenterEngine.GoLive()` sets `_isLive = true; _isBlacked = false;` and never resets them when the window closes (`WorshipPlayer.Presenter/Services/PresenterEngine.cs`, `GoLive`). There is no code path that flips `IsLive` back to false on output-window close.
- So after the GO-LIVE → close sequence: `IsLive == true`, `IsBlacked == false` → the guard `(IsBlacked || !IsLive)` is `false` → `_onGoLive` (i.e. `HandleGoLiveSingleMonitor`) is **not** called → the window is not recreated. The button appears dead.

The recreation code that never gets reached is correct in isolation — `PresenterIntegration.HandleGoLiveSingleMonitor()`:

```csharp
if (_outputWindow == null || !_outputWindow.IsLoaded)
{
    _outputWindow = new OutputWindow();
    _outputWindow.Closed += (s, e) => _outputWindow = null;
}
```

And `_outputWindow.Closed += (s, e) => _outputWindow = null;` (same file, also in `ApplyDisplaySettings`) means the integration's field *is* correctly nulled on close — but `OperatorWindow._outputWindow` is **not** refreshed. `SetOutputWindow(...)` is only called from inside `HandleGoLiveSingleMonitor`/`ApplyDisplaySettings`, both gated behind the broken guard, so the operator window keeps forwarding slides to a dead window reference even if the engine state were corrected.

### Problem 2 — ~2 GB RAM growth after closing while LIVE

`WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs`:

- The class creates two LibVLC-backed services as fields:
  ```csharp
  private readonly VideoBackgroundService _videoBackgroundService = new();
  private readonly VideoPlaybackService _videoPlaybackService = new();
  ```
  initialized in `InitializeVideoServices()` (ctor).
- There is **no** `OnClosed` override, no `Closing` handler, and no `IDisposable` on `OutputWindow`. Grep for `Closing|OnClosed|Dispose` in the file finds only a doc-comment. So when the user closes the window nothing calls `_videoBackgroundService.Dispose()` / `_videoPlaybackService.Dispose()`.
- `VideoBackgroundService` (`WorshipPlayer.Presenter/Services/VideoBackgroundService.cs`) runs a self-perpetuating loop: `PlayBackground` subscribes `_mediaPlayer.EndReached += OnMediaEndReached`, and `OnMediaEndReached` re-creates a new `Media` and calls `Play` again on every loop (`input-repeat=65535` plus an explicit restart). While `_shouldLoop` is true this keeps the native decoder running and allocating indefinitely on the orphaned window. Only `StopBackground()`/`Dispose()` clears `_shouldLoop` and unsubscribes — neither is called on window close.
- `PresenterIntegration.Dispose()` closes windows but still never disposes the OutputWindow's video services (there is no API to), and in this scenario Dispose isn't running anyway — the main window is still alive.
- Secondary contributor (only if NDI was on): `OutputCaptureService` (`WorshipPlayer.Presenter/Services/OutputCaptureService.cs`) runs a `DispatcherTimer` at up to 60 FPS allocating a fresh `RenderTargetBitmap` + `byte[]` every tick; it is only stopped via `StopNdiOutput()`, which the window close does not trigger.

Net: closing the LIVE output window leaves native VLC decode + managed `Media`/bitmap allocation running with no owner and no stop path → unbounded memory growth matching the observed 184 MB → ~2 GB.

---

## Recommended fixes (minimal, concrete)

### Fix 1a — recreate/reshow the output when GO LIVE is pressed while already LIVE but the window is gone

In `OperatorWindow.GoLive_Click` (and the `Key.Enter` branch), stop gating the single-monitor hook behind `IsBlacked || !IsLive`. Always ensure the window exists before deciding what to do:

```csharp
private void GoLive_Click(object sender, RoutedEventArgs e)
{
    if (_engine == null) return;

    // Always (re)create/full-screen the single-monitor output first; the hook is a
    // no-op on a dual-monitor rig and when the window already exists & is loaded.
    _onGoLive?.Invoke();

    if (_engine.IsBlacked || !_engine.IsLive)
    {
        _engine.GoLive();
    }
    _outputWindow?.Unblack();
}
```

For this to work the operator window must get the fresh reference. Have the host push it back: `HandleGoLiveSingleMonitor()` already calls `_operatorWindow?.SetOutputWindow(_outputWindow)` at the end — keep that, and make the hook always run (above) so the stale `OperatorWindow._outputWindow` is refreshed after a recreate. (`HandleGoLiveSingleMonitor`'s own `if (_outputWindow == null || !_outputWindow.IsLoaded)` already recreates the closed window.)

### Fix 1b (preferred, defensive) — intercept the close during a live service: hide instead of destroy

Add a `Closing` handler to `OutputWindow` that, when the service is LIVE, cancels the close and hides the window instead of destroying it. This preserves the single live `OutputWindow` instance and its video services, and sidesteps the stale-reference problem entirely:

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    if (SuppressDestroyWhileLive)   // set true by the host when IsLive
    {
        e.Cancel = true;
        Hide();
        return;
    }
    base.OnClosing(e);
}
```

Then GO LIVE just calls `Show()` + `GoFullScreen(false)` on the existing instance. This is the smaller behavioral change for a live service and avoids re-initializing LibVLC on every reopen.

### Fix 2 — dispose the video/capture resources when the OutputWindow actually closes

Give `OutputWindow` an `OnClosed` override that tears down everything it owns, so a real close (service not live, or app shutdown) cannot leak:

```csharp
protected override void OnClosed(EventArgs e)
{
    base.OnClosed(e);
    _alertTimer?.Stop();
    _videoPlaybackService.PlaybackEnded -= OnVideoPlaybackEnded;
    _videoBackgroundService.Dispose();   // clears _shouldLoop, unsubscribes EndReached, disposes LibVLC
    _videoPlaybackService.Dispose();
}
```

If Fix 1b is adopted, this `OnClosed` still runs on the genuine (non-live) close and on app shutdown, which is exactly when disposal is wanted. If NDI capture is in use, also ensure `OutputCaptureService.Stop()`/`Dispose()` is called from the operator window when the output goes away (it currently only stops via the NDI toggle).

### Recommended combination

Apply **Fix 1b + Fix 2**: during a live service, closing hides (keeps the one instance alive, so RAM is bounded and GO LIVE simply re-shows it); on a genuine close/app-exit, `OnClosed` disposes the LibVLC and timer resources. Fix 1a is the lighter alternative if you prefer to keep destroy-on-close semantics, but it must be paired with Fix 2 or the leak remains.
