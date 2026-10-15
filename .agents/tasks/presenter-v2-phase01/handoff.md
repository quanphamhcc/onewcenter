# OWC Presenter v2 — Phase 0 + Phase 1 Handoff

Target project: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2`

## Build result

- Command: `dotnet build OwcPresenter.sln --nologo`
- Result: **Build succeeded — 0 errors, 0 warnings.**
- Projects built:
  - `WorshipPlayer.Core` (net8.0)
  - `WorshipPlayer.Audio` (net8.0-windows)
  - `OwcPresenter.App` (net8.0-windows, WPF)

## Startup / launch check

- Code-path review of the startup chain is clean:
  - `App.xaml` sets `StartupUri="MainWindow.xaml"` and merges `Themes/OwcTheme.xaml`. The theme compiled successfully as part of the build, so the merged dictionary and its `NavTabButton` / `NavTabButtonSelected` styles resolve at runtime (no `FindResource` failure expected).
  - `MainWindow` constructor only calls `InitializeComponent()` and sets Home content on the `Loaded` event. No file IO, database, or service construction runs at startup, so there is **no unhandled startup-exception path in code**.
- **Visual launch could NOT be verified in this environment.** This is a headless/non-interactive agent environment with no display, so `OwcPresenter.App` was not actually run and the window was not eyeballed. The user must launch it to confirm the UI (see checklist below).

## Screenshot-check CHECKLIST (user to run)

Launch `OwcPresenter.App` and compare against `OWC-Presenter-v2\docs\UI-SPEC.md`:

- [ ] **Header tab cluster is centered** — `○ Live · Services · Songs · Slides · Media · Scripture` sit in the middle of the header (not under the logo).
- [ ] **Green accent `#3DDC84`** used throughout (logo mark, selected tab, primary button, Online dot). NOT cyan and NOT pink.
- [ ] **Quick start = 6 buttons** — Tour, Services, Songs, Slides, Media, Scripture.
- [ ] **Upcoming services** — dashed-border empty state ("You have no upcoming services. Add a new service to get started!") with a green **+ Add a new service** button centered below.
- [ ] **Left panel empty state** — dashed box, centered text "To begin, select a service from the Services list. Your service items will appear here."
- [ ] **Right column order (top → bottom)**: Main Audience Output (dropdown) → black 16:9 preview → icon row (volume/message/timer/box/snapshot) → media player ("No media", `00:00 ──── 00:00`, ◀ ▶) → Stage Display message block.
- [ ] **Bottom status bar** shows green **● Online** at the right.

## Version control

- Repo: `c:\Users\Quang\Documents\OneWorshipcenter\OWC-Presenter-v2` (git already initialized, branch `main`).
- New commit added this step: `feat: add Core/Audio projects and UI spec (Phase 0+1)` — stages the previously-untracked `WorshipPlayer.Core/`, `WorshipPlayer.Audio/`, and `docs/` folders.
- Prior commit: `feat: add OwcPresenter.App WPF UI shell (Phase 1)`.
- Working tree is clean. **Nothing was committed into the old repo.**

## What Phase 2+ will add

- Tab views wired to Core: `SongManager` / `SongLibraryService` for Songs, `BibleDatabaseService` for Scripture, `ServiceOrderManager` for Services (replacing the current placeholder views).
- Output / audience window (second-screen presentation) plus the live Main Audience Output preview.
- Backtrack / audio engine wiring from `WorshipPlayer.Audio` (AudioEngine, MultiTrackMixer, TrackPlayer, beat/MIDI cue) into the media player and transport controls.
