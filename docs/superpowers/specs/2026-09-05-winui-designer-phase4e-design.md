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

- `DependencyProperty.GetFullName()` — the reference's `Metadata.cs` has
  its own `GetFullName(this DependencyProperty p)` extension
  (`OwnerType.FullName + "." + Name`). This port already has a replacement
  at `src/WinUIDesigner.DesignModel/DesignItemProperty.cs:98-107`, because
  WinUI's `DependencyProperty` has no `.OwnerType`/`.Name` to compose from
  (a real, permanent difference, already documented there) — it returns
  `p.ToString()` instead. Metadata's string-keyed dictionaries
  (`HideProperty`/`AddPopularProperty`, keyed via
  `DesignItemProperty.DependencyFullName` which itself calls
  `DependencyProperty.GetFullName()`) reuse this existing extension
  unchanged; the reference's own `GetFullName` is not re-ported.
- `DesignItemProperty.DependencyFullName` (`DesignItemProperty.cs:64-71`)
  already exists and is exactly the string the reference's
  `Metadata.IsBrowsable(DesignItemProperty)` /
  `Metadata.IsPopularProperty(DesignItemProperty)` /
  `Metadata.GetValueRange(DesignItemProperty)` look up by — no new property
  needed on `DesignItemProperty` for this phase.
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
   `GetFullName` is not re-ported (see above — already exists).
3. **Merge `PlacementMetadata` into `Metadata`,** using the reference's own
   method names (`DisablePlacement`/`IsPlacementDisabled`, not
   `PlacementMetadata`'s `RegisterPlacementDisabled`) so a future reader
   can grep the reference and this port with the same name. Update
   `DefaultPlacementBehavior.cs:41` to call `Metadata.IsPlacementDisabled`;
   delete `PlacementMetadata.cs`; its behavior is covered by the new
   `MetadataTests.cs` instead of a dedicated file.
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
   calling `designItem.Properties[key].SetValue(value)` for each entry,
   exactly matching the reference. `CreateComponentTool.CreateItem`
   (`Services/CreateComponentTool.cs:201-211`) calls it between
   `RegisterComponentForDesigner` and `ApplyDefaultInitializers`, matching
   the reference's ordering; the stale comment naming this exact gap is
   removed.
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
  `HideProperty` (both overloads) / `IsBrowsable`; `AddPopularProperty`
  (both overloads) / `IsPopularProperty`; `AddPopularControl` /
  `GetPopularControls` / `IsPopularControl`; `AddValueRange` /
  `GetValueRange` (including "no range registered returns null");
  `DisablePlacement` / `IsPlacementDisabled`; `AddDefaultSize` /
  `GetDefaultSize` (exact-type hit, base-type-walk hit,
  `checkBasetype: false` miss, no-registration-returns-null);
  `AddDefaultPropertyValue` / `GetDefaultPropertyValues` (multiple values
  for one type, unregistered type returns null).
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
  `CLAUDE.md` testing rule. `Metadata` itself touches no live WinUI object
  (pure static class over `Type`/`DependencyProperty`/`Size`), so
  `MetadataTests.cs` also runs under `dotnet test` for IDE discovery, but
  the `.exe` run remains the real gate for the Designer-layer tests
  (`DefaultPlacementBehaviorTests`/`ModelToolsTests`/
  `CreateComponentToolTests`), which do touch live `DesignItem`/view
  objects.
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

## Out of scope for Phase 4e

- PropertyGrid itself, including its ~12 property editors
  (`BoolEditor`/`ColorEditor`/`CollectionEditor`/etc.) — Phase 4f.
- OutlineView itself — Phase 4g.
- The XamlDom `Point`/`PointCollection` parse-and-save gap — an unrelated,
  already-flagged prerequisite for Phase 5's Save feature (see the
  `project_xamldom_serialization_gap` memory), not touched here.
- Any Toolbox or shell-app UI that would be `Metadata`'s real-world caller
  for popular-controls/seed data — Phase 5.
