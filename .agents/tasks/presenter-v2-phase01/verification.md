# Verification — OWC Presenter v2, Phase 1 (UI Shell)

Iteration: FIRST (no `review.json` present at start).

## Build

Command:

```
dotnet build c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.sln --nologo
```

Result: **Build succeeded in ~5.3s. 0 errors, 0 warnings.**

Projects built:
- `WorshipPlayer.Core` (net8.0) — engine, unchanged.
- `WorshipPlayer.Audio` (net8.0-windows) — engine, unchanged.
- `OwcPresenter.App` (net8.0-windows, WPF) — new, references the two engine projects.

Solution membership confirmed: `OwcPresenter.App` added to `OwcPresenter.sln`.

## Launch (headless smoke check)

Because this is a Windows WPF host, a full pixel-level visual confirmation requires a human looking at the window. What was verified automatically:

- Started `OwcPresenter.App\bin\Debug\net8.0-windows\OwcPresenter.App.exe` as a process.
- Waited 4 seconds; the process was **still running** (did not exit early), i.e. **no unhandled startup exception** — App.xaml resource merge, MainWindow XAML parse, and the `Loaded` → `ShowHome()` path all succeed at runtime.
- Process then terminated by the test harness.

Not verified automatically: the exact pixel appearance / layout fidelity against the mock. That remains the human acceptance gate per the plan's fallback note. Every exact spec string and region was confirmed present in XAML (see self-check below).

## Per-region self-check against UI-SPEC §2

| Spec region | Where implemented | Status |
|---|---|---|
| Theme palette (bg #1C1C1E, panel #2A2A2E, hover #3A3A3F, border #38383C, text #FFFFFF, secondary #9A9AA0, accent green #3DDC84, Live red #E53935) | `Themes/OwcTheme.xaml` as `Color` + matching `*Brush` keys | ✅ all 8 keys present; views reference brushes by key, no scattered hex |
| App.xaml merges theme | `App.xaml` → MergedDictionaries `Themes/OwcTheme.xaml` | ✅ |
| System menu placeholder (Edit · Help · Window) | `MainWindow.xaml` top `Menu` | ✅ |
| Header left logo "▣ Presenter" (green mark + white text) | `MainWindow.xaml` left `StackPanel` (▣ in AccentGreen, "Presenter" white) | ✅ |
| Centered rounded nav cluster, 6 tabs (○ Live w/ status dot, Services, Songs, Slides, Media, Scripture); selected tab brighter/raised | `MainWindow.xaml` center `Border` (PanelBg, CornerRadius 8); Live has a Live-red status Ellipse; `NavTabButtonSelected` style raises/brightens the active tab | ✅ |
| Header right 3 icons (sync ⟳, output 🖥 with green dot, settings ⚙ with dropdown ▾) | `MainWindow.xaml` right `StackPanel`; output has green Ellipse; settings has ▾ | ✅ |
| 3-region body: left ~200px / center workspace / right ~240px | `LeftPanelView` Width=200, center `ContentControl`, `RightColumnView` Width=240 | ✅ |
| Bottom status bar with green "● Online" at right | `MainWindow.xaml` bottom `Border`, right-aligned green ● + "Online" | ✅ |
| Nav tabs switch center content; only Home has real content, others are titled placeholders | `MainWindow.xaml.cs` `NavTab_Click` loads `PlaceholderView(title)`; `ShowHome()` loads `HomeView` on load | ✅ |
| Home: Quick start card, 6 square icon+label buttons (Tour, Services, Songs, Slides, Media, Scripture), bg #2A2A2E hover #3A3A3F | `HomeView.xaml` ItemsControl of `QuickStartButton` (PanelBg/SurfaceHover hover) with glyph+label | ✅ |
| Home: Upcoming services card, dashed empty-state box with exact text "You have no upcoming services. Add a new service to get started!" + centered green "+ Add a new service" | `HomeView.xaml` dashed `Rectangle` overlay + exact string + `PrimaryGreenButton` | ✅ exact string present |
| Left panel dashed empty-state exact text "To begin, select a service from the Services list. Your service items will appear here." | `LeftPanelView.xaml` dashed `Rectangle` + exact string in TextSecondary | ✅ exact string present |
| Right column: "Main Audience Output" combobox header | `RightColumnView.xaml` `ComboBox` first item "Main Audience Output" | ✅ |
| Right column: black 16:9 preview rectangle | `RightColumnView.xaml` 160×90 black `Rectangle` in a `Viewbox` | ✅ |
| Right column: thin icon row (volume, message, timer, square, snapshot) | `RightColumnView.xaml` row of 5 `FlatIconButton`s | ✅ |
| Right column: media player ("No media", 00:00 ──── 00:00 scrubber, prev + play) | `RightColumnView.xaml` media block: "No media", `00:00` / `00:00` with a scrubber line, ◀ + ▶ | ✅ both `00:00` labels present |
| Right column: Stage Display message block | `RightColumnView.xaml` "Stage Display message" title + panel | ✅ |
| Flat dark look, rounded 6–8px, no glow/glassmorphism | CornerRadius 6–8 on cards/buttons; no blur/drop-shadow/opacity-glow effects used | ✅ |

## Notes
- Glyphs rendered as Segoe UI / Segoe UI Symbol text (no icon-font dependency added), per plan.
- Buttons are inert this phase (no audio/output/backtrack wiring), except nav tabs which swap center content.
- Settings dropdown and output menu are visual affordances only (no popups), per plan.
