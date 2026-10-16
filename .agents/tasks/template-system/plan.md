# Implementation Plan — Template System (G1, G5, G6)

Wire the already-complete template backend into the operator UI so template management (list built-ins, create, duplicate, rename, delete, edit via the 3-tab editor) is reachable and persists to disk.

## Verified facts (read from code, correcting the task brief)

- `TemplateService` is at `WorshipPlayer.Presenter\Services\TemplateService.cs`, namespace `WorshipPlayer.Presenter.Services`, implements `WorshipPlayer.Core.Services.Interfaces.ITemplateService`. API is **async** (`GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `DuplicateAsync`, ...). Constructor: `TemplateService(string templatesFolder)`.
- Built-ins are **auto-seeded without duplication** already: `EnsureLoadedAsync()` (TemplateService.cs) always adds `GetBuiltInTemplates()` first, then loads `*.json` from the folder skipping any whose `Id` already exists. **Do NOT add any manual seeding/merge** — acceptance #4 is satisfied by the service itself. Just construct the service with a valid folder path.
- `TemplateService` is never constructed in app wiring (confirmed: no `new TemplateService(` outside the Presenter service + tests).
- Composition point: `WorshipPlayer.UI\Services\PresenterIntegration.cs`. All services are built in the constructor (lines ~96-123, e.g. `MediaLibraryService = new MediaLibraryService(mediaPath)`) and handed to the operator window in `OpenPresenterWindow()` via `_operatorWindow.SetServices(...)` (lines ~206-218).
- `OperatorWindow.SetServices(...)` is at `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs:473` and stores services into private fields declared at ~lines 89-98.
- Tools dropdown XAML: `ToolsContextMenu` in `WorshipPlayer.Presenter\Views\OperatorWindow.xaml:193-198` (items Alert/Countdown/Search/CCLI Report). Tool handlers are grouped around OperatorWindow.xaml.cs:1749-1828; `ToolSearch_Click` (~1784) is the pattern to mirror — it hosts a `UserControl` panel inside a `new Window { ... Owner = this }` and calls `ShowDialog()`.
- `TemplateManagerPanel` (`WorshipPlayer.Presenter\Views\Templates\TemplateManagerPanel.xaml.cs`) exposes a public `ITemplateService? TemplateService` setter that auto-refreshes the list. It is orphaned (no `new TemplateManagerPanel()` anywhere).
- **Persistence gap**: `TemplateEditorWindow.SaveButton_Click` (TemplateEditorWindow.xaml.cs) copies control values into the in-memory `_template` but never calls `CreateAsync`/`UpdateAsync`. `TemplateManagerPanel.NewButton_Click` opens the editor but never persists. `RenameButton_Click` only raises an unconsumed `EditTemplateRequested` event. So create/rename/edit currently do NOT reach disk. Duplicate and delete DO call the service already.
- `SlideTemplate.Id` (`WorshipPlayer.Core\Models\SlideTemplate.cs:12`) defaults to a fresh Guid (not empty), so "empty Id ⇒ new" is NOT reliable; use an explicit new-vs-edit flag.

## Build / test commands (verified)

- Build: `dotnet build "<worktree>\WorshipBacktrackPlayer.sln" -c Debug` — currently succeeds, 0 errors (4 pre-existing warnings).
- Backend tests: `dotnet test "<worktree>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj" --filter "FullyQualifiedName~TemplateServiceTests"` — 20 pass.

`<worktree>` = `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\template-system`

---

## Plan

- [ ] 1. Add a `_templateService` field and a `TemplateService` parameter to `OperatorWindow.SetServices`, storing the passed service.
      Add `private ITemplateService? _templateService;` beside the other service fields (OperatorWindow.xaml.cs ~line 90-98). Add an `ITemplateService? templateService` parameter to `SetServices` (declaration at OperatorWindow.xaml.cs:473) and assign `_templateService = templateService;` in the body (~line 485-497). Order the new parameter last so existing positional callers are easy to update.
      Files: WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs
      Verify: `dotnet build ...WorshipBacktrackPlayer.sln -c Debug` — expect 0 errors (there will be one call site to fix in step 3; build after step 3).

- [ ] 2. Construct `TemplateService` in `PresenterIntegration` and expose it.
      Add `public ITemplateService TemplateService { get; }` to the "Additional services" property group (PresenterIntegration.cs ~lines 44-52). In the constructor, after the other service constructions (~line 118-123), add `var templatesPath = Path.Combine(appFolder, "Templates"); TemplateService = new TemplateService(templatesPath);`. (`using WorshipPlayer.Presenter.Services;` and `using WorshipPlayer.Core.Services.Interfaces;` are already imported.) The service lazily creates the folder, so no directory pre-creation is required.
      Files: WorshipPlayer.UI\Services\PresenterIntegration.cs
      Verify: part of the step-3 build.

- [ ] 3. Pass the constructed `TemplateService` into the operator window.
      In `PresenterIntegration.OpenPresenterWindow()`, update the `_operatorWindow.SetServices(...)` call (~lines 206-218) to pass `TemplateService` as the new trailing argument.
      Files: WorshipPlayer.UI\Services\PresenterIntegration.cs
      Verify: `dotnet build ...WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

- [ ] 4. Add the "Templates" entry point to the Tools dropdown and its handler.
      In OperatorWindow.xaml, add `<MenuItem Header="Templates" Click="ToolTemplates_Click" />` inside `ToolsContextMenu` (xaml:193-198), after the CCLI Report item. In OperatorWindow.xaml.cs, add a `ToolTemplates_Click` handler next to `ToolCcliReport_Click` (~line 1811-1828) that builds a `TemplateManagerPanel`, sets `panel.TemplateService = _templateService` (guarded for null), hosts it in a `new Window { Title = "Templates", Width = 720, Height = 560, Owner = this, WindowStartupLocation = CenterOwner, Background = #0F1117 }` exactly like `ToolSearch_Click`, and calls `ShowDialog()`. Wrap in try/catch calling `ShowToolError("Templates", ex.Message)`. Add `using WorshipPlayer.Presenter.Views.Templates;` if not already present.
      Files: WorshipPlayer.Presenter\Views\OperatorWindow.xaml, WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs
      Verify: `dotnet build ...WorshipBacktrackPlayer.sln -c Debug` — 0 errors. Manual: Tools → Templates opens a window listing the 3 built-ins (Default Centered, Lower Third, Trendy Line) with lock icons.

- [ ] 5. Make the template editor persist to the service on Save (create vs update).
      Add a `public bool IsNewTemplate { get; set; }` to `TemplateEditorWindow`. Change `SaveButton_Click` (TemplateEditorWindow.xaml.cs) to, after gathering control values into `_template`, call the service: if `IsNewTemplate` set `_template.Id = string.Empty;` then `await _templateService.CreateAsync(_template)`, else `await _templateService.UpdateAsync(_template)`. Make the handler `async void`, guard `_templateService == null`, wrap the await in try/catch with a `MessageBox` on failure, and only set `WasSaved = true; DialogResult = true; Close();` after a successful save.
      Files: WorshipPlayer.Presenter\Views\Templates\TemplateEditorWindow.xaml.cs
      Verify: `dotnet build ...WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

- [ ] 6. Wire the manager's New / Rename(Edit) buttons to the persisting editor.
      In `TemplateManagerPanel.NewButton_Click`, set `editor.IsNewTemplate = true;` before `ShowDialog()`. Replace the body of `RenameButton_Click` (and consume the existing `EditTemplateRequested` intent) so it opens `TemplateEditorWindow` for the selected non-built-in template: fetch the full `SlideTemplate` via `await _templateService.GetByIdAsync(selected.Id)`, set `editor.IsNewTemplate = false; editor.Template = fetched; editor.TemplateService = _templateService; editor.Owner = Window.GetWindow(this);`, `ShowDialog()`, then `await RefreshTemplatesAsync()` if saved. Make `RenameButton_Click` `async void`. Relabel the button to "Edit" in TemplateManagerPanel.xaml (optional, clearer UX) or keep "Rename".
      Files: WorshipPlayer.Presenter\Views\Templates\TemplateManagerPanel.xaml.cs (and optionally .xaml)
      Verify: `dotnet build ...WorshipBacktrackPlayer.sln -c Debug` — 0 errors. Manual: New → edit → Save adds a custom template that survives reopening the Templates window; Edit on a custom template persists changes; Duplicate/Delete still work; built-ins remain locked.

- [ ] 7. Confirm the backend contract still holds and the whole solution builds.
      No backend edits expected; this is a regression gate.
      Files: (none)
      Verify: `dotnet test "<worktree>\WorshipPlayer.Tests\WorshipPlayer.Tests.csproj" --filter "FullyQualifiedName~TemplateServiceTests"` — 20 pass; and `dotnet build ...WorshipBacktrackPlayer.sln -c Debug` — 0 errors.

## Notes / assumptions

- Persistence folder chosen as `<AppBase>\Templates`, consistent with sibling services (`Media`, `ServiceOrders`, `song-usage.json`) that all hang off `AppDomain.CurrentDomain.BaseDirectory`.
- Service is passed typed as `ITemplateService` to match the panel/editor properties and the interface-typed style of `SetServices`.
- Acceptance #4 (built-ins on first run, no duplication) requires NO new code — it is already guaranteed by `TemplateService.EnsureLoadedAsync`. Do not add seeding logic.
- Keep all changes scoped to template wiring + the one Tools entry; do not touch unrelated panels or features.
