# Phase 2: WinUIDesigner.DesignModel — design-item object model

## Context

Continues the 5-phase plan from the Phase 1 spec
(`docs/superpowers/specs/2026-08-31-winui-xamldom-phase1-design.md`).
**Phase 1** (`WinUIDesigner.XamlDom` — parse/serialize WinUI XAML into an
object model with live instances, no UI) is done and merged to `master`.

The original Phase 1 spec described Phase 2 as "core engine retarget
(`DesignItem`/`Metadata`/extension framework) + build an overlay layer to
replace WPF's `Adorner`". Brainstorming for this spec narrowed that: Phase 2
covers **only** the design-item object model — no `Adorner` replacement, no
rendering of any kind. That work moves entirely into Phase 3 alongside
`DesignSurface`. Phases 3–5 keep their original shape otherwise.

Reference source for porting is split across two reference projects, not
one — a deliberate finding from this spec's brainstorming, since the
reference repo doesn't cleanly separate "abstract model" from "concrete
implementation":

- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\` — abstract base
  classes (`DesignContext`, `DesignItem`, `DesignItemProperty`,
  `ChangeGroup`, `ServiceContainer`, the `Extensions/` framework). ~61 files.
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\Xaml\` —
  the only concrete, XAML-backed implementation of those abstractions
  (`XamlDesignContext`, `XamlDesignItem`, `XamlModelProperty`,
  `XamlModelPropertyCollection`, `XamlModelCollectionElementsCollection`,
  `XamlComponentService`). ~1876 lines.
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\Services\UndoService.cs`
  — the only concrete implementation of `ChangeGroup` (`UndoTransaction`,
  `ITransactionItem`, `IUndoAction`, `UndoService`). Missed in this spec's
  first draft, then added after independent review (see "Known gaps" —
  without it `ChangeGroup` is unusable and there is no undo/redo to test.

Phase 2 builds directly on Phase 1's public surface. Phase 1 already
supports mutation (`XamlProperty.Reset()`, `XamlProperty`'s `SetValue`
path down to `XamlPropertyInfo.SetValue(instance, value)`), but
`XamlPropertyInfo` is `internal` to `WinUIDesigner.XamlDom` and
`XamlDocument`'s constructor is also internal — confirmed by reading
`src/WinUIDesigner.XamlDom/XamlPropertyInfo.cs:123` and the assembly's lack
of `InternalsVisibleTo`. Phase 2 lives in a separate assembly, so it talks
only to the public surface: `XamlProperty`, `XamlDocument`, `XamlParser`,
`CollectionSupport`. **No changes to the Phase 1 assembly are required for
Phase 2** — verified by reading the reference's own `XamlModelProperty` etc.,
which only ever touch the equivalent public members in WPF's `System.Xaml`
world.

## Decisions already made

- **Fork continues.** New project `WinUIDesigner.DesignModel` under `src/`,
  no shared abstraction with the WPF reference — same rationale as Phase 1.
- **Scope boundary: object model only.** No `DesignSurface`, no rendering,
  no `Adorner`s, no property-grid UI, no mouse/selection handling. Confirmed
  with the user over a larger alternative that would have also included a
  minimal live-rendering surface — rejected to keep Phase 2 testable without
  any UI dependency, same reasoning Phase 1 used to stay parser-only.
- **Concrete classes get pulled out of `WpfDesign.Designer`.** The XAML-backed
  implementation classes and `UndoService` are ported alongside the abstract
  `WpfDesign` core, deliberately separated from `WpfDesign.Designer`'s other
  ~170 UI-facing files they currently sit next to.

## Architecture

```
D:\Project\ST4I\Research\WinUIDesigner\
  src\
    WinUIDesigner.XamlDom\          (Phase 1, unchanged)
    WinUIDesigner.DesignModel\
      WinUIDesigner.DesignModel.csproj   (net10.0-windows10.0.19041.0, UseWinUI)
      DesignContext.cs                   (abstract)
      XamlDesignContext.cs                (concrete, wraps XamlDocument)
      DesignItem.cs                       (abstract)
      XamlDesignItem.cs                   (concrete, wraps XamlObject)
      DesignItemProperty.cs               (abstract)
      XamlModelProperty.cs
      XamlModelPropertyCollection.cs
      XamlModelCollectionElementsCollection.cs
      XamlComponentService.cs             (component <-> DesignItem registry)
      ChangeGroup.cs                      (abstract)
      UndoService.cs                      (UndoTransaction, ITransactionItem, IUndoAction)
      ServiceContainer.cs                 (trimmed, see "In scope")
      Services.cs                         (trimmed interface set)
      Extensions\
        Extension.cs
        ExtensionServer.cs
        ExtensionServerAttribute.cs
        ExtensionForAttribute.cs
        ExtensionAttribute.cs
        ExtensionManager.cs
        ExtensionInterfaces.cs
        DefaultExtension.cs
        CustomInstanceFactory.cs
        DefaultInitializer.cs
        DesignItemInitializer.cs
        NeverApplyExtensionsExtensionServer.cs
      Interfaces\
        IObservableList.cs
      DesignerException.cs
      ServiceRequiredException.cs
      EventArgs.cs
      XamlErrorService.cs
  tests\
    WinUIDesigner.TestHost\              (new, extracted — see "Testing")
    WinUIDesigner.DesignModel.Tests\
      WinUIDesigner.DesignModel.Tests.csproj
      DesignItemTests.cs
      DesignItemPropertyTests.cs
      UndoRedoTests.cs
      ExtensionFrameworkTests.cs
      RoundTripTests.cs
  docs\superpowers\specs\
    2026-09-01-winui-designmodel-phase2-design.md   (this file)
```

New WinUI-specific types introduced at this layer (mechanical swaps, no
behavior change from the reference):

| WPF type | WinUI type |
| --- | --- |
| `System.Windows.Point` (`DesignItem.Position`) | `Windows.Foundation.Point` |
| `System.Windows.Rect` | `Windows.Foundation.Rect` |
| `System.Windows.UIElement` (`DesignItem.View`) | `Microsoft.UI.Xaml.UIElement` |
| `System.Windows.DependencyObject` | `Microsoft.UI.Xaml.DependencyObject` |

## In scope

**Core abstractions:** `DesignContext`, `DesignItem` (minus two members, see
below), `DesignItemProperty`, `ChangeGroup`, `ServiceContainer` (minus four
convenience properties, see below).

**Concrete XAML-backed implementation:** `XamlDesignContext`,
`XamlDesignItem`, `XamlModelProperty`, `XamlModelPropertyCollection`,
`XamlModelCollectionElementsCollection`, `XamlComponentService`.

**Undo/redo:** `UndoService.cs` in full — `UndoTransaction`,
`ITransactionItem`, `IUndoAction`, `UndoService`, `PropertyChangeAction`.
This is the only concrete `ChangeGroup`; without it there is nothing to
route `DesignItemProperty.SetValue` through and no undo/redo to test.
Ported with its existing semantics kept intact rather than redesigned:
LIFO `_transactionStack` that throws on out-of-order commit/abort,
`Execute()` routing to the innermost open transaction, `MergeWith`
merge-on-execute.

**Extension framework:** `Extension`, `ExtensionServer` +
`ExtensionServerAttribute`/`ExtensionForAttribute`/`ExtensionAttribute`,
`ExtensionManager`, `ExtensionInterfaces`, `DefaultExtension`,
`CustomInstanceFactory`, `DefaultInitializer`, `DesignItemInitializer`,
`NeverApplyExtensionsExtensionServer` (referenced by
`[ExtensionServer(...)]` on the three factory/initializer classes above).
Only load-bearing path: `ExtensionManager` wiring
`CustomInstanceFactory`/`DefaultInitializer` into Phase 1's
`XamlParserSettings.CreateInstanceCallback` (already proven to exist and
work — `XamlParser.cs:307`).

**Supporting types:** `IObservableList` (return type of
`DesignItemProperty.CollectionElements`), `DesignerException`,
`ServiceRequiredException` (thrown by `ServiceContainer.GetRequiredService`,
which the two kept convenience properties depend on),
`EventArgs.cs` (`DesignItemEventArgs`, `DesignItemPropertyChangedEventArgs`
— required by the trimmed `IComponentService`), `XamlErrorService`
(constructed by `XamlDesignContext` and used by `XamlLoadSettings`-equivalent
error reporting).

**Services kept from `Services.cs`:** `IComponentService` (trimmed, see
below), `IComponentPropertyService`. `ServiceContainer` keeps its generic
`AddService`/`GetService`/`GetRequiredService` machinery — which throws
`ServiceRequiredException` (kept, see "Supporting types") — but of its
seven hard-typed convenience properties (`ServiceContainer.cs:157-223`)
only **`Component`** (→ `IComponentService`) and **`ExtensionManager`**
(→ `Extensions.ExtensionManager`) are kept; `Selection`, `Tool`,
`CopyPasteService`, `View` (→ `ViewService`, itself dropped), and
`DesignPanel` are dropped — each points at an out-of-scope service
interface. Callers needing those services in a later phase add the
properties back when those services exist.

## Explicitly out of scope (dropped, not deferred-but-stubbed)

Dropped because they depend entirely on things this phase excludes — not
ported as stubs, not left half-working:

- `XamlEditOperations.cs` — Clipboard, `Services.Selection`,
  `Services.DesignPanel`, `PlacementOperation`/`PlacementType` throughout.
- `XamlInstanceFactory.cs` — built on `System.Xaml`'s `XamlServices.Load`,
  already established as unportable in Phase 1.
- `BehaviorExtension.cs` — its only consumer, `DesignItem.AddBehavior`, is
  used exclusively by placement/UI extensions.
- `ViewService`, `IPropertyDescriptionService` — no consumer without a
  design surface or property-grid UI; signature (`GetModel(DependencyObject)`)
  doesn't even match `DesignItem.View`'s `UIElement` type cleanly.
- `IComponentService.SetDefaultPropertyValues` — implemented via
  `Metadata.cs` in the reference, and `Metadata.cs` (property-grid metadata)
  is Phase 4. Method dropped from the ported interface.
- `IComponentService.RegisterComponentForDesignerRecursiveUsingXaml` —
  implemented via `System.Windows.Markup.XamlWriter.Save`, which has no
  WinUI equivalent (same finding as Phase 1 gap 1). Dropped from the ported
  interface.
- `DesignItem.CreateOutlineNode()` — depends on `IOutlineNodeService`
  (Phase 4/5, OutlineView). Dropped from the ported abstract class.
- `DesignItem.GetCompleteAppliedTransformationToView()` — depends on WPF
  `Visual`/`ContainerVisual`/`FrameworkElement.LayoutTransform`, none of
  which exist in WinUI; it's a placement/rendering concern. Dropped from the
  ported abstract class.
- `Adorners/*`, `Placement*.cs`, `DesignPanelHitTestResult.cs`,
  `DrawItemExtension.cs`, `MouseInteraction.cs`, `HitTestType.cs`,
  `Tools.cs` (`ITool`/`IToolService`/`IDesignPanel` — mouse, hit-test,
  adorner-list interfaces), `SelectionExtensionServers.cs`,
  `MouseOverExtensionServer.cs`, `ISelectionService`, `IErrorService`,
  `ITopLevelWindowService`, `IKeyBindingService`, `ICopyPasteService`,
  `ISelectionFilterService` — all need a live mouse/visual surface. Phase 3.
- `PropertyGrid/*`, `Metadata.cs`, `IOutlineNodeService`,
  `IOutlineNodeNameService`, OutlineView — Phase 4/5.
- `DummyValueInsteadOfNullTypeDescriptionProvider.cs` — checked: its only
  consumer in the reference is `WpfDesign.Designer/Project/Extensions/PanelInstanceFactory.cs`,
  a panel-specific `CustomInstanceFactory` not in this phase's scope.
  Nothing in Phase 2's ported files references it.

`DesignItem.Position` (a plain `Point`) is kept despite being
placement-adjacent — it's inert data with no rendering dependency, and
dropping it would just mean re-adding it in Phase 3.

## Known gaps — flagged, not guessed

WPF→WinUI porting gaps this layer hits, analogous to Phase 1's six. Each
needs an explicit, tested resolution before Phase 2 is done, not a silent
fallback:

1. **`DependencyProperty` has no `Name`/`OwnerType`.** Already established
   in Phase 1 (`XamlPropertyInfo.cs:157-160`). This phase's
   `DesignItemPropertyCollection.GetProperty(DependencyProperty)`,
   `GetAttachedProperty(DependencyProperty)`, and
   `DesignItemProperty.DependencyFullName` (via `Metadata.GetFullName`) all
   key off those two members in the reference. Resolution to design at
   plan time: either a DP→(name, ownerType) side table built once via
   reflection over registered DPs, or drop the DP-keyed overloads in favor
   of string-keyed lookup only.
2. **No `DesignerProperties.SetIsInDesignMode` equivalent.** WinUI's
   `DesignMode.DesignModeEnabled` is a read-only global, not settable per
   instance. `CustomInstanceFactory.CreateInstance` in the reference calls
   the WPF setter on every constructed `UIElement`. Needs an explicit
   decision: drop the call (no design-mode marking at the instance level)
   or find another signal — verify there isn't a WinUI attached property
   serving the same purpose before assuming there's no substitute.
3. **No `{x:Type}` markup extension in WinUI.** `XamlComponentService`'s
   `NullExtension`/`TypeExtension` branch in the reference must lose the
   `TypeExtension` case entirely — not stubbed, since there is nothing to
   stub against.
4. **No real `INameScope`.** Phase 1's `NameScopeHelper` only exposes
   `NameChanged` and `FindNamedObject` over a flat per-document
   `XamlDocument.NamedObjects` table — not a true scoped namescope. The
   reference's unique-name-on-paste logic and `FindName`-with-scope calls
   in `XamlComponentService`/`XamlDesignItem` assume real scoping
   (`onlyFromSameNamescope`); that parameter becomes meaningless and the
   logic needs rewriting against the flat table, not a mechanical port.
5. **`XamlObject.Instance` can be `null`.** When `TemplateHelper.GetFrameworkTemplate`
   fails to find a `FrameworkTemplate` in the loaded wrapper, Phase 1's
   `ParseObject` returns a `XamlObject` with a null `Instance`. The
   reference's component registration (`_sites.Add(site.Component, site)`)
   would throw `ArgumentNullException` on this. Resolution to design at plan
   time: registration for a null-`Component` `DesignItem` either refuses
   with a clear, documented exception, or is represented explicitly as a
   valid-but-inert `DesignItem` — pick one, don't let it surface as an NRE.
6. **Templates are opaque leaves.** Phase 1's `ParseObject` returns
   immediately for any `FrameworkTemplate` subtype with no children parsed
   and no properties. A `DesignItem` wrapping a `DataTemplate`/
   `ControlTemplate` therefore has zero children and an empty `Properties`
   collection. **Explicit non-goal for Phase 2:** no editing inside template
   subtrees. State this in the Definition of Done, not just here.
7. **`Binding` is not a `MarkupExtension` in WinUI** (Phase 1 finding,
   `XamlObject.cs:605-609`). Anywhere the reference filters on
   `typeof(System.Windows.Markup.Reference)` (e.g.
   `XamlDesignItem`'s reference-fixup-on-name-change logic) must instead
   check against Phase 1's `ReferenceExtensionPlaceholder`.
8. **`DependencyPropertyDescriptor.FromProperty` has no WinUI counterpart**
   (Phase 1 finding, `XamlPropertyInfo.cs:96-99`). `ExtensionMethods.cs` (the
   reference's `GetProperty(this DesignItemPropertyCollection, MemberDescriptor)`
   and `IsAttachedDependencyProperty`) is built entirely on it, plus a
   `Round()` helper built on `PlacementInformation.BoundsPrecision`
   (out of scope). Checked against every other reference file: nothing in
   Phase 2's scope calls any of the three — only
   `WpfDesign.Designer/Project/PropertyGrid/PropertyGrid.cs` (Phase 4) and
   the placement/extension files (out of scope) do. `ExtensionMethods.cs`
   is dropped entirely, not reworked.

## Undo/redo behavior note

`PropertyChangeAction.Undo` in the reference, for a previously-unset
non-DP property, restores `ValueOnInstance` inside a `try`/`catch` that
swallows failure. Whether Phase 2 keeps this exact swallow-on-undo-failure
behavior, or makes it visible (e.g. debug-only assert) during development,
is a plan-time decision — flagged here so it isn't silently inherited
without a conscious choice.

## Data flow

XAML text → Phase 1's `XamlParser.Parse(XmlReader, XamlParserSettings)` →
`XamlDocument`/`XamlObject` tree (live instances already, Phase 1's
internal `XamlPropertyInfo` not visible outside that assembly) →
`XamlDesignContext` wraps the `XamlDocument`, lazily creating one
`XamlDesignItem` per `XamlObject` (cached by identity in
`XamlComponentService`) → caller calls `DesignItemProperty.SetValue(...)` →
opens or joins a `ChangeGroup` via `UndoService.StartTransaction`/`Execute`
→ writes through `XamlProperty`'s public `SetValue` path (Phase 1,
unchanged) → `XamlDocument.Save` (Phase 1, unchanged) for round-trip
serialization. `Commit()`/`Abort()`/`Dispose()` on the `ChangeGroup` drive
`UndoTransaction`'s recorded `IUndoAction` list forward or backward.

## Testing / Definition of Done

Extract a shared `WinUIDesigner.TestHost` project from Phase 1's
NUnitLite/thread-affinity scaffolding (`Program.cs`, the UI-thread host,
`ReflectionXamlMetadataProvider`) instead of copying it a second time —
both `WinUIDesigner.XamlDom.Tests` and the new
`WinUIDesigner.DesignModel.Tests` reference it. WinUI objects stay
thread-affine at this layer too (`DesignItem.Component`/`View` are live
instances), so the `.exe` NUnitLite runner remains the authoritative test
command, same as Phase 1.

Phase 2 is done when:

- A round-trip test parses a real WinUI 3 `MainWindow.xaml` into a
  `XamlDesignContext`, mutates a property via
  `DesignItemProperty.SetValue` inside a `ChangeGroup`, commits, calls
  `XamlDocument.Save`, and the result re-parses via the real
  `Microsoft.UI.Xaml.Markup.XamlReader.Load` oracle to the mutated value.
- An undo test performs the same mutation and calls `Abort()` instead of
  `Commit()`, asserting the live instance's value reverted.
- Each of the eight flagged gaps above has an explicit, tested resolution.
- A test confirms `WinUIDesigner.DesignModel` compiles against
  `WinUIDesigner.XamlDom` using only public members — no
  `InternalsVisibleTo` needed (verifies the "no Phase 1 changes required"
  claim above rather than assuming it).
- A test confirms the template non-goal explicitly: wrapping a
  `DataTemplate`/`ControlTemplate` produces a `DesignItem` with zero
  children, not a crash.

## Out of scope for Phase 2

No `DesignSurface`, no `Adorner`/overlay rendering, no live visual-tree
hosting or hit-testing, no mouse/selection handling, no property-grid
metadata (`Metadata.cs`) or property-grid UI, no `OutlineView`, no
clipboard/copy-paste, no placement/drag-drop geometry
(`PlacementOperation` et al.), no editing inside `FrameworkTemplate`
subtrees. Design surface and rendering are Phase 3; property grid,
metadata, and outline view are Phase 4; concrete design-time behaviors and
the shell app are Phase 5 — per the original decomposition, refined during
this phase's brainstorming.
