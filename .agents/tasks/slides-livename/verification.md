# Verification — Slides linked item LIVE DISPLAYED NAME

## Commands run (in the worktree root)

Worktree: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slides-livename`

### 1. Build

```
dotnet build WorshipBacktrackPlayer.sln -c Debug
```

Result: **Build succeeded. 0 Error(s), 4 Warning(s)** (all 4 pre-existing, unrelated to this change):
- `MediaLibraryPanel._currentView` assigned but never used (CS0414) — counted once per project build pass.
- `ChordChartWindow` CS8604 possible null arg.
- `WaveformEditorWindow._inReviewMode` CS0414.

### 2. Tests

```
dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj -c Debug
```

Result: **Passed! Failed: 0, Passed: 671, Skipped: 0, Total: 671, Duration: 16 s**

Baseline was ~665; +6 from the new `ServiceItemDisplayNameTests`. No regressions.

## Change trace (file:line)

### New pure Core helper
- `WorshipPlayer.Core/Services/ServiceItemDisplayName.cs` —
  `ResolveDisplayName(ServiceItem item, SlidePresentation? linked)`:
  returns `linked.Name.Trim()` when `item.Type == Slides` AND `SlidePresentationId` non-empty AND
  `linked != null` AND `linked.Name` non-blank; otherwise returns stored `item.Title`. Throws
  `ArgumentNullException` for null `item`. UI-free, mirrors `SlidePresentationMapper` style.

### New unit tests (6 facts)
- `WorshipPlayer.Tests/ServiceItemDisplayNameTests.cs`: linked→name(trimmed); missing(null)→Title;
  blank name→Title; Slides w/ no link id→Title; non-Slides(Song)→Title; null item→throws.

### Sidebar row title (DISPLAY-time resolution)
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` `MakeItemRow(ServiceItem)`:
  changed from `static` to instance method; `Title = ServiceItemDisplayName.ResolveDisplayName(item, ResolveLinkedPresentation(item))`
  (replaced `Title = item.Title`). Icon mapping unchanged.
- New helper `ResolveLinkedPresentation(ServiceItem)` reads `_slidePresentationCache.GetValueOrDefault(SlidePresentationId)`
  only for Slides items with a link id; returns null otherwise (→ fallback to stored Title). Synchronous
  cache read — no new UI-thread blocking/async.
- All three `MakeItemRow(item)` call sites in `RefreshServiceOrderList` and the
  `MakeItemRow(item).Icon` use in `BuildSectionHeader` compile unchanged as instance calls.

### Center section header title
- `OperatorWindow.xaml.cs` `BuildSectionHeader(ServiceItem)`: title `TextBlock.Text` now uses
  `displayName = ServiceItemDisplayName.ResolveDisplayName(item, ResolveLinkedPresentation(item))`,
  keeping the existing `"(untitled)"` fallback for a blank result. Icon `TextBlock` unchanged.

### Removed stored-Title mutation (don't rewrite stored data)
- `OperatorWindow.xaml.cs` `PrimeSlidePresentationCacheAsync`: removed the
  `foreach (... linkedItem.Title = presentation.Name)` block; the prime now only populates the cache.
  Stored `ServiceItem.Title` is never rewritten.

### Refresh timing — IMMEDIATE (shipped)
- `OnEditPresentationRequested`: after `await PrimeSlidePresentationCacheAsync();` now calls BOTH
  `RefreshServiceOrderList();` (new — rebuilds the sidebar with the live name) and the existing
  `RefreshSlideSections();`. A rename shows in the sidebar AND center header immediately on editor close.
- `OnAddPresentationToServiceRequested`: reordered so prime runs BEFORE `RefreshServiceOrderList()`
  (prime → RefreshServiceOrderList → RefreshSlideSections) so a newly added row shows the live name
  on first paint.

## Safety / scope confirmation
- Only `OperatorWindow.xaml.cs` (code-behind, no XAML — OperatorWindow stays StaticResource-free),
  plus one new pure Core file + its test.
- No change to `ServiceOrder.cs` storage, `SlidePresentationService`, or `SlidePresentation`.
- `app.db` not reset / untouched. OWC branding untouched. Existing events/features preserved.
- Non-Slides items: `ResolveDisplayName` returns `item.Title` → songs/scripture/etc. unchanged.
