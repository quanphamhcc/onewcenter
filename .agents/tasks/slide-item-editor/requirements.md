# Requirements — Edit Service Item (Slides) screen

## Summary

Build a full-featured "Edit Service Item" screen for a Slides-type service item that is
already part of a service order. This is distinct from two existing editors:

- The Slides **library** grid (browse/manage authored presentations).
- `SlideEditorWindow`, which edits slide **content** of a `SlidePresentation`.

This screen edits the **service-item wrapper**: which library presentation the item links to,
its background media, auto-advance behavior, transition timing, attached audio, and an optional
per-item stage-display template override — plus renaming the item's display name. The current
path routes Slides items through the generic `AddServiceItemDialog`
(`OperatorWindow.EditSelectedServiceItem`); this feature replaces that path for Slides items with
a dedicated, polished dark-theme editor.

The user explicitly asked for "full, đầy đủ chức năng và layout đẹp" (full, complete functionality
and a beautiful layout), so both the complete behavior set below and a dark-theme layout matching
the OWC design system (`OwcDesignSystem.xaml`, as used by `SlideEditorWindow`) are in scope.

The editor must keep the Slides item a **live link** by `SlidePresentationId` — never snapshot the
presentation's content or name. Display name, content, and the linked presentation are resolved on
demand through the existing `ServiceItemDisplayName` / `_slidePresentationCache` /
`PrimeSlidePresentationCacheAsync` machinery in `OperatorWindow`.

### Assumptions (to be confirmed in design review)

- A template store already exists (`ITemplateService` with full CRUD and `SlideTemplate` carrying
  font/thumbnail-relevant fields), so the Stage Display Template override is expected to be fully
  wired to `ITemplateService`, not a stub. If design finds the template is not a true
  stage-display concept, a documented functional stub is the fallback (requirement FR-7).
- Background media "Source" maps to the existing `ServiceItem.BackgroundType` /
  `MediaPath` / `MediaType` fields and sources choices from the existing media library
  (`IMediaLibraryService`). No new media store is introduced.
- The editor opens modally (owned window, `ShowDialog`), consistent with
  `SlideEditorWindow` / `ItemSettingsWindow` / `AddServiceItemDialog`.
- "Unsaved change" is tracked as a dirty flag that flips true on any edit to any bound field and
  is reset after a successful save.

## Functional Requirements

### FR-1 Header bar (name + actions)

- Left: the service item's **display name**, large and bold, resolved via
  `ServiceItemDisplayName.ResolveDisplayName` (live presentation name for a linked Slides item,
  falling back to the stored `Title`).
- A small pencil icon button next to the name opens inline rename.
- Right: three buttons — **Remove service item**, **Close**, and **Save and close** (primary blue,
  styled `OwcBlueButton`). "Save and close" is disabled until there is an unsaved change.
- Renaming a **live-linked** item renames the underlying presentation's name (so the live display
  name everywhere reflects it), consistent with the live-name convention; renaming a non-linked
  (legacy snapshot) item updates the stored `Title`. Design decides and documents which field the
  pencil writes for each case so the sidebar + center pane update live.

### FR-2 Slides section (live link)

- A surface card shows the linked presentation's **name** (bold), resolved live via
  `ISlidePresentationService.GetByIdAsync(SlidePresentationId)`.
- A **Select** action on the right opens a picker of library presentations
  (`ISlidePresentationService.GetAllAsync`) to change which presentation this item links to.
- Selecting a presentation sets `SlidePresentationId` only — it must **not** snapshot content or
  copy the name into `Title`. The item stays a live link.
- If the currently linked presentation was deleted, the card shows a safe fallback label rather
  than crashing.

### FR-3 Background media section

- A card with a **Source** dropdown: `None` plus available background media sources drawn from the
  existing media library.
- Selecting a source wires the item's background fields (`BackgroundType`, `MediaPath`,
  `MediaType`). `None` clears them (sets `BackgroundType` to null per the `WhenWritingNull`
  convention and clears `MediaPath`).
- No new media store is created; choices come from `IMediaLibraryService`.

### FR-4 Auto-advance section

- A **Go to next slide after** dropdown: `Disabled` plus the existing after-conditions
  (Time Delay → `Timer`, End of Media → `AudioEnd`, Manual). Mapping must match
  `BulkEditService.ParseAfterCondition` exactly so bulk-edit and per-item edit agree.
- A **When complete** dropdown: `Do nothing` plus sensible actions (e.g. next item / loop /
  specific item), mapped to the additive "when complete" field (FR-5 gap #1).
- **When complete** is disabled/greyed when **Go to next slide after** is `Disabled`.
- Values round-trip through `ServiceItem.AutoAdvanceSettings` (`ServiceItemSettings`): after-
  condition, delay, and go-to target.

### FR-5 Settings section (two side-by-side cards)

- **Transitions** card: a **Transition time** slider bound to `ServiceItem.TransitionTimeMs`
  (ms; existing default 500).
- **Audio** card: a list of attached audio files with a **+** button to add a file; empty state
  shows "No audio files added". Backed by the multi-audio field (FR-5 gap #2), which migrates the
  legacy single `AudioAttachmentPath` into a list, backward-compatibly.

### FR-6 Stage Display Template override section

- A **thumbnail** + a **"No template selected"** label + a **Select** action to pick a stage-
  display template.
- Preferred: fully wire to `ITemplateService` (`GetAllAsync` / `GetByIdAsync`) and store the chosen
  template id on the item via a new additive nullable field (FR-5 gap #4). "Select" opens a picker;
  the thumbnail/label reflect the selection; clearing restores "No template selected".
- Fallback only if design determines no suitable stage-display template concept exists: a
  documented functional stub that persists the chosen id round-trips but notes the limitation.

### FR-7 Persistence and refresh

- **Save and close** persists via the existing `ServiceOrderManager.SaveAsync` (JSON authoritative
  + SQLite mirror) and refreshes the service listing / sidebar / center pane, including re-priming
  the slide presentation cache (`PrimeSlidePresentationCacheAsync`) so live names/content update.
- **Close** (and window-X) discards all unsaved edits, mirroring `SlideEditorWindow`.
- **Remove service item** removes the item from the current service order via the existing
  remove/save/refresh flow (`ServiceOrderManager.RemoveItem` → `SaveAsync` → refresh) and closes
  the editor.

### Model gaps to resolve in design (additive, backward-compatible)

1. **"When complete" action** — an additive enum field (JSON-serialized, omitted when
   default/null per the `WhenWritingNull` convention). Lives on `ServiceItemSettings` or
   `ServiceItem` as design decides; must not break existing files that lack it.
2. **Multiple audio files** — migrate the legacy single `AudioAttachmentPath` (string) into a
   list. On load, a non-empty legacy value becomes the list's first entry; the legacy field stays
   readable for backward compatibility. Design specifies the exact migration/serialization shape.
3. **Background media Source** — map to existing `BackgroundType` / `MediaPath` / `MediaType` and
   the existing media library. Do not invent a store. `None` clears the fields.
4. **Per-item Stage Display Template override** — a new additive nullable template-id field,
   JSON-omitted when null. Prefer full `ITemplateService` wiring; otherwise a documented functional
   stub (see FR-6).

## Non-Functional Requirements

- **Backward/additive compatibility**: model changes are additive only. The three real
  service-order JSON files and the demo files must still load without error. Nullable additive
  fields are omitted from JSON when null/default (`WhenWritingNull`).
- **Persistence stack**: no EF. SQLite access only via the shared `AppDatabase`
  (`Microsoft.Data.Sqlite`). Additive JSON fields flow into the `ServiceOrders.DataJson` mirror
  automatically; a round-trip test must verify this.
- **Theming/branding**: keep OWC branding and the dark theme, reusing `OwcDesignSystem.xaml`
  styles (`OwcBlueButton`, `OwcPanelBorder`, `OwcTextBox`, `OwcTextSecondaryBrush`, etc.) exactly
  as `SlideEditorWindow` does.
- **Live-link integrity**: the Slides item is never snapshotted; display name and content resolve
  on demand through the existing cache/display-name helpers.
- **Build/test**: builds with `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug`;
  tests run with `dotnet test`. NEVER delete `bin`/`obj` or run a clean — runtime data lives under
  `bin`.

## Acceptance Criteria

1. Opening the editor on a Slides service item shows a dark-theme window matching the OWC design
   system, with header, Slides, Background media, Auto-advance, Settings (Transitions + Audio), and
   Stage Display Template sections laid out top to bottom as specified.
2. The header shows the item's live display name (resolved via `ServiceItemDisplayName`), and a
   pencil button renames it; after save+refresh the new name appears in both the sidebar row and
   the center section header without any stored `Title` snapshot of the presentation name.
3. The Slides card shows the linked presentation's current name; **Select** changes
   `SlidePresentationId` only, and reopening/reloading proves no content or name was snapshotted
   (editing the presentation elsewhere still reflects live).
4. The Background **Source** dropdown lists `None` + media-library sources; choosing a source sets
   `BackgroundType`/`MediaPath`/`MediaType`, and choosing `None` clears them (null
   `BackgroundType` is omitted from the saved JSON).
5. **Go to next slide after** offers `Disabled` + after-conditions mapped identically to
   `BulkEditService.ParseAfterCondition`; **When complete** offers `Do nothing` + actions and is
   greyed/disabled whenever **Go to next slide after** is `Disabled`.
6. The Transition time slider is two-way bound to `TransitionTimeMs`; moving it and saving persists
   the new value across a reload.
7. The Audio card shows "No audio files added" when empty, lists each attached file, and the **+**
   button adds a file to the list; the list persists across a reload.
8. A service order written before this feature (legacy single `AudioAttachmentPath` set, no
   when-complete, no template-override fields) loads without error, and its legacy audio path
   appears as the first entry in the audio list.
9. The Stage Display Template section shows "No template selected" with a placeholder thumbnail by
   default; **Select** picks a template (from `ITemplateService` when wired), updating the
   thumbnail/label, and the chosen id persists across a reload. If a stub is used, the limitation
   is documented and the id still round-trips.
10. **Save and close** is disabled on open with no changes, enables on the first edit to any field,
    persists via `ServiceOrderManager.SaveAsync`, refreshes the listing/sidebar/center, and
    re-primes the slide presentation cache.
11. **Close** (and window-X) discards all unsaved edits — reopening shows the pre-edit state.
12. **Remove service item** removes the item from the current service order via the existing
    remove/save/refresh flow and closes the editor; the item no longer appears in the sidebar.
13. A round-trip unit/integration test confirms every additive field (audio list, template-override
    id) serializes to JSON, is omitted when null/default, loads back identically, and is present in
    the `ServiceOrders.DataJson` SQLite mirror after `SaveAsync`. The "when complete" selection is
    **not** a new field — it is persisted via the existing `AutoAdvanceSettings.AutoAdvanceGoTo` +
    `GoToItemId`; the test asserts those existing keys round-trip (e.g. `SpecificItem` + a
    `GoToItemId`) rather than a `whenComplete` key.
14. `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds and `dotnet test`
    passes; no `bin`/`obj` deletion or clean was performed.

## Out of Scope

- Editing slide **content** (text, order, per-slide background) — that remains `SlideEditorWindow`.
- The Slides **library** grid (create/duplicate/delete presentations).
- Authoring or editing **templates** themselves (that is the Template Manager); this feature only
  selects an existing template as a per-item override.
- Importing new media into the library (the Source dropdown consumes existing library media only).
- Any change to Song/Scripture/other service-item types' editors.
- Non-additive model refactors, EF adoption, or changes to the JSON-authoritative +
  SQLite-mirror persistence design.
