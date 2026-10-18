# Home landing screen visual redesign — completion report

Branch: `feature/home-redesign`
Worktree: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\home-redesign`
Iteration: FIRST (no `review.json` present at start)

## Scope

Visual redesign of the HOME landing screen only, to match the reference layout (left dashed
placeholder card + "Quick start" icon grid + "Upcoming services" table + blue "+ Add a new
service" CTA), keeping OWC branding. No reference "Presenter" wordmark/logo was added. The brand
"OWC" text and the top-nav (Live/Services/Songs/Scripture/Media/Tools) live in the OperatorWindow
chrome, NOT in HomeView, so nothing was added to HomeView for those (avoids a double nav).

## Files changed

1. `WorshipPlayer.Presenter/Themes/OwcDesignSystem.xaml` — ADDITIVE blue accent resources:
   - `OwcBlueColor` = `#2F6FED`, `OwcBlueHoverColor` = `#2B63D4` (added to the Colors block).
   - `OwcBlueBrush`, `OwcBlueHoverBrush` SolidColorBrushes (added to the Brush block, declared
     after `OwcBorderBrush`, honoring the file's "colors/brushes first" ordering).
   - `OwcBlueButton` style `BasedOn="{StaticResource OwcButtonBase}"`: `Background=OwcBlueBrush`,
     `Foreground=#FFFFFF`, `FontWeight=SemiBold`, with a `Style.Triggers` `IsMouseOver` setter to
     `OwcBlueHoverBrush` (overrides the base surface-hover so the button stays blue on hover).
   - No existing key was changed or removed.

2. `WorshipPlayer.Presenter/Views/Home/HomeView.xaml` — layout restructured into the two-column
   reference layout (see element-by-element below). No hardcoded hex introduced; all colors come
   from the existing theme keys (plus the new `OwcBlueButton`). The outer `ScrollViewer` is kept.

3. `WorshipPlayer.Presenter/Views/Home/HomeView.xaml.cs` — ADDITIVE only:
   - Added `public event EventHandler<string>? EditServiceRequested;`
   - Added `Edit_Click` handler mirroring `Open_Click`/`Rename_Click` (resolves path via
     `ResolvePath`, raises `EditServiceRequested`).
   - All existing events, handlers, and the `SetServices(IEnumerable<ServiceListRow>)` signature
     are unchanged. The services `ListView` keeps `x:Name="ServicesListView"`.

4. `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — ONE additive wiring line in
   `EnsureHomeView()`: `_homeView.EditServiceRequested += OnEditServiceRequested;` (the
   `OnEditServiceRequested` handler already existed and is used by the sibling ServicesListView).
   No other wiring, the `SetServices(...)` call, or any other panel was changed.

## Element-by-element layout (HomeView.xaml)

- Outer `ScrollViewer` (vertical auto) → `Grid Margin="24"` with 3 columns: `320` (left), `20`
  (gap), `*` (right).
- LEFT column: a card (`OwcPanelBorder`, `MinHeight="360"`) containing a `Rectangle` with
  `Stroke=OwcBorderBrush`, `StrokeDashArray="4 3"`, `RadiusX/Y=10` (WPF `Border` cannot draw a
  dashed outline, so a Rectangle provides the dashed placeholder look) layered behind two centered,
  wrapped, muted (`OwcTextMutedBrush`) `TextBlock`s with the exact helper text:
  - "To begin, select a service from the Services list."
  - "Your service items will appear here."
- RIGHT column: a vertical `StackPanel` with two cards:
  - "Quick start" card (`OwcPanelBorder`, padding 20): header `FontSize=16 FontWeight=SemiBold`
    `OwcTextPrimaryBrush`, then a `UniformGrid Rows=1 Columns=6` of 6 large (`MinHeight=90`)
    `OwcButtonBase` buttons, each a centered line-icon glyph above a 12px label: Tour, Services,
    Songs, Slides, Media, Scripture.
  - "Upcoming services" card (`OwcPanelBorder`, padding 20): header, then a `ListView`
    (`OwcDarkListView`, header style `OwcDarkGridViewColumnHeader`, `MaxHeight=320`,
    `x:Name="ServicesListView"`) with columns Date (`{Binding DateDisplay}`), Type
    (`{Binding Type}`), Title (`{Binding Title}`), plus an overflow column: a vertical-ellipsis
    (`&#x22EE;`) `OwcButtonBase` transparent button `Tag="{Binding FilePath}"` whose context menu
    is Open / Edit / Rename / (separator) / Delete, each `Tag="{Binding FilePath}"` — mirroring the
    sibling ServicesListView menu. Below the table, a centered blue (`OwcBlueButton`)
    "+ Add a new service" button.

## Interactive element → event mapping

| Element | Click handler | Event raised |
|---|---|---|
| Quick start: Tour | `Tour_Click` | `TourRequested` |
| Quick start: Services | `Services_Click` | `ServicesRequested` |
| Quick start: Songs | `Songs_Click` | `SongsRequested` |
| Quick start: Slides | `Slides_Click` | `SlidesRequested` |
| Quick start: Media | `Media_Click` | `MediaRequested` |
| Quick start: Scripture | `Scripture_Click` | `ScriptureRequested` |
| "+ Add a new service" (blue) | `AddService_Click` | `AddServiceRequested` |
| Overflow glyph (opens menu) | `Overflow_Click` | (opens ContextMenu; no event) |
| Overflow → Open | `Open_Click` | `OpenServiceRequested` (arg = `FilePath`) |
| Overflow → Edit | `Edit_Click` | `EditServiceRequested` (arg = `FilePath`) **(new)** |
| Overflow → Rename | `Rename_Click` | `RenameServiceRequested` (arg = `FilePath`) |
| Overflow → Delete | `Delete_Click` | `DeleteServiceRequested` (arg = `FilePath`) |

## HomeView public surface — before / after

Events BEFORE: `TourRequested`, `ServicesRequested`, `SongsRequested`, `SlidesRequested`,
`MediaRequested`, `ScriptureRequested`, `AddServiceRequested`, `OpenServiceRequested`,
`RenameServiceRequested`, `DeleteServiceRequested`.

Events AFTER: all of the above **unchanged**, PLUS the additive
`EditServiceRequested` (`EventHandler<string>`). Nothing removed or renamed.

Method BEFORE and AFTER (identical): `public void SetServices(IEnumerable<ServiceListRow> rows)`.

So the only public-surface delta is the additive `EditServiceRequested` event (requested by the
user's message 4: the 3-dot button should also offer Edit + Delete). The `SetServices` contract is
intact.

## Build / test results (run from the worktree root)

Command: `dotnet build WorshipBacktrackPlayer.sln -c Debug`
Result: **Build succeeded, 0 errors, 4 warning(s)** in ~10.4s. The 4 warnings are PRE-EXISTING and
unrelated to this change:
- `MediaLibraryPanel.xaml.cs(16,20): warning CS0414` — `_currentView` assigned but never used.
- `ChordChartWindow.xaml.cs(235,51): warning CS8604` — possible null arg `lyricText`.
- `WaveformEditorWindow.xaml.cs(1775,18): warning CS0414` — `_inReviewMode` assigned but never used.
(The MediaLibraryPanel warning is emitted twice — once per Presenter compile pass — hence the "4"
count; all are pre-existing and outside Home scope.)

Command: `dotnet test WorshipPlayer.Tests\WorshipPlayer.Tests.csproj -c Debug`
Result: **total: 620, failed: 0, succeeded: 620, skipped: 0** in ~62.0s. (The console line
"Error loading service order: 't' is an invalid start of a property name" is expected output from a
test that exercises a malformed-JSON code path; the suite is fully green.) Note: Tests target
`net8.0` and reference Core only, so there are no WPF/HomeView unit tests — the build is the real
gate for this XAML-only change and `dotnet test` guards that Core still compiles/passes.

## Visual confirmation note

WPF cannot be screenshotted headless from this environment, so the ON-SCREEN APPEARANCE
(dark theme, dashed left placeholder, 6-icon Quick start grid, dark Upcoming-services table, blue
Add button, spacing/padding) needs the USER's visual confirmation by running the app
(`dotnet run --project WorshipPlayer.UI`). The build/parse success above confirms the XAML is valid
and all resource keys resolve, but not the pixel-level look.

## Constraints honored

- No new NuGet packages.
- No change to `ServiceListRow` or its builder; no change to the `SetServices` contract or
  OperatorWindow's existing wiring (only one additive `+=` line).
- No brand text / "Presenter" wordmark / top-nav added inside HomeView.
- No scattered hardcoded hex in HomeView; one additive blue brush set + button style added to
  `OwcDesignSystem.xaml` under the planned keys.
