# Findings: "Open The Heavens" shows "No slides to show for this item (could not load)"

## Summary (definitive answer)

The failure is **slide GENERATION returning an empty list**, NOT song resolution and NOT a
swallowed exception.

`PresenterEngine.GenerateSlidesForSong(song)` iterates `song.Sections` and skips every section
whose `Text` is blank. **All 9 sections in `Songs\Open The Heavens\song.json` have NO `text`
field**, so each one is skipped and the method returns an **empty** `List<Slide>`. The center-pane
builder sees `slides.Count == 0` and renders the note "No slides to show for this item (could not
load)."

`TheBlessing` works because **all 13 of its sections include a populated `text` field**, so
generation produces slides.

This has **nothing to do with the spaces** in "Open The Heavens" (folder name or title). Resolution
succeeds for the spaced song (see §2). The sole cause is missing per-section lyric `text` in that
song's data.

- Failure type: **GENERATION → zero slides** (empty result, no throw).
- The "could not load" string is the `slides.Count == 0` branch, **not** the catch branch.

---

## Evidence

### 1. Where the "could not load" text is produced — the zero-slides branch

`WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`

- `RefreshSlideSections()` at **line 1986** builds one section per `ServiceItem`.
- For each item it calls `GetSlidesForItem(item)` at **line 2034**, then at **line 2036** checks
  `if (slides.Count == 0)` and adds the TextBlock with
  `Text = "No slides to show for this item (could not load)."` at **line 2040**.
- There IS an outer `try/catch` (catch at **line 2082**), but it writes a **different** string:
  `"This item could not be loaded."` (**line 2086**) and only logs
  `RefreshSlideSections: failed to build section for '{item.Title}': {ex.Message}` (**line 2083**).
  Because the reported symptom is the *"No slides to show … (could not load)"* wording, we are on
  the **`slides.Count == 0`** path (line 2036), **not** an exception. So generation did not throw —
  it returned empty.

`GetSlidesForItem(ServiceItem item)` at **line 2218**:

- **Line 2224**: pre-baked slides win — `if (item.Slides != null && item.Slides.Count > 0) return item.Slides;`
- **Line 2226**: `if (_engine == null || _songManager == null) return new List<Slide>();`
- **Line 2228**: `var song = SongPathResolver.Resolve(_songManager.Songs, item.SongFolder, item.Title);`
- **Lines 2229-2236**: if `song == null`, logs
  `GetSlidesForItem: could not resolve Song item (SongFolder=…, Title=…)` and returns empty.
- **Line 2238**: otherwise `return _engine.GenerateSlidesForSong(song);`  ← the empty list comes
  from here for Open The Heavens.

Note the pre-baked-slides short-circuit at line 2224: this bug only manifests when the service
item has **no** pre-baked `Slides`. (In the sample `ServiceOrders\Sunday Morning Demo.json`, both
Song items happen to carry pre-baked slides with real text, so there both render — the failure
reproduces for a Song item whose `Slides` list is empty and that therefore falls through to
generation.)

### 2. Resolution SUCCEEDS for the spaced song (rules out the "spaces" hypothesis)

`WorshipPlayer.Core\Services\SongPathResolver.Resolve(...)` (SongPathResolver.cs):

- **byPath** (lines ~71-74): `Normalize(id)` runs `Path.GetFullPath("Songs\Open The Heavens")`,
  which resolves relative to the process working directory. That may or may not equal the absolute
  scan path, so this match is not guaranteed — but it is not the only attempt.
- **byFolderName** (lines ~76-81): `Path.GetFileName("Songs\Open The Heavens")` → `"Open The Heavens"`,
  compared **OrdinalIgnoreCase** against each loaded song's folder leaf name
  (`Path.GetFileName(s.FolderPath…)` = `"Open The Heavens"`). **This matches**, spaces and all.
- **byTitle** (lines ~84-88): as a final fallback, `s.Title` vs `"Open The Heavens"` —
  the song.json `title` is "Open The Heavens", so this would also match.

`SongManager.LoadAllSongsAsync()` (SongManager.cs):

- **Lines ~87-110**: scans `Directory.GetDirectories(_songsBasePath)`, reads each `song.json`,
  deserializes to `Song`, and sets `song.FolderPath = dir` (the absolute scanned dir). A folder with
  spaces ("Open The Heavens") is enumerated and loaded exactly like any other; spaces do not affect
  `Directory.GetDirectories`, `Path.Combine`, `File.ReadAllTextAsync`, or `JsonSerializer`.
- The deserialize is inside a `try/catch` that only logs on failure; Open The Heavens' `song.json`
  is valid JSON and deserializes fine (confirmed by inspection), so it **is** in
  `_songManager.Songs`.

Therefore resolution returns a non-null `Song` for Open The Heavens, and the code reaches
`GenerateSlidesForSong` at line 2238. (If resolution had been the cause, it would fail identically
for any spaced song, yet the symptom is specific to this song's content.)

### 3. Generation returns zero slides because every section's `Text` is blank

`WorshipPlayer.Presenter\Services\PresenterEngine.cs`, `GenerateSlidesForSong(Song song)` at
**line 466**:

- **Lines 469-472**: `if (song.Sections == null || song.Sections.Count == 0) return generated;`
  (Open The Heavens has 9 sections, so this guard is NOT hit.)
- **Line ~476**: `var chartSlides = song.ChordChart?.Slides;` — **null-safe**; the missing
  `chordChart` field is fine.
- The per-section loop contains:
  `string sectionText = section.Text; if (string.IsNullOrWhiteSpace(sectionText)) continue;`
  **For Open The Heavens every section hits `continue`**, so no blocks are produced and the method
  returns an **empty** list.

The model default makes this silent, not a crash:
`WorshipPlayer.Core\Models\Song.cs`, `class Section`, **line 198**:
`[JsonPropertyName("text")] public string Text { get; set; } = "";`
Because the on-disk sections omit `"text"`, deserialization leaves `Text = ""` → `IsNullOrWhiteSpace`
is true → skipped.

`LyricSlideGenerator.cs` (`GenerateFromSong`) has the same skip rule
(`if (string.IsNullOrWhiteSpace(sectionLyrics)) continue;`) and also guards `song.ChordChart?.Slides`,
so the missing optional fields (`composer/lyricist/arranger/themes/chordChart/midiCues/
beatPositions_ms`) never cause a throw anywhere on this path.

### 4. Disk evidence (what actually differs between the two songs)

Inspected on disk:

- `Songs\Open The Heavens\song.json`: 9 sections. **0 sections contain a `text` property at all.**
  First section (INTRO) keys: `name, loopIn_ms, loopOut_ms, startBar, endBar, color,
  lightingPresetIndex` — no `text`.
- `Songs\TheBlessing\song.json`: 13 sections. **All 13 contain a non-empty `text`** (e.g. START =
  `"[F] - [F] - [F]"`). It also carries the extra fields `chordChart`, `midiCues`,
  `beatPositions_ms`, etc.

So the concrete difference that decides pass/fail is purely the presence of section `text`, not the
extra metadata fields and not the spaces.

---

## Conclusion

| Question | Answer |
|---|---|
| Resolution or generation? | **Generation** (zero slides). Resolution succeeds. |
| Throw or empty? | **Empty result** (no exception). The "No slides to show (could not load)" branch is `slides.Count == 0`, not the catch. |
| Exact failure site | `PresenterEngine.GenerateSlidesForSong` (PresenterEngine.cs:466) skips every section via `if (string.IsNullOrWhiteSpace(section.Text)) continue;` because each `Section.Text` is `""` (Song.cs:198 default) — the `song.json` sections omit `"text"`. |
| Where surfaced | `OperatorWindow.xaml.cs:2036` (`slides.Count == 0`) → text at `:2040`. |
| Why TheBlessing passes | All 13 of its sections include populated `text`; Open The Heavens' 9 sections have none. |
| Spaces / missing metadata fields | **Not the cause.** `byFolderName`/`byTitle` match the spaced song; all missing optional fields are null-safe on this path. |

## Recommended fix (data vs. code — pick per intent)

This is fundamentally a **data problem**: "Open The Heavens" has no lyric text in its sections, so
there is genuinely nothing to show. There are two complementary fixes:

1. **Primary (data):** Populate `sections[].text` for `Songs\Open The Heavens\song.json` (the same
   field TheBlessing has). After that, generation yields slides with no code change. This is the
   correct fix if the song is supposed to display lyrics.

2. **Optional (UX, code):** Make the message honest for songs that legitimately have sections but no
   lyric text. In `OperatorWindow.xaml.cs` `GetSlidesForItem`/`RefreshSlideSections`, distinguish
   three cases instead of one catch-all "(could not load)":
   - resolution returned null → "Song not found",
   - resolved but every section has empty `text` → e.g. "No lyrics entered for this song",
   - genuine exception (catch at :2082) → "could not load".
   The current wording wrongly implies a load/IO failure when the real state is "no lyric text."
   A minimal version: when `song != null` but `GenerateSlidesForSong` returns empty, show a
   "no lyrics" note rather than "could not load."

## Risks

- Fix (1) is low-risk: it only adds `text` to section objects; all consumers already default/guard
  this field. Verify the lyric line-splitting (`ExtractSlideBlocks`, blank-line separated blocks)
  produces the intended slide breaks after you add the text.
- Fix (2) is a UI/string change with no behavioral impact on generation or the live path; low risk,
  but keep the existing catch-branch wording for true exceptions so real load failures stay
  distinguishable.
- Do NOT "fix" this in `SongPathResolver` or around spaces — resolution is not the failing step;
  changing it would be chasing the wrong cause and could regress the folder-name/title fallbacks.

_Content was rephrased for compliance where source text was summarized._
