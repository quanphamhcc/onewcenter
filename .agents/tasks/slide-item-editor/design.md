# Technical Design — Edit Service Item (Slides) editor

> Revision 4 — resolves the Revision-3 CHANGES_REQUESTED design review
> (`design-review.md` / `design-review.json`, HIGH×1, MEDIUM×2, NIT×3). The one HIGH blocker
> (the new `WhenComplete` field duplicating the engine's existing `AutoAdvanceGoTo` end-of-item
> navigation) is resolved by **dropping `WhenComplete`/`WhenCompleteAction` entirely** and mapping
> the "When complete" dropdown directly onto the existing `AutoAdvanceGoTo` + `GoToItemId`, reusing
> the vocabulary `ItemSettingsPanel`/`ItemSettingsMapping` already use. See **"Review responses"**
> at the end for a point-by-point disposition of every finding (Revision-1, Revision-2, and
> Revision-3 findings are all dispositioned there).

## Overview

This design adds a dedicated, full-featured "Edit Service Item" editor for a **Slides-type**
service item that is already part of the loaded service order. It replaces the generic
`AddServiceItemDialog` path (used today by `OperatorWindow.EditSelectedServiceItem` /
`EditServiceItemDirect`) **for Slides items only**; every other item type continues to open
`AddServiceItemDialog` unchanged.

The editor edits the *service-item wrapper* — the linked library presentation
(`SlidePresentationId`), background media, auto-advance behavior, transition timing, attached
audio, an optional per-item stage-display template override, and the item's display name — while
keeping the Slides item a **live link** (never snapshotting content or the presentation name).

The implementation is **additive and backward-compatible**: **two** new nullable/additive model
fields (`ServiceItem.AudioAttachments`, `ServiceItem.StageTemplateId`), one new standalone
`Window`, one routing change in `OperatorWindow`, and round-trip tests. The "When complete"
behavior adds **no** new field — it reuses the existing `ServiceItemSettings.AutoAdvanceGoTo` +
`GoToItemId`. No EF, no new persistence store, no theme changes, no `bin`/`obj` deletion.

Technology stack (locked): **.NET 8 / WPF (net8.0-windows)**, C#, `System.Text.Json`
serialization via `ServiceOrderManager._jsonOptions` (`WriteIndented`,
`DefaultIgnoreCondition = WhenWritingNull`, `JsonStringEnumConverter`), SQLite via the shared
`AppDatabase` (`Microsoft.Data.Sqlite`), and the existing `OwcDesignSystem.xaml` dark theme.
Tests: **xUnit** in `WorshipPlayer.Tests` (net8.0, references `WorshipPlayer.Core`;
Presenter sources are linked individually via `<Compile Include>`).

---

## Hosting model (decided)

**Decision: a standalone modal `Window` — `SlideServiceItemEditorWindow` — in
`WorshipPlayer.Presenter\Views\Slides\`, opened with `ShowDialog()` and owned by
`OperatorWindow`.**

Rationale:

- It matches every peer editor in the codebase: `SlideEditorWindow`, `SlideNameDialog`,
  `SongMetadataEditorWindow`, `ItemSettingsWindow`, `AddServiceItemDialog` are all owned modal
  windows. Reusing that shape keeps the entry-point wiring, dirty/discard semantics, and refresh
  flow identical to patterns the team already maintains.
- The requirements explicitly state (Assumptions) "The editor opens modally (owned window,
  `ShowDialog`), consistent with `SlideEditorWindow`/`ItemSettingsWindow`/`AddServiceItemDialog`."
- A standalone `Window` **merges `OwcDesignSystem.xaml`** and therefore **may use the keyed dark
  styles** (`OwcBlueButton`, `OwcPanelBorder`, `OwcSurfaceBorder`, `OwcTextBox`, `OwcDarkComboBox`,
  `OwcSlider`, `OwcTextSecondaryBrush`, etc.) — exactly as `SlideEditorWindow` does. This avoids
  re-inventing inline brushes and gives the "layout đẹp" the user asked for cheaply.

### CRITICAL styling invariant (which rule applies here)

`OperatorWindow.xaml` declares **no** `ResourceDictionary` and hardcodes inline brushes (its XAML
header literally says "NO resource dictionaries - all hardcoded to prevent template conflicts").
Any markup placed *directly inside* `OperatorWindow` MUST use inline hex brushes.

**This editor is NOT placed inside `OperatorWindow`.** It is a separate `Window`, so the
`SlideEditorWindow` rule applies: its `Window.Resources` merges
`pack://application:,,,/WorshipPlayer.Presenter;component/Themes/OwcDesignSystem.xaml` and it uses
keyed `StaticResource` styles. The implementer therefore **uses keyed OWC styles** in
`SlideServiceItemEditorWindow.xaml` (and in every child picker window) and must **not** copy
`OperatorWindow`'s inline-brush discipline into it. (The only place inline brushes are required is
if any snippet is ever added directly to `OperatorWindow.xaml` — not the case here.)

The palette, for reference when a keyed style does not exist for a given element: background
`#0F1117`, panel `#1A1D27`, surface `#252A36`, border `#2A2E3A`, accent/cyan `#00D4FF`, blue CTA
`#2F6FED`, primary text `#F0F2F5`, secondary text `#8B92A5`, muted `#4A5068`, live/red `#FF3366`.

---

## Model changes (additive, backward-compatible)

All new fields follow the established `WhenWritingNull` convention already used by
`SlidePresentationId` / `ThemeId` / `BackgroundType`: a plain nullable property with only
`[JsonPropertyName]`, so `ServiceOrderManager._jsonOptions`
(`DefaultIgnoreCondition = WhenWritingNull`) omits it from JSON when null/default. No existing file
needs any of these keys; deserialization of an older file simply leaves them null.

### Gap #1 — "When complete" action → EXISTING `ServiceItemSettings.AutoAdvanceGoTo` + `GoToItemId` (reworked per Revision-3 Finding 1; NO new field/enum)

**Context verified in source.** `ServiceItemSettings` (`WorshipPlayer.Core/Models/ServiceItemSettings.cs`)
already owns:

```csharp
public AutoAdvanceGoTo AutoAdvanceGoTo { get; set; } = AutoAdvanceGoTo.NextSlide; // NextSlide|NextItem|SpecificItem|Loop
public AutoAdvanceAfterCondition AutoAdvanceAfterCondition { get; set; } = AutoAdvanceAfterCondition.Manual;
public int AutoAdvanceDelayMs { get; set; } = 5000;
public int TransitionFadeTimeMs { get; set; } = 500;
public string? GoToItemId { get; set; }
public string OrganizationId { get; set; } = "";
```

The engine **already implements end-of-item navigation** off these fields. `AutoAdvanceResolver.Resolve`
(`WorshipPlayer.Core/Services/AutoAdvanceResolver.cs`, verified) fires when
`AutoAdvanceAfterCondition == Timer` and then switches on `AutoAdvanceGoTo`: `NextSlide`/`NextItem`
→ advance to the next item, `SpecificItem` → jump to `GoToItemId` (falling back to next item if the
id is missing/invalid), `Loop` → re-show the current item. So `AutoAdvanceGoTo` + `GoToItemId`
**is** the end-of-item navigation vocabulary, and nothing in the codebase reads any separate
"when-complete" field.

**Decision (final, applied verbatim from the resolved design-review blocker) — DROP the
previously-proposed `WhenComplete` field and `WhenCompleteAction` enum entirely. The "When complete"
dropdown maps DIRECTLY onto the EXISTING `AutoAdvanceGoTo` + `GoToItemId`, reusing the exact
vocabulary `ItemSettingsPanel`/`ItemSettingsMapping` already use.**

A new field would have duplicated and silently conflicted with the engine's existing navigation: it
would collide on the single `GoToItemId` field (two writers), and — since no code reads it — the UI
selection (e.g. `Loop`) would never match what the resolver actually does. Reusing `AutoAdvanceGoTo`
removes the duplication, makes the feature actually drive the engine, and satisfies model-gap #1 by
**reusing an existing field rather than adding one**. There is therefore **no model change for Gap
#1** — `ServiceItemSettings` is used as-is.

**Dropdown label → `AutoAdvanceGoTo` mapping (copies `ItemSettingsMapping.ToGoTo`/`GoToLabel`
verbatim; verified in `WorshipPlayer.Presenter\Views\ServiceBuilder\ItemSettingsPanel.xaml.cs`):**

| "When complete" label | `AutoAdvanceGoTo` written | `GoToItemId` |
|---|---|---|
| **Do nothing** | left at the model default (`NextSlide`); no auto-advance navigation is wired (effect governed by whether a Timer trigger is set — see below) | untouched |
| **Next item** | `AutoAdvanceGoTo.NextItem` | untouched |
| **Specific item** | `AutoAdvanceGoTo.SpecificItem` | `= chosen item id` |
| **Loop** | `AutoAdvanceGoTo.Loop` | untouched |

Load side (the inverse, matching `ItemSettingsMapping.GoToLabel`): `SpecificItem → "Specific item"`,
`Loop → "Loop"`, `NextItem → "Next item"`, and `NextSlide` (the model default, not surfaced as its
own option) → `"Do nothing"`. When the stored `AutoAdvanceGoTo == SpecificItem`, the target dropdown
preselects the item whose `Id == GoToItemId`. (Casing note: this editor's labels use sentence case
— "Specific item", "Next item" — to match the reference screenshot; the *mapping* is identical to
`ItemSettingsMapping`, which uses "Specific Item"/"Next Item". The editor's own small `ToGoTo`/
`GoToLabel` equivalents use this editor's labels but the enum targets are the same.)

**Coherence with "Go to next slide after" (the two dropdowns together):**
"Go to next slide after" sets `AutoAdvanceAfterCondition` (the *trigger*), and "When complete" sets
`AutoAdvanceGoTo` + `GoToItemId` (the *destination*). `AutoAdvanceGoTo` + `GoToItemId` only drives
**live** navigation when the after-condition is `Timer` (which is exactly what "Go to next slide
after" controls). So the two dropdowns are coherent: with no Timer trigger, the destination is
stored but dormant; with a Timer trigger, the resolver obeys the chosen `AutoAdvanceGoTo`. This also
means the **"When complete" dropdown is DISABLED/greyed when "Go to next slide after" is
"Disabled"** (as the reference screenshot shows) — see the Auto-advance mapping section for the
exact enable rule.

**`allItems` is consumed** (Revision-1 Finding 1): choosing `Specific item` reveals a second
dropdown (target item) populated from `allItems` — each entry carries the item's `Id` (written to
`GoToItemId`) and displays a resolved label. The current item is excluded from the target list.

**Target-label resolution (specified per Revision-2 Finding 2).** `ServiceItemDisplayName.ResolveDisplayName`
is a **pure two-arg helper** — `ResolveDisplayName(ServiceItem item, SlidePresentation? linked)` —
that takes an **already-resolved** `SlidePresentation`; it does **not** fetch from
`ISlidePresentationService` or any cache, and there is **no one-arg overload**. The editor therefore
resolves the `linked` argument itself when building the target list. Since the editor already takes
`slidePresentationService`, the decided approach is:

> Build the target list **once on open**, asynchronously, under the `_suppressDirty` guard. For each
> `allItems` entry `it` except the current item, compute its label as
> `ServiceItemDisplayName.ResolveDisplayName(it, it.SlidePresentationId is null ? null : await _slidePresentationService.GetByIdAsync(it.SlidePresentationId))`.
> Non-Slides and non-linked items pass `linked: null`, so the helper falls back to the item's
> `Title`. A `GetByIdAsync` that returns null (deleted presentation) also passes `linked: null`,
> giving the same `Title` fallback rather than a blank row.

This is N async `GetByIdAsync` calls at most, once per open, and does **not** depend on
`OperatorWindow._slidePresentationCache` (which is not passed to the editor). The resolved
`(Id, label)` pairs are cached in the window for the lifetime of the dialog; the dropdown binds to
that prebuilt list. (If a future optimization wants to avoid the N calls, the alternative is to pass
a prebuilt `IReadOnlyDictionary<string,string>` id→display-name map in from `OperatorWindow`'s
already-primed cache as an additional constructor argument — not adopted now to keep the constructor
and the host wiring minimal, but noted as the sanctioned fallback.)

**Where the async build runs (Revision-3 Finding 4).** A WPF `Window` constructor is synchronous and
cannot `await`. So the constructor does **only** the synchronous work — the deep-clone, `_live`
capture, and `allItems` capture — and then this async target-list build (plus any other awaited
population, e.g. resolving the linked presentation name, the media-library enumeration, and the
template resolve) runs in a `Loaded` handler (or an `async void` init method invoked from the
constructor). `_suppressDirty` is set `true` **before** the awaited population begins and is cleared
**only after all awaited work completes**, so no programmatic selection during population can arm
the dirty flag. There is **no** `.Result`/`.Wait()` on the UI dispatcher (which would risk a
deadlock). See also the Dirty-tracking invariant.

### Gap #2 — Multiple audio files → `ServiceItem.AudioAttachments`

Add an additive list and **keep the legacy single `AudioAttachmentPath` field intact** for
backward compatibility (verified: it is a non-nullable `string` defaulting to `""`, so it is always
present in old files and cannot simply be dropped without a non-additive change).

New property on `ServiceItem`:

```csharp
/// <summary>
/// Attached audio files for this item. Additive multi-audio replacement for the legacy single
/// <see cref="AudioAttachmentPath"/>. Nullable so it is omitted from JSON when unset (older files
/// have no such key). On load, a non-empty legacy AudioAttachmentPath is migrated into this list
/// as the first entry (see ServiceItemAudio.Resolve).
/// </summary>
[JsonPropertyName("audioAttachments")]
public List<string>? AudioAttachments { get; set; }
```

**Migration / read model (pure, testable) — `ServiceItemAudio` in `WorshipPlayer.Core.Services`:**

Rather than mutating the model on deserialization (which `System.Text.Json` does not give us a
clean hook for and which would risk rewriting legacy files unexpectedly), add a pure helper that
the editor and any consumer call to get the effective list:

```csharp
public static class ServiceItemAudio
{
    /// <summary>
    /// Effective audio list for an item: AudioAttachments when present (non-null, non-empty);
    /// otherwise a single-element list from a non-empty legacy AudioAttachmentPath;
    /// otherwise empty. Pure; never mutates the item.
    /// </summary>
    public static List<string> Resolve(ServiceItem item)
    {
        if (item.AudioAttachments is { Count: > 0 }) return new(item.AudioAttachments);
        if (!string.IsNullOrWhiteSpace(item.AudioAttachmentPath)) return new() { item.AudioAttachmentPath };
        return new();
    }
}
```

**Write-back rule (decided):** when the editor saves, it writes the edited list into
`AudioAttachments`. It **keeps the legacy `AudioAttachmentPath` in sync** by setting it to the
list's first entry (or `""` when the list is empty). Keeping it in sync — rather than deprecating
it silently — means any older code path still reading the single field continues to show the
primary audio, and no consumer breaks. This is the "kept in sync" option from the model-gap brief,
chosen because the single field is non-nullable and already referenced as plain data elsewhere, so
a sync write is zero-risk and additive. The list is authoritative in the UI; the single field is a
mirror of `[0]`.

### Gap #3 — Background media "Source" → existing `BackgroundType` / `MediaPath` / `MediaType` (reworked per Finding 4)

**No new field and no new store.** The Source dropdown maps onto the item's existing
`BackgroundType? BackgroundType`, `string MediaPath`, and `MediaItemType MediaType`, sourcing its
choices from the existing `IMediaLibraryService` (`_mediaLibraryService` is already injected into
`OperatorWindow` and will be passed to the editor).

**Verified enum reality:** `BackgroundType` = `{ SolidColor, Image, Video, Gradient }`;
`ServiceItem.MediaType` is the separate `MediaItemType` enum (`Image` default); `MediaLibraryItem`
carries `FilePath` and a `MediaType : MediaLibraryType`. So two issues the review raised must be
handled explicitly: (a) a pre-existing `SolidColor`/`Gradient` background has no media-library row
to represent it, and (b) only `Image`/`Video` library rows are meaningful as a background Source.

**Source dropdown composition (deterministic):**

1. **`None`** — always first.
2. **A read-only "Current: <type>" sentinel row — ONLY when the item opens with a non-media
   background**, i.e. `BackgroundType == SolidColor` or `BackgroundType == Gradient`. This row is
   selected by default on open for such items and is **not** backed by a library item. Leaving it
   selected on save **preserves the existing `BackgroundType`/`MediaPath`/`MediaType` untouched**
   (the editor does not rewrite background fields unless the user changes the selection away from
   the sentinel). The sentinel is not offered for items whose background is already `Image`/`Video`
   or null — those map to a real list row or `None`.
3. **Media-library rows** — from `IMediaLibraryService.GetByTypeAsync(MediaLibraryType.Image)` and
   `GetByTypeAsync(MediaLibraryType.Video)` **only**. `MediaLibraryType` is **exactly**
   `{ Image, Video, Audio }` (verified in `WorshipPlayer.Core/Models/MediaLibraryItem.cs`; there is
   **no `Document` member**). Only `MediaLibraryType.Audio` rows are excluded (audio is not a
   background). Each row carries its `MediaLibraryItem`.

**Selection → field mapping (save side):**

- **`None`** selected → clears per `WhenWritingNull`: `BackgroundType = null` (omitted from JSON),
  `MediaPath = ""`. `MediaType` left at its default `Image` (non-nullable, harmless when `MediaPath`
  is empty).
- **"Current: <type>" sentinel** selected (unchanged) → write **nothing** to the three background
  fields; they round-trip exactly as loaded. (This is how a pre-existing `SolidColor`/`Gradient`
  background is preserved — Finding 4a.)
- **A media-library row** selected → set:
  - `MediaPath = row.FilePath`
  - `MediaType = row.MediaType == MediaLibraryType.Video ? MediaItemType.Video : MediaItemType.Image`
  - `BackgroundType = row.MediaType == MediaLibraryType.Video ? BackgroundType.Video : BackgroundType.Image`

**Load side (which row is selected on open):** `None` when `BackgroundType == null`;
the "Current: <type>" sentinel when `BackgroundType` is `SolidColor`/`Gradient`; otherwise the
library row whose `FilePath == MediaPath` (matched case-insensitively), falling back to a
transient "Current: <file name>" row if the path no longer exists in the library (so an
out-of-library media path is still shown and preserved rather than silently reset to `None`).

### Gap #4 — Per-item Stage Display Template override → `ServiceItem.StageTemplateId`

A real `ITemplateService` with full CRUD and a `SlideTemplate` model **already exists** (interface
in `WorshipPlayer.Core.Services.Interfaces`, impl `TemplateService` in Presenter, backed by the
`SlideTemplates` table in `app.db`; it is already injected into `OperatorWindow` as
`_templateService`). So we **fully wire** to it rather than stubbing (the requirements' preferred
option; the stub was only a fallback).

New additive nullable field on `ServiceItem`:

```csharp
/// <summary>
/// Optional per-item stage-display template override, by SlideTemplate id (ITemplateService).
/// Null = use the default template. Additive; JSON-omitted when null (WhenWritingNull).
/// </summary>
[JsonPropertyName("stageTemplateId")]
public string? StageTemplateId { get; set; }
```

**Thumbnail reality:** `SlideTemplate` carries font/style metadata (FontFamily, FontSize,
alignment, margins, shadow, etc.) — it has **no thumbnail image path**. So the "thumbnail" in the
UI is a lightweight style-preview tile, not a bitmap: a dark tile showing the template `Name` and
a one-line font/alignment descriptor (e.g. "Segoe UI · Bold · Center"). This is a documented,
accurate rendering of what the template concept actually is — not a stub. The stored id round-trips
and resolves via `ITemplateService.GetByIdAsync`. When the id is null or resolves to null (template
deleted), the tile shows "No template selected". "Applying" the template to live rendering is out
of scope (FR-6 / Out-of-Scope: this feature only *selects* an existing template as a per-item
override and persists the id).

---

## The editor window — `SlideServiceItemEditorWindow`

### Files

- **New:** `WorshipPlayer.Presenter\Views\Slides\SlideServiceItemEditorWindow.xaml`
- **New:** `WorshipPlayer.Presenter\Views\Slides\SlideServiceItemEditorWindow.xaml.cs`
- **Modify:** `WorshipPlayer.Core\Models\ServiceOrder.cs` (add `AudioAttachments`,
  `StageTemplateId` to `ServiceItem`)
- **Unchanged:** `WorshipPlayer.Core\Models\ServiceItemSettings.cs` (NO change — "When complete"
  reuses the existing `AutoAdvanceGoTo` + `GoToItemId`; no `WhenComplete` field/enum is added)
- **New:** `WorshipPlayer.Core\Services\ServiceItemAudio.cs` (pure audio-resolve helper)
- **Modify:** `WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs` (route Slides items here;
  save/refresh/remove wiring)
- **New test:** `WorshipPlayer.Tests\SlideServiceItemEditorModelTests.cs` (round-trip +
  migration + mirror)

### Constructor / inputs

```csharp
public SlideServiceItemEditorWindow(
    ServiceItem item,
    IReadOnlyList<ServiceItem> allItems,          // consumed for the "GoTo specific item" target list
    ISlidePresentationService slidePresentationService,
    IMediaLibraryService mediaLibraryService,
    ITemplateService templateService)
```

The window operates on an **editable working copy** of the item so that `Close`/X can discard
cleanly without touching the live model (mirrors `SlideEditorWindow`, which treats its passed
object as the editable copy; here we take a copy because the caller's `ServiceItem` is the live one
in the loaded order). Implementation: deep-copy via a JSON round-trip:

```csharp
var json = JsonSerializer.Serialize(item, _cloneJsonOptions);
_working = JsonSerializer.Deserialize<ServiceItem>(json, _cloneJsonOptions)!;
```

**Sync vs async split (Revision-3 Finding 4).** This deep-clone, the `_live`/`allItems` capture, and
`_suppressDirty = true` are the **only** work done in the synchronous `Window` constructor — a WPF
constructor cannot `await`. All awaited population (the specific-item target-list build via
`await _slidePresentationService.GetByIdAsync(...)`, the linked-presentation name resolution, the
media-library enumeration, and the template resolve) runs in a `Loaded` handler (or an `async void`
init method), keeping `_suppressDirty` true for the **entire** awaited sequence and clearing it only
once all awaits complete. No `.Result`/`.Wait()` is ever used on the UI dispatcher.

`_cloneJsonOptions` is a **window-local** `JsonSerializerOptions` carrying only
`Converters = { new JsonStringEnumConverter() }`. It is **intentionally not** the manager's
`_jsonOptions` instance: the clone never hits disk, so it does not need `WriteIndented` or
`WhenWritingNull`; keeping it local avoids coupling the window to the manager's serialization policy
and makes clear to a maintainer that the two option sets are deliberately separate (Revision-1
Finding 8).

**Why the missing `WhenWritingNull` on the clone is correct, and must stay (Revision-2 Finding 5).**
The clone is a serialize→deserialize round-trip in memory. Without `WhenWritingNull`, nullable
fields serialize as explicit `null` and deserialize straight back to `null` — exactly the input
state, which is what a clone must preserve. It correctly round-trips the two new additive fields
(`AudioAttachments`, `StageTemplateId`) because they are ordinary properties, and it round-trips the
existing `ServiceItemSettings` (incl. `AutoAdvanceGoTo`/`GoToItemId`) with the
`JsonStringEnumConverter` covering the `AutoAdvanceGoTo` enum. The additive-omission guarantee
(AC-13: "omitted when null/default") is **the manager's `_jsonOptions` on the real save**, not this
clone's job — the clone never reaches disk or the SQLite mirror. A maintainer must **not** "fix" the
clone to use the manager options (adding `WhenWritingNull` would make the clone *drop* null fields
rather than preserve them, which is wrong for an in-memory copy and would also needlessly couple the
window to the manager's policy).

The original (live) `item` reference is retained as `_live` so that, on save, the edited fields are
copied back **in place** onto it (see "Save — copy-back mechanism" below). `allItems` is retained for
the specific-item target dropdown.

**`allItems` is the live `CurrentServiceOrder.Items` list (Revision-3 Finding 6).** The host passes
the live `List<ServiceItem>` directly rather than a snapshot. This is **safe by deliberate choice**
because the editor is **modal** (`ShowDialog`): the current service order cannot be mutated
underneath the dialog while it is open, so enumerating the live list once to build the target labels
cannot race a concurrent add/remove. The specific-item target is also stored as an **id** in
`GoToItemId` (position-independent), so even a later reorder does not invalidate it. We therefore
pass the live list as-is; no defensive copy is made.

`public ServiceItem? Result { get; private set; }` is non-null only after a successful save
(mirrors `SlideEditorWindow.Result`), and `DialogResult = true`. The Remove flow uses a **separate
event**, never `Result` (Finding 2) — see Remove flow.

### Save — copy-back mechanism (specified per Finding 6)

On **Save and close**, the editor assigns the **enumerated edited fields** from `_working` onto the
**live `_live` item in place** — it does **not** replace the list element and does **not** touch any
other field. `_live.Id` and `_live.Order` are preserved implicitly because they are never written,
so group membership and ordering (which key off `Id`/`Order`) are unaffected.

Fields written on save (the complete list — nothing else is touched):

| Field | Source on `_working` | Notes |
|---|---|---|
| `SlidePresentationId` | working | link only; never snapshots name/content |
| `BackgroundType` | working | may be null (None) or unchanged (sentinel) |
| `MediaPath` | working | `""` when None |
| `MediaType` | working | `MediaItemType` value from the Gap #3 mapping |
| `TransitionTimeMs` | working | from the slider |
| `AudioAttachments` | working | the edited list (may be null when empty) |
| `AudioAttachmentPath` | derived | `= AudioAttachments?.FirstOrDefault() ?? ""` (legacy sync) |
| `StageTemplateId` | working | template id or null |
| `AutoAdvanceSettings` | working | the whole `ServiceItemSettings` object incl. `AutoAdvanceGoTo`, `GoToItemId`, and `TransitionFadeTimeMs` — see Gap #1 and the Auto-advance mapping rules |
| `Title` | working | **only** for a non-linked item rename (see Rename semantics); untouched for linked items |

Fields explicitly **NOT written** (preserved from `_live` as-is): `Id`, `Subtitle`, `Notes`,
`DurationMinutes`, `Order`, `ThemeId`, `ForegroundType`, `Slides`, and every Song/Scripture field.
(The working copy still *carries* these because it is a full clone, but the save step never assigns
them back, so a field the editor does not expose can never be clobbered.)

**Two transition-time fields (both real) — kept consistent.** `ServiceItem.TransitionTimeMs` (int,
default 500; the field FR-5 binds the slider to) and `ServiceItemSettings.TransitionFadeTimeMs`
(int, default 500; inside the settings object) are distinct and both exist in source. The slider
binds to `ServiceItem.TransitionTimeMs` only (per FR-5). **Whenever the save step creates or updates
a `ServiceItemSettings` object** (the auto-advance cases), it sets
`TransitionFadeTimeMs = _working.TransitionTimeMs` so the two fields never silently diverge from a
non-500 slider value. When no settings object exists (`AutoAdvanceSettings == null`, the canonical
off state), there is no `TransitionFadeTimeMs` to write and `TransitionTimeMs` is the single source
of truth.

### Layout (top → bottom, matching FR-1…FR-6)

A `DockPanel`/`ScrollViewer` body, `Width≈760 Height≈780`, `WindowStartupLocation=CenterOwner`,
`Background="#0F1117"`, merging `OwcDesignSystem.xaml`:

1. **Header bar** (FR-1): left = large bold display name `TextBlock` + a pencil icon `Button`
   (`OwcCompactButton`) that swaps the name for an inline `TextBox` (`OwcTextBox`) + confirm.
   Right = `Remove service item` (`OwcButtonBase`), `Close` (`OwcButtonBase`),
   `Save and close` (`OwcBlueButton`, `IsEnabled` bound to the dirty flag).
2. **Slides** card (FR-2, `OwcPanelBorder`): linked presentation **name** (bold, resolved live) +
   a `Select` button (`OwcCompactButton`) opening the presentation picker.
3. **Background media** card (FR-3): a `Source` `ComboBox` (`OwcDarkComboBox`) = `None`
   [+ "Current: <type>" sentinel when applicable] + Image/Video library rows.
4. **Auto-advance** card (FR-4): `Go to next slide after` `ComboBox` + `When complete` `ComboBox`
   (the latter `IsEnabled=false` when the former is `Disabled`). A delay `TextBox`/stepper shows
   only for `Time Delay`. A **target-item** `ComboBox` (populated from `allItems`) shows only when
   `When complete == "Specific item"` (i.e. `AutoAdvanceGoTo.SpecificItem`).
5. **Settings** row (FR-5): two side-by-side cards — **Transitions** (a `Slider` `OwcSlider` for
   `TransitionTimeMs`, range 0–3000 ms, plus a value label) and **Audio** (a `ListBox` of
   attached files with a `+` `OwcBlueButton` to add, an inline remove `×` per row, and a
   "No audio files added" empty-state `TextBlock` in `OwcTextSecondaryBrush`).
6. **Stage Display Template** card (FR-6): the style-preview tile + a label
   ("No template selected" default) + a `Select` button opening the template picker, plus a
   `Clear` affordance.

### Dirty tracking

A single `private bool _isDirty` flipped to true by every bound control's change handler
(`TextChanged`, `SelectionChanged`, `ValueChanged`, list add/remove, rename-confirm). `Save and
close` button `IsEnabled = _isDirty`. Open with `_isDirty = false` so the primary button starts
disabled (AC-10). A guard flag (`_suppressDirty`, same shape as
`SlideEditorWindow._suppressTextSync`) wraps the initial control population so loading the item does
not mark it dirty. A **pending rename** of a linked presentation also sets `_isDirty` (so that an
otherwise-unchanged item becomes saveable after a rename — see Rename semantics).

**Initial-selection invariant (Revision-3 Finding 3).** `_suppressDirty = true` is set before any
control population and stays true for the **entire** synchronous *and* awaited population (the
async target-list build, linked-name resolution, media/template resolution — see "Where the async
build runs"), and is cleared **only after all awaited work completes**. Every initial
dropdown/control selection on open happens **inside** that guard — specifically **all** of:

- **Source** — including the read-only "Current: &lt;type&gt;" sentinel auto-selected for a
  pre-existing `SolidColor`/`Gradient` background, and the transient "Current: &lt;file&gt;" row for
  an out-of-library media path;
- **"Go to next slide after"** and **"When complete"** (and the specific-item target dropdown);
- the **template** selection (or "No template selected").

Because these fire `SelectionChanged` under the guard, none of them arms `_isDirty`. The invariant,
stated flatly: **"Programmatic initial selection never sets `_isDirty`; only user-originated changes
do."** `_isDirty` is **asserted `false` at the end of construction/population** regardless of which
rows were auto-selected — so `Save and close` is disabled on open with no changes even for
SolidColor/Gradient and out-of-library-media items (AC-10).

### Pickers (reused patterns, small modal lists)

- **Presentation picker:** a small owned modal listing `ISlidePresentationService.GetAllAsync()`
  by Name; returns the chosen id. Selecting sets `_working.SlidePresentationId` **only** — no
  content/name snapshot (FR-2, AC-3). Implemented as a reusable `ListPickerDialog` (name + id) or
  inline; either is acceptable, but it MUST merge `OwcDesignSystem.xaml` (it is its own window).
- **Template picker:** same shape over `ITemplateService.GetAllAsync()`.
- **Audio add:** `Microsoft.Win32.OpenFileDialog` with the **explicit filter**
  `"Audio|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All files|*.*"` (Finding 9), appending the chosen
  path to the working list. The filter is a convenience only (the user can switch to All Files);
  paths are stored verbatim with no eager existence check. (No import into the media library —
  Out of Scope.)

### Rename semantics (FR-1, decided — reworked per Finding 3)

The pencil writes **different targets** depending on link state, and in **both** cases the write is
**deferred to the Save-and-close step** so that `Close`/X truly discards it (AC-11):

- **Live-linked Slides item** (`SlidePresentationId` set): confirming the pencil stores the new name
  in a window field `_pendingPresentationName` (and updates the header display immediately for
  feedback) and sets `_isDirty = true`. **No service call happens at this point.** Only on
  **Save and close** does the editor call
  `ISlidePresentationService.GetByIdAsync(id)` → set `Name` → `UpdateAsync`, as part of the save
  sequence, **before** the host persists the service order and re-primes the cache. Because the
  sidebar row and center header resolve the name live from the primed cache
  (`ServiceItemDisplayName.ResolveDisplayName`), the new name then shows everywhere — **without**
  writing `ServiceItem.Title` (no snapshot). On **Close/X**, `_pendingPresentationName` is dropped
  and the presentation is never renamed, so reopening shows the pre-edit name (AC-11 holds).
- **Non-linked item** (legacy snapshot, no `SlidePresentationId`): confirming the pencil writes
  `_working.Title` (not the live item) and sets `_isDirty`. `ResolveDisplayName` falls back to
  `Title` for such items, so the sidebar/center update after the normal save+copy-back+refresh. On
  Close/X the working copy is discarded, so the rename is discarded (AC-11 holds).

This makes the rename consistent with the discard requirement: **nothing is persisted until Save**.
Edge case: if the linked presentation was deleted, the pencil is disabled for the linked case and
the Slides card shows the fallback label (FR-2 last bullet) instead of crashing; the deferred
rename is skipped on save if `GetByIdAsync` returns null (logged, non-fatal — see Error handling).

---

## Entry-point wiring in `OperatorWindow.xaml.cs`

The current edit paths both open `AddServiceItemDialog` for every type:
`EditSelectedServiceItem()` (sidebar double-click / Edit button) and `EditServiceItemDirect(item)`
(section-header Edit). We branch **Slides** items to the new editor and leave all other types on
`AddServiceItemDialog`.

New private helper (single place so both call sites agree). **Remove and save-and-close are mutually
exclusive, and `Close`/X does neither** (Finding 2):

```csharp
private bool TryOpenSlideServiceItemEditor(ServiceItem item)
{
    if (item.Type != ServiceItemType.Slides) return false;
    if (_serviceOrderManager?.CurrentServiceOrder == null) return false;
    if (_slidePresentationService == null || _mediaLibraryService == null || _templateService == null)
        return false; // services unavailable → fall back to the generic dialog, never crash

    var win = new SlideServiceItemEditorWindow(
        item,
        _serviceOrderManager.CurrentServiceOrder.Items,
        _slidePresentationService,
        _mediaLibraryService,
        _templateService) { Owner = this };

    // Remove is a distinct event. On remove the window sets DialogResult=false and closes,
    // so ShowDialog() returns false and 'saved' is false — remove and save never both fire.
    bool removed = false;
    win.RemoveRequested += (_, __) =>
    {
        removed = true;
        win.DialogResult = false;   // ensure ShowDialog() != true
        win.Close();
    };

    bool saved = win.ShowDialog() == true;   // true ONLY after a successful Save and close

    _ = Dispatcher.InvokeAsync(async () =>
    {
        if (removed)
        {
            // Remove path: run the existing remove/save/refresh flow. Nothing was copied back.
            int index = _serviceOrderManager.CurrentServiceOrder.Items.IndexOf(item);
            if (index >= 0) _serviceOrderManager.RemoveItem(index);   // RemoveItem guards range
        }
        else if (!saved)
        {
            return;   // Close/X: discard everything — no save, no refresh.
        }
        // else: saved — the window already copied the edited fields back onto `item` in place
        // (and already applied a deferred linked-presentation rename) before DialogResult=true.

        await SaveServiceOrderAsync();          // existing persist (JSON + SQLite mirror)
        await PrimeSlidePresentationCacheAsync();
        RefreshServiceOrderList();
        RefreshSlideSections();
    });
    return true;
}
```

Both edit entry points call the helper with **their own item reference** **before** constructing
`AddServiceItemDialog` — the two call sites differ (verified in source):
`EditSelectedServiceItem()` reads `ServiceOrderList.SelectedItem is ServiceItemDisplay display`
(and already null-guards `display.Item`), so it calls
`if (TryOpenSlideServiceItemEditor(display.Item)) return;`; `EditServiceItemDirect(ServiceItem item)`
receives the item directly (there is no `display` variable in that method), so it calls
`if (TryOpenSlideServiceItemEditor(item)) return;`. (`OpenSelectedItemSettings` — the auto-advance modal — is left as-is; the
new editor supersedes it for Slides but the brief only requires routing the *edit* entry points.
Per the requirements, editing a Slides item must open THIS editor; the item-settings button is not
a required rerouting and keeping it avoids scope creep.)

**Remove flow contract (pinned — Revision-2 Finding 2 + Revision-3 Finding 5):** the editor declares
`public event EventHandler? RemoveRequested;`. **The editor's in-window `Remove service item` button
handler raises `RemoveRequested` and does NOTHING else — it must NOT set `Result` or `DialogResult`,
and must NOT call `Close` or the `ServiceOrderManager`.** The **host** owns closing: its
`RemoveRequested` subscriber sets `DialogResult = false` (which ends `ShowDialog`) exactly as the
entry-point snippet shows. `Result` stays typed `ServiceItem?` and is non-null only after a save.
This pins the invariant at the one place it could be violated: `removed` and `saved` are mutually
exclusive because the remove path never sets `DialogResult = true`
(`win.RemoveRequested += (_, __) => { removed = true; win.DialogResult = false; }; bool saved =
win.ShowDialog() == true;`). The host owns the remove/save/refresh idiom
(the same one used in `RemoveServiceItem_Click`), runs it exactly once in the `removed` branch
above, and because `removed ⇒ DialogResult=false`, a remove can never also be treated as a
save-and-close. If the item is already gone, `IndexOf` returns −1 and the branch no-ops; the
subsequent `SaveServiceOrderAsync` simply persists the (unchanged) current order — but since the
item was found and removed in the normal case, the save reflects the removal.

---

## Persistence & round-trip

- **Save:** on `Save and close`, the window (1) applies any deferred linked-presentation rename via
  `ISlidePresentationService.UpdateAsync`, (2) copies the enumerated edited fields back onto the
  live `item` **in place** (see "Save — copy-back mechanism"), (3) sets `Result = item` and
  `DialogResult = true`. The host then calls the **existing** `ServiceOrderManager.SaveAsync()`
  (via `SaveServiceOrderAsync`), which writes the authoritative JSON file and best-effort upserts
  the `ServiceOrders.DataJson` SQLite mirror. Because both the file and the mirror serialize the
  whole `ServiceOrder` with the same `_jsonOptions`, the three additive fields flow into both
  automatically — **no `ServiceOrderManager` or schema change is required**.
- **Close/X:** discards (nothing copied back, no deferred rename applied), mirroring
  `SlideEditorWindow`.
- **Refresh:** host re-primes `PrimeSlidePresentationCacheAsync` then `RefreshServiceOrderList` +
  `RefreshSlideSections` so live names/content update (AC-2, AC-10).

### Backward compatibility verification

- A pre-feature file has no `audioAttachments` or `stageTemplateId` keys → both
  deserialize to null; `ServiceItemAudio.Resolve` turns a legacy non-empty
  `audioAttachmentPath` into a single-element list (AC-8). Loading the three real service-order
  JSONs and the demo files must still succeed; the implementer runs that load check (AC-8) since it
  is not provable in this design pass.
- Nullable additive fields are omitted when null/default (`WhenWritingNull`), so an untouched item
  re-serializes byte-identically except for the manager's always-updated `updatedAt`.

---

## Auto-advance mapping (deterministic round-trip rule — reworked per Revision-3 Findings 1 & 2)

The two Auto-advance dropdowns map onto **existing** `ServiceItemSettings` fields only:

- **"Go to next slide after"** → `AutoAdvanceAfterCondition` (the *trigger*), plus
  `AutoAdvanceDelayMs` when Time Delay.
- **"When complete"** → `AutoAdvanceGoTo` + `GoToItemId` (the *destination*), per the Gap #1 label
  table. There is **no** separate `WhenComplete` field.

### "Go to next slide after" ⇄ `AutoAdvanceAfterCondition`

Save side maps **exactly** through `BulkEditService.ParseAfterCondition`
(`"Time Delay"→Timer`, `"End of Media"→AudioEnd`, else `Manual`) so per-item and bulk edit agree
(FR-4, AC-5). "Disabled" in the dropdown is the editor's label for the no-auto-advance state and
maps to `Manual` on save (the resolver no-ops on anything other than `Timer`).

**Load side — the full four-member map (Revision-3 Finding 2).** `AutoAdvanceAfterCondition` has
**four** members in source — `{ Manual, Timer, AudioEnd, VideoEnd }` — so the load-side label map
must handle `VideoEnd`, which `BulkEditService.ParseAfterCondition` has **no** label for. This
editor copies the existing `ItemSettingsMapping.AfterLabel` (verified in source), which already
folds `VideoEnd → "End of Media"`:

| Stored `AutoAdvanceAfterCondition` | Dropdown label on load |
|---|---|
| `Timer` | "Time Delay" |
| `AudioEnd` | "End of Media" |
| `VideoEnd` | **"End of Media"** (folded with `AudioEnd` — no distinct Video label exists, and the resolver treats `AudioEnd`/`VideoEnd` identically as deferred no-ops) |
| `Manual` | "Disabled" |

Because "End of Media" parses back to `AudioEnd` on save, **re-saving an item whose stored condition
was `VideoEnd` normalizes it to `AudioEnd`**. This is an **intended, harmless normalization** — the
resolver already treats `AudioEnd` and `VideoEnd` identically (both deferred, both currently
no-ops), so no behavior changes — and must **not** be read as accidental data loss. (Preserving
`VideoEnd` distinctly would require adding an "End of Video" label and diverging from the shared
`ParseAfterCondition`, which would also touch bulk edit; not done, to keep the shared mapping
single-sourced.)

### "When complete" ⇄ `AutoAdvanceGoTo` + `GoToItemId`

Save side (per the Gap #1 label table): `Do nothing → NextSlide` (model default, no navigation
wired), `Next item → NextItem`, `Specific item → SpecificItem` + `GoToItemId = chosen id`,
`Loop → Loop`. Load side is the inverse (`ItemSettingsMapping.GoToLabel`): `SpecificItem →
"Specific item"` (+ preselect the target whose `Id == GoToItemId`), `Loop → "Loop"`,
`NextItem → "Next item"`, `NextSlide → "Do nothing"`.

### What is written on save (per selection)

- **"Go to next slide after = Disabled"** → `AutoAdvanceAfterCondition = Manual`. The settings
  object is still populated with the chosen `AutoAdvanceGoTo`/`GoToItemId` (the destination is
  stored but dormant because the trigger is not `Timer`). If the item had **no** settings object and
  the user leaves both dropdowns at their defaults ("Disabled" + "Do nothing"), no settings object is
  fabricated (`AutoAdvanceSettings` stays `null`) — the canonical off state.
- **"Go to next slide after" = a real after-condition** (`Time Delay`/`End of Media`) → settings
  object with that `AutoAdvanceAfterCondition`, `AutoAdvanceDelayMs` from the delay field (when
  `Time Delay`), and `AutoAdvanceGoTo`/`GoToItemId` from the "When complete" controls.

In **every** case where a `ServiceItemSettings` object is created or updated,
`TransitionFadeTimeMs = _working.TransitionTimeMs` so the settings-object copy of the transition
time stays consistent with the slider-bound `ServiceItem.TransitionTimeMs` (Revision-2 Finding 4).
When no settings object exists, `ServiceItem.TransitionTimeMs` is the single source of truth.

### Enable rule

**The "When complete" dropdown is DISABLED/greyed whenever "Go to next slide after" is "Disabled"**
(matching the reference screenshot): with no `Timer` trigger the destination would never fire, so
offering it would be misleading. It enables as soon as "Go to next slide after" is set to a real
after-condition. This is a straightforward one-way dependency (no special "manual but act on
completion" state), which is why dropping the separate `WhenComplete` field also simplified this
rule versus Revision 3.

The save rule is a pure function of the two dropdowns (+ target) and the load rule is a pure
function of the stored settings; they are mutual inverses for every reachable state (modulo the
documented `VideoEnd → AudioEnd` normalization).

---

## Error handling (per operation)

- **Load linked presentation fails / returns null** (`ISlidePresentationService.GetByIdAsync`):
  recoverable. The Slides card shows a safe fallback label ("Linked presentation unavailable");
  `Select` still works to relink; the rename pencil is disabled for the linked case. Logged at
  debug via `Debug.WriteLine` (consistent with `PrimeSlidePresentationCacheAsync`). Not surfaced as
  a blocking dialog.
- **Media library enumeration fails** (`IMediaLibraryService.GetByTypeAsync`): recoverable. The
  Source dropdown degrades to `None` (+ the "Current" sentinel/row when the item already had a
  background, so the existing value is still shown and preserved). Logged at debug.
- **Template enumeration / resolve fails** (`ITemplateService.GetAllAsync` /
  `GetByIdAsync` / `GetBuiltInTemplatesAsync`): recoverable. The design makes **no** no-throw or
  exact-count guarantee (Finding 7). On success the picker lists whatever
  `GetAllAsync`/`GetBuiltInTemplatesAsync` returns (which includes the code-seeded built-ins); if
  enumeration throws, the picker shows an **empty list** and the tile stays "No template selected".
  Logged at debug. (For reference only, `TemplateService.GetBuiltInTemplates()` seeds three
  built-ins — `Default Centered`, `Lower Third`, `Trendy Line` — but `GetAllAsync` also touches the
  DB via `EnsureInitializedAsync`, so it is treated as fallible.)
- **Deferred rename of linked presentation fails** (`UpdateAsync` throws or `GetByIdAsync` returns
  null at save time): recoverable, fatal-to-the-rename-only. Show a non-modal error via the existing
  `MessageBox` pattern used in `SlideEditorWindow.SaveAndClose_Click` ("Failed to rename the
  presentation: {msg}"); the rename is skipped but the rest of the save proceeds (the item's other
  edits still persist). Logged at debug.
- **Save fails** (`ServiceOrderManager.SaveAsync` throws): the host's `SaveServiceOrderAsync`
  already wraps this and calls `ShowToolError("Save Service", ex.Message)`. The editor has already
  closed on `Save and close`, so the host's error toast is the user-visible outcome (matches every
  other save site). The JSON file is authoritative and written before the mirror, so a mirror-only
  failure is swallowed by `UpsertRowSafeAsync` and does not lose data.
- **Remove with stale index** (item already gone): `RemoveItem` guards the index range and no-ops;
  `IndexOf` returning −1 short-circuits. No crash.
- **Add audio: file does not exist / cancelled dialog:** cancelled → no change. A path is accepted
  as-is (the app references audio by path elsewhere); no eager existence check (matches the legacy
  single-path field's permissiveness).

## Input validation (each external input)

- **Display name (rename):** optional-but-not-blank. Trimmed; an empty/whitespace entry is
  rejected (keep prior name), mirroring `SlideEditorWindow` ("a blank name keeps the prior
  non-empty name") and `SlideNameDialog` (OK disabled while blank). Written only on Save (deferred).
- **Transition time slider:** numeric, clamped to the slider range [0, 3000] ms; two-way bound to
  `TransitionTimeMs` (int). No free-text, so no parse failure.
- **Auto-advance delay (when `Time Delay`):** integer ms ≥ 0; non-numeric input rejected / coerced
  to the current value; maps to `ServiceItemSettings.AutoAdvanceDelayMs`.
- **`Go to next slide after` label → enum:** save through `BulkEditService.ParseAfterCondition`
  (`Disabled → Manual`); load through the four-member label map above (incl. `VideoEnd → "End of
  Media"`). Governed by the Auto-advance mapping rule above.
- **`When complete` label → `AutoAdvanceGoTo`:** direct label map, copying
  `ItemSettingsMapping.ToGoTo`/`GoToLabel` (`Do nothing→NextSlide`, `Next item→NextItem`,
  `Specific item→SpecificItem`, `Loop→Loop`). When `Specific item`, the target dropdown value (an
  item `Id` from `allItems`, current item excluded) is written to `GoToItemId`; the dropdown's
  displayed labels are the prebuilt resolved names from the Gap #1 "Target-label resolution" rule
  (two-arg `ResolveDisplayName` with a per-item `linked` argument). A missing/stale target id
  resolves to no selection and falls back to `Do nothing` (i.e. `NextSlide`) on save. There is **no
  `WhenCompleteAction` enum** — the destination is the existing `AutoAdvanceGoTo`.
- **Background Source:** enumerated choices only (`None` + optional "Current" sentinel + Image/Video
  library rows) — no free text. Mapping per Gap #3.
- **Template selection:** enumerated template ids only; a missing id resolves to "No template
  selected".
- **Audio paths:** strings from `OpenFileDialog` (filter above); stored verbatim.

## Invariant ownership

- **Live-link integrity (never snapshot a linked Slides item's name/content):** owned by the
  **editor** — the presentation `Select` writes `SlidePresentationId` only, and the rename writes
  the presentation (via the service, deferred to save) for linked items rather than `Title`.
  Enforced here because this editor is the only new write path to these fields; the pure
  `ServiceItemDisplayName` / `GetSlidesForItem` resolvers already guarantee display/content stay
  live.
- **Additive-JSON omission (`WhenWritingNull`):** owned by the **model + `ServiceOrderManager`**
  (the shared `_jsonOptions`). The editor only needs to leave fields null/default to get omission
  for free.
- **Audio list ⇄ legacy single path sync:** owned by the **editor's save step** (writes
  `AudioAttachmentPath = AudioAttachments?.FirstOrDefault() ?? ""`). The read side is owned by the
  pure `ServiceItemAudio.Resolve` helper.
- **"When complete" → existing `AutoAdvanceGoTo` + `GoToItemId` (no duplicate field):** owned by the
  **editor's mapping** — the dropdown writes `AutoAdvanceGoTo` (and `GoToItemId` for `SpecificItem`)
  using the same vocabulary `ItemSettingsMapping` uses, so the engine's `AutoAdvanceResolver` obeys
  the UI selection with no second source of truth. Documented as the Gap #1 label table.
- **Disabled-auto-advance canonical state:** owned by the **editor's load/save mapping** (the
  deterministic rule above), not by any model default.
- **Field preservation on save (no clobber of untouched fields):** owned by the **editor's in-place
  copy-back** (enumerated write list), so `Id`/`Order`/`Subtitle`/`Notes`/`Slides`/Song-Scripture
  fields survive unchanged.

---

## Testability

**Unit-testable in `WorshipPlayer.Tests` (net8.0, Core-only — no WPF):**

- `ServiceItemAudio.Resolve`: legacy single path → one-element list; populated list wins;
  both empty → empty (AC-8).
- Additive-field JSON round-trip through `ServiceOrderManager`'s `_jsonOptions` shape: serialize a
  `ServiceOrder` whose item has `AudioAttachments` and `StageTemplateId` set, plus an
  `AutoAdvanceSettings` with `AutoAdvanceGoTo = SpecificItem` + `GoToItemId` set (the "When complete"
  target) → deserialize → fields equal; and with the two new additive fields null/empty →
  **absent** from the JSON string (assert the serialized text does not contain `"audioAttachments"` /
  `"stageTemplateId"`). "When complete" has no new JSON key to assert — it round-trips via the
  existing `AutoAdvanceGoTo`/`GoToItemId` keys, so the test asserts those deserialize back equal
  (AC-13, NFR).
- Legacy-file load: a JSON blob with `audioAttachmentPath` set and none of the new keys
  deserializes with the three new fields null and `Resolve` yielding the legacy path first (AC-8).
- SQLite mirror: construct a `ServiceOrderManager` against a temp `app.db` + temp ServiceOrders
  folder (mirrors `TemplateServiceTests` temp-db discipline — clear SQLite pools in teardown),
  `LoadAsync`/set current/`SaveAsync`, then read `DataJson` from the `ServiceOrders` row and assert
  the additive fields are present and equal (AC-13). This reuses the public manager API; no schema
  change to assert.
- Auto-advance round-trip rule: a small pure mapping table covering the after-condition axis
  (Disabled/Manual, Time Delay/Timer, End of Media/AudioEnd, **and the VideoEnd→"End of Media" load
  fold**) × the "When complete" axis (`AutoAdvanceGoTo` NextSlide/NextItem/SpecificItem+GoToItemId/
  Loop) can be covered by extracting the load/save mapping into a pure static helper
  (`SlideAutoAdvanceMap`) in Core and testing it — including an explicit case asserting that a
  loaded `VideoEnd` shows "End of Media" and re-saves as `AudioEnd` (the documented normalization).
  This keeps the rule verifiable without WPF. (Recommended: implement the rule as such a helper so
  the code-behind just calls it.)
- `BulkEditService.ParseAfterCondition` already covers the shared label→enum mapping; the editor
  depends on it rather than duplicating, so the existing test guards both.

**Integration / manual (WPF, not automatable in the net8.0 test project):** window layout and
styling (AC-1), dirty→enable behavior (AC-10), discard on Close/X including the deferred rename
(AC-11), the pickers, the remove event wiring (AC-12), and the sidebar/center live-name refresh
after save (AC-2). These are verified by running
`dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` and a manual pass; noted as
non-unit-testable because the editor is a `Window` and the Tests project cannot reference the
WPF Presenter assembly (it links individual Core-safe sources only).

A design note on testability: all business rules that *can* be pure (audio resolve, label→enum,
auto-advance load/save map, display-name) live in Core helpers, so the `Window` code-behind stays
thin (bind, copy back, raise events). That keeps the hard-to-test WPF surface minimal — a sign the
split is right.

---

## Out of scope (restated)

Editing slide **content** (stays `SlideEditorWindow`); the Slides **library** grid; authoring
templates (Template Manager); importing new media; other item types' editors; applying the chosen
template to live rendering; any non-additive model refactor, EF adoption, or persistence-stack
change; deleting `bin`/`obj` or running a clean.

---

## Summary of decisions

- **Hosting:** standalone modal `Window` `SlideServiceItemEditorWindow` (merges
  `OwcDesignSystem.xaml`, uses keyed OWC styles) — NOT hosted in `OperatorWindow`, so the
  inline-brush rule does not apply here. Opened via `ShowDialog`, owned by `OperatorWindow`.
- **Gap #1 When-complete:** **NO new field or enum.** The "When complete" dropdown maps directly
  onto the EXISTING `ServiceItemSettings.AutoAdvanceGoTo` + `GoToItemId`, reusing
  `ItemSettingsMapping.ToGoTo`/`GoToLabel`'s vocabulary (`Do nothing→NextSlide`,
  `Next item→NextItem`, `Specific item→SpecificItem`+`GoToItemId`, `Loop→Loop`). This is what the
  engine's `AutoAdvanceResolver` already reads, so the UI selection drives live navigation with no
  duplicate source of truth and no `GoToItemId` collision. `AutoAdvanceGoTo`+`GoToItemId` only drive
  **live** navigation when the after-condition is `Timer`; "When complete" is greyed when "Go to
  next slide after" is "Disabled". `SpecificItem` consumes the constructor's `allItems` for the
  target dropdown.
- **Gap #2 Multi-audio:** new nullable `ServiceItem.AudioAttachments` (List<string>); pure
  `ServiceItemAudio.Resolve` migrates legacy `AudioAttachmentPath` to the first entry on read; save
  keeps the legacy single field in sync with `[0]` (not deprecated/dropped).
- **Gap #3 Background Source:** maps onto existing `BackgroundType`/`MediaPath`/`MediaType`, sourced
  from `IMediaLibraryService` filtered to `Image`/`Video` rows only. `None` nulls `BackgroundType` +
  clears `MediaPath`; a pre-existing `SolidColor`/`Gradient` background is shown as a read-only
  "Current: <type>" sentinel that preserves the fields untouched. No new store, no new field.
- **Gap #4 Template override:** new nullable `ServiceItem.StageTemplateId`, fully wired to the
  existing `ITemplateService`; the "thumbnail" is an accurate style-preview tile (SlideTemplate has
  no bitmap) — functional, not a stub.
- **Entry point:** `TryOpenSlideServiceItemEditor` branches Slides items out of both
  `EditSelectedServiceItem` and `EditServiceItemDirect`; other types keep `AddServiceItemDialog`.
- **Remove:** a `RemoveRequested` event (never `Result`); host runs the existing
  remove/save/refresh once; remove and save-and-close are **mutually exclusive** and Close/X does
  neither.
- **Persistence:** reuse `ServiceOrderManager.SaveAsync`; save copies an enumerated field set back
  **in place** onto the live item (Id/Order and all untouched fields preserved); additive fields
  round-trip through JSON and the `ServiceOrders.DataJson` mirror with no manager/schema change;
  dirty flag gates `Save and close`; `Close`/X discards (including the deferred rename).
- **Auto-advance:** a single deterministic load/save rule makes `Disabled` round-trip and
  coexist with a when-complete action (extractable as a pure `SlideAutoAdvanceMap` helper).
- **Tests:** add `SlideServiceItemEditorModelTests` (round-trip, omission-when-null, legacy
  migration, SQLite-mirror presence) + the pure auto-advance map test; reuse
  `BulkEditService.ParseAfterCondition`'s existing test.

---

## Review responses (disposition of every finding)

### Revision 3 review (current pass — HIGH×1, MEDIUM×2, NIT×3, CHANGES_REQUESTED)

**Finding 1 [HIGH] — `WhenComplete` duplicates and conflicts with the existing `AutoAdvanceGoTo` +
`AutoAdvanceResolver`; nothing consumes it and it collides on `GoToItemId`.**
**ADDRESSED (preferred fix, applied verbatim from the decided resolution).** Dropped the
`WhenComplete` field and the `WhenCompleteAction` enum **entirely** — there is now **no new model
field for Gap #1**. The "When complete" dropdown maps directly onto the **existing**
`ServiceItemSettings.AutoAdvanceGoTo` + `GoToItemId`, reusing `ItemSettingsMapping.ToGoTo`/`GoToLabel`'s
vocabulary (`Do nothing→NextSlide`, `Next item→NextItem`, `Specific item→SpecificItem`+`GoToItemId`,
`Loop→Loop`). This is what `AutoAdvanceResolver` already reads, so the UI selection drives live
navigation with one source of truth and no `GoToItemId` collision. Clarified that
`AutoAdvanceGoTo`+`GoToItemId` only drive live navigation when the after-condition is `Timer` (set by
"Go to next slide after"), that "When complete" is greyed when "Go to next slide after" is
"Disabled", and that AC-13's round-trip now targets `AutoAdvanceGoTo`/`GoToItemId` (no `whenComplete`
JSON key). Updated Gap #1, the Auto-advance mapping section, Input validation, Invariant ownership,
the Files list, the clone paragraph, the copy-back table, Testability, and the Summary of decisions.

**Finding 2 [MEDIUM] — `AutoAdvanceAfterCondition.VideoEnd` exists but the load-side map drops it.**
**ADDRESSED.** The Auto-advance mapping "load side" now gives the full four-member map and folds
`VideoEnd → "End of Media"` (copying the existing `ItemSettingsMapping.AfterLabel`, verified in
source), documenting that re-saving normalizes `VideoEnd → AudioEnd` as an **intended, harmless**
normalization (the resolver treats `AudioEnd`/`VideoEnd` identically as deferred), **not** data loss.
Added a matching Testability case.

**Finding 3 [MEDIUM] — SolidColor/Gradient "Current:" sentinel selected on open could arm Save.**
**ADDRESSED.** The Dirty-tracking section now states that **all** initial dropdown/control
selections on open — Source (incl. the "Current: &lt;type&gt;" sentinel and the transient
current-file row), "Go to next slide after", "When complete", the specific-item target, and the
template — happen **inside** the `_suppressDirty` guard (held across the whole sync+async
population), with `_isDirty` **asserted `false` at end of construction** and the invariant
"Programmatic initial selection never sets `_isDirty`; only user-originated changes do." Preserves
AC-10.

**Finding 4 [NIT] — async target-list build has no hook in a synchronous constructor.**
**ADDRESSED.** The Constructor/inputs section and the Gap #1 "Target-label resolution" rule now
specify: synchronous deep-clone + `_live`/`allItems` capture + `_suppressDirty = true` in the
constructor; all awaited population in a `Loaded` handler / `async void` init, `_suppressDirty` held
for the entire awaited sequence and cleared only after all awaits complete; no `.Result`/`.Wait()`
on the UI dispatcher.

**Finding 5 [NIT] — remove-flow mutual exclusion not pinned at the editor's Remove button handler.**
**ADDRESSED.** The Remove flow contract now states explicitly that the editor's in-window
`Remove service item` button handler **raises `RemoveRequested` and does nothing else** — it must
not set `Result`/`DialogResult`, call `Close`, or touch the `ServiceOrderManager`; the host owns
closing via `DialogResult = false`, keeping `removed`/`saved` mutually exclusive.

**Finding 6 [NIT] — `allItems` passed as the live `CurrentServiceOrder.Items`.**
**ADDRESSED.** The Constructor/inputs section now states this is a deliberate choice: the modal
(`ShowDialog`) nature makes passing the live list safe (no concurrent mutation while open), and the
specific-item target is stored as a position-independent id in `GoToItemId`. No defensive snapshot
is made.

### Revision 2 review (prior pass — MEDIUM×2, NIT×3, CHANGES_REQUESTED)

**Finding 1 [MEDIUM] — Gap #3 reasons about a non-existent `MediaLibraryType.Document`.**
**ADDRESSED.** Verified in source: `WorshipPlayer.Core/Models/MediaLibraryItem.cs` defines
`enum MediaLibraryType { Image, Video, Audio }` — no `Document`. Gap #3's dropdown-composition rule
now states the enum is exactly `{ Image, Video, Audio }`, sources rows from
`GetByTypeAsync(Image)` + `GetByTypeAsync(Video)`, and excludes only `MediaLibraryType.Audio`. The
stale "Audio and Document excluded" phrasing in the Revision-1 Finding 4 response was corrected to
drop the `Document` mention. See Gap #3.

**Finding 2 [MEDIUM] — specific-item target dropdown names a helper whose two-arg signature it never
satisfies.** **ADDRESSED.** Verified `ServiceItemDisplayName.ResolveDisplayName(ServiceItem,
SlidePresentation?)` is pure and two-arg with no cache/service fetch and no one-arg overload. Gap #1
now carries an explicit "Target-label resolution" rule: build the target list once on open,
asynchronously under `_suppressDirty`, resolving each item's `linked` argument via
`it.SlidePresentationId is null ? null : await _slidePresentationService.GetByIdAsync(it.SlidePresentationId)`
(non-Slides/non-linked/deleted → `linked: null`, falling back to `Title`). No dependency on
`OperatorWindow._slidePresentationCache`; the prebuilt map alternative is noted as the sanctioned
fallback. The Input-validation entry was updated to point at this rule. See Gap #1 and Input
validation.

**Finding 3 [NIT] — entry-point snippet claims both call sites pass `display.Item`.**
**ADDRESSED.** Verified `EditServiceItemDirect(ServiceItem item)` has no `display` variable while
`EditSelectedServiceItem()` reads `ServiceOrderList.SelectedItem is ServiceItemDisplay display`. The
Entry-point wiring prose now distinguishes the two call sites:
`TryOpenSlideServiceItemEditor(display.Item)` in `EditSelectedServiceItem`,
`TryOpenSlideServiceItemEditor(item)` in `EditServiceItemDirect`. See Entry-point wiring.

**Finding 4 [NIT] — fabricated settings object does not define `TransitionFadeTimeMs`.**
**ADDRESSED.** The save-copy-back section and both auto-advance save rules now state that whenever a
`ServiceItemSettings` object is created/updated, `TransitionFadeTimeMs = _working.TransitionTimeMs`,
keeping it consistent with the slider-bound `ServiceItem.TransitionTimeMs`; when no settings object
exists, `TransitionTimeMs` is the single source of truth. See "Save — copy-back mechanism" and
Auto-advance mapping.

**Finding 5 [NIT] — clone options (no `WhenWritingNull`) safe but under-justified.**
**ADDRESSED.** The Constructor/inputs section now explains that the in-memory clone intentionally
serializes nulls (to preserve the input state), round-trips the three additive fields as ordinary
properties with the enum converter covering `WhenCompleteAction`, and that null/default omission is
the manager's `_jsonOptions` job on the real save — with an explicit warning not to "fix" the clone
to the manager options. See Constructor / inputs.

### Revision 1 review (prior pass — HIGH×2, MEDIUM×4, NIT×3)

**Finding 1 [HIGH] — "When complete" enum collides with `AutoAdvanceGoTo` and drops "specific item".**
**ADDRESSED.** Dropped `Clear`; added `GoToSpecificItem`; the enum is now
`{ DoNothing, GoToNextItem, GoToSpecificItem, Loop }`. Added an explicit interaction rule:
`WhenComplete` is end-of-item navigation, independent of `AutoAdvanceGoTo` (slide-to-slide), and
`GoToSpecificItem` stores its target in the **existing** `GoToItemId`. `allItems` is now consumed to
populate the specific-item target dropdown (current item excluded). See Gap #1.

**Finding 2 [HIGH] — Remove flow specified two ways with a save-ordering ambiguity.**
**ADDRESSED.** Pinned to a single contract: `public event EventHandler? RemoveRequested;`, the
window sets `DialogResult = false` and closes on remove, and `Result` stays `ServiceItem?` (save
only). The host snippet now calls `RemoveItem(index)` first in the `removed` branch, returns early
on Close/X (`!saved`), and saves/refreshes exactly once. Remove and save-and-close are stated as
mutually exclusive; Close/X does neither. See Entry-point wiring.

**Finding 3 [MEDIUM] — Linked rename persists even when the item edit is discarded.**
**ADDRESSED (option b, the preferred one).** The linked-presentation rename is now **deferred** to
the Save-and-close step (held in `_pendingPresentationName`, applied via `UpdateAsync` only on save,
before persist/prime). Close/X drops the pending name, so AC-11 holds. See Rename semantics.

**Finding 4 [MEDIUM] — Background Source ignores SolidColor/Gradient and leaves Document rows
unaddressed.** **ADDRESSED.** Added a read-only "Current: <type>" sentinel row for pre-existing
`SolidColor`/`Gradient` backgrounds that preserves the fields untouched unless the user changes
away from it; stated the Source list is filtered to `MediaLibraryType.Image`/`Video` only, with
only `MediaLibraryType.Audio` excluded (`MediaLibraryType` is exactly `{ Image, Video, Audio }` —
there is no `Document` member; see Revision-2 Finding 1). Added the load-side selection rule,
including a transient "Current: <file>" row for an out-of-library media path. See Gap #3.

**Finding 5 [MEDIUM] — "Disabled" auto-advance is a branch, not a round-trip rule.**
**ADDRESSED.** Replaced with one deterministic rule covering both save and load:
`Disabled ⇔ settings null OR (Manual AND WhenComplete null/DoNothing)`; load shows `Disabled` for
that canonical state and `Manual` when a when-complete action coexists; save writes `null` for the
canonical off state and a `Manual` settings object only when a when-complete action requires it.
Recommended extracting it as a pure `SlideAutoAdvanceMap` helper for unit testing. See the
Auto-advance mapping section.

**Finding 6 [MEDIUM] — Working-copy copy-back mechanism unspecified.**
**ADDRESSED.** Specified in-place copy-back of an enumerated field set onto the live item (not a
list-element replace), with a full table of fields written and an explicit list of fields preserved
(`Id`, `Subtitle`, `Notes`, `DurationMinutes`, `Order`, `ThemeId`, `ForegroundType`, `Slides`,
Song/Scripture). See "Save — copy-back mechanism".

**Finding 7 [NIT] — Template fallback over-claims built-ins "cannot fail".**
**ADDRESSED.** Dropped the no-throw/exact-count assertion; the error-handling entry now says it
falls back to whatever `GetAllAsync`/`GetBuiltInTemplatesAsync` returns and shows an empty picker if
enumeration throws. The three built-in names are cited for reference only (verified in
`TemplateService.GetBuiltInTemplates`), not relied on as a guarantee.

**Finding 8 [NIT] — Window clone options vs manager options not disambiguated.**
**ADDRESSED.** Named the window's clone options `_cloneJsonOptions` and stated they are local,
need only `JsonStringEnumConverter`, and are intentionally not the manager's instance since the
clone never hits disk. See Constructor / inputs.

**Finding 9 [NIT] — Audio "+" filter not enumerated.**
**ADDRESSED.** Named the filter: `"Audio|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All files|*.*"`. See
Pickers → Audio add.
