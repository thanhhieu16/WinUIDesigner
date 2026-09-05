# Phase 4e: Metadata — WinUIDesigner.DesignModel

## Revision history

Brainstormed 2026-09-05, as the first of a three-way decomposition surfaced
during Phase 5 scoping. Phase 5 was originally "the shell app" per the
five-phase decomposition, but the reference's actual shell app
(`XamlDesigner/MainWindow.xaml`) depends directly on `PropertyGridView` and
`OutlineView`/`Outline.xaml.cs` — both of which live in the reference's
`WpfDesign.Designer` project (this port's `WinUIDesigner.Designer` layer,
Phase 4 territory) and were never ported in 4a-4d. Phase 2's own spec
(`docs/superpowers/specs/2026-09-01-winui-designmodel-phase2-design.md:346`)
had already named "property grid, metadata, and outline view" as originally
slated for "Phase 4," but 4a-4d shipped placement/drag-move, context menus,
and drawing tools instead.

Decomposed into three sequential sub-phases, same precedent as 4a-4d (each
gets its own spec + plan, brainstormed one at a time): **4e (this spec) —
Metadata**, the shared dependency both later pieces need; **4f —
PropertyGrid**; **4g — OutlineView**. The unrelated XamlDom
Point/PointCollection serialization gap (silent parse failure on read,
silent data loss on write — see the `project_xamldom_serialization_gap`
memory) is a separate, already-flagged prerequisite for Phase 5's Save
feature specifically; it is not part of this three-way decomposition and is
not addressed by this spec.

## Context

The reference's `Metadata.cs` (`WpfDesign/Project/Metadata.cs`, 458 lines)
is a static-registry helper: per-`Type`/per-`DependencyProperty` lookup
tables for standard/named values (used by combo-box-style property
editors), browsable/hidden properties, "popular" properties and controls
(property-grid ordering, toolbox contents), value ranges (numeric editors),
placement-disabled types, default sizes for newly-created controls, and
default property values for newly-created controls. It has never been
ported to this codebase as a class — but three call sites already exist in
this port, each with its own comment explicitly naming the gap:

1. `src/WinUIDesigner.DesignModel/PlacementMetadata.cs` — an early,
   partial extraction of just `DisablePlacement`/`IsPlacementDisabled`
   (renamed `RegisterPlacementDisabled`), added because
   `DefaultPlacementBehavior` (Phase 4a/4b) needed it and the full class
   didn't exist. Its own doc comment names the source ("the reference's
   Metadata.cs (458 lines)...ported alone").
2. `src/WinUIDesigner.Designer/ModelTools.cs:36-60` — `GetDefaultSize`
   is a trimmed port of the reference's `ModelTools.GetDefaultSize`
   (`WpfDesign.Designer/Project/ModelTools.cs:161-190`) that keeps only the
   Measure-based sizing, explicitly dropping "`Metadata.GetDefaultSize`'s
   per-type registry (`Metadata.cs` — the class, not just this one method —
   was never ported to this codebase at all... 4e territory)."
3. `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs:201-211`
   (`CreateItem`) — its own comment says: "Reference also calls
   `context.Services.Component.SetDefaultPropertyValues(item)` here -
   `IComponentService` in this port has no such method (confirmed via grep
   across `src/`), so it's dropped."

This phase closes all three gaps by porting the actually-used subset of
`Metadata.cs` into `WinUIDesigner.DesignModel` (matching the reference's
own layering — `Metadata.cs` lives in `WpfDesign/Project/`, the reference's
core layer, not `WpfDesign.Designer`), then wiring it into the three
existing call sites.

## Verified against the current codebase

- **`DependencyProperty.GetFullName()`/`DesignItemProperty.DependencyFullName`
  are confirmed broken as a differentiator — do not use them.** Empirically
  probed during plan-writing (throwaway `WinUITestHost.Run` test, built and
  run against the real `.exe`, then deleted — no residue): WinUI's
  `DependencyProperty` does not override `ToString()`, so
  `GetFullName()` (`DesignItemProperty.cs:98-107`, `return p.ToString();`)
  returns the literal string `"Microsoft.UI.Xaml.DependencyProperty"` for
  *every* `DependencyProperty` instance — `Button.ContentProperty`,
  `TextBlock.TextProperty`, and `FrameworkElement.WidthProperty` all
  stringify identically. `GetFullName`/`DependencyFullName` have zero
  callers anywhere in `src/` today (grepped), so this has been a latent,
  untriggered bug since Phase 2 — Phase 4e would have been its first real
  caller, and using it as originally planned would make
  `HideProperty`/`AddPopularProperty`/`AddValueRange`'s
  `DependencyProperty`-taking overloads collide across every property in
  the system (hiding one property would hide all of them). Metadata does
  not use `GetFullName`/`DependencyFullName` at all — see Decision 3a
  below. Fixing `GetFullName` itself (it would need reflection to recover
  which static field on which owning CLR type a given `DependencyProperty`
  instance was registered through) is out of scope for this phase; it
  stays unused and unfixed, flagged again under Known Gaps.
- **`DesignItemProperty`/`DesignItemPropertyCollection` are string-keyed
  only** — no `DependencyProperty`-indexed overload — an already-established
  project-wide constraint (`docs/superpowers/specs/2026-09-01-winui-designer-phase4a-design.md:163-169`,
  carried forward through 4b/4c). `Metadata.AddDefaultPropertyValue`/
  `GetDefaultPropertyValues`, whose reference signature keys by
  `DependencyProperty`, cannot be ported with that signature unchanged:
  the consumer (`SetDefaultPropertyValues`) has no way to turn a bare
  `DependencyProperty` back into a `Properties[...]` lookup key. See
  Decision 5a below — this phase's spec originally missed this constraint
  when drafting Decision 5; corrected here before any task is written.
- `DesignItemProperty.FullName` (`DesignItemProperty.cs:56-58`,
  `DeclaringType.FullName + "." + Name`) and `DesignItemProperty.DependencyProperty`
  (the abstract property every concrete `DesignItemProperty` already
  implements) are both already correct and already used elsewhere — these,
  not `DependencyFullName`, are what Metadata's lookup methods use.
- `PlacementMetadata.cs` has exactly one caller:
  `DefaultPlacementBehavior.cs:41`
  (`PlacementMetadata.IsPlacementDisabled(ExtendedItem.ComponentType)`).
  No test file references `PlacementMetadata` by name (grepped across
  `tests/`) — it is currently exercised only indirectly through
  `DefaultPlacementBehaviorTests.cs`.
- `ModelTools.GetDefaultSize`/`CreateComponentTool.CreateItem` are
  likewise only exercised indirectly today; grepping `tests/` finds no
  test naming `GetDefaultSize` directly.
- `IComponentService` (`src/WinUIDesigner.DesignModel/Services.cs:26-37`)
  declares `GetDesignItem`×2, `RegisterComponentForDesigner`×2, and four
  events — no `SetDefaultPropertyValues`. `XamlComponentService.cs`
  implements exactly that interface surface, nothing more.
- Reference's real call chain for point 3, confirmed by reading
  `WpfDesign.Designer/Project/Xaml/XamlComponentService.cs:86-94` and
  `WpfDesign.Designer/Project/Services/CreateComponentTool.cs:251`: a
  public `SetDefaultPropertyValues(DesignItem designItem)` method on the
  component service, reading `Metadata.GetDefaultPropertyValues`, called by
  `CreateComponentTool` right after the new instance is registered.
- Test-file convention in both `.Tests` projects is flat, one file per
  concern, no subfolders (e.g. `ModelToolsTests.cs`,
  `ModelToolsArrangeItemsTests.cs`, `ServiceContainerTests.cs`). This
  phase's new tests follow the same convention.
- Reference's seed data (`WpfDesign.Designer/Project/BasicMetadata.cs`,
  the file that actually calls `Metadata.AddStandardValues(typeof(Brush),
  typeof(Brushes))` etc. at startup) references several types this port's
  Known-WinUI-gaps list already confirms absent from WinUI 3: the static
  `Brushes` class, `Cursors`, `PixelFormats`, `TextDecorations`, plus
  WPF-only command classes (`ApplicationCommands`/`EditingCommands`/etc.).
  Real seeding cannot be ported verbatim and has no consumer yet in this
  codebase (no Toolbox, no PropertyGrid) — see Decision 6.

## Decisions already made

1. **Home:** `WinUIDesigner.DesignModel` namespace, new file
   `src/WinUIDesigner.DesignModel/Metadata.cs` — matches the reference's
   own layering and `PlacementMetadata`'s existing location.
2. **Port only the "live" reference API.** The reference's own
   `Metadata.cs` has a `Category`/`IsAdvanced` cluster
   (`AddCategory`/`GetCategory`/`AddAdvancedProperty`/`IsAdvanced`)
   entirely commented out — dead in the reference itself. Not ported.
   `GetFullName` is not re-ported (already exists) and not called by
   anything in this phase (see Decision 3a — it's confirmed broken).
3. **Merge `PlacementMetadata` into `Metadata`,** using the reference's own
   method names (`DisablePlacement`/`IsPlacementDisabled`, not
   `PlacementMetadata`'s `RegisterPlacementDisabled`) so a future reader
   can grep the reference and this port with the same name. Update
   `DefaultPlacementBehavior.cs:41` to call `Metadata.IsPlacementDisabled`;
   delete `PlacementMetadata.cs`; its behavior is covered by the new
   `MetadataTests.cs` instead of a dedicated file.
3a. **`HideProperty`/`AddPopularProperty`/`AddValueRange` key their
   `DependencyProperty`-taking overloads by the `DependencyProperty` object
   itself, not by a derived string.** Corrected from the reference's
   design (which unifies its `DependencyProperty` and `Type,string`
   overloads into one `GetFullName()`-keyed string space) because
   `GetFullName()` is confirmed broken here (see above) and cannot provide
   that unification. Concretely: `HideProperty(DependencyProperty)` adds to
   a `HashSet<DependencyProperty>`; `HideProperty(Type, string)` adds to a
   *separate* `HashSet<string>` keyed by `type.FullName + "." + member`
   (unchanged from the reference — this half never needed `GetFullName`).
   `IsBrowsable(DesignItemProperty p)` returns `false` if *either* store
   matches: `p.DependencyProperty != null && dependencyPropertySet.Contains(p.DependencyProperty)`,
   or `stringSet.Contains(p.FullName)` (using the already-correct
   `DesignItemProperty.FullName`, not `DependencyFullName`). Same pattern
   for `AddPopularProperty`/`IsPopularProperty` (two sets, one check each)
   and `AddValueRange`/`GetValueRange` (a `Dictionary<DependencyProperty, NumberRange>`
   plus a `Dictionary<string, NumberRange>`, `GetValueRange` checks the
   `DependencyProperty` dictionary first, then the string one). A property
   registered via one overload is still found via a `DesignItemProperty`
   lookup regardless of which overload registered it, since the lookup
   always checks both stores — the only thing lost versus the reference is
   cross-overload aliasing (registering by `Type,string` and separately
   querying by an unrelated `DependencyProperty` object that happens to
   share a name — not a real usage pattern in this codebase's only caller,
   the not-yet-built PropertyGrid).
4. **`ModelTools.GetDefaultSize` gains the registry composition,** matching
   the reference's exact `ModelTools.cs:161-190` algorithm: (a) an
   exact-type registry hit (`checkBasetype: false`) short-circuits
   immediately; (b) otherwise, Measure the view for `DesiredSize`; (c) if a
   base-type-walking registry hit exists (`checkBasetype: true`) and the
   measured width/height is not greater than 5, the registry value
   overrides that dimension; (d) the existing NaN → explicit-Width/Height
   fallback still applies last. Existing callers/tests that rely on pure
   Measure behavior (no registry entries registered) are unaffected, since
   an empty registry returns `null` at every lookup.
5. **`IComponentService`/`XamlComponentService` gain
   `SetDefaultPropertyValues(DesignItem)`,** reading
   `Metadata.GetDefaultPropertyValues(designItem.ComponentType)` and
   calling `designItem.Properties[key].SetValue(value)` for each entry.
   `CreateComponentTool.CreateItem`
   (`Services/CreateComponentTool.cs:201-211`) calls it between
   `RegisterComponentForDesigner` and `ApplyDefaultInitializers`, matching
   the reference's ordering; the stale comment naming this exact gap is
   removed.
5a. **`AddDefaultPropertyValue`/`GetDefaultPropertyValues` key by property
   *name string*, not by `DependencyProperty`** — a deviation from the
   reference's `Dictionary<DependencyProperty, object>` signature, forced
   by the same "string-keyed only" constraint as Decision 3a: unlike
   `AddValueRange`/`AddPopularProperty` (which only ever need to check set
   *membership* against a `DesignItemProperty` the caller already has),
   `SetDefaultPropertyValues` must turn its result into an actual
   `Properties[...]` lookup, and nothing in this port can turn a bare
   `DependencyProperty` object back into that lookup's string key (no
   `.Name`, and `GetFullName()`/`ToString()` is confirmed broken — see
   above). Signature:
   `AddDefaultPropertyValue(Type t, string propertyName, object value)` /
   `GetDefaultPropertyValues(Type t)` returning `Dictionary<string, object>`.
   `SetDefaultPropertyValues` becomes a direct, no-bridging loop:
   `foreach (var kvp in values) designItem.Properties[kvp.Key].SetValue(kvp.Value);`.
   This mirrors the exact same deviation `ModelTools.ApplyTransform` already
   made for its own `DependencyProperty transformProperty` parameter
   (`ModelTools.cs:78-87`, citing this same "string-keyed only" finding).
6. **No `BasicMetadata`-style real seed data in this phase.** Most of the
   reference's seed calls reference WinUI-absent types (see "Verified
   against the current codebase" above), and nothing in this codebase yet
   consumes seeded data (no Toolbox, no PropertyGrid). Tests seed the
   registry directly per-test via `Metadata.AddXxx` calls. Real,
   WinUI-appropriate seed data is deferred to whichever later phase first
   needs it (5's shell app, or 4f if PropertyGrid needs real demo data
   sooner) — a decision for that phase's own brainstorming, not this one.
7. **`IsBrowsable`/`IsPopularProperty`/`GetStandardValues`/
   `GetNamedStandardValues`/`AddPopularControl`/`GetPopularControls`/
   `IsPopularControl` ship with no production caller yet in this phase.**
   This matches the project's established pattern (Phase 4d's drawing-tool
   `Cancel()` seams: build the mechanism, test it directly, let the
   feature that needs it call in later) — Phase 4f (PropertyGrid) and
   Phase 5 (Toolbox) are their first real callers.

## Architecture

One new file, four modified:

- **Create** `src/WinUIDesigner.DesignModel/Metadata.cs` — the `Metadata`
  static class plus the `NumberRange` class, both ported from the
  reference (mechanical, no WinUI-API surprises: `Type`, `DependencyProperty`,
  `Windows.Foundation.Size`, and plain `Dictionary`/`HashSet` are all
  already used this way elsewhere in the codebase).
- **Delete** `src/WinUIDesigner.DesignModel/PlacementMetadata.cs`.
- **Modify** `src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs`
  (1-line call-site swap, line 41).
- **Modify** `src/WinUIDesigner.Designer/ModelTools.cs` (`GetDefaultSize`
  body gains the registry composition; doc comment updated to drop the
  "never ported" note).
- **Modify** `src/WinUIDesigner.DesignModel/Services.cs`
  (`IComponentService` interface: +1 method) and
  `src/WinUIDesigner.DesignModel/Xaml/XamlComponentService.cs`
  (+1 method implementation).
- **Modify** `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs`
  (`CreateItem`: +1 call, stale comment removed).

## Data flow

Two runtime chains gain a registry consultation:

1. **Extension attachment** (unchanged trigger, new backing store):
   `ExtensionManager` attaches `DefaultPlacementBehavior` to a newly
   extended `DesignItem` → `OnInitialized` checks
   `Metadata.IsPlacementDisabled(ExtendedItem.ComponentType)` (previously
   `PlacementMetadata`) before registering the `IPlacementBehavior`.
2. **Component creation** (`CreateComponentTool.CreateItem`, both the
   click-to-place and toolbox-drop paths already built in 4d):
   `ExtensionManager.CreateInstanceWithCustomInstanceFactory` →
   `Component.RegisterComponentForDesigner` →
   **`Component.SetDefaultPropertyValues` (new)**, consulting
   `Metadata.GetDefaultPropertyValues` →
   `ExtensionManager.ApplyDefaultInitializers`. Separately,
   `AddItemsWithDefaultSize` calls `ModelTools.GetDefaultSize`, which now
   consults `Metadata.GetDefaultSize`/`Metadata.AddDefaultSize` before
   falling back to Measure.

## Testing / Definition of Done

- **New `tests/WinUIDesigner.DesignModel.Tests/MetadataTests.cs`** — one
  test per Add/Get pair: `AddStandardValues` (all 3 overloads) /
  `GetStandardValues(Type)` / `GetStandardValues(DependencyProperty)` /
  `GetNamedStandardValues`, including the enum-type special case
  (`GetStandardValues(typeof(SomeEnum))` returns `Enum.GetValues` even with
  nothing registered, and the `Nullable<SomeEnum>` case too);
  `HideProperty` (both overloads) / `IsBrowsable` — one case per overload
  (a `DependencyProperty` registration found via a `DesignItemProperty`
  whose `.DependencyProperty` matches; a `Type,string` registration found
  via `.FullName`) plus a not-hidden case; same two-overload,
  two-lookup-path shape for `AddPopularProperty`/`IsPopularProperty` and
  `AddValueRange`/`GetValueRange` (per Decision 3a); `AddPopularControl` /
  `GetPopularControls` / `IsPopularControl`;
  `DisablePlacement` / `IsPlacementDisabled`; `AddDefaultSize` /
  `GetDefaultSize` (exact-type hit, base-type-walk hit,
  `checkBasetype: false` miss, no-registration-returns-null);
  `AddDefaultPropertyValue` / `GetDefaultPropertyValues` — string-keyed
  per Decision 5a (multiple values for one type, unregistered type returns
  null).
- **`DefaultPlacementBehaviorTests.cs`** — existing placement-disabled
  coverage still passes after the call-site swap; add one case that
  registers via `Metadata.DisablePlacement` directly (not the deleted
  `PlacementMetadata`).
- **`ModelToolsTests.cs`** — new cases: an exact-type registry entry
  short-circuits Measure entirely; a base-type registry entry overrides a
  small (`≤5`) measured dimension but not both axes if only one qualifies;
  a registry entry is ignored when the measured dimension is `>5`
  (regression: existing controls with real content keep their measured
  size); the existing explicit-Width/Height fallback still wins when
  nothing is registered (regression).
- **`CreateComponentToolTests.cs`** — new case: creating an item of a type
  with registered default property values (via
  `Metadata.AddDefaultPropertyValue`) ends up with those values set on the
  resulting `DesignItem`; a type with none registered is unaffected
  (regression — every existing `CreateComponentToolTests` case must still
  pass unchanged).
- All of the above run via each project's built `.exe` per this repo's
  `CLAUDE.md` testing rule. `Metadata`'s own logic is a pure static class
  over `Type`/`DependencyProperty`/`Size`, but real test cases for its
  `DependencyProperty`-taking overloads need a real static
  `DependencyProperty` field (e.g. `Button.ContentProperty`), and the
  `IsBrowsable`/`IsPopularProperty`/`GetValueRange` cases need a real
  `DesignItemProperty` (from a loaded `XamlDesignContext`) to call them
  with — both require `WinUITestHost.Run(...)`, same as every other test
  in this codebase touching a live WinUI type. `MetadataTests.cs` is not
  an exception to the `.exe`-run rule.
- Full existing suite (`WinUIDesigner.DesignModel.Tests`,
  `WinUIDesigner.Designer.Tests`) stays green — this phase touches shared,
  already-exercised call sites (`DefaultPlacementBehavior`, `ModelTools`,
  `CreateComponentTool`), so a regression here would show up as an
  existing test failing, not just a new one.

## Known gaps — flagged, not guessed

- **No `BasicMetadata`-equivalent real seed data.** Decision 6. Whichever
  phase first needs real per-control/per-property registrations (Toolbox
  in Phase 5, or PropertyGrid demo data in 4f) will need to re-derive the
  reference's ~300-line seed list against WinUI's actual control surface —
  several referenced WPF types (`Brushes`, `Cursors`, `PixelFormats`,
  `TextDecorations`, the WPF command classes) have no WinUI equivalent and
  cannot be ported verbatim.
- **`IsBrowsable`/`IsPopularProperty`/`GetStandardValues`/
  `GetNamedStandardValues`/popular-control cluster have no production
  caller yet.** Decision 7. Verified correct by direct unit test only,
  same as several Phase 4d seams before their real callers arrived.
- **`DependencyProperty.GetFullName()`/`DesignItemProperty.DependencyFullName`
  remain unused and unfixed.** Confirmed broken (returns the same
  constant string for every `DependencyProperty` — see "Verified against
  the current codebase"). Metadata routes around both entirely (Decisions
  3a/5a) rather than fixing them, since a real fix needs reflection to
  recover a `DependencyProperty` instance's owning CLR type and the static
  field name it was registered through — a bigger, separate task with no
  caller needing it yet. Whoever eventually wants `GetFullName` to work
  should re-derive it via reflection at that point, not assume the current
  `p.ToString()` implementation is a placeholder that merely needs
  polish — it is silently, completely wrong today.

## Out of scope for Phase 4e

- PropertyGrid itself, including its ~12 property editors
  (`BoolEditor`/`ColorEditor`/`CollectionEditor`/etc.) — Phase 4f.
- OutlineView itself — Phase 4g.
- The XamlDom `Point`/`PointCollection` parse-and-save gap — an unrelated,
  already-flagged prerequisite for Phase 5's Save feature (see the
  `project_xamldom_serialization_gap` memory), not touched here.
- Any Toolbox or shell-app UI that would be `Metadata`'s real-world caller
  for popular-controls/seed data — Phase 5.
