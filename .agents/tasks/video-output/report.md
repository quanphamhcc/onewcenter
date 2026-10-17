# Completion Report — Real Video OUTPUT (7.1 motion backgrounds + 7.2 video service item)

Branch: `feature/video-output` (worktree `.worktrees/video-output`). Not merged, not pushed.

## Summary

Replaced the two `ContentControl` placeholders with real `LibVLCSharp.WPF.VideoView` surfaces,
added the native libvlc payload package, initialized the LibVLC core exactly once per process,
made close/shutdown disposal safe, added a pure `VideoRouteResolver` in Core, wired a `Video`
service item to full-screen playback via `OperatorWindow.OnItemChanged`, and added unit tests for
the routing decision.

## Packages added (and why)

- `VideoLAN.LibVLC.Windows` **3.0.21** → `WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj:18`.
  - Why: `LibVLCSharp` 3.8.5 + `LibVLCSharp.WPF` 3.8.5 were already referenced (the managed binding
    and the `VideoView` control), but NO native libvlc runtime was referenced in any csproj. Without
    the native core, `new LibVLC(...)` throws and `VideoBackgroundService.Initialize()` silently
    swallowed it — the root cause of "nothing displays."
  - Version choice: the managed binding and native payload are versioned independently; LibVLCSharp
    3.x binds to native libvlc 3.0.x. `3.0.21` is the long-established, widely-deployed stable build
    proven with the LibVLCSharp 3.8.x line, chosen over newer 3.0.23.x/3.0.24 to keep risk low and
    the change minimal. Documented fallback if a restore ever fails: `3.0.20`. It pinned to an exact
    version (no range).
  - Native payload copy CONFIRMED: `WorshipPlayer.UI/bin/Debug/net8.0-windows/libvlc/win-x64/libvlc.dll`
    is present along with `.../win-x64/plugins/` (the DLLs flow transitively because UI → Presenter).
  - No NEW build warnings came from the package (build stays at the 4 pre-existing warnings).

## Wired-path trace (both paths), confirming REAL VideoView bound to a MediaPlayer

### 7.1 — Motion background (theme/background path), looping behind lyrics
1. `OutputWindow.xaml.cs:~198-230` `ApplyTheme` → `case BackgroundType.Video` → `StartMotionBackground(theme.MotionBackgroundPath, theme.MotionBackgroundOpacity)`.
2. `OutputWindow.xaml.cs` `StartMotionBackground`: guards on `_videoBackgroundService.IsInitialized && Player != null`; sets `BackgroundVideoView.MediaPlayer = _videoBackgroundService.Player;` sets `Opacity`, makes it `Visible`, then `_videoBackgroundService.PlayBackground(videoPath, loop:true)`.
3. `VideoBackgroundService.cs` `PlayBackground` (loop=true): adds `input-repeat=65535` + an `EndReached` self-restart loop → continuous looping on its own `MediaPlayer`.
4. `OutputWindow.xaml:38-41` `BackgroundVideoView` is now `<vlc:VideoView .../>` (REAL control, was `ContentControl`), `Panel.ZIndex=0` inside `BackgroundLayer` (renders behind lyric layers `SlideA`/`SlideB`).

### 7.2 — Video service item → full-screen playback
1. `ServiceOrderManager.ItemChanged` → `PresenterEngine.OnServiceItemChanged` fires `ItemChanged` → `OperatorWindow.OnItemChanged` (`OperatorWindow.xaml.cs`, subscribed during Initialize).
2. `OperatorWindow.OnItemChanged`: `var videoRoute = VideoRouteResolver.Resolve(e.NewItem);` then
   `if (videoRoute.PlayVideo) _outputWindow?.ShowVideo(videoRoute.VideoPath); else if (_outputWindow?.IsVideoMode == true) _outputWindow.StopVideo();`
3. `OutputWindow.xaml.cs` `ShowVideo`: guards on `_videoPlaybackService.IsInitialized && Player != null`; sets `_isVideoMode = true`, hides lyric layers, sets `FullScreenVideoView.MediaPlayer = _videoPlaybackService.Player;` makes it `Visible`, then `_videoPlaybackService.Play(videoPath)` (single-shot, `Loop=false`).
4. `OutputWindow.xaml:249-252` `FullScreenVideoView` is now `<vlc:VideoView .../>` (REAL control, was `ContentControl`), `Panel.ZIndex=100` (above lyric layers).
5. On end: `VideoPlaybackService.PlaybackEnded` → `OutputWindow.OnVideoPlaybackEnded` → `StopVideo()` (restores lyric layers) + raises `VideoPlaybackEnded` for auto-advance.
6. A non-video item: resolver returns `PlayVideo=false`, so `OnItemChanged` calls `StopVideo()` when previously in video mode; slides for a video item are no-ops while `_isVideoMode` (`OutputWindow.xaml.cs:279`), so slides don't fight the video.

Both VideoViews are the `LibVLCSharp.WPF` control (xmlns `vlc` = `clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF` at `OutputWindow.xaml:4`), and their `MediaPlayer` properties are assigned from the respective service's `Player` (a `LibVLCSharp.Shared.MediaPlayer`).

## Lifecycle / dispose handling

- `Core.Initialize()` runs EXACTLY ONCE per process: guarded by `static bool _coreInitialized` under a `static readonly object _coreInitLock` in `VideoBackgroundService.Initialize()`, BEFORE the first `new LibVLC(...)`. Each `OutputWindow` (recreated after close) still builds its own `LibVLC`/`MediaPlayer`, but the native core loads once. Fully qualified as `LibVLCSharp.Shared.Core.Initialize()` to avoid a name clash with the `WorshipPlayer.Core` namespace.
- The existing try/catch is preserved: a missing native payload leaves the service uninitialized and the app continues with lyric-only output (unchanged behavior).
- `OutputWindow.OnClosed`: BEFORE disposing the services, detaches `BackgroundVideoView.MediaPlayer = null` and `FullScreenVideoView.MediaPlayer = null` on the UI thread to avoid the VideoView touching a MediaPlayer that is about to be released (native-handle race). Then unsubscribes `PlaybackEnded`, disposes `_videoBackgroundService` (owns + disposes the shared `LibVLC`) and `_videoPlaybackService` (disposes only its own `MediaPlayer`, not the shared `LibVLC` — confirmed in `VideoPlaybackService.Dispose`).
- "Starting a video stops the prior one": `PlayBackground` calls `StopBackground()` first; `Play` calls `Stop()` first (both unchanged).
- "No video set ⇒ unchanged": every new VideoView access is guarded by `IsInitialized`/non-null `Player`, so lyric/image/solid backgrounds behave exactly as before.

## Scope

Changed only: the two VideoView surfaces + xmlns, the `VideoLAN.LibVLC.Windows` ref, one-time
`Core.Initialize()`, `OnClosed` detach, the pure `VideoRouteResolver` + its tests, and the single
`OnItemChanged` routing hook. No changes to songs/templates/scripture/service-groups/bulk-edit
internals, NDI, stage display, or the audio engine.

## Build / test results (recorded)

- `dotnet build WorshipBacktrackPlayer.sln -c Debug` → **Build succeeded, 0 errors, 4 warnings**.
  The 4 warnings are PRE-EXISTING and unrelated: `MediaLibraryPanel._currentView` (CS0414),
  `ChordChartWindow.BuildChordLine` (CS8604), `WaveformEditorWindow._inReviewMode` (CS0414).
  No NEW warnings from the added package.
- Native payload copy: `Test-Path WorshipPlayer.UI/bin/Debug/net8.0-windows/libvlc/win-x64/libvlc.dll` → **True**; `.../win-x64/plugins/` present.
- `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug` → **Passed! Failed: 0, Passed: 560, Skipped: 0, Total: 560**. (Baseline was 550; the 10 new cases are the `VideoRouteResolver` theory/fact rows.)

## Files changed

- `WorshipPlayer.Presenter/WorshipPlayer.Presenter.csproj` — add `VideoLAN.LibVLC.Windows` 3.0.21.
- `WorshipPlayer.Presenter/Services/VideoBackgroundService.cs` — one-time `Core.Initialize()`.
- `WorshipPlayer.Presenter/Views/OutputWindow.xaml` — `vlc` xmlns; both placeholders → `vlc:VideoView`.
- `WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs` — bind MediaPlayer in `StartMotionBackground`/`ShowVideo`; detach in `OnClosed`.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — route Video item in `OnItemChanged`.
- `WorshipPlayer.Core/Services/VideoRouteResolver.cs` — new pure resolver.
- `WorshipPlayer.Tests/VideoRouteResolverTests.cs` — new unit tests.

## HONESTY: what was NOT and CANNOT be verified headless

Actual video rendering/playback CANNOT be verified without a display + the native core at runtime.
I did NOT see video play. What IS proven: clean build with the package resolving, the native payload
copied into the output bin, the real `VideoView` controls bound to a `MediaPlayer` in code (traced
above), passing routing-logic unit tests, and the full wired-path traces.

### Requires MANUAL on-screen verification
1. **7.1 motion background**: apply a theme with `BackgroundType.Video` + a valid `MotionBackgroundPath`; confirm the loop renders behind lyrics at the configured opacity and loops seamlessly.
2. **7.2 service video**: add a `Video` service item with a short `.mp4` in `MediaPath`, activate it; confirm it plays full-screen and that playback end auto-returns to lyric mode (`VideoPlaybackEnded`).
3. **No-video unchanged**: with no video configured, confirm lyric output is visually identical to before.
4. **Lifecycle**: open/close the output window several times; confirm no crash and no steadily growing memory (native handle leaks).
5. **Performance/seamlessness**: loop restart smoothness and full-screen decode performance on the target hardware.
