# Verification — Slides "Add Slide" UX (two improvements)

Branch: `feature/slides-add-slide-ux`
Worktree: `.worktrees/slides-add-slide-ux`

## What was changed

### Improvement 1 — open the slide CONTENT editor from the service-item editor
- `WorshipPlayer.Presenter/Views/Slides/SlideServiceItemEditorWindow.xaml`
  - In the SLIDES card, wrapped the existing `Select` button plus a new `EditSlidesButton`
    in a single horizontal `StackPanel DockPanel.Dock="Right"`. New button is placed BEFORE
    `Select` (Margin `0,0,6,0`), uses the keyed `OwcCompactButton` style (same as `Select`),
    `MinWidth=96`, `Content="Edit slides"`, `Click="EditSlides_Click"`. Keyed OWC styles only
    (the window merges `OwcDesignSystem.xaml`). No other card touched.
- `WorshipPlayer.Presenter/Views/Slides/SlideServiceItemEditorWindow.xaml.cs`
  - Added `public event EventHandler? LinkedPresentationSlidesEdited;`.
  - Added `UpdateEditSlidesButtonState(SlidePresentation? linked)`:
    - Enabled only when the item has a linked presentation (`SlidePresentationId` non-empty).
    - **Chosen behavior for no link: DISABLE with tooltip** "Link a presentation with Select first"
      (not hide) — keeps the affordance discoverable and layout stable, steering the user to Select.
    - Label is `"+ Add slides"` when the linked presentation currently has 0 slides, else
      `"Edit slides"`. Never calls `MarkDirty`.
  - Called from `PopulateLinkedPresentationAsync` (both the no-link and resolved branches — runs
    under the ctor/`OnLoaded` `_suppressDirty`) and from `SelectPresentation_Click` (passing the
    chosen presentation, alongside the existing `MarkDirty()`).
  - Added `EditSlides_Click`:
    - Guards on empty `SlidePresentationId`.
    - Resolves the CURRENTLY-LINKED presentation via `_slidePresentationService.GetByIdAsync`
      (the service already held by the ctor — **no new ctor params, no call-site change**). On
      null/exception shows a "could not be found" MessageBox and returns (mirrors
      `OnEditPresentationRequested`).
    - Opens `new SlideEditorWindow(presentation, _slidePresentationService) { Owner = this }`.
    - On `ShowDialog() == true`: re-fetches via `GetByIdAsync` under a save/restore `_suppressDirty`
      guard, updates the Slides-card label + header (only when no pending rename is staged) and the
      button state. **Does NOT set `_isDirty`** — slide content lives on the presentation and was
      already persisted by the inner window via `UpdateAsync`.
    - Raises `LinkedPresentationSlidesEdited`.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `TryOpenSlideServiceItemEditor`
  - Subscribed to `win.LinkedPresentationSlidesEdited` and, through the EXISTING host flow
    (`PrimeSlidePresentationCacheAsync` → `RefreshServiceOrderList` → `RefreshSlideSections` on the
    dispatcher), re-primes/refreshes so added slides show immediately even if the user later
    Closes (discards) the service-item editor. The existing post-dialog save/prime/refresh block is
    unchanged (remains the Save path; idempotent). No duplicated refresh logic.

### Improvement 2 — actionable empty-state message
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs` — `RefreshSlideSections`, `slides.Count == 0`
  branch. The note selection is now three cases:
  1. Song → unchanged lyrics message.
  2. Slides item WITH a link (`item.Type == ServiceItemType.Slides && !IsNullOrEmpty(SlidePresentationId)`)
     → "This presentation has no slides yet. Open Edit and click + Add slides to create one."
  3. otherwise → unchanged generic "No slides to show for this item."
  - The `TextBlock` construction is unchanged (FontSize 11, `Foreground = new SolidColorBrush(TextSecondary)`,
    same Margin, `TextWrapping`). **INLINE BRUSHES ONLY — no `StaticResource`/`ResourceDictionary` added to
    OperatorWindow** (verified: no new StaticResource token in the diff).

Optional clickable empty-state note: intentionally OUT of scope per plan D7. The static actionable
message plus the in-editor button fully resolve "where do I add slides".

## Invariants honored
- Live-link integrity: the button edits the linked presentation BY ID; never writes
  `SlidePresentationId` and never snapshots slides onto the service item. Only `Select` changes the link.
- `_suppressDirty` discipline preserved: population and the post-inner-save refresh run under
  `_suppressDirty=true` (save/restore), so slide edits never arm the service-item dirty flag.
- Dirty-gating and remove/save mutual exclusion in the service-item editor unchanged.
- OperatorWindow stays StaticResource-free (inline brushes only).

## Build + test evidence
- Command: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` (from the worktree root).
  - Result: **Build succeeded**, exit 0, **6 warning(s)** — all pre-existing/known:
    CS4014 OperatorWindow.xaml.cs(728), CS0414 MediaLibraryPanel._currentView,
    CS0414 WaveformEditorWindow._inReviewMode, CS8604 ChordChartWindow.xaml.cs(235).
    No new warnings from the changed code.
- Command: `dotnet test --nologo`.
  - Result: **total 842, failed 0, succeeded 842, skipped 0**. (The "Error loading service order"
    / invalid-start-of-property lines are expected negative-path test fixture output, not failures.)
- NEVER ran clean / deleted bin/obj. Incremental build only.

## Verified vs deferred to manual
- Verified automatically: compilation of the new XAML + code-behind, no new warnings, full suite
  green (guards model/dirty-logic regressions).
- Deferred to manual (headless WPF run not performed here) — expected visuals, section by section:
  - SLIDES card: with a linked 0-slide presentation the new button reads **"+ Add slides"** and is
    enabled, sitting immediately left of **Select**; with a linked non-empty presentation it reads
    **"Edit slides"**; with no link it is **disabled** and shows tooltip "Link a presentation with
    Select first".
  - Clicking the button opens **SlideEditorWindow** for the item's currently-linked presentation.
  - Adding a slide + "Save and close" in the inner window returns to the service-item editor with
    "Save and close" still **disabled** (no dirty armed); the Slides-card label stays correct and the
    button flips to "Edit slides".
  - Closing (discarding) the service-item editor still shows the newly-added slide in the center grid
    (host re-prime via `LinkedPresentationSlidesEdited`).
  - Center-grid empty state for a linked 0-slide Slides item reads the new actionable text; OWC dark
    theme/branding intact (keyed styles in the editor, inline brushes in the center note).
