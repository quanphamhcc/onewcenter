# Design Review — Edit Service Item (Slides) editor (Revision 4)

Reviewer: fresh design-review subagent (no prior-authoring context).
Scope: `design.md` (Revision 4) against `requirements.md`, assumptions verified against source in
the `.worktrees/slide-item-editor` worktree. No build/test was run (per instructions).

Verdict: **APPROVED** — 0 HIGH, 0 MEDIUM, 2 NIT.

## Summary

Revision 4 resolves the Revision-3 HIGH blocker exactly as the decided resolution required. The new
`WhenComplete` field and `WhenCompleteAction` enum are **gone** — I searched the whole design and
there is no remaining declaration of either; the "Model changes" section explicitly states "There
is therefore **no model change for Gap #1** — `ServiceItemSettings` is used as-is," the Files list
marks `ServiceItemSettings.cs` **Unchanged**, and the two remaining additive model fields are only
`ServiceItem.AudioAttachments` and `ServiceItem.StageTemplateId`. The "When complete" dropdown now
maps onto the existing `AutoAdvanceGoTo` + `GoToItemId` (`Do nothing`→`NextSlide`, `Next item`→
`NextItem`, `Specific item`→`SpecificItem` + `GoToItemId`, `Loop`→`Loop`), AC-13's round-trip target
was rewritten to assert `AutoAdvanceGoTo`/`GoToItemId` with no `whenComplete` JSON key, the "When
complete" dropdown greys out when "Go to next slide after" is Disabled, and the Timer-only
live-navigation coherence is stated in both the Gap #1 section and the Auto-advance mapping section.
The single `GoToItemId` collision from Revision 3 is eliminated (one writer now).

I re-verified every source claim the Revision-4 rework newly leans on — principally
`ItemSettingsMapping` and `AutoAdvanceResolver`, which the earlier rounds had not needed to pin down
at this level. Both exist and behave as the design describes, with two small wording mismatches that
do not change behavior (NIT 1). The 2 MEDIUM fixes (VideoEnd fold + the `_suppressDirty`
initial-selection invariant) and the 3 NIT fixes (sync/async constructor split, Remove-handler
contract, live-list-safe-because-modal) are all present and correctly stated. The design is
implementable without ambiguity.

---

## Findings

### 1. [NIT] "copies `ItemSettingsMapping.ToGoTo`/`GoToLabel` verbatim" overstates it — the editor's `Do nothing` label has no counterpart in the real mapping

**Where:** Gap #1 label table and its "(copies `ItemSettingsMapping.ToGoTo`/`GoToLabel` verbatim …)"
caption; Auto-advance mapping "When complete ⇄ AutoAdvanceGoTo" section; Summary of decisions.

**Problem (documentation accuracy, not a behavior bug).** I read the real source
(`WorshipPlayer.Presenter/Views/ServiceBuilder/ItemSettingsPanel.xaml.cs`). `ItemSettingsMapping` is:

```csharp
ToGoTo(label):   "Specific Item" => SpecificItem, "Loop" => Loop, _ => NextItem
GoToLabel(goTo): SpecificItem => "Specific Item", Loop => "Loop", _ => "Next Item"   // NextSlide AND NextItem both -> "Next Item"
```

So the real mapping has **no "Do nothing" label at all** — `NextSlide` (the model default) is
folded into **"Next Item"**, not surfaced separately. The editor's table instead maps
`NextSlide ⇄ "Do nothing"` and `NextItem ⇄ "Next item"` as two distinct labels. That is a
deliberate, reasonable design choice (it matches the reference screenshot's "Do nothing" option and
gives `NextSlide` a visible home), and the design even acknowledges a casing difference — but it
repeatedly calls the mapping a "verbatim copy," which it is not: it adds a label and splits the
`NextSlide`/`NextItem` fold that the original collapses. An implementer told "copy verbatim" could
either (a) copy the original and lose the "Do nothing" option, or (b) follow the table and diverge —
the two instructions conflict.

Separately, the design recommends extracting the rule as a **pure `SlideAutoAdvanceMap` helper in
Core**. `ItemSettingsMapping` is `internal` and lives in the **Presenter** assembly, so a Core
helper cannot call it — the editor must **re-implement** the vocabulary, not reuse the symbol. The
design already implies this ("the editor's own small `ToGoTo`/`GoToLabel` equivalents"), so the
plan is sound; only the "verbatim copy of `ItemSettingsMapping`" phrasing is misleading.

**Concrete fix.** Change the caption from "copies `ItemSettingsMapping.ToGoTo`/`GoToLabel` verbatim"
to something like: "mirrors `ItemSettingsMapping`'s enum targets but uses this editor's sentence-case
labels and adds an explicit **Do nothing** option mapped to the model default `NextSlide` (the
original folds `NextSlide` into 'Next Item'); the mapping is **re-implemented** in the Core
`SlideAutoAdvanceMap` helper because `ItemSettingsMapping` is `internal` to the Presenter assembly."
No code logic changes — only the claim of verbatim reuse.

---

### 2. [NIT] "Do nothing" + "Go to next slide after = Time Delay" is a reachable combination whose live behavior contradicts the label, and the design does not say what happens

**Where:** Gap #1 label table ("Do nothing" row: "effect governed by whether a Timer trigger is
set"); Auto-advance mapping "What is written on save" (second bullet: a real after-condition writes
`AutoAdvanceGoTo`/`GoToItemId` "from the 'When complete' controls").

**Problem (minor coherence gap, not blocking).** The design correctly notes that `AutoAdvanceGoTo`
only drives live navigation when the after-condition is `Timer`, and greys "When complete" when "Go
to next slide after" is "Disabled". But "Go to next slide after" also offers **Time Delay** (→
`Timer`) and **End of Media** (→ `AudioEnd`). With "Go to next slide after = Time Delay" the "When
complete" dropdown is **enabled**, so a user can leave it at **"Do nothing"**, which the save rule
maps to `AutoAdvanceGoTo = NextSlide`. I verified `AutoAdvanceResolver.Resolve`: with
`AutoAdvanceAfterCondition == Timer` and `AutoAdvanceGoTo == NextSlide`, it **advances to the next
item** (`NextSlide` and `NextItem` share the same branch). So after the timer elapses the engine
advances — which contradicts a label literally reading "Do nothing." This is an edge of the
label vocabulary, not a model conflict, and the stored data is coherent (it is exactly what the
existing panel would store for its "Next Item" default), so it does not block. But the design never
states the expected outcome for this specific pairing, so an implementer has no spec for whether
"Do nothing" should be disabled/relabelled under a Timer trigger or accepted as "advance to next."

**Concrete fix.** Add one sentence to the "Do nothing" row or the enable-rule section, e.g.: "When
'Go to next slide after' is a real trigger (Time Delay / End of Media), 'Do nothing' stores
`NextSlide`, which the resolver treats as 'advance to next item' under a Timer trigger; this is the
intended behavior — 'Do nothing' means 'no special end-of-item destination', i.e. fall through to
the natural next item — and matches the existing panel's `NextSlide`/`NextItem` fold. (If a true
'stay put' is ever wanted under a timer, that is `Loop`.)" This removes the only remaining ambiguity
in the two-dropdown interaction.

---

## Verified Assumptions (newly checked this round, or re-checked because Revision 4 touched them)

1. **`ItemSettingsMapping` exists and its enum targets match the design's table** —
   `ToGoTo`: `"Specific Item"→SpecificItem`, `"Loop"→Loop`, else `NextItem`; `GoToLabel`:
   `SpecificItem→"Specific Item"`, `Loop→"Loop"`, else `"Next Item"`; `AfterLabel`:
   `Timer→"Time Delay"`, `AudioEnd→"End of Media"`, **`VideoEnd→"End of Media"`**, else `"Manual"`;
   `ToAfter` delegates to `BulkEditService.ParseAfterCondition`. The `VideoEnd→"End of Media"` fold
   the design cites for MEDIUM-2 is present **verbatim** in source. Two caveats surfaced as NIT 1:
   the class is `internal` (Presenter assembly, so a Core helper re-implements rather than calls it),
   and it has **no "Do nothing" label** (`NextSlide` folds into "Next Item"). (`ItemSettingsPanel.xaml.cs`.)
2. **`AutoAdvanceResolver.Resolve` is the engine's end-of-item navigation and reads
   `AutoAdvanceGoTo` + `GoToItemId`** — fires only when `AutoAdvanceAfterCondition == Timer`;
   `NextSlide`/`NextItem`→next item (shared branch), `SpecificItem`→`GoToItemId` (fallback to next
   item if missing/invalid), `Loop`→re-show current; `AudioEnd`/`VideoEnd` are deferred no-ops. This
   confirms the Revision-3 HIGH diagnosis and confirms Revision 4's reuse decision drives live
   navigation with a single source of truth. The Timer-only coherence and the `NextSlide`≡`NextItem`
   fold the design relies on are both exactly as source. (`AutoAdvanceResolver.cs`.)
3. **`ServiceItemSettings` fields and defaults** — `AutoAdvanceGoTo` (default `NextSlide`),
   `AutoAdvanceAfterCondition` (default `Manual`), `AutoAdvanceDelayMs` (5000), `TransitionFadeTimeMs`
   (500), `GoToItemId` (nullable), `OrganizationId`. **No new `WhenComplete` field** — the file is
   unchanged, exactly as the design's Files list claims. (`ServiceItemSettings.cs`.)
4. **`AutoAdvanceAfterCondition`** = `{ Manual, Timer, AudioEnd, VideoEnd }` — four members, matching
   the design's load-side four-member table. **`AutoAdvanceGoTo`** = `{ NextSlide, NextItem,
   SpecificItem, Loop }`. Both have `[JsonConverter(typeof(JsonStringEnumConverter))]` at the enum,
   so they serialize as strings even without the manager options (relevant to the in-memory clone).
   (`ServiceItemSettings.cs`.)
5. **`ServiceItem` has neither `AudioAttachments` nor `StageTemplateId` yet** — confirming both are
   genuinely additive. The legacy `AudioAttachmentPath` is a non-nullable `string` defaulting to
   `""` (so the "keep in sync with `[0]`" rule is needed, as the design states), `TransitionTimeMs`
   is `int` default 500, `BackgroundType` is `BackgroundType?`, `MediaType` is `MediaItemType`
   (default `Image`), and `SlidePresentationId`/`ThemeId`/`AutoAdvanceSettings` are nullable. The
   Song/Scripture/`Id`/`Order`/`Subtitle`/`Notes`/`Slides`/`ForegroundType` fields the copy-back
   table lists as "preserved" all exist. (`ServiceOrder.cs`.)
6. **`ItemSettingsPanel.LoadSettings` excludes the current item from the target list by
   `!ReferenceEquals(candidate, item) && candidate.Id != item.Id` and stores the target as an id** —
   this is the exact pattern the design reuses for the specific-item target dropdown, confirming the
   "current item excluded" + "id-based, position-independent" claims. (`ItemSettingsPanel.xaml.cs`.)

## Previously-verified assumptions carried forward (not re-checked; Revision 4 did not touch them)

The 18 assumptions confirmed in prior rounds (model fields, `MediaLibraryType` = `{Image,Video,Audio}`
with no `Document`, `BackgroundType` enum, `ServiceItemDisplayName` two-arg purity,
`BulkEditService.ParseAfterCondition`, `ITemplateService`/`SlideTemplate` shape,
`ISlidePresentationService`, `_jsonOptions` + SQLite mirror, `RemoveItem` range guard, the two entry
points, injected services, host refresh helpers, hosting/styling rule, `Result`/`DialogResult`/
`_suppressTextSync` patterns) stand. Revision 4 changed only the Gap #1 model/mapping, the VideoEnd
load fold, the dirty-tracking invariant, and the three NIT clarifications — none of which invalidate
those earlier confirmations.

## Unverified / Wrong Assumptions

1. **UNVERIFIED (not blocking, deferred to implementer by the design itself): the three real
   service-order JSON files + demo files load without error after the two additive fields.** The
   additive-nullable + `WhenWritingNull` approach is sound in principle (verified options), and
   dropping the `WhenComplete` field actually *reduces* round-trip surface vs Revision 3, but
   "loads without error" is an AC-8 implementer check, not proven in this pass.
2. **UNVERIFIED (not blocking): `TemplateService.GetBuiltInTemplates()` seeds exactly the three
   named built-ins.** The design cites them "for reference only" and makes no no-throw/exact-count
   guarantee, so nothing depends on it; not re-verified.
3. **UNVERIFIED (not blocking): `ServiceOrders.DataJson` column name / exact upsert shape.** Prior
   rounds confirmed the mirror is serialized with the same `_jsonOptions`; AC-13 only needs the
   additive fields present in that serialized `DataJson`, which follows from the shared options.
4. **No WRONG assumptions remain.** The Revision-3 "`WhenComplete` is independent of
   `AutoAdvanceGoTo`" error is fully retired — the design now states the opposite (they are the same
   vocabulary) and acts on it. The after-condition four-member reality is now correctly handled.
