# OWC Presenter — Feature Status Map

Read-only investigation. No code was modified. All citations are `file:line` from the current working tree. The solution **builds clean** (`dotnet build WorshipBacktrackPlayer.sln` → `Build succeeded. 0 Error(s)`, 4 warnings, net8.0-windows, WPF + ASP.NET Core Kestrel + LibVLCSharp).

## Summary — overall maturity and composition

The app is a **.NET 8 WPF desktop** solution (`WorshipBacktrackPlayer.sln`) with five projects: `WorshipPlayer.Core` (models + services, no UI), `WorshipPlayer.Audio` (NAudio/SoundTouch backtrack engine), `WorshipPlayer.Presenter` (presenter views, services, the Kestrel stage/remote web server), `WorshipPlayer.UI` (the WinExe entry point — the Backtrack Player's `MainWindow`), and `WorshipPlayer.Tests` (xUnit, ~40 test files). The executable is `WorshipPlayer.UI` (`App.xaml` → `MainWindow`); the Presenter is launched **from inside the Backtrack Player** via `PresenterIntegration.OpenPresenterWindow()`, which constructs `OperatorWindow` and injects the service objects (`PresenterIntegration.cs:218-250`).

**The operator window is the real composition point.** `OperatorWindow` exposes top-nav tabs that are actually reachable: **Home**, **Services** (a services-list screen + the slide-grid editor), **Songs**, **Scripture**, **Media**, and a **Tools** dropdown (Alert, Countdown, Global Search, CCLI Report), plus a **Settings** window. These are wired in `SwitchToTab(...)` (`OperatorWindow.xaml.cs:1575-1660`) and the Tools handlers (`OperatorWindow.xaml.cs:1750-1830`).

**Maturity is uneven and the headline risk is "backend built, UI orphaned."** The live-service spine is mature: the backtrack→slide engine, dual congregation/stage rendering, the slide grid, congregation output, and especially the **web/iPad stage display with per-musician personalization** are implemented, wired, and tested. But a large tier of Priority-2/3 "builder" UI exists only as **orphaned XAML user controls that are never instantiated**: `ServiceGroupPanel`, `BulkEditPanel`, `ItemSettingsPanel`, `SongSlideEditor`, `SongMetadataEditor`, `ChordProImportDialog`, `PrintExportDialog`, `ImportMediaDialog`, `MediaMetadataPanel`, `ScriptureSlidePreview`, and the entire `Templates/` editor (`TemplateEditorWindow`, `TemplateManagerPanel`, 3 tabs). A whole-repo search for `new <Panel>()` finds **zero** constructions of these outside their own code-behind (grep across `**/*.cs` and `**/*.xaml`). Several have fully working Core services behind them (templates, ChordPro, media) — the gap is wiring, not algorithms. Finally, the **10 Settings sections are static placeholders**: none reference `SettingsService` or `AppSettings` (grep across `Views/Settings/*.cs` → no matches); only ScreenConfig feeds two display indices back on Save (`SettingsWindow.xaml.cs:95-105`).

Entry/launch chain verified: `App.xaml.cs` → `MainWindow` → `PresenterIntegration` (`PresenterIntegration.cs:66-123` constructs ServiceOrderManager, PresenterEngine, DisplayManager, StageDisplayServer, SongLibraryService, MediaLibraryService, GlobalSearchService, ExportService, AlertService, CountdownService, SongUsageService, BibleDatabaseService). Note which services are **not** constructed here: `TemplateService`, `ChordProParserService` (except inside the stage server), `CcliReportService`, `ScriptureSlideGenerator`, cloud sync/API, and video services (those live in `OutputWindow`).

---

## Feature status table

Status legend: **DONE** = implemented and reachable by the user; **PARTIAL** = real code, incomplete or not wired; **STUB** = exists but empty / fake data / no-op; **MISSING** = no meaningful code.

| Feature ID | Name | Status | Evidence (file:line) | What remains |
|---|---|---|---|---|
| C2 | Service order groups (collapsible) | PARTIAL | Model `ServiceGroup.cs` (full) + `ServiceOrder.Groups` (`ServiceOrder.cs:48-49`); UI `ServiceGroupPanel.xaml` exists but **never instantiated** (no `new ServiceGroupPanel`); `ServiceOrderManager` has **no** group logic (grep: no `Groups`/`ServiceGroup` hits) | Wire a group panel into the sidebar; add create/collapse/assign logic in `ServiceOrderManager`; render groups in `RefreshServiceOrderList` |
| C3 | Bulk edit (multi-select batch) | PARTIAL | `BulkEditPanel.xaml(.cs)` exists, **orphaned** (never constructed) | Add multi-select to the sidebar, instantiate the panel, implement batch-apply against items |
| C4 | Per-item auto-advance (Go To / After) | PARTIAL | Model complete: `ServiceItemSettings.cs` (AutoAdvanceGoTo/AfterCondition/DelayMs/GoToItemId), `ServiceItem.AutoAdvanceSettings` (`ServiceOrder.cs:147`); `ItemSettingsPanel.xaml` **orphaned**; no engine logic consumes these settings | Wire ItemSettingsPanel; implement timer/audio-end/video-end driven advance in `PresenterEngine` |
| C6 | Import from Planning Center | MISSING | Repo-wide search for `PlanningCenter`/`PCO`/`planning center` finds only roadmap docs, no code | Build PCO API client + importer from scratch |
| C7 | Import from ProPresenter | MISSING | Repo-wide search for `ProPresenter`/`propresenter` finds only docs | Build ProPresenter file/setlist parser + importer from scratch |
| D2 | Song slide WYSIWYG editor | PARTIAL | `SongSlideEditor.xaml(.cs)` exists, **orphaned** (never constructed) | Instantiate from Songs tab; wire per-slide text/background/template editing + persistence to `song.json` |
| D3 | CCLI SongSelect online import | MISSING | No code; explicitly out-of-scope per `presenter-offline-hardening/design.md:616`; only doc references | Requires online API + key; net-new |
| D4 | Song metadata editor | PARTIAL | `SongMetadataEditor.xaml(.cs)` exists, **orphaned** | Instantiate + wire fields to `Song` model and `song.json` save |
| D5 | ChordPro (.chopro) import | PARTIAL | Parser is **fully implemented**: `ChordProParserService.cs` (parse UG/ChordPro/plain, transpose, Nashville, timestamps). Dialog `ChordProImportDialog.xaml(.cs)` exists but **orphaned**; service **not constructed** in `PresenterIntegration` | Wire dialog to Songs tab; construct `ChordProParserService`; write parsed sections to a song |
| E2 | Full offline Bible DB (66 books) | PARTIAL | All **66 books' metadata** seeded (`BibleSeedData.cs:289-359` GetBookData); verse **text only for 4 chapters**: Genesis 1, Psalm 23, John 3, Romans 8, **KJV only** (`BibleSeedData.cs:93-285`); WEB translation row exists but **0 verses** (`BibleSeedData.cs:55-64`); only `SeedKjvVersesAsync()` runs at startup (`PresenterIntegration.cs:166-176`) | Load full KJV + WEB verse text (bulk import); currently ~0.03% of verses present |
| E3 | Scripture auto-split into slides | PARTIAL/DONE | Core `ScriptureSlideGenerator.cs` is full (LinesPerSlide, verse numbers, options) **but not referenced by the wired panel**; the reachable `ScriptureBrowserPanel` splits internally. The orphaned `ScriptureSlidePreview.xaml.cs:59-116` has its own splitter | Confirm the wired ScriptureBrowserPanel path produces slides to output; consider using the Core generator |
| E4 | Scripture templates | MISSING/PARTIAL | No scripture-specific template application wired; template system itself is orphaned (see G1) | Depends on G1/G7 template wiring |
| E5 | Reference / translation display options | PARTIAL | Options exist in `ScriptureSlideGenerator.SlideGenerationOptions` (ShowReference/ShowTranslationName) and in orphaned `ScriptureSlidePreview` checkboxes (`ScriptureSlidePreview.xaml.cs:67-102`) | Expose toggles in the wired ScriptureBrowserPanel and persist as defaults |
| F2 | Import media (file picker) | DONE | Wired: `MediaLibraryPanel.ImportRequested` → `OnMediaImportRequested` → OpenFileDialog → `MediaLibraryService.ImportAsync` (`OperatorWindow.xaml.cs:1695-1740`); service copies file + sidecar metadata (`MediaLibraryService.cs:51-90`). (Uses the simple dialog; the richer `ImportMediaDialog` is orphaned) | None for basic import; optional: wire the richer dialog |
| F3 | Filter panel (type/tags) | PARTIAL | Service supports it (`MediaLibraryService.GetByTypeAsync`/`GetByTagAsync`/`SearchAsync`); whether `MediaLibraryPanel` UI exposes type/tag filters needs the panel's own controls (service-side DONE, UI filter controls not confirmed present) | Add/confirm filter UI in MediaLibraryPanel bound to the service methods |
| F4 | Assign media to slide | MISSING | No drag-to-assign or set-background-per-slide path found in the wired grid; thumbnails render text only (`OperatorWindow.xaml.cs:1331-1451`) | Implement media→slide background assignment + persistence |
| G1 | Template editor (3 tabs) | PARTIAL | `TemplateEditorWindow.xaml` + `FormattingTab`/`AdvancedTab`/`MarginsTab` exist; opened only by `TemplateManagerPanel` (`TemplateManagerPanel.xaml.cs:60`), which is itself **orphaned** (never instantiated) | Add a reachable Templates entry point (nav/settings) that opens TemplateManagerPanel |
| G5 | Template management (CRUD) | PARTIAL | `TemplateService.cs` **fully implements** GetAll/Create/Update/Delete/Duplicate + JSON persistence; service **not constructed** anywhere in the app wiring | Construct `TemplateService`; surface the manager panel |
| G6 | Built-in templates (Default/Lower Third/Trendy Line) | DONE (defined) / PARTIAL (reach) | All three defined with full styling in `TemplateService.GetBuiltInTemplates()` (`TemplateService.cs:159-230`) | Reachable only once G1/G5 UI is wired |
| L2 | Defaults settings | STUB | `DefaultsSection.xaml.cs` is a placeholder (only a slider label handler); no `SettingsService`/`AppSettings` use (grep: none) | Bind controls to settings load/save |
| L3 | Footer / CCLI settings | STUB | `FooterSection` placeholder; model `FooterSettings.cs` exists but section does not read/write it | Bind FooterSettings; apply footer to output |
| L4 | Interface settings | STUB | `InterfaceSection` placeholder; `InterfaceSettings.cs` model exists, unused by the section | Bind + persist |
| L5 | Bibles settings (import XML, manage) | STUB | `BiblesSection.xaml.cs`: translation list **hardcoded** (not read from DB); `ImportXmlButton_Click` only shows a MessageBox — **no XML parsing** (`BiblesSection.xaml.cs:41-55`); no XML import anywhere in Core (grep `XDocument`/`XmlReader` → none) | Implement XML Bible parser + DB import; read installed translations from the DB |
| L7 | Stage Display layout editor | STUB/MISSING | `StageDisplaySection` placeholder; `StageDisplayLayout.cs` model exists; no drag-block editor | Build the block-arrangement editor; persist `StageDisplayLayout` |
| L8 | Library & Database settings | STUB | `LibrarySection` placeholder; no media-path/reset wiring to `MediaLibraryService.GetLibraryPath()` | Bind to real paths + reset actions |
| L9 | MIDI settings (command→note) | STUB | `MidiSection` placeholder; model `MidiCommandMapping.cs` + `MidiCueService` exist but section doesn't bind them | Bind the 6-command mapping table to real MIDI config |
| L10 | Updates & cache | STUB | `UpdatesCacheSection` placeholder; no version/update/cache logic | Implement version read, cache mgmt (auto-update likely out of scope) |
| H4 | Per-musician web views | DONE | `StageDisplayServer` parses `/stage?capo=&nashville=&view=&name=` and per-client WebSocket config, then personalizes via `MusicianViewRenderer.CreateMusicianData` (`StageDisplayServer.cs:105,197-227,588-599,696-700`) | None (web path). No separate WPF per-musician window |
| I3 | Capo display | DONE | `MusicianViewRenderer.RenderForMusician` applies `CapoTranspose` (`MusicianViewRenderer.cs:43-46`); `PresenterEngine.GetCapoText` (`PresenterEngine.cs:583-589`); tests `CapoTransposeTests.cs` | None |
| I4 | Nashville numbers | DONE | `ChordProParserService.ConvertToNashvilleNumbers` + degree mapping; wired via renderer + engine; tests `NashvilleNumberTests.cs` | None |
| I5 | One/two-column chart views | DONE | `view=twocol` honored server-side + full two-column CSS/JS in `StageDisplayHtml.cs:95,143-189,268-280,378-382` | None (web path) |
| I8 | Stage display layout editor | MISSING | Same as L7 — no block editor exists | Build editor (duplicate of L7) |
| H7 | NDI output (real) | PARTIAL/STUB | `NdiOutputService` is wired in `OperatorWindow` (`:852-869`) and captures frames via `OutputCaptureService`, **but** only a `StubNdiOutput` is attached (`OperatorWindow.xaml.cs:856`); the stub discards frames (`StubNdiOutput.cs:95-109`) — no real NDI SDK | Integrate NewTek NDI SDK + P/Invoke; replace the stub |
| J4 | CCLI auto-submit / report | PARTIAL | Usage **tracking is DONE** (`SongUsageService` logs on load, `PresenterIntegration.cs:195-205`); `CcliReportDialog` + report aggregation wired (`OperatorWindow.xaml.cs:1835-1860`); `CcliReportService.cs` exists. **Auto-submit to CCLI API = MISSING** | Export is reachable; API auto-submit is net-new (online) |
| 11.1 | Slide backgrounds in thumbnails | MISSING | `RefreshSlideThumbnails` renders section label + stripped text + number only; no background image draw (`OperatorWindow.xaml.cs:1331-1451`) | Render per-slide background image/color behind thumbnail text |
| 11.5 | Drag-to-reorder slides | MISSING | No drag handlers in the grid (grep `Drag`/`AllowDrop`/`PreviewMouseMove` → none in OperatorWindow) | Add drag-reorder + persist new order |
| 11.5 | Per-slide edit/delete on hover | MISSING | No hover pencil/X affordance in thumbnail build | Add hover overlay with edit/delete actions |
| 11.10 | Pop-out preview windows | MISSING | `AudiencePreview_Click`/`StagePreview_Click` are explicit placeholders (`OperatorWindow.xaml.cs:983-991`) | Implement detached preview windows |
| M1 | User account menu | MISSING | No account/login code; roadmap Month 2-3 | Net-new (cloud) |
| M2 | Cloud song library API | STUB | Interfaces only: `ISongLibraryApi`, `IServicePlanApi`, DTOs under `Models/Cloud` — **no concrete HTTP implementation** | Implement API client against a backend |
| M3 | Desktop sync service | STUB | `DesktopSyncService.cs` implements workflow/state machine but all upload/download are **comment stubs**, "no HTTP calls (no backend yet)" (`DesktopSyncService.cs:5-11,84-100`) | Add real HTTP transfer against M2 |
| M4 | Team/user management | MISSING | No code | Net-new (cloud) |
| M5 | Community marketplace | MISSING | No code | Net-new (cloud) |
| M6 | Subscription/billing (Stripe) | MISSING | No code | Net-new (cloud) |
| H8 | AirPlay/Miracast | MISSING (research only) | Only `Docs/MiracastResearch.md`; no implementation | Net-new |
| 7.1 | Motion backgrounds (video loops) | PARTIAL | `VideoBackgroundService` (LibVLCSharp) full; invoked by theme path (`OutputWindow.xaml.cs:226-229`), **but** `BackgroundVideoView` is a `ContentControl` placeholder, not a VLC `VideoView` — code comments "VLC not in XAML" (`OutputWindow.xaml.cs:495-502`, `OutputWindow.xaml:38-41`) so no video actually renders | Host a real LibVLCSharp.WPF `VideoView`; bind the MediaPlayer |
| 7.2 | Video playback as service item | PARTIAL | `VideoPlaybackService` full; `OutputWindow.ShowVideo` exists but `FullScreenVideoView` is also a placeholder ContentControl (`OutputWindow.xaml:249-252`); no service-item type routes to it | Add VideoView surface; route a `Video` service item to `ShowVideo` |

---

## Biggest gaps and risks

1. **Settings are non-functional (L2-L10).** Nine of ten sections are static XAML with zero persistence; `SettingsWindow` only wires ScreenConfig's two display indices (`SettingsWindow.xaml.cs:95-105`). Any feature that "set defaults in settings" depends on first building a load/save bridge from each section to `SettingsService`/`AppSettings`. This is a foundational prerequisite, not a leaf feature.
2. **Bible DB is ~4 chapters, not 66 books of text (E2).** Metadata for all 66 books is present, but only Genesis 1 / Psalm 23 / John 3 / Romans 8 have KJV verse text; WEB has none. The scripture browser can only resolve those references. This is the single largest "advertised but absent" data gap.
3. **A whole UI tier is orphaned.** ServiceBuilder (C2/C3/C4), Song editors (D2/D4), ChordPro dialog (D5), Templates (G1/G5), rich media/scripture dialogs — all exist as XAML+code-behind but are never instantiated. Risk: easy to mistake "file exists" for "feature done."
4. **NDI and video don't actually output (H7, 7.1, 7.2).** NDI runs through a frame-discarding stub; video services load LibVLC but render into `ContentControl` placeholders because no `VideoView` is in XAML. Both are "plumbed but dark."
5. **Import/migration (C6, C7, D3) and all cloud/account features (M1-M6, J4 auto-submit, H8) are genuinely absent** — net-new work, several requiring an online backend that does not exist.

## Closest to done (quick wins)

- **G5/G6 + G1 template wiring.** `TemplateService` is complete with persistence and the 3 built-ins defined; `TemplateManagerPanel`/`TemplateEditorWindow`/tabs all exist. Remaining work is mostly construction + a reachable entry point.
- **D5 ChordPro import.** `ChordProParserService` is complete and tested; only the orphaned dialog needs wiring and the service constructing.
- **F3 media filter.** `MediaLibraryService` already exposes type/tag/search; needs filter controls bound in the panel.
- **C4 per-item settings UI.** Data model is complete; wiring `ItemSettingsPanel` + a modest engine advance-rule is tractable.

## Requires large new work

E2 full Bible text import (+ L5 XML importer); C6/C7 PCO/ProPresenter importers; D3 CCLI SongSelect; H7 real NDI SDK integration; 7.1/7.2 real VideoView hosting; all of M1-M6 (cloud, accounts, sync transport, marketplace, Stripe); H8 AirPlay/Miracast; L7/I8 drag-block stage-layout editor; and the 11.x slide-grid interactions (background thumbnails, drag-reorder, hover edit/delete, pop-out previews).

## Notes / caveats

- "Reachable" is judged from `OperatorWindow.SwitchToTab`, the Tools menu handlers, and `SettingsWindow.GetSection`; a control with no `new X()` anywhere is reported orphaned.
- F3's service layer is confirmed; the panel's filter *controls* were not individually opened, so F3 is marked PARTIAL rather than DONE.
- The app's primary entry is the Backtrack Player; the Presenter opens as a child window. Audio-driven auto-advance lives in the Backtrack Player, consistent with `.agents/tasks/demo-theblessing.md`.
