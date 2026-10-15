# OWC Presenter v2 Phase 1 — WorshipTools Presenter Home shell

The change stands up `OwcPresenter.App`, a .NET 8 WPF shell that re-creates the WorshipTools Presenter Home screen: a shared green/dark theme dictionary, a header with centered nav cluster, a three-column body (left service panel, center Home workspace, right output/media/stage column), and a bottom status bar. All content is visual-only this phase, with nav tabs swapping the center `ContentControl`. Reviewed the authored XAML/C# and the recorded build+launch evidence; the diff is untracked-new (whole app folder), so every file was read directly rather than from a git diff.

Watch for: nothing blocking. The spec's palette, every exact empty-state string, the region ordering, the centered nav cluster, and the engine references all match the source of truth (confirmed). One cosmetic deviation worth noting but not blocking: the Live status dot is rendered red (`LiveRedBrush`) in a Live-not-active state, which is defensible against the mock (possible).

**Verdict**: APPROVED

## High-level view

The theme is a single `Themes/OwcTheme.xaml` ResourceDictionary merged once in `App.xaml`, holding the exact UI-SPEC §1 palette as `Color` + `*Brush` pairs (green accent `#3DDC84`, bg `#1C1C1E`, panels `#2A2A2E`). Every view binds brushes by key; the only inline hex are `#000000` for the preview rectangle and `#1C1C1E` as the green button's foreground, both deliberate and spec-consistent. No glow/blur/glassmorphism effects appear anywhere; corners are 6–8px.

The header is a 3-column grid: left logo `▣ Presenter` (green mark, white text), a center-aligned rounded `PanelBg` border holding the six nav tabs with Live first and preceded by a status dot, and a right group of sync / output-with-green-dot / settings-with-dropdown icons. The star-column layout (`* / Auto / *`) genuinely centers the cluster rather than parking it under the logo.

View switching is plain code-behind: `MainWindow` loads `HomeView` on `Loaded` and swaps a `PlaceholderView(title)` into the center `ContentControl` on tab click, re-styling the selected tab. Home is the startup view, not a Command Center.

Home carries a Quick start row of six square icon+label buttons (Tour, Services, Songs, Slides, Media, Scripture) and an Upcoming-services card with the dashed empty-state box, the exact "no upcoming services" string, and a centered green "+ Add a new service" button. The left panel shows the exact "To begin, select a service…" dashed empty state. The right column is ordered output dropdown → black 16:9 preview → icon row → media player → stage display, matching §2.4.

Build evidence is recorded: 0 errors / 0 warnings, `OwcPresenter.App` in the solution, referencing the copied `WorshipPlayer.Core` and `WorshipPlayer.Audio` via `..\` relative paths inside the v2 repo — not the old repo's WPF projects.

<details>
<summary>Issues (1)</summary>

1. **Live status dot color (non-blocking)** — the dot preceding "Live" uses `LiveRedBrush`; the spec header diagram shows a neutral `○`. Harmless and arguably closer to WorshipTools where Live glows red when live. No action required.

</details>

<details>
<summary>Details</summary>

### Theme as a shared dictionary

`OwcTheme.xaml` defines all eight palette colors exactly as UI-SPEC §1 specifies and pairs each with a `SolidColorBrush`. `App.xaml` merges it once through `Application.Resources` → `MergedDictionaries`, so views resolve `AppBgBrush`, `PanelBgBrush`, `AccentGreenBrush`, etc. by `StaticResource` (confirmed across every view). The accent is `#3DDC84` green everywhere brand/accent appears — logo mark, quick-start glyphs, primary button, online dot, output green dot — with no cyan or pink anywhere (confirmed). The only hard-coded hex outside the dictionary are `PreviewBlackBrush` `#000000` for the audience preview and `Foreground="#1C1C1E"` on `PrimaryGreenButton` (dark text on green); both are intentional and consistent with the palette, not scattered theming.

Flatness holds: no `DropShadowEffect`, `BlurEffect`, opacity-glow, or gradient brushes are used. Card and button corners are `CornerRadius` 6–8 (nav cluster 8, quick-start 8, primary button 6, media card 6), matching the 6–8px rule.

### Header layout and centered nav

The header grid uses `* / Auto / *` columns, which forces the `Auto` nav border to the true horizontal center regardless of logo or icon width — this is the correct mechanism for "tabs centered, not under the logo" (confirmed). The nav border is `PanelBg` with `CornerRadius=8`, holding six `NavTabButton`s. Live is first and composed of an `Ellipse` status dot followed by the "Live" text; the other five are plain-content buttons. Selection swaps to `NavTabButtonSelected` (brighter, SemiBold, raised `SurfaceHover` background) via `SelectTab`, restoring the prior tab's base style — so exactly one tab reads as active at a time.

The right group is three `HeaderIconButton`s: sync `⟳`, an output `🖥` overlaid with a small green `Ellipse` (top-right), and settings `⚙` with a `▾` dropdown caret. These are visual affordances only, per plan.

The Live dot is filled with `LiveRedBrush` rather than a neutral ring. The spec diagram draws `○ Live`. This is a cosmetic difference, not a fidelity break — WorshipTools itself shows a red Live indicator — so it does not block (possible false-positive if strict literal matching were required, which the spec prose does not demand).

### View switching and startup

`MainWindow` wires `Loaded → ShowHome()`, which sets `CenterContent.Content = new HomeView()` and clears tab selection, so the app opens on Home, not a Command Center (confirmed). `NavTab_Click` reads the button `Tag` and loads `PlaceholderView(title)` into the same `ContentControl`, so the five non-Home tabs swap the center region to a titled placeholder. The left and right columns live outside the swapped `ContentControl`, so they persist across tab changes, matching the fixed-column intent of §2.

### Home content and exact strings

`HomeView` is a `ScrollViewer` → `StackPanel`: a "Quick start" `CardTitle`, an `ItemsControl` (horizontal `StackPanel` panel) of six `QuickStartButton`s with glyph-over-label content for Tour, Services, Songs, Slides, Media, Scripture in that order, then an "Upcoming services" `CardTitle` and a `PanelBg` card. Inside the card a dashed `Rectangle` overlay wraps the exact string:

```
You have no upcoming services. Add a new service to get started!
```

followed by a centered `PrimaryGreenButton` reading "+ Add a new service" (confirmed verbatim). The quick-start buttons are `PanelBg` with `SurfaceHover` on hover, as specified.

`LeftPanelView` is a fixed 200px `UserControl` with a dashed `Rectangle` and the exact string:

```
To begin, select a service from the Services list. Your service items will appear here.
```

in `TextSecondary` (confirmed verbatim).

### Right column ordering

`RightColumnView` (fixed 240px) stacks, top to bottom: the "Main Audience Output" `ComboBox` (first/selected item), a black 16:9 preview (`Rectangle` 160×90 in a `Viewbox`, so it scales at a true 16:9 ratio), a centered five-icon row (volume/message/timer/blank/snapshot), a divider, the media-player card ("No media", a `00:00 ──── 00:00` scrubber with a middle divider line, and ◀ ▶ transport buttons), and the "Stage Display message" block. This is exactly the §2.4 order (confirmed).

### Status bar

The bottom `Border` right-aligns a green `●` (`AccentGreenBrush`) and "Online" text, matching §2.5's "● Online" (confirmed).

### Build and project references

The recorded verification reports `dotnet build OwcPresenter.sln` succeeding with 0 errors / 0 warnings and the launched process surviving a 4-second smoke window (no startup exception, so the resource merge and all XAML parse at runtime). The solution file lists `OwcPresenter.App` alongside the two engine projects. `OwcPresenter.App.csproj` references `..\WorshipPlayer.Core\WorshipPlayer.Core.csproj` and `..\WorshipPlayer.Audio\WorshipPlayer.Audio.csproj` — relative paths that resolve inside the OWC-Presenter-v2 repo (both folders present there as untracked copies), confirming it binds the copied engine, not the old repo's `WorshipPlayer.UI`/`WorshipPlayer.Presenter` (confirmed). The csproj shape (`net8.0-windows`, `UseWPF`, `EnableWindowsTargeting`, nullable, implicit usings, `RootNamespace`) matches the plan. No spot-check build was warranted: the recorded evidence is specific and the authored XAML binds only keys that exist in the merged dictionary.

### Not tested

Pixel-level visual fidelity against the mock is not machine-verifiable on this headless host and remains the human acceptance gate per the plan. Nav-tab click behavior, settings/output dropdown affordances, and all inert buttons are visual-only and unexercised by any automated test — acceptable for a visual-only phase.

</details>

<details>
<summary>File map</summary>

- `OwcPresenter.App/OwcPresenter.App.csproj` — new WPF project targeting net8.0-windows, references copied Core + Audio.
- `OwcPresenter.sln` — adds `OwcPresenter.App`.
- `OwcPresenter.App/App.xaml(.cs)` — merges the theme dictionary, `StartupUri=MainWindow`.
- `OwcPresenter.App/Themes/OwcTheme.xaml` — shared palette + button/combobox styles.
- `OwcPresenter.App/MainWindow.xaml(.cs)` — header, nav cluster, 3-column body, status bar, view switching.
- `OwcPresenter.App/Views/HomeView.xaml` — Quick start + Upcoming services.
- `OwcPresenter.App/Views/LeftPanelView.xaml` — service empty state.
- `OwcPresenter.App/Views/RightColumnView.xaml` — output/preview/media/stage column.
- `OwcPresenter.App/Views/PlaceholderView.xaml(.cs)` — titled placeholder for non-Home tabs.

No git diff available; the entire app folder is untracked-new. Files read directly from disk.

</details>
