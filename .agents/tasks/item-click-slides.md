# Findings: Clicking a Song service-item shows no slides

Repo: `owc-multitrack-feature-lyric-presenter` (main HEAD b4c33e1). .NET 8 WPF (OWC / WorshipBacktrackPlayer).
Investigation is READ-ONLY — no code was changed.

---

## (a) Summary answer — why clicking a Song item shows no slides

When the operator single-clicks a Song row in the SERVICE ORDER sidebar, the slide grid is driven **entirely by `ServiceItem.Slides`** — never by the item's linked library song. The chain is:

1. `ServiceOrderList_SelectionChanged` → `_serviceOrderManager.GoToItem(index)` then `RefreshSlideThumbnails()`
   (`WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs:1429-1449`).
2. `GoToItem` raises `ItemChanged`, handled by `PresenterEngine.OnServiceItemChanged`, which sets the engine's slide list **directly from the item**:
   `_currentSlides = e.NewItem.Slides ?? new List<Slide>()` (`WorshipPlayer.Presenter\Services\PresenterEngine.cs`, `OnServiceItemChanged`, ~line 843+ — the `// Load slides for the new service item` block).
3. `RefreshSlideThumbnails` reads `_engine.TotalSlides` (= `_currentSlides.Count`). If it is `0` it shows the empty-state text and returns (`OperatorWindow.xaml.cs:1832-1841`).

For a `TheBlessing`-style item, `ServiceItem.Slides` is an **empty list** (`ServiceOrder.cs:133` — `Slides` defaults to `new()`), even though `ServiceItem.SongFolder` links to a real library song. So `TotalSlides == 0`, `EmptyStateText.Visibility = Visible`, and the grid shows:
> "Load a song in the backtrack to see slides here. Or click Load Service in the sidebar."

**Nothing in the Song-item selection path ever resolves `SongFolder` to a `Song` or generates lyric slides.** Slide generation from a song only happens through `PresenterEngine.GenerateSlidesFromSong`, which is private and reached only via `LoadSong` / `LoadStandaloneSong` — and those are called **only** from the Songs tab (`OnSongSelected`), never from sidebar item selection.

---

## (b) Evidence (file:symbol:line)

### Sidebar selection handler — no song resolution, no generation
`OperatorWindow.xaml.cs:1429` `ServiceOrderList_SelectionChanged`:
```
int index = items.IndexOf(display.Item);
if (index >= 0)
{
    _serviceOrderManager?.GoToItem(index);   // -> ItemChanged -> OnServiceItemChanged
    RefreshSlideThumbnails();                 // reads engine slide list only
}
```
It does not look at `display.Item.Type`, does not touch `SongFolder`, and never calls a generator. `MouseDoubleClick` (`:1450`) just opens the edit dialog (`EditSelectedServiceItem`), unrelated to slides.

### Engine loads item.Slides verbatim
`PresenterEngine.cs` `OnServiceItemChanged` (the `// Load slides for the new service item` block):
```
_currentSlides   = e.NewItem.Slides ?? new List<Slide>();
_currentSlideIndex = _currentSlides.Count > 0 ? 0 : -1;
```
For a Song item with no pre-baked slides, `_currentSlides` is empty → `TotalSlides == 0`.

### Empty-state condition
`OperatorWindow.xaml.cs:1832` `RefreshSlideThumbnails`:
```
if (_engine == null || _engine.TotalSlides == 0)
{
    EmptyStateText.Visibility = Visibility.Visible;   // the message the user sees
    return;
}
EmptyStateText.Visibility = Visibility.Collapsed;      // only when slides exist
```

### Slides ARE meant to be generated from the song (not pre-baked)
- `ServiceItem.Slides` doc comment: "For songs, these are auto-generated from lyrics." (`ServiceOrder.cs:128-134`).
- `SongSlideEditor` doc: "slides are generated from sections at display time by LyricSlideGenerator; the only persisted lyric unit is Section.Text" (`SongLibrary\SongSlideEditor.xaml.cs:11-13`).
- `PresenterEngine.GenerateSlidesFromSong` builds slides from `song.Sections[].Text` via `_chordParser.ExtractSlideBlocks`, assigns timing, publishes to `_currentSlides` under `_stateLock`.

So the design intent is **generate-on-demand from the linked song**, and the Song-item path simply never invokes it.

### The working template already exists — the Songs tab
`OperatorWindow.xaml.cs:2365` `OnSongSelected`:
```
var song = SongPathResolver.Resolve(_songManager.Songs, songItem.Id, songItem.Title);
if (song != null)
    _engine.LoadStandaloneSong(song);
```
`PresenterEngine.LoadStandaloneSong` (PresenterEngine.cs) unconditionally calls `GenerateSlidesFromSong(song)`, fires `SongLoaded`, then fires `SlideChanged` for slide 0.

### Scripture template (E3) — same center grid
`OperatorWindow.xaml.cs:2431` `OnScripturePresentRequested` → `_engine.LoadScriptureSlides(slides)`. `PresenterEngine.LoadScriptureSlides` sets `_currentSlides`/`_currentSlideIndex` under `_stateLock` and fires `SlideChanged` for slide 0. That `SlideChanged` is handled by `OnSlideChanged` (`:620`) for previews/output, but note the grid itself is repainted elsewhere (see the risk in (d)).

### Song resolution handles both `Songs\TheBlessing` and `TheBlessing`
`SongPathResolver.Resolve` (`WorshipPlayer.Core\Services\SongPathResolver.cs:57`) tries, in order: normalized full-path match, **leaf folder-name** match (`Path.GetFileName` of the trimmed folder → matches a bare `TheBlessing`), then Title. `Normalize` (`:24`) collapses separators/casing. So passing `item.SongFolder` ("Songs\\TheBlessing" or "TheBlessing") plus `item.Title` resolves correctly. The model consumed downstream is confirmed present: `Song.Sections` is `List<Section>` (`Song.cs:56`) and `Section.Text` holds the lyrics (`Song.cs:198`) — exactly what `GenerateSlidesFromSong` and `LyricSlideGenerator.GenerateFromSong` read.

### `LyricSlideGenerator` is currently dead code in the app
A repo-wide search shows `LyricSlideGenerator` is never instantiated outside its own file. The live generation path is the engine's private `GenerateSlidesFromSong`. This matters for the fix: prefer the engine entry point over standing up a new generator.

---

## (c) On-demand vs persisted

Generate **on-demand** at selection time from the linked song. This matches the stated design (`ServiceOrder.cs:128-134`, `SongSlideEditor.xaml.cs:11-13`: Section.Text is the only persisted lyric unit), matches the Songs-tab and Scripture flows, and avoids staleness when a song's lyrics are edited. Do not persist generated slides back into `ServiceItem.Slides`.

---

## (b-fix) Exact, minimal wiring

Mirror `OnSongSelected`. In `ServiceOrderList_SelectionChanged` (`OperatorWindow.xaml.cs:1429`), after `GoToItem`, when the selected item is a Song that produced no slides, resolve + load it through the engine:

```csharp
int index = items.IndexOf(display.Item);
if (index >= 0)
{
    _serviceOrderManager?.GoToItem(index);   // existing

    // NEW: a Song item carries no pre-baked slides -> generate from its linked library song.
    if (display.Item.Type == ServiceItemType.Song
        && _engine != null && _songManager != null
        && _engine.TotalSlides == 0)
    {
        var song = SongPathResolver.Resolve(
            _songManager.Songs, display.Item.SongFolder, display.Item.Title);
        if (song != null)
            _engine.LoadStandaloneSong(song);   // generates slides + fires SlideChanged
    }

    RefreshSlideThumbnails();                  // existing — now sees TotalSlides > 0
}
```

Why these exact calls:
- `display.Item.Type == ServiceItemType.Song` — `ServiceItemType.Song` is the first enum value (`ServiceOrder.cs:179-182`); guard so Scripture/Media/etc. are untouched.
- `_engine.TotalSlides == 0` — only generate when the item has no pre-baked slides, preserving the F2/F3 "ServiceItem slides are the single source of truth" rule documented on `PresenterEngine.LoadSong`. If a Song item ever does carry slides, this is a no-op.
- `SongPathResolver.Resolve(..., display.Item.SongFolder, display.Item.Title)` — identical resolution to `OnSongSelected`; handles `Songs\TheBlessing`, `TheBlessing`, and title fallback (`SongPathResolver.cs:57`).
- `_engine.LoadStandaloneSong(song)` — the existing unconditional generate-and-emit entry point (`PresenterEngine.cs`). It calls the private `GenerateSlidesFromSong` and fires `SlideChanged` for slide 0. Prefer it over a new `LyricSlideGenerator` so timing/chord handling stays identical to the Songs tab.
- Keep the trailing `RefreshSlideThumbnails()`; it now reads `TotalSlides > 0` and paints the grid.

Alternative (functionally equivalent) placement: resolve in the handler and call `LoadScriptureSlides(generatedSlides)` after generating with a `LyricSlideGenerator(_engine.Settings...theme)`. Not recommended — it duplicates generation logic that `LoadStandaloneSong`/`GenerateSlidesFromSong` already own and would diverge from the Songs tab.

Null/empty guards to include: `_songManager` may be null early in startup (already null-checked in `OnSongSelected`); `song == null` → leave the empty state (optionally `Debug.WriteLine` like `OnSongSelected` does); a resolved song with no `Sections[].Text` legitimately yields zero slides and the empty state remains (correct).

---

## (d) Risks and how to avoid regressions

1. **Thumbnail grid may not repaint after `LoadStandaloneSong` alone.** `LoadStandaloneSong` fires only `SongLoaded` and `SlideChanged`. `OnSongLoaded` (`OperatorWindow.xaml.cs:728`) updates only key/capo text; `OnSlideChanged` (`:620`) calls `UpdateSlideThumbnailHighlights`, which **only recolors existing borders and never rebuilds the grid** (`:2017` — it iterates `SlideThumbnailsPanel.Children` and sets brushes). So the grid is populated by the **explicit `RefreshSlideThumbnails()`** call. Keep that call after `LoadStandaloneSong` (the snippet above does). This is also why the Songs tab currently relies on whatever later refresh occurs — in the sidebar path we call it directly, which is correct.

2. **Ordering: generate before `RefreshSlideThumbnails`.** `LoadStandaloneSong` sets `_currentSlides`/`_currentSlideIndex` synchronously (under `_stateLock`), so a synchronous `RefreshSlideThumbnails()` immediately afterward sees the new slides. Do not reorder.

3. **F2/F3 source-of-truth coupling.** The `_engine.TotalSlides == 0` guard is essential: unconditionally calling `LoadStandaloneSong` would override any pre-baked item slides and contradict the documented `LoadSong` contract in `PresenterEngine.cs`. Gating on empty slides keeps existing behavior for items that do have slides.

4. **Backtrack coupling is independent.** `LoadStandaloneSong` does not start the backtrack engine, so clicking a Song item to preview slides will not play audio — matching the Songs-tab behavior and the user's "ProPresenter-like" expectation. Auto-advance timing from `ChartSlide`/`Sections` is still assigned by `GenerateSlidesFromSong`, so later backtrack playback can drive navigation if started separately.

5. **`_isEditMode` / selection highlighting.** `RefreshSlideThumbnails` resets `_selectedSlideIndex` only when not in edit mode (`:1857`). The new path runs in performance mode (single-click navigation); no change needed, but keep the guard so edit-mode selection isn't clobbered.

6. **Scripture/Media items unaffected.** The `Type == Song` guard ensures Scripture (E3 path via `OnScripturePresentRequested`/`LoadScriptureSlides`) and other types keep their current behavior.

---

## Conclusion / recommendation

Clicking a Song item shows no slides because selection loads `ServiceItem.Slides` (empty) directly into the engine and never resolves the linked library song. The minimal, low-risk fix is to mirror the existing, working Songs-tab flow inside `ServiceOrderList_SelectionChanged`: when a `ServiceItemType.Song` row has `TotalSlides == 0`, resolve the song via `SongPathResolver.Resolve(_songManager.Songs, item.SongFolder, item.Title)` and call `_engine.LoadStandaloneSong(song)` before the existing `RefreshSlideThumbnails()`. Generate on-demand (do not persist). Guard on `Type == Song` and `TotalSlides == 0` to protect pre-baked slides and other item types.
