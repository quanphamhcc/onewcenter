# Implementation Plan — Edit Service Item (Slides) editor

Derived from the APPROVED `design.md` (Revision 4) and `requirements.md`. All paths are absolute
under the worktree `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slide-item-editor`
on branch `feature/slide-item-editor`. Build command (run from the worktree):
`dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug`. Tests: `dotnet test`.
NEVER delete/clean `bin`/`obj` — runtime data lives under `bin`. Model changes are additive only;
the three real service JSONs + demos must still load.

Decisions grounded in the code I read:
- `WorshipPlayer.Tests` references `WorshipPlayer.Core` by project ref and links individual
  Presenter sources via `<Compile Include>` (it is net8.0, cannot reference the WPF Presenter).
  Therefore ALL unit-testable rules live in **Core** (`ServiceItemAudio`, `SlideAutoAdvanceMap`) so
  the WPF code-behind stays thin and the tests need no new linked Presenter source.
- "When complete" adds NO new field — it maps onto the existing
  `ServiceItemSettings.AutoAdvanceGoTo` + `GoToItemId` (verified in `ServiceItemSettings.cs` and
  `ItemSettingsPanel.xaml.cs` / `ItemSettingsMapping`).
- The editor is a standalone modal `Window` that merges `OwcDesignSystem.xaml` and uses keyed
  styles (`OwcButtonBase`, `OwcBlueButton`, `OwcTextBox`, `OwcTextSecondaryBrush`, `OwcPanelBorder`,
  etc.), exactly as `SlideEditorWindow.xaml` does — NOT hosted in `OperatorWindow` (so no inline
  brushes).

---

- [ ] 1. Add the two additive nullable fields to `ServiceItem`.
      Add `public List<string>? AudioAttachments` (`[JsonPropertyName("audioAttachments")]`) and
      `public string? StageTemplateId` (`[JsonPropertyName("stageTemplateId")]`) to `ServiceItem`.
      Leave `AudioAttachmentPath` (non-nullable string) and `ServiceItemSettings` unchanged. Both
      new props are plain nullable so the manager's `WhenWritingNull` omits them when null.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slide-item-editor\WorshipPlayer.Core\Models\ServiceOrder.cs`
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds.

- [ ] 2. Create the pure `ServiceItemAudio` helper in Core.
      Static `Resolve(ServiceItem) : List<string>` — returns a copy of `AudioAttachments` when
      non-null/non-empty, else a one-element list from a non-empty legacy `AudioAttachmentPath`,
      else empty. Pure; never mutates the item.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slide-item-editor\WorshipPlayer.Core\Services\ServiceItemAudio.cs`
      Verify: build succeeds (tested in task 4).

- [ ] 3. Create the pure `SlideAutoAdvanceMap` helper in Core.
      Static load/save mapping for the two Auto-advance dropdowns onto existing
      `ServiceItemSettings` fields. Save: "Go to next slide after" label → `AutoAdvanceAfterCondition`
      via `BulkEditService.ParseAfterCondition` ("Disabled"→Manual); "When complete" label →
      `AutoAdvanceGoTo` (`Do nothing`→NextSlide, `Next item`→NextItem, `Specific item`→SpecificItem,
      `Loop`→Loop) + `GoToItemId` for SpecificItem. Load: inverse using the four-member after map
      (`Timer`→"Time Delay", `AudioEnd`/`VideoEnd`→"End of Media", `Manual`→"Disabled") copied from
      `ItemSettingsMapping.AfterLabel`, and `GoToLabel` (NextSlide→"Do nothing", NextItem→"Next item",
      SpecificItem→"Specific item", Loop→"Loop"). Expose the "When-complete enabled only when
      after≠Disabled" rule as a predicate.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slide-item-editor\WorshipPlayer.Core\Services\SlideAutoAdvanceMap.cs`
      Verify: build succeeds (tested in task 4).

- [ ] 4. Add `SlideServiceItemEditorModelTests` (Core-only, xUnit).
      Cover: (a) `ServiceItemAudio.Resolve` — legacy single path→one-element list, populated list
      wins, both empty→empty; (b) additive-field JSON round-trip through a `JsonSerializerOptions`
      matching the manager's shape (`WriteIndented`, `WhenWritingNull`, `JsonStringEnumConverter`):
      item with `AudioAttachments` + `StageTemplateId` + `AutoAdvanceSettings{AutoAdvanceGoTo=SpecificItem,GoToItemId=…}`
      round-trips equal; with both new fields null the serialized text does NOT contain
      `"audioAttachments"`/`"stageTemplateId"`; (c) legacy-file load — a JSON blob with
      `audioAttachmentPath` and none of the new keys deserializes with the new fields null and
      `Resolve` yields the legacy path first; (d) `SlideAutoAdvanceMap` load/save table incl. the
      `VideoEnd`→"End of Media"→re-save as `AudioEnd` normalization; (e) SQLite mirror — build a
      `ServiceOrderManager` against a temp `app.db` + temp ServiceOrders folder (mirror
      `TemplateServiceTests` temp-db + `SqliteConnection.ClearAllPools()` teardown), set current,
      `SaveAsync`, read `DataJson` from the `ServiceOrders` row, assert the additive fields present
      and equal.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slide-item-editor\WorshipPlayer.Tests\SlideServiceItemEditorModelTests.cs`
      Verify: `dotnet test --filter "FullyQualifiedName~SlideServiceItemEditorModelTests"` — all new tests pass.

- [ ] 5. Create `SlideServiceItemEditorWindow` XAML (dark theme, keyed OWC styles).
      Standalone `Window` in `Views\Slides\`, `Width≈760 Height≈780`,
      `WindowStartupLocation=CenterOwner`, `Background="#0F1117"`, merging
      `pack://application:,,,/WorshipPlayer.Presenter;component/Themes/OwcDesignSystem.xaml`.
      `ScrollViewer` body with sections top→bottom: (1) Header — bold name `TextBlock` + pencil
      `Button` that swaps to an inline `OwcTextBox`; right-side `Remove service item`, `Close`
      (`OwcButtonBase`), `Save and close` (`OwcBlueButton`, `IsEnabled` bound to dirty); (2) Slides
      card (`OwcPanelBorder`) — linked name + `Select`; (3) Background media — `Source` ComboBox;
      (4) Auto-advance — "Go to next slide after" + "When complete" ComboBoxes (latter greyed when
      former is Disabled), a delay field shown only for Time Delay, a target-item ComboBox shown
      only for Specific item; (5) Settings row — two cards: Transitions (`Slider` 0–3000ms + value
      label) and Audio (`ListBox` + `+` `OwcBlueButton`, per-row remove `×`, "No audio files added"
      empty state); (6) Stage Display Template — style-preview tile + label (default "No template
      selected") + `Select` + `Clear`.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slide-item-editor\WorshipPlayer.Presenter\Views\Slides\SlideServiceItemEditorWindow.xaml`
      Verify: build succeeds after task 6 wires the code-behind.

- [ ] 6. Create `SlideServiceItemEditorWindow` code-behind.
      Constructor `(ServiceItem item, IReadOnlyList<ServiceItem> allItems, ISlidePresentationService,
      IMediaLibraryService, ITemplateService)`. Synchronous ctor does only: deep-clone `_working`
      via JSON round-trip with a window-local `_cloneJsonOptions` (only
      `Converters={ new JsonStringEnumConverter() }`, NO `WhenWritingNull`), capture `_live` + `allItems`,
      set `_suppressDirty=true`. A `Loaded` handler runs all awaited population (linked-presentation
      name, media-library enumeration via `GetByTypeAsync(Image)`+`(Video)`, template resolve,
      specific-item target labels via `ServiceItemDisplayName.ResolveDisplayName(it, …GetByIdAsync…)`)
      and clears `_suppressDirty` only after all awaits complete; initial selections never arm dirty
      (`_isDirty` asserted false after population). Implement: dirty tracking; Source load/save per
      Gap #3 (None / "Current:<type>" sentinel for SolidColor/Gradient / Image-Video rows / transient
      out-of-library row); Auto-advance via `SlideAutoAdvanceMap`; Transitions slider bound to
      `_working.TransitionTimeMs`; Audio list backed by `ServiceItemAudio.Resolve`, `+` uses
      `OpenFileDialog` filter `"Audio|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All files|*.*"`; template
      style-preview tile + pickers (presentation/template small modal lists that also merge
      `OwcDesignSystem.xaml`). Rename deferred to save (`_pendingPresentationName` for linked items via
      `ISlidePresentationService.UpdateAsync`; `_working.Title` for non-linked). `public event
      EventHandler? RemoveRequested;` raised by the Remove button and NOTHING else. `public ServiceItem?
      Result`. Save: apply deferred rename, copy the enumerated edited field set back onto `_live`
      in place (set `TransitionFadeTimeMs = _working.TransitionTimeMs` whenever a settings object is
      created/updated; keep `AudioAttachmentPath = AudioAttachments?.FirstOrDefault() ?? ""`),
      `Result=item`, `DialogResult=true`. Close/X discards.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slide-item-editor\WorshipPlayer.Presenter\Views\Slides\SlideServiceItemEditorWindow.xaml.cs`
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds.

- [ ] 7. Wire the entry points in `OperatorWindow`.
      Add `private bool TryOpenSlideServiceItemEditor(ServiceItem item)`: returns false for non-Slides
      or when any of `_serviceOrderManager?.CurrentServiceOrder`, `_slidePresentationService`,
      `_mediaLibraryService`, `_templateService` is null (fall back to the generic dialog). Construct
      the editor owned by `this`, subscribe `RemoveRequested` (set `removed=true; DialogResult=false;
      Close()`), `ShowDialog()`, then on the dispatcher: if removed → `IndexOf`+`RemoveItem`; if not
      saved → return; else (saved) the window already copied back. Then `await SaveServiceOrderAsync();
      await PrimeSlidePresentationCacheAsync(); RefreshServiceOrderList(); RefreshSlideSections();`.
      Call `if (TryOpenSlideServiceItemEditor(display.Item)) return;` at the top of
      `EditSelectedServiceItem()` (before building `AddServiceItemDialog`) and
      `if (TryOpenSlideServiceItemEditor(item)) return;` at the top of `EditServiceItemDirect(item)`.
      Leave `OpenSelectedItemSettings` and all other item types unchanged.
      Files: `c:\Users\Quang\Documents\OneWorshipcenter\owc-multitrack-feature-lyric-presenter\owc-multitrack-feature-lyric-presenter\.worktrees\slide-item-editor\WorshipPlayer.Presenter\Views\OperatorWindow.xaml.cs`
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds.

- [ ] 8. Final verification: build + full test suite + legacy-load check.
      Confirm the three real service-order JSONs + demo files still deserialize without error
      (covered by the legacy-load test in task 4; if any real file path is available under the
      worktree, load it in a test too). Run the whole suite.
      Files: none (verification only).
      Verify: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug` succeeds AND
      `dotnet test` passes; no `bin`/`obj` deletion or clean was performed.
