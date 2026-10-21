# Implementation Plan — Remove blocking AI-Training-Data startup prompt

## Goal (verbatim scope)
Stop the app from popping the folder-picker titled "Select folder for AI Training Data (section corrections will be saved here)" on every launch. Default the training-data folder to a path that always exists, create it, persist it, and keep the AI-training feature working. Code only; don't break anything; don't reset data.

## Verified root cause (read from source in this worktree)

- `WorshipPlayer.Core/Models/AppSettings.cs:47-48` — `TrainingDataFolder` is initialized to `@"E:\temp\OWClogs"`:
  ```csharp
  [JsonPropertyName("trainingDataFolder")]
  public string TrainingDataFolder { get; set; } = @"E:\temp\OWClogs";
  ```
  The `E:` drive does not exist on this machine, so `Directory.Exists(...)` returns false.

- `WorshipPlayer.UI/Views/MainWindow.xaml.cs:62-87` (inside `MainWindow_Loaded`) — on EVERY launch, when the folder is empty or missing, it opens a blocking `OpenFolderDialog`, and on cancel re-bakes `@"E:\temp\OWClogs"`:
  ```csharp
  // ─── FIRST LAUNCH: AI Training Data folder prompt ─────────────────
  if (string.IsNullOrEmpty(_settings.TrainingDataFolder) || !Directory.Exists(_settings.TrainingDataFolder))
  {
      var dialog = new Microsoft.Win32.OpenFolderDialog
      {
          Title = "Select folder for AI Training Data (section corrections will be saved here)"
      };
      if (dialog.ShowDialog(this) == true)
      {
          _settings.TrainingDataFolder = dialog.FolderName;
          await _settingsService.SaveAsync();
      }
      else
      {
          _settings.TrainingDataFolder = @"E:\temp\OWClogs";   // <-- missing drive, so prompt returns next launch
      }
  }

  // Ensure folder exists
  if (!string.IsNullOrEmpty(_settings.TrainingDataFolder))
  {
      try { Directory.CreateDirectory(_settings.TrainingDataFolder); } catch { }
  }

  _trainingDataService = new TrainingDataService(_settings.TrainingDataFolder);   // line ~89
  ```
  Because the cancel/fallback path writes a missing-drive path and never persists a valid one, the check at line 63 fails again on the next launch and the dialog reappears forever.

- `WorshipPlayer.Core/Services/TrainingDataService.cs:20-26` — the service only needs a valid, writable folder. `IsConfigured => !string.IsNullOrEmpty(_logFolderPath) && Directory.Exists(_logFolderPath)` (line 20). Constructor takes the folder path (line 24). So feeding it a safe, created folder keeps the feature fully working.

## Exact default-path expression to use
Use the user's Documents folder, which always resolves on Windows:
```csharp
Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "OWC", "TrainingData")
```
Rationale: `SpecialFolder.MyDocuments` always resolves to an existing, writable per-user location (no dependency on an `E:` drive that may be absent), matches the task's recommended path, and `Directory.CreateDirectory` safely creates the `OWC\TrainingData` subtree. A pure static helper is added in Core so it is unit-testable without WPF.

## Build / test environment (verified)
- Solution: `WorshipBacktrackPlayer.sln` at worktree root. Projects: `WorshipPlayer.Core` (net8.0), `WorshipPlayer.Audio` (net8.0-windows), `WorshipPlayer.Presenter` (net8.0-windows), `WorshipPlayer.UI` (net8.0-windows WPF, `UseWPF`), `WorshipPlayer.Tests` (net8.0, xUnit 2.6.6, references Core only + links selected source files).
- The Tests project targets `net8.0` and references **only** `WorshipPlayer.Core`; it cannot reference the WPF `WorshipPlayer.UI`. Therefore the new default-path logic must live in `WorshipPlayer.Core` to be unit-testable. `MainWindow.xaml.cs` is verified by the full solution build, not a unit test.
- Existing test pattern to follow: `WorshipPlayer.Tests/SettingsServiceTests.cs` (xUnit `[Fact]`, `Assert.Equal`).

Commands (run from `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\no-training-prompt`):
- Build everything: `dotnet build WorshipBacktrackPlayer.sln -c Debug`
- Run tests: `dotnet test WorshipBacktrackPlayer.sln -c Debug`

Expected outcome: build succeeds with 0 errors (pre-existing warnings OK); all tests pass (green), including the new Core test.

---

## Ordered implementation items

- [ ] 1. Add a pure default-path helper in Core.
      Add a static helper that returns the safe default training-data folder, so both AppSettings and MainWindow resolve the same path and it is unit-testable. Create `WorshipPlayer.Core/Services/TrainingDataPaths.cs` with a static class `TrainingDataPaths` exposing `public static string DefaultFolder()` returning `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OWC", "TrainingData")`. Also add `public static bool IsUsable(string? path)` returning `!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)` (used by the UI to detect a stale/missing-drive setting as "unset"). Do not create any directory in the helper (keep it pure).
      Files: `WorshipPlayer.Core/Services/TrainingDataPaths.cs`
      Verify: `dotnet build WorshipPlayer.Core/WorshipPlayer.Core.csproj -c Debug` — 0 errors.

- [ ] 2. Change the AppSettings default away from the missing-drive path.
      In `WorshipPlayer.Core/Models/AppSettings.cs:48`, change the `TrainingDataFolder` initializer from `@"E:\temp\OWClogs"` to `TrainingDataPaths.DefaultFolder()`. Add `using WorshipPlayer.Core.Services;` at the top if not already present (file currently has only `using System.Text.Json.Serialization;`). Keep the `[JsonPropertyName("trainingDataFolder")]` attribute unchanged so persisted settings keep the same JSON key. Do not touch any other property in this file.
      Files: `WorshipPlayer.Core/Models/AppSettings.cs`
      Verify: `dotnet build WorshipPlayer.Core/WorshipPlayer.Core.csproj -c Debug` — 0 errors.

- [ ] 3. Replace the blocking startup prompt in MainWindow_Loaded with silent default + create + persist.
      In `WorshipPlayer.UI/Views/MainWindow.xaml.cs`, replace the entire block at lines 62-87 (the `// ─── FIRST LAUNCH: AI Training Data folder prompt ───` comment through the end of the `if (string.IsNullOrEmpty(...) || !Directory.Exists(...))` block that contains the `OpenFolderDialog` and the `E:\temp\OWClogs` fallback) with a NON-blocking block that: if `!TrainingDataPaths.IsUsable(_settings.TrainingDataFolder)` (empty, whitespace, or a directory that doesn't exist — this also covers a stale saved path on a missing drive), set `_settings.TrainingDataFolder = TrainingDataPaths.DefaultFolder();` and `await _settingsService.SaveAsync();`. NO `OpenFolderDialog`, NO `E:\temp\OWClogs`, NO `ShowDialog`. Add `using WorshipPlayer.Core.Services;` if not already imported (it is already imported in this file). Leave the existing "Ensure folder exists" block (lines ~83-87, `Directory.CreateDirectory(...)` in try/catch) and the `_trainingDataService = new TrainingDataService(_settings.TrainingDataFolder);` line (~89) exactly as they are — they still create the folder and construct the service with a valid path.
      Suggested replacement:
      ```csharp
      // AI Training Data folder: resolve silently, never prompt on startup.
      if (!TrainingDataPaths.IsUsable(_settings.TrainingDataFolder))
      {
          _settings.TrainingDataFolder = TrainingDataPaths.DefaultFolder();
          await _settingsService.SaveAsync();
      }
      ```
      Files: `WorshipPlayer.UI/Views/MainWindow.xaml.cs`
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` — 0 errors; no remaining reference to `"Select folder for AI Training Data"` or `E:\temp\OWClogs` in this file.

- [ ] 4. Add a Core unit test for the default-path helper.
      Add `WorshipPlayer.Tests/TrainingDataPathsTests.cs` following the xUnit `[Fact]` pattern in `SettingsServiceTests.cs`. Cover: (a) `DefaultFolder()` returns a non-empty path that ends with `OWC\TrainingData` and is rooted under `Environment.GetFolderPath(SpecialFolder.MyDocuments)`; (b) `IsUsable(null)`, `IsUsable("")`, and `IsUsable(@"E:\temp\OWClogs")` (nonexistent drive/path) all return false; (c) `IsUsable(<a real temp dir created in the test>)` returns true (clean up the temp dir in the test). Do not assert that `DefaultFolder()` exists on disk (the helper is pure and does not create it).
      Files: `WorshipPlayer.Tests/TrainingDataPathsTests.cs`
      Verify: `dotnet test WorshipBacktrackPlayer.sln -c Debug --filter FullyQualifiedName~TrainingDataPathsTests` — new tests pass.

- [ ] 5. Full build and test gate.
      Build the whole solution and run the full test suite to confirm nothing regressed.
      Files: (none — verification only)
      Verify: `dotnet build WorshipBacktrackPlayer.sln -c Debug` → 0 errors (pre-existing warnings OK); then `dotnet test WorshipBacktrackPlayer.sln -c Debug` → all tests green.

---

## What must NOT change
- Do NOT modify audio engine, Python analysis, or metronome/click code beyond the single startup path in `MainWindow_Loaded` and the one default constant in `AppSettings.cs` (plus the new pure Core helper and its test).
- Do NOT delete or reset `app.db`, `settings.json`, songs, setlists, or any user data. The fix only writes a valid folder path into settings; it never clears existing data.
- Do NOT change OWC branding, window titles, or other dialogs. The Songs-folder prompt (lines ~91-115) and any Settings-UI "Browse" for the training folder stay as-is — only the forced STARTUP training-data prompt is removed.
- Keep the AI-training feature functional: `TrainingDataService` must still be constructed with a valid, existing folder (guaranteed by the silent default + `Directory.CreateDirectory`), so `IsConfigured` is true and `SaveTrainingDataAsync` keeps working.

## Notes / assumptions
- A previously saved setting pointing at a missing drive (e.g., `E:\temp\OWClogs`) is treated as unset because `TrainingDataPaths.IsUsable(...)` returns false when `Directory.Exists` fails; the app then silently adopts and persists the new default. No prompt is shown in that case.
- The net8.0 Tests project cannot reference the WPF UI project, so the startup-path change in `MainWindow.xaml.cs` is covered by the solution build rather than a unit test; the testable logic (default path + usability check) was extracted into Core precisely so it can be unit-tested.
