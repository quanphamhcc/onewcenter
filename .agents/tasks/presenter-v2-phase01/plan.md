# Implementation Plan — OWC Presenter v2, Phase 1 (UI Shell)

## Goal and success criterion
Build **OwcPresenter.App**, a C#/WPF (.NET 8, Windows) app that VISUALLY re-creates the WorshipTools Presenter desktop UI. The #1 success criterion: the running app's **Home** screen looks like the WorshipTools Presenter mock described in the UI spec. Phase 1 is **VISUAL-ONLY** — no audio/backtrack/output-window wiring. Buttons may be inert or just switch views.

## Source of truth
- UI spec: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\docs\UI-SPEC.md` (read in full; this plan is derived from it).
- Solution to extend: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.sln` (currently holds only WorshipPlayer.Core and WorshipPlayer.Audio).

## Design decisions (grounded in exploration)
- **Project target & properties:** `net8.0-windows`, `UseWPF=true`, `EnableWindowsTargeting=true`, `Nullable=enable`, `ImplicitUsings=enable`, `RootNamespace=OwcPresenter.App`. Mirrors the sibling WPF project pattern found at `WorshipPlayer.Presenter.csproj` in the old repo. `EnableWindowsTargeting=true` is required because the installed SDK is 10.0.400 and avoids Windows-targeting restore issues. (Decision: match the existing, proven WPF csproj shape rather than invent a new one.)
- **Engine references:** reference the copied `WorshipPlayer.Core` (`net8.0`, namespace `WorshipPlayer.Core`) and `WorshipPlayer.Audio` (`net8.0-windows`, namespace `WorshipPlayer.Audio`) from the **OWC-Presenter-v2** solution ONLY. Do NOT reference the old repo's `WorshipPlayer.UI` / `WorshipPlayer.Presenter`. Phase 1 surfaces no engine types in the UI, but the references are added now so later phases can wire them without re-touching the csproj. (Decision: add refs early for a stable project graph; they are harmless to a visual-only build.)
- **View-switching mechanism:** code-behind + a `ContentControl` whose `Content` is swapped by click handlers on the nav tabs — no MVVM framework, no DI. The old repo's WPF code is plain code-behind with a static `ResourceDictionary`; matching that keeps the shell approachable and dependency-free. Only **Home** has real content; the other five tabs (Live, Services, Songs, Slides, Media, Scripture) are reusable placeholder views that render their own title. (Decision: a single `ContentControl` + handler is the smallest mechanism that satisfies "nav tabs switch the center content" without over-engineering; MVVM can be layered in later.)
- **Theme:** a single shared `Themes/OwcTheme.xaml` ResourceDictionary holds every color, brush, and font, merged in `App.xaml`. All views reference brushes by key — no scattered magic hex. The old `App.xaml` uses the inline color→brush pattern but with the WRONG palette (blue `#4A9FD9`, bg `#1E1E2E`); this plan replaces it with the WorshipTools palette (green accent `#3DDC84`, bg `#1C1C1E`). (Decision: reuse the proven Color+SolidColorBrush+Style structure, swap in the correct palette, and move it into its own file per the spec.)
- **Icons/glyphs:** use the glyph characters from the spec (▣ ○ ⟳ 🖥 ⚙ 🔊 💬 ⏱ ◀ ▶ +) rendered as `TextBlock`/button content in Segoe UI / Segoe UI Symbol. No icon-font package is added — avoids a new dependency for a visual-only phase. (Decision: text glyphs keep Phase 1 dependency-free and match the mock closely enough.)

## Exact theme palette (from UI-SPEC §1 — use these keys everywhere)
| Key | Hex | Role |
|---|---|---|
| `AppBg` | `#1C1C1E` | deepest app background |
| `PanelBg` | `#2A2A2E` | panels / cards / quick-start buttons |
| `SurfaceHover` | `#3A3A3F` | hover state |
| `BorderColor` | `#38383C` | borders / dividers |
| `TextPrimary` | `#FFFFFF` | primary text |
| `TextSecondary` | `#9A9AA0` | secondary / muted text |
| `AccentGreen` | `#3DDC84` | brand accent, Add-service button, Online dot (NOT cyan/pink) |
| `LiveRed` | `#E53935` | Live indicator |

Rounded corners 6–8px on cards/buttons. Font: Segoe UI (weights 400/600). Flat dark look — NO glow, NO glassmorphism.

---

# Implementation Plan

- [ ] 1. Create the WPF app project `OwcPresenter.App` and add it to the v2 solution.
      Create `OwcPresenter.App.csproj` with `net8.0-windows`, `UseWPF=true`, `EnableWindowsTargeting=true`, `Nullable=enable`, `ImplicitUsings=enable`, `RootNamespace=OwcPresenter.App`, and `ProjectReference`s to `..\WorshipPlayer.Core\WorshipPlayer.Core.csproj` and `..\WorshipPlayer.Audio\WorshipPlayer.Audio.csproj`. Add the project to the solution. Delete any default `MainWindow.xaml`/`Class1.cs` scaffolding that conflicts with files created later; App.xaml/MainWindow are authored in later steps.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\OwcPresenter.App.csproj`, `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.sln`
      Verify: run `dotnet sln "c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.sln" list` — the output lists `OwcPresenter.App`. Then `dotnet restore "c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.sln"` completes with no errors.

- [ ] 2. Author the shared theme ResourceDictionary `Themes/OwcTheme.xaml`.
      Define `Color` entries + matching `SolidColorBrush` entries for every key in the palette table above (`AppBgBrush`, `PanelBgBrush`, `SurfaceHoverBrush`, `BorderColorBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `AccentGreenBrush`, `LiveRedBrush`). Add reusable `Style`s: `NavTabButton` (flat, transparent bg, white text, rounded, hover→SurfaceHover, a `Selected`-style variant via a second key `NavTabButtonSelected` that is brighter/raised), `QuickStartButton` (square ~96x96, `PanelBg` bg, hover `SurfaceHover`, CornerRadius 8, icon over label), `PrimaryGreenButton` (AccentGreen bg, white text, rounded 6), `HeaderIconButton` (transparent, white glyph, hover highlight), and a default `TextBlock`/font setup to Segoe UI. No hard-coded hex outside this file.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\Themes\OwcTheme.xaml`
      Verify: covered by the build in step 3 (XAML parse/compile). The dictionary must contain exactly the 8 brush keys above; later views bind to them.

- [ ] 3. Create `App.xaml` + `App.xaml.cs` and merge the theme; set a minimal empty `MainWindow` so the app builds and launches.
      `App.xaml` sets `StartupUri="MainWindow.xaml"` and merges `Themes/OwcTheme.xaml` via `Application.Resources` → `ResourceDictionary.MergedDictionaries`. Create a minimal `MainWindow.xaml` (title "OWC Presenter", `Background="{StaticResource AppBgBrush}"`, size ~1280x800) and its `MainWindow.xaml.cs` so the project compiles and runs end-to-end before the real layout is added.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\App.xaml`, `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\App.xaml.cs`, `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\MainWindow.xaml`, `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\MainWindow.xaml.cs`
      Verify: `dotnet build "c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.sln" -c Debug` succeeds with 0 errors. The window reference to `AppBgBrush` resolves (no missing-resource build error).

- [ ] 4. Build the placeholder view used by the five non-Home tabs.
      Create `Views/PlaceholderView.xaml` (+ `.xaml.cs`) — a `UserControl` with `AppBg` background that shows a centered title in `TextPrimary`. Expose a settable `Title` (dependency property or a public method/property set from code-behind) so the same control serves Live/Services/Songs/Slides/Media/Scripture by passing the tab name.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\Views\PlaceholderView.xaml`, `...\Views\PlaceholderView.xaml.cs`
      Verify: `dotnet build ... -c Debug` succeeds. (This view is wired into switching in step 7.)

- [ ] 5. Build the Home center view with its sub-components.
      Create `Views/HomeView.xaml` (+ `.xaml.cs`) containing, top to bottom: (a) a **"Quick start"** card (title + horizontal row of 6 `QuickStartButton`s: Tour, Services, Songs, Slides, Media, Scripture, each glyph+label, bg `#2A2A2E` hover `#3A3A3F`); (b) an **"Upcoming services"** card (title + dashed-border empty-state box reading exactly "You have no upcoming services. Add a new service to get started!" with a centered green `PrimaryGreenButton` labelled "+ Add a new service" below). Use brush keys only. Buttons inert (no handlers required) this phase.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\Views\HomeView.xaml`, `...\Views\HomeView.xaml.cs`
      Verify: `dotnet build ... -c Debug` succeeds and the exact empty-state string and the 6 button labels are present in `HomeView.xaml`.

- [ ] 6. Build the left panel and right column controls.
      Create `Views/LeftPanelView.xaml` (+ `.xaml.cs`): fixed width ~200px, a dashed-border empty-state with the exact text "To begin, select a service from the Services list. Your service items will appear here." in `TextSecondary`. Create `Views/RightColumnView.xaml` (+ `.xaml.cs`): fixed width ~240px, top-to-bottom: a "Main Audience Output" `ComboBox` header; a black 16:9 preview `Border`/`Rectangle`; a thin icon row (volume 🔊, message 💬, timer ⏱, square ▭, snapshot ▣); a media-player block ("No media", a scrubber line showing `00:00 ──── 00:00`, prev ◀ + play ▶ buttons); and a "Stage Display message" block. Visual-only — no backend wiring.
      Files: `...\Views\LeftPanelView.xaml`, `...\Views\LeftPanelView.xaml.cs`, `...\Views\RightColumnView.xaml`, `...\Views\RightColumnView.xaml.cs`
      Verify: `dotnet build ... -c Debug` succeeds and both exact empty-state strings and the `00:00` scrubber labels are present in the respective XAML files.

- [ ] 7. Assemble `MainWindow.xaml` with the full layout and view-switching, replacing the step-3 placeholder window.
      Lay out with a `DockPanel`/`Grid`: a top system menu placeholder (Edit · Help · Window); a header row with left logo "▣ Presenter" (green ▣ + white text), a horizontally-CENTERED rounded nav cluster of 6 `NavTabButton`s (○ Live [status dot precedes Live], Services, Songs, Slides, Media, Scripture) inside a rounded `PanelBg` border, and a right group of 3 `HeaderIconButton`s (sync ⟳, output 🖥 with a small green dot, settings ⚙ with a dropdown ▾); a 3-column body — `LeftPanelView` (~200px) | a center `ContentControl x:Name="CenterContent"` | `RightColumnView` (~240px); and a bottom status bar with a right-aligned green "● Online". In `MainWindow.xaml.cs`, implement click handlers: Home-equivalent default loads `HomeView` into `CenterContent`; each other tab sets `CenterContent.Content` to a `PlaceholderView` with the tab's title; update the selected tab's visual (swap to `NavTabButtonSelected` style / set a selected flag). Initialize to Home on load.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\MainWindow.xaml`, `...\MainWindow.xaml.cs`
      Verify: `dotnet build "c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.sln" -c Debug` succeeds with 0 errors; XAML names (`CenterContent`) resolve in code-behind.

- [ ] 8. Build + visual verification of the running app.
      Run the app and confirm the Home screen matches the mock. (Automated build is the hard gate; the visual check is the acceptance gate.)
      Files: none (verification only).
      Verify: `dotnet build "c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.sln" -c Debug` → 0 errors/0 warnings-as-errors. Then launch: `dotnet run --project "c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2\OwcPresenter.App\OwcPresenter.App.csproj"` and visually confirm against UI-SPEC §2: (1) dark `#1C1C1E` bg with green `#3DDC84` accent (no cyan/pink); (2) centered nav cluster with 6 tabs, Live preceded by a status dot, selected tab brighter; (3) left dashed empty-state with the exact "To begin, select a service…" text; (4) Home center shows Quick start row of 6 buttons + Upcoming-services dashed box with exact empty text and a green "+ Add a new service" button; (5) right column shows Output combobox → black 16:9 preview → icon row → media player (No media, 00:00 ──── 00:00, ◀ ▶) → Stage Display message; (6) bottom status bar shows green "● Online" at the right; (7) clicking Services/Songs/Slides/Media/Scripture swaps the center to a titled placeholder and returns to Home works. Close the window to end the run.

## Notes / assumptions
- Window chrome: use the standard WPF title bar for Phase 1 (the spec's "Edit · Help · Window" is an in-content menu placeholder row, not a custom OS chrome). Assumption made to avoid custom window-chrome scope creep in a visual-only phase.
- The settings ⚙ dropdown and the output 🖥 menu are visual affordances only (glyph + ▾ / green dot); no popup behavior is required this phase.
- If `dotnet run` cannot open a window in the execution environment, a successful `dotnet build` plus confirmation that each exact spec string and region is present in the XAML is the fallback evidence; note that the live visual check is still required for final sign-off.

## Loop stop contract (unchanged)
The implement/review loop stops when the reviewer writes `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.agents\tasks\presenter-v2-phase01\review.json` with top-level `"verdict": "APPROVED"`, written last.
