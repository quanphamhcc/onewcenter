# Design Review — Home & Services-List Screens (OWC Presenter)

Reviewed: `.agents/tasks/home-services-screens/design.md`
Repository: `owc-multitrack-feature-lyric-presenter`
Review mode: fresh read, every load-bearing source claim re-verified against the actual code.

## Summary

This is a mature design that has already absorbed two prior review rounds, and it shows: the
crash-avoidance invariants (no `{StaticResource}` in `OperatorWindow.xaml`, keyed-only styles,
UserControls that merge `OwcDesignSystem.xaml` in their own scope), the Core/WPF split for
testability, the reuse of the existing `ServiceOrderManager` scan, and the scoped "untouched areas"
list are all correct and well-matched to the source. The great majority of the source-level claims
the design makes — the save wrapper being parameterless, `GetDefaultFilePath`/`SanitizeFileName`
being private, `OnServiceOrderLoaded` only firing `GoToItem(0)` for non-empty orders, the `*Brush`
resource-key names, the `Initialize(...)` signature being a synchronous `void`, the
`NewService_Click` gating + `AddServiceItemDialog` chaining, the `ServiceInfoDialog.Initialize`
signature, and the 200/220 sidebar-width drift — were checked and are accurate.

One integration contract does not hold against the current source and would make a documented
behavior ("only switch the center on a successful load") un-implementable as written. That is the
blocking finding. Two NITs follow.

## Findings

1. **MEDIUM — `LoadServiceOrderAsync` returns no success signal, but Decision 3 / item 14 branch on "only on success".**
   Decision 3 says Open "funnels through … `LoadServiceOrderAsync(path)` … then `ShowServiceEditor()`",
   and item 14 is explicit: *"`OpenServiceRequested(path)` → `await LoadServiceOrderAsync(path);` and
   **only on success** `_currentServicePath = path; ShowServiceEditor(); RefreshServiceOrderList();`
   (we do not switch the center if the load failed)."* Verified in `OperatorWindow.xaml.cs`:
   `private async Task LoadServiceOrderAsync(string path)` returns `Task` (not `Task<bool>`) and
   **swallows** both the null case and exceptions into a `MessageBox` internally — the caller gets no
   success/failure value back. As written, the host cannot implement the "only on success" guard; it
   would always run `ShowServiceEditor()` even when the file was missing/invalid, contradicting the
   design's own stated behavior.
   CONCRETE FIX — pick one and state it in the design:
   (a) Change `LoadServiceOrderAsync` to `private async Task<bool> LoadServiceOrderAsync(string path)`
   returning `loaded != null` (and `false` in the catch), then `if (await LoadServiceOrderAsync(path)) { _currentServicePath = path; ShowServiceEditor(); RefreshServiceOrderList(); }`. This is a one-method
   signature edit; note it alongside item 10a as a second edit to an existing helper (the existing
   `LoadService_Click` caller ignores the return, so it stays source-compatible).
   (b) Or have the host check success itself without touching the helper:
   `await LoadServiceOrderAsync(path); if (_serviceOrderManager.CurrentServiceOrder != null && SamePath(_serviceOrderManager.GetFilePathFor(...), path)) { … }` — but this is fragile against a
   prior-loaded order and is **not** recommended; prefer (a).

2. **NIT — empty-service header uses `ServiceOrder.ServiceType` (service-type string) where `OnItemChanged` uses the item's `Type` (enum).**
   Item 13 specifies that for an empty/none-current service, `ShowServiceEditor()` writes
   `CurrentItemSubtitle.Text = _serviceOrderManager.CurrentServiceOrder?.ServiceType ?? ""`.
   Verified: `ServiceOrder.ServiceType` is a `string` (default `"Sunday Morning"`), so this compiles
   and renders. But the reactive path `OnItemChanged` sets `CurrentItemSubtitle.Text = e.NewItem?.Type.ToString()`
   — the *item's* `ServiceItemType` enum (e.g. "Song"), a different concept. So the subtitle means
   "service type" for an empty service and "current item type" for a non-empty one. That is a cosmetic
   inconsistency, not a bug, but it is undocumented.
   CONCRETE FIX — add a one-line note to item 13 acknowledging the subtitle shows the *service type*
   for an empty service vs. the *item type* once items exist (intended), so it is not later flagged as a
   mismatch; or, if a consistent header is wanted, show the service name/date in both cases.

3. **NIT — item 8 says the new hosts "mirror [`ServicesPanel`'s] structure exactly," but `ServicesPanel` is a `Grid`, not a `Border`.**
   Verified in `OperatorWindow.xaml`: `<Grid x:Name="ServicesPanel">` (containing a `ScrollViewer` +
   `WrapPanel`), whereas item 8 proposes `<Border x:Name="HomePanel"><ContentPresenter/></Border>`.
   Functionally fine — all three live as overlapping siblings in the same Row-1 `Grid` cell and are
   toggled by `Visibility`, so a `Border` host is a valid sibling — but "mirroring its structure
   exactly" is slightly inaccurate wording.
   CONCRETE FIX — reword item 8 to "add two sibling hosts in the same Row-3/Column-1 content `Grid`
   cell as `ServicesPanel`, toggled by `Visibility` like the existing panels" and drop "mirroring its
   structure exactly" (the inner structure differs: `Border` + `ContentPresenter` vs. `Grid` +
   `ScrollViewer`).

## Verified Assumptions

- `OperatorWindow.SaveServiceOrderAsync` is the **parameterless** `private async Task SaveServiceOrderAsync()`
  → `_serviceOrderManager.SaveAsync()` (no path); no `(string)` overload exists. (Decision 9 / item 10a correct.)
- `ServiceOrderManager.SaveAsync(string? filePath = null)` honors an explicit path and derives
  `{Date}_{SanitizedName}.json` via `GetDefaultFilePath` when null. (Correct.)
- `GetDefaultFilePath(ServiceOrder)` and `SanitizeFileName(string)` are **both `private`** on
  `ServiceOrderManager`; the Presenter cannot reproduce the filename. (Decision 9 / item 5b accessor justified.)
- `ServiceOrderManager.LoadAllAsync` scans `Directory.GetFiles(_serviceOrdersBasePath, "*.json")`,
  skips bad files with a **bare `catch { }`** (no logging), and returns `OrderByDescending(o => o.Date)`.
  (Decision 5 and the error-handling parity note correct.)
- `ServiceOrderManager.LoadAsync` sets `CurrentServiceOrder`, sets `CurrentItemIndex` (0 if items else -1),
  and fires `ServiceOrderLoaded`; it does **not** fire `ItemChanged`. (Decision 7 rename-hijack rationale correct.)
- `PresenterIntegration.OnServiceOrderLoaded` calls `GoToItem(0)` **only when `order.Items.Count > 0`**,
  so a brand-new empty service never fires `ItemChanged`. (Decision 7 / finding-2-from-prior-round / the
  explicit `RefreshServiceOrderList()` after `CreateNew` is justified.)
- `OperatorWindow.OnItemChanged` sets `CurrentItemTitle.Text = e.NewItem?.Title` and
  `CurrentItemSubtitle.Text = e.NewItem?.Type.ToString()`, and calls `RefreshServiceOrderList()` only
  when the item count changed. (Item 13 header-ownership claim correct.)
- `NewService_Click` is gated on `CurrentServiceOrder == null` and **unconditionally chains**
  `AddServiceItemDialog` afterward. (Decision 7 "cannot reuse" rationale correct.)
- `OperatorWindow.Initialize(PresenterEngine, OutputWindow?, ThemeManager?, DisplayManager?, Action<PresenterSettings>?)`
  is a synchronous `public void` with no window `Loaded` handler. (Item 10 fire-and-forget rationale correct.)
- `CreateNew(string name, string date, string serviceType = "Sunday Morning")` — the design's
  `CreateNew(dlg.ServiceName, dlg.ServiceDate)` call is valid via the default third arg.
- `ServiceInfoDialog.Initialize(string name, string date, DateTime? startTime)` exists; `ShowDialog()==true`
  with `Confirmed`/`ServiceName`/`ServiceDate`/`StartTime`. (Rename/create flows correct.)
- `OwcDesignSystem.xaml` brush keys are all `*Brush`-suffixed: `OwcTextPrimaryBrush`,
  `OwcTextSecondaryBrush`, `OwcTextMutedBrush`, `OwcPanelBrush`, `OwcSurfaceBrush`,
  `OwcSurfaceHoverBrush`, `OwcBorderBrush`, `OwcAccentBrush`, `OwcBackgroundBrush`. No bare
  `OwcTextPrimary`/`OwcPanel`/`OwcSurfaceHover` keys exist. Keyed styles `OwcPanelBorder`,
  `OwcButtonBase`, `OwcAccentButton`, `OwcTextBox`, `OwcDarkComboBox` exist. (Decision 4 / item 15 correct.)
- `ServiceDateFormatter.FormatUs(string?, DateTime?)` and `TryParseStoredDate(string?, out DateTime)`
  exist with the stated behavior. (Decision 6 reuse correct.)
- `ServiceOrder`: `Name` (`name`), `Date` (`date`, `yyyy-MM-dd`), `ServiceType` (`serviceType`, **string**),
  `StartTime` (`startTime`, `DateTime?`). (Model mapping correct.)
- `OperatorWindow.xaml` sidebar width drift confirmed: Row-3 `ColumnDefinition x:Name="SidebarColumn" Width="200"`
  while the top-nav uses `Width="220"`. (Item 9 "do not hardcode a width" rationale correct.)
- `SwitchToTab` hides `ServicesPanel/SongsPanel/ScripturePanel/MediaPanel/ErrorPanel`, lazily builds the
  Songs/Scripture/Media panels, and `"Services"` currently shows `ServicesPanel`. (Decision 2 repoint target correct.)
- `ServicesPanel` contains the slide-grid empty state `EmptyStateText`. (Item 14 "Slides with no service" note correct.)

## Unverified / Wrong Assumptions

- **WRONG (basis of Finding 1):** the implied contract that `LoadServiceOrderAsync` yields a success
  value the Open handler can branch on. The method returns `Task` and swallows failures; "only on
  success" is not implementable without the signature change in Finding 1.
- **Unverified (acceptable, pinned by tests):** `RelativeTimeFormatter` bucket boundaries (59s → "just now",
  1–59 min, the 24h "yesterday" window, multi-day → "N days ago", clock-skew guard). These are new Core code,
  so there is nothing to verify against; the test names in item 17 are the authority the implementer codes to.
- **Unverified (acceptable):** `ServiceListingBuilder.Build`/`Filter` ordering + projection and
  `ListServiceFilesAsync` behavior — all new code to be covered by `ServiceListingBuilderTests` /
  `ServiceOrderManagerTests`-style temp-folder tests (items 17–18). No existing source to contradict.
- **Not inspected (out of scope):** `GlobalSearchService`'s exact use of `LoadAllAsync` — the design's
  caution ("don't change `LoadAllAsync`'s shape") is prudent regardless, so leaving `LoadAllAsync`
  untouched (Decision 5) is the safe call whether or not the dependency is exhaustively traced.

## Verdict

CHANGES_REQUESTED — 1 MEDIUM, 2 NIT. The MEDIUM (Finding 1) is a real integration gap: a documented
behavior cannot be implemented against the current `LoadServiceOrderAsync` signature. Resolve it (and,
ideally, note the two NITs) and the design is ready for implementation.
