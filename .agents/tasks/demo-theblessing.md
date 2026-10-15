# Demo: Loading "The Blessing" from the Songs tab (OWC Presenter Command Center)

## Summary answer (read this first)

The Songs-tab path **is wired** end to end — selecting a song calls
`OperatorWindow.OnSongSelected` → `PresenterEngine.OnSongLoaded(song)` → `LoadSong(song)`.
But **it is silently blocked at startup by the F2/F3 "ServiceItem slides are the source of
truth" guard.** On launch the app auto-loads the bundled service order
`ServiceOrders/2026-08-03_Sunday Morning.json`, whose first item **"Open The Heavens" has 4
pre-baked slides**, and makes it the current item. When you then pick **The Blessing** from the
Songs tab, `LoadSong` sees `CurrentItem.Slides.Count > 0`, so it **skips
`GenerateSlidesFromSong` and skips firing `SlideChanged`**. Result: the grid + previews keep
showing Open The Heavens and nothing visibly happens — exactly the reported
"tôi chọn thebless… nhưng ko có gì xảy ra".

- Chord-over-lyric rendering on the STAGE preview is fine (the recent fix). It just never gets
  the chance to render The Blessing because the slides are never generated.
- Audio-driven auto-advance lives in the separate **Worship Backtrack Player (MainWindow)**, not
  in this window. A no-audio manual demo (NEXT/PREV) is the right way to demo The Blessing.
- **One-line fix** (see §7): in `OnSongSelected`, advance off the current service item before
  loading, so the fallback slide generator runs. Then the simple click sequence in §6 works.

---

## Evidence (file : symbol)

### Q1 — Songs tab selection path (WIRED)
- `WorshipPlayer.Presenter/Views/SongLibrary/SongLibraryPanel.xaml.cs`
  - `SongList_SelectionChanged` fires `SongSelected?.Invoke(this, song)` where `song` is a
    `SongListItem` whose `Id` comes from `ISongLibraryService.GetAllAsync()`.
- `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`
  - `SwitchToTab("Songs")` lazily creates the panel and subscribes:
    `_songLibraryPanel.SongSelected += OnSongSelected;`
  - `OnSongSelected(sender, songItem)`:
    ```csharp
    if (_engine != null && !string.IsNullOrEmpty(songItem.Id)) {
        var song = _songManager?.Songs.FirstOrDefault(s => s.FolderPath == songItem.Id);
        if (song != null) _engine.OnSongLoaded(song);
    }
    ```
  - So selection does reach the engine. It is **not** just a list highlight. The exact method is
    `OnSongSelected` → `PresenterEngine.OnSongLoaded` (alias of `LoadSong`).
- Id/FolderPath match is correct: `SongLibraryService.MapToLibraryItem` sets
  `Id = song.FolderPath`, and `OnSongSelected` matches on `s.FolderPath == songItem.Id`.

### Q2 — Slide generation + the F2/F3 guard (THIS IS THE GAP)
- `WorshipPlayer.Presenter/Services/PresenterEngine.cs` `LoadSong`:
  ```csharp
  bool itemHasSlides = _serviceOrderManager.CurrentItem?.Slides?.Count > 0;
  ...
  if (!itemHasSlides) GenerateSlidesFromSong(song);   // fallback only
  SongLoaded?.Invoke(...);
  if (!itemHasSlides && _currentSlides.Count > 0 && _currentSlideIndex >= 0)
      SlideChanged?.Invoke(...);                       // slide 0 emit, fallback only
  ```
  For a standalone song opened from the Songs tab, the fallback runs **only if the current
  ServiceItem has zero slides.** `GenerateSlidesFromSong` itself is correct for The Blessing — it
  iterates `song.Sections`, calls `ChordProParserService.ExtractSlideBlocks(section.Text)`
  (splits on blank lines), and builds `SlideType.Lyrics` slides, pulling per-slide timing from
  `song.ChordChart.Slides` when present.
- The blocking current item is real:
  - `WorshipPlayer.UI/Services/PresenterIntegration.cs`
    - ctor is given the shared `SongManager`; `AutoLoadMostRecentServiceOrderAsync()` loads the
      newest `ServiceOrders/*.json` on startup.
    - `OnServiceOrderLoaded` → `ServiceOrderManager.GoToItem(0)` makes item 0 current.
  - `ServiceOrders/2026-08-03_Sunday Morning.json` → first item **"Open The Heavens", 4 slides**
    (verified by reading the file). So `CurrentItem.Slides.Count == 4` → `itemHasSlides == true`
    → The Blessing's slides are **never generated** and **no SlideChanged fires**.

### Q3 — SongManager discovery (OK; text survives)
- `WorshipPlayer.Core/Services/SongManager.cs` `LoadAllSongsAsync` scans `Songs/*/song.json`
  (sets `song.FolderPath = dir`) **and** `LoadSongsFromRegistryAsync` reads
  `Songs/_song_registry.json`. The Blessing is reachable both ways (it is a subdir of `Songs/`
  **and** listed in the registry → de-duped by `FolderPath`).
- `Song.Sections` is `[JsonPropertyName("sections")]` and `Section.Text` deserializes straight
  from `song.json`, so the inline `[B] The Lord bless you…` text survives into the model the
  engine consumes.
- Same instance everywhere: `MainWindow` creates one `SongManager`, calls `LoadAllSongsAsync`,
  and passes it to `new PresenterIntegration(...)`, which passes it to both
  `new SongLibraryService(_songManager, …)` and `OperatorWindow.SetServices(..., _songManager)`.
  No instance mismatch.

### Q4 — Chords on preview (OK once slides exist)
- `OperatorWindow.UpdatePreviews`:
  - AUDIENCE: `CurrentSlidePreview.Text`; chords stripped via
    `_previewChordParser.StripChords(...)` when `Settings.CongregationShowChords == false`
    (default), shown otherwise.
  - STAGE: `_engine.GetStageText(current, PitchShiftSemitones)` →
    `_previewChordParser.BuildChordOverLyric(stageText)` → `StagePreviewItems.ItemsSource`
    (green chord row above white lyric row). This is the recent chord-above-lyric fix and is
    correct. It simply never runs for The Blessing today because no slide becomes current.

### Q5 — Audio / auto-advance relationship
- Audio playback and auto-advance stems live in **WorshipPlayer.UI `MainWindow`** (the "Worship
  Backtrack Player"), not in the Presenter Command Center. `MainWindow` owns the `AudioEngine`;
  on playback it raises Section/Position events that `PresenterIntegration` forwards to
  `PresenterEngine.OnSectionChanged / OnPlaybackPositionUpdate / OnMidiCueReceived`, which drive
  auto-advance via `midiCues`/`chordChart` timing.
- The two windows share one process and one `PresenterEngine`/`SongManager`.
  `MainWindow.LoadSong` calls `_presenterIntegration.OnSongLoaded(song)` → same
  `PresenterEngine.LoadSong`. **Important:** when a song is loaded via MainWindow with the Sunday
  service still current, it hits the **same** `itemHasSlides` guard — so even the backtrack path
  won't regenerate The Blessing's slides while "Open The Heavens" is the current item.
- The Blessing's 17 stems (`01_Click.mp3 … 17_LeadVocal.mp3`) exist under `Songs/TheBlessing`,
  so audio auto-advance is possible — **but only from MainWindow**, and only after the slide
  source actually becomes The Blessing. A no-audio manual demo avoids all of that.

---

## 6. Simplest reliable demo (manual NEXT/PREV, no audio)

> As the code stands today, step 3 does nothing visible because of the §2 guard. Apply the §7
> one-line fix first; then this sequence works. The sequence a non-developer follows is:

1. Launch the app. The Command Center (OWC Presenter) opens automatically.
2. Click the **Songs** tab (top navigation).
3. In the song list, click **The Blessing**.
4. The slide grid fills with The Blessing's lyric slides and the AUDIENCE/STAGE previews show
   slide 1 (STAGE shows chords above the lyrics).
5. Press **→** (Right arrow) or **Space**, or click **NEXT**, to step forward slide by slide.
   Press **←** (Left arrow) or **PREV** to go back. Number keys **1–9** jump to a slide.
6. (Optional) Click **GO LIVE** (or press Enter) to push the current slide to the audience output
   once a display is configured; **B** blacks out.

If you must demo **without** the fix: load The Blessing through a **service order whose current
item has no pre-baked slides** (or clear the current item) so the fallback generator runs — but
that is clumsy. The one-line fix below is the right path.

## 7. Minimal fix to make the Songs tab work

The intent of picking a song from the Songs tab is "display THIS song now," which should not be
gated by whatever ServiceItem happens to be current. Make `OnSongSelected` leave the
pre-baked-slide item before loading, so `LoadSong`'s `itemHasSlides` guard is false and the
fallback generator + `SlideChanged` run.

File: `WorshipPlayer.Presenter/Views/OperatorWindow.xaml.cs`, method `OnSongSelected`:

```csharp
private void OnSongSelected(object? sender, SongListItem songItem)
{
    if (_engine != null && !string.IsNullOrEmpty(songItem.Id))
    {
        var song = _songManager?.Songs.FirstOrDefault(s => s.FolderPath == songItem.Id);
        if (song != null)
        {
            _serviceOrderManager?.ClearCurrentItem();   // <-- add: drop the pre-baked ServiceItem
            _engine.OnSongLoaded(song);
        }
    }
}
```

This needs a tiny helper on `ServiceOrderManager` that nulls `CurrentItem`/`CurrentItemIndex`
(and ideally raises `ItemChanged` with `NewItem == null`) so the engine's `itemHasSlides` check
is false. If adding a manager method is undesirable, the equally small alternative is a dedicated
engine entry point for standalone songs:

```csharp
// PresenterEngine
public void LoadStandaloneSong(Song song)   // always generates + emits, ignores CurrentItem
{
    lock (_stateLock) { _currentSong = song; }
    _pitchShiftSemitones = 0; _tempoPercent = 100.0;
    GenerateSlidesFromSong(song);
    SongLoaded?.Invoke(this, new SongLoadedEventArgs { Song = song });
    if (_currentSlides.Count > 0 && _currentSlideIndex >= 0)
        SlideChanged?.Invoke(this, new SlideChangedEventArgs {
            NewSlide = _currentSlides[_currentSlideIndex],
            Transition = SlideTransition.Fade, TransitionDurationMs = 500 });
}
```
…and call `_engine.LoadStandaloneSong(song)` from `OnSongSelected`. This keeps the F2/F3
"ServiceItem is source of truth" behavior intact for the service-order flow while giving the
Songs tab its own unconditional path. Prefer this second option if you want zero risk to the
service-order path.

**Verification after the fix:** build `WorshipPlayer.Presenter`/`WorshipPlayer.UI`; run the app;
Songs tab → The Blessing → confirm 13 sections' worth of lyric slides appear in the grid and the
STAGE preview shows chords above lyrics. Add a unit test alongside
`WorshipPlayer.Tests/PresenterEngineSlideSourceTests.cs` asserting that loading a standalone song
(no current ServiceItem slides) produces `TotalSlides > 0` and raises `SlideChanged`.

---

## Conclusions

- The Songs-tab wiring exists and the data model is intact; the only defect is the F2/F3 guard
  firing for standalone Songs-tab loads while a slide-bearing service item is current.
- Chord-over-lyric STAGE rendering already works; it is unreached, not broken.
- Auto-advance by audio is a MainWindow concern and shares the same guard caveat.
- One small, well-scoped change (clear current item, or add `LoadStandaloneSong`) unblocks the
  entire manual demo. No change to the chord rendering is needed.
