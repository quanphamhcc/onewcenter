# Slides feature: rename / go-live / edit — root-cause investigation

READ-ONLY diagnosis. No code was changed. Repo investigated:
`c:\Users\Quang\...\owc-multitrack-feature-lyric-presenter` (branch `main`, HEAD `4bdcb31`).

---

## Summary answer (TL;DR)

The three complaints have **two different root causes**, and most of what the user sees is
actually *expected behavior for presentations that contain zero slides*:

| # | Complaint (VI) | Verdict |
|---|----------------|---------|
| Q1 | "tên ko đổi" — renamed name doesn't show | **REAL BUG.** The rename *is* saved to the DB, but the **sidebar / center / current-item header never refresh with the resolved live name after the presentation cache is primed**, so the old stored `Title` keeps showing. |
| Q2 | "ko thấy show item ở live" — clicking the item shows nothing live | **EXPECTED BEHAVIOR.** The linked presentation has **0 slides** (`"slides": []` in the DB). Nothing can go live because there is nothing to display. The click→live path itself is wired correctly. |
| Q3 | "edit slide cũng ko thấy gì" — editing a slide shows nothing | **EXPECTED BEHAVIOR.** Same 0-slides cause. The editor *does* open; it just opens empty because the presentation has no slides. Possibly compounded by confusion between the two different "Edit" entry points. |

**The one actionable bug is Q1 (rename not refreshing the display name).** Q2 and Q3 are the
direct, correct consequence of the two presentations genuinely having no slides.

### Decisive data (from the live app.db)

`WorshipPlayer.UI\bin\Debug\net8.0-windows\app.db` (the DB the running
`WorshipBacktrackPlayer.exe` uses) contains exactly the two presentations from the screenshot,
**both with empty slide lists**:

```json
{ "id": "182e9409-5784-44cc-a1c7-a97967ec74b6",
  "name": "New Presentation hhhjhjhj",   "slides": [] }
{ "id": "1f846c8d-e733-46e5-9726-70800b38ce6f",
  "name": "New Presentation (Copy)",     "slides": [] }
```

And the service order has one live-linked Slides item that points at the first one:

```json
{ "id": "df83ef88-...", "type": "Slides", "title": "New Presentation",
  "slides": [], "slidePresentationId": "182e9409-5784-44cc-a1c7-a97967ec74b6" }
```

Note three things this proves at once:
1. A rename **was** persisted — the stored presentation name is `New Presentation hhhjhjhj`
   (the "hhhjhjhj" the user typed), so `SlidePresentationService.UpdateAsync` works.
2. The service item's stored `title` is still the OLD `"New Presentation"` — **by design**
   (the live name is meant to be resolved at display time, not written back).
3. Both presentations have `"slides": []` — so "nothing live" and "empty editor" are correct.

---

## Q1 — Rename does not show ("tên ko đổi"): REAL BUG

### Where a rename happens
There is **no standalone "Rename"** in the library grid. The ⋮ overflow menu only offers *Add to
current service / Edit / Duplicate / Delete*
(`WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml`, the `ContextMenu` block ~lines
108–123). Renaming happens in one of two editors:

- **`SlideEditorWindow`** (content editor, opened from the grid ⋮→Edit): `NameTextBox` →
  `SaveAndClose_Click` writes `_presentation.Name` and calls `_service.UpdateAsync`
  (`SlideEditorWindow.xaml.cs`, `SaveAndClose_Click`).
- **`SlideServiceItemEditorWindow`** (per-service-item editor, opened from the sidebar / double
  click): `Rename_Click` / `RenameConfirm_Click` store a *pending* name, applied on
  `SaveAndClose_Click` which loads the linked presentation and calls `UpdateAsync`
  (`SlideServiceItemEditorWindow.xaml.cs`, "Deferred rename" + `SaveAndClose_Click` step (1)).

Both persist correctly — confirmed by the DB name being `New Presentation hhhjhjhj`.

### The display-name design (correct)
The live name of a linked Slides item is resolved **at render time**, never written back onto the
item's `Title`:
- `ServiceItemDisplayName.ResolveDisplayName(item, linked)` returns the linked presentation's
  current `Name` for a Slides item with a non-empty `SlidePresentationId`
  (`WorshipPlayer.Core\Services\ServiceItemDisplayName.cs`).
- The sidebar row uses it: `MakeItemRow` → `ResolveDisplayName(item, ResolveLinkedPresentation(item))`
  (`OperatorWindow.xaml.cs`, `MakeItemRow`).
- `ResolveLinkedPresentation` only reads the in-memory cache
  `_slidePresentationCache.GetValueOrDefault(id)` (`OperatorWindow.xaml.cs`,
  `ResolveLinkedPresentation`). If the cache has no entry, it returns null and the display **falls
  back to the stored `Title`** ("New Presentation").
- `PrimeSlidePresentationCacheAsync` is the only thing that fills that cache; it `Clear()`s and
  reloads every linked presentation (`OperatorWindow.xaml.cs`, `PrimeSlidePresentationCacheAsync`).

So the whole scheme depends on **priming the cache and THEN rebuilding the sidebar**.

### The bug: initial / navigation refresh rebuilds the sidebar BEFORE the cache is primed, and never again
In `OnItemChanged` (fires on every service open and item navigation —
`OperatorWindow.xaml.cs`, `OnItemChanged`):

1. `CurrentItemTitle.Text = e.NewItem?.Title` — uses the **raw stored Title**, not the resolved
   name. (Top of the handler.)
2. `RefreshServiceOrderList()` is called **synchronously** while `_slidePresentationCache` is still
   whatever it was (empty on first load). The Slides row therefore renders the fallback Title.
3. Only afterwards, a deferred `Dispatcher.InvokeAsync(async () => { await
   PrimeSlidePresentationCacheAsync(); RefreshSlideSections(); ... })` primes the cache — but it
   **only calls `RefreshSlideSections()` (center grid), NOT `RefreshServiceOrderList()`**. The
   sidebar row is never rebuilt after the cache is populated, so it keeps showing "New
   Presentation".

That asymmetry is the bug: the center-grid section header will eventually show the renamed name
(it is rebuilt after the prime), but the **sidebar row label and the `CurrentItemTitle` header
stay on the stale stored Title**. From the user's point of view the rename "didn't take".

The only paths that DO refresh the sidebar correctly (prime → `RefreshServiceOrderList()` →
`RefreshSlideSections()`) are the ones that run right after an edit action:
- `OnEditPresentationRequested` (grid ⋮→Edit return): primes then calls both refreshes
  (`OperatorWindow.xaml.cs`, `OnEditPresentationRequested`). ✅
- `OnAddPresentationToServiceRequested`: primes then both refreshes. ✅
- `TryOpenSlideServiceItemEditor` save/remove flow: primes then both refreshes. ✅

So a rename made in `SlideEditorWindow` *is* reflected immediately when that window closes.
But as soon as the service is re-opened, the app restarts, or the user navigates to another item
and back — `OnItemChanged` runs and the sidebar reverts to the stale Title because of the missing
`RefreshServiceOrderList()` after the prime. This matches "I renamed it but the name doesn't
change" on a fresh launch.

### Secondary rename gap
`CurrentItemTitle.Text = e.NewItem?.Title` (`OnItemChanged`, first line) always shows the stored
`Title`, never `ResolveDisplayName(...)`. Even once the sidebar is fixed, the big "current item"
header would still show the old name for a linked Slides item.

**Q1 ROOT CAUSE:** rename is persisted correctly, but the live display name is resolved from
`_slidePresentationCache`, and the cache-priming in `OnItemChanged` happens *after* the sidebar is
built and is **not** followed by a `RefreshServiceOrderList()`. The sidebar (and the
`CurrentItemTitle` header, which uses raw `Title` unconditionally) therefore keep showing the stale
stored `Title`. **Real bug.**

---

## Q2 — Clicking the item doesn't go live ("ko thấy show item ở live"): EXPECTED (0 slides)

### The click→live path is correctly wired
- Single-click a sidebar row → `ServiceOrderList_SelectionChanged` → `_serviceOrderManager.GoToItem(index)`
  (`OperatorWindow.xaml.cs`, `ServiceOrderList_SelectionChanged`). `GoToItem` fires `ItemChanged`
  → engine loads that item's slides.
- Clicking a thumbnail in the center grid → the `onClick` in `RefreshSlideSections` does
  `_serviceOrderManager?.GoToItem(capturedItemIndex); _engine?.GoToSlide(idx);`
  (`OperatorWindow.xaml.cs`, inside `RefreshSlideSections`).

### Why nothing appears: there are no slides to show
For a live-linked Slides item, slides are resolved on demand:
`GetSlidesForItem` → when `item.Type == Slides && SlidePresentationId` is set, it reads the primed
cache and returns `SlidePresentationMapper.ToSlides(presentation)`
(`OperatorWindow.xaml.cs`, `GetSlidesForItem`). `ToSlides` returns an **empty list for an empty
`Slides` collection** (`SlidePresentationMapper.cs`, `ToSlides(List<PresentationSlide>?)` — early
`return result;` on null/empty).

The linked presentation `182e9409…` has `"slides": []` (DB evidence above). So:
- `GetSlidesForItem` returns an empty list → `RefreshSlideSections` renders the empty-state note
  **"No slides to show for this item."** (`OperatorWindow.xaml.cs`, `RefreshSlideSections`, the
  `slides.Count == 0` branch).
- There are no thumbnails to click, and `GoToItem`/`GoToSlide` have no slide to push to the output
  window — so Live legitimately shows nothing.

This is the screenshot's "0 slides" made literal. There is **no defect in the click→live path**;
the item simply has no content.

**Q2 ROOT CAUSE:** the linked presentation contains zero slides, so the item correctly renders the
"No slides to show for this item." note and has nothing to push live. **Expected behavior, not a
bug.** (The only soft issue is discoverability: an empty presentation gives no strong prompt to
"add slides first.")

---

## Q3 — Editing a slide shows nothing ("edit slide cũng ko thấy gì"): EXPECTED (0 slides)

There are two different "Edit" entry points, which is itself a likely source of confusion:

1. **Grid ⋮ → Edit** → `OnEditPresentationRequested` → opens **`SlideEditorWindow`** (the slide
   *content* editor). It `GetByIdAsync`-loads the presentation; if found it opens the window
   (`OperatorWindow.xaml.cs`, `OnEditPresentationRequested`). The window's ctor runs
   `RebuildRows()` over `_presentation.Slides`; with an empty list `_rows` is empty, the list box is
   blank, `SlideTextBox` is disabled, and the placeholder shows
   (`SlideEditorWindow.xaml.cs`, ctor + `RebuildRows` + `LoadSelectedSlideText`). The window **opens
   fine — it is just empty** because there are no slides. The user must click **"+ Add Slide"**
   (`AddSlide_Click`) to create one.

2. **Sidebar "Edit" / double-click a service item** → `EditSelectedServiceItem` →
   `TryOpenSlideServiceItemEditor` → opens **`SlideServiceItemEditorWindow`**
   (`OperatorWindow.xaml.cs`, `EditSelectedServiceItem` / `TryOpenSlideServiceItemEditor`). This
   editor edits the *service item's* properties (linked presentation, background source, audio,
   auto-advance, template, transition, rename). It does **not** show individual slide text/content
   at all — so a user expecting to "edit the slide" here will see "nothing" about slide content by
   design. To edit slide content they must use path (1).

In both cases the window opens; neither "fails to open". With 0 slides, path (1) is correctly empty
and path (2) never shows slide content.

**Q3 ROOT CAUSE:** the editor opens correctly; it is empty because the presentation has zero slides
(same cause as Q2), and the two separate "Edit" surfaces (content editor vs service-item editor)
make an already-empty editor more confusing. **Expected behavior, not a crash/bug.**

---

## Cross-cutting: where data lives, and could a recent change have wiped it?

- **Storage:** `SlidePresentationService` persists presentations as rows in the shared SQLite
  `app.db`, table `SlidePresentations` (`WorshipPlayer.Core\Services\SlidePresentationService.cs`,
  `CreateSchemaSql`). Each row keeps full JSON in `DataJson` plus promoted `Name`/`SlideCount`.
- **DB path:** `AppDatabase.DefaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
  "app.db")` (`WorshipPlayer.Core\Services\AppDatabase.cs`, `DefaultPath`). For the running app that
  resolves to `WorshipPlayer.UI\bin\Debug\net8.0-windows\app.db` (the only `app.db` on disk, and
  `WorshipBacktrackPlayer.exe` lives in that folder).
- **Schema init is additive / non-destructive:** `EnsureInitializedAsync` runs only
  `CREATE TABLE IF NOT EXISTS` + `CREATE INDEX IF NOT EXISTS`; there is **no DROP/reset and no
  legacy-import branch** (`SlidePresentationService.cs`, `EnsureInitializedAsync` + class remarks).
- **The SQLite-source-of-truth-for-songs change** (`a9dbb1c feat: make SQLite the source of truth
  for songs`) touches the songs path, not `SlidePresentations`, and does not reset `app.db`.

Because `app.db` lives under `bin\…\`, a **Clean / Rebuild or deleting bin can wipe it**, which
would reset presentations to empty. But that is **not what happened here**: the DB still contains
both presentations with the user's typed name "New Presentation hhhjhjhj". The presentations were
simply **created without ever adding slides** — nobody clicked "+ Add Slide" in `SlideEditorWindow`.
So the 0-slides state is user-created content, not a data-loss event.

---

## Conclusions

1. **Q1 is the only real bug.** Rename persists, but the live display name resolved from
   `_slidePresentationCache` is not re-applied to the sidebar (and the `CurrentItemTitle` header
   uses the raw stored `Title` unconditionally). The post-prime refresh in `OnItemChanged` rebuilds
   only the center grid, not the sidebar.
2. **Q2 and Q3 are expected behavior** caused by both presentations having **zero slides**. The
   click→live path and both editors are wired correctly; there is simply no slide content.
3. **No data loss / no DB reset occurred.** The rename is in the DB; the presentations were just
   never given any slides.

## Recommendations (NOT implemented — read-only pass)

1. **Fix Q1 refresh asymmetry (primary):** in `OnItemChanged`, after the deferred
   `await PrimeSlidePresentationCacheAsync()`, also call **`RefreshServiceOrderList()`** (not just
   `RefreshSlideSections()`), so the sidebar row re-resolves the renamed live name once the cache is
   populated. Equivalently, prime the cache *before* the synchronous `RefreshServiceOrderList()` on
   service open.
2. **Fix the current-item header:** replace `CurrentItemTitle.Text = e.NewItem?.Title` with
   `ServiceItemDisplayName.ResolveDisplayName(e.NewItem, ResolveLinkedPresentation(e.NewItem))` so
   the big header also shows the live name for linked Slides items.
3. **Reduce the Q2/Q3 confusion (UX, not a defect):**
   - Make the empty-state note for a linked Slides item actionable, e.g. "This presentation has no
     slides yet — open Edit and click + Add Slide." (It currently says only "No slides to show for
     this item.")
   - Consider surfacing a clear path from the service-item editor to the slide-content editor, since
     the two "Edit" surfaces are easy to confuse.
4. **Harden data durability (optional):** `app.db` lives under `bin\…`, so a Clean/Rebuild can wipe
   all presentations/songs. Consider moving `AppDatabase.DefaultPath` to a stable per-user app-data
   folder so rebuilds never discard user data. (Not the cause of the current report, but a latent
   risk.)

---

### Evidence index (file : symbol)

- `WorshipPlayer.Core\Services\SlidePresentationService.cs` — `EnsureInitializedAsync` (additive
  schema, no reset), `UpdateAsync`/`UpsertAsync` (rename persists), `GetAllAsync`.
- `WorshipPlayer.Core\Services\ServiceItemDisplayName.cs` — `ResolveDisplayName` (live name rule).
- `WorshipPlayer.Core\Services\SlidePresentationMapper.cs` — `ToSlides` (empty in → empty out),
  `ToServiceItemLink` (link stores no slides).
- `WorshipPlayer.Core\Services\AppDatabase.cs` — `DefaultPath` (BaseDirectory/app.db).
- `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs` — `OnItemChanged` (the Q1 refresh
  asymmetry + raw `Title` header), `RefreshServiceOrderList`/`MakeItemRow`/`ResolveLinkedPresentation`
  (sidebar name resolution), `PrimeSlidePresentationCacheAsync` (cache fill), `GetSlidesForItem`
  (on-demand resolve), `RefreshSlideSections` (0-slides note + thumbnail click→GoToItem),
  `OnEditPresentationRequested`/`OnAddPresentationToServiceRequested`/`TryOpenSlideServiceItemEditor`
  (the paths that DO refresh correctly), `EditSelectedServiceItem` (sidebar Edit routing).
- `WorshipPlayer.Presenter\Views\Slides\SlidesLibraryPanel.xaml(.cs)` — ⋮ menu (no Rename),
  `LoadAsync`/`Refresh` (grid rebind).
- `WorshipPlayer.Presenter\Views\Slides\SlideEditorWindow.xaml.cs` — ctor/`RebuildRows`/`AddSlide_Click`
  (content editor opens empty when 0 slides), `SaveAndClose_Click` (rename persists).
- `WorshipPlayer.Presenter\Views\Slides\SlideServiceItemEditorWindow.xaml.cs` — `Rename_Click`/
  `RenameConfirm_Click`/`SaveAndClose_Click` step (1) (deferred rename persists).
- Live DB: `WorshipPlayer.UI\bin\Debug\net8.0-windows\app.db` — two presentations
  (`New Presentation hhhjhjhj`, `New Presentation (Copy)`) both `"slides": []`; service item
  `df83ef88…` `type:Slides`, `slidePresentationId: 182e9409…`, `title: "New Presentation"`.
