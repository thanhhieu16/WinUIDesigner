# Phase 3: WinUIDesigner.Designer — DesignSurface, overlay layer, hit-testing, minimal selection

## Context

Continues the 5-phase plan from the Phase 1 spec
(`docs/superpowers/specs/2026-08-31-winui-xamldom-phase1-design.md`).
**Phase 1** (`WinUIDesigner.XamlDom`) and **Phase 2**
(`WinUIDesigner.DesignModel` — design-item object model) are done and
merged to `master`.

The original Phase 1 spec described Phase 3 as "`DesignPanel`/`DesignSurface`
for WinUI — live visual tree hosting, hit-testing, a basic selection
rectangle." Phase 2's brainstorming additionally moved the Adorner-
replacement overlay layer into Phase 3 (originally slated for Phase 2).
This spec covers all three: the overlay layer, hit-testing, and DesignSurface
hosting, kept to a **minimal** slice — enough to prove the pipeline end to
end with one selected element and one highlight adorner, nothing more.

Reference source, same two reference projects as Phase 2:

- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\` — abstract
  layer: `Adorners\{AdornerPanel,AdornerPlacement,AdornerProvider,
  IAdornerLayer}.cs`, `Tools.cs` (`ITool`/`IToolService`/`IDesignPanel`),
  `HitTestType.cs`, `DesignPanelHitTestResult.cs`, `Services.cs`
  (`ISelectionService`, `IErrorService`, `ITopLevelWindowService`,
  `IKeyBindingService`), `Services\{ICopyPasteService,
  ISelectionFilterService}.cs`, `Extensions\SelectionExtensionServers.cs`,
  `PlacementOperation.cs` (one static helper only, see below).
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\` —
  concrete: `DesignSurface.cs`/`.xaml`, `DesignPanel.cs`,
  `Controls\AdornerLayer.cs`, `Services\SelectionService.cs`
  (`DefaultSelectionService`).

### WinUI API surface — verified, not assumed

Checked directly against `Microsoft.UI.Xaml.winmd`
(`Microsoft.WindowsAppSDK 1.7.250606001`, the package this repo already
references) by scanning its metadata strings, rather than assumed from WPF
familiarity:

**Absent (confirmed not in WinUI 3):** `Adorner`, `AdornerLayer`,
`HitTestCore`, `Decorator`, `GetVisualChild`/`VisualChildrenCount`/
`AddVisualChild`/`RemoveVisualChild`, `TransformToAncestor`/
`TransformToDescendant`, `IInputElement`, `GeneralTransformGroup`,
`IsDescendantOf`/`GetIsAncestorOf`, `SizeChangedInfo`/`OnRenderSizeChanged`
(no override hook — `SizeChanged` is an event instead).

**Present (confirmed in WinUI 3):**
`VisualTreeHelper.FindElementsInHostCoordinates` and
`FindAllElementsInHostCoordinates` (replaces `VisualTreeHelper.HitTest` +
`HitTestFilterCallback`), `TransformToVisual`, `LayoutUpdated`,
`RenderTransform`, `MatrixTransform`, `SizeChanged`, `PointerPressed`/
`PointerReleased`/`PointerEventHandler` (replaces WPF mouse events),
`PointerRoutedEventArgs.KeyModifiers` / `VirtualKeyModifiers`, `Popup`,
`ActualOffset`, `CompositeTransform`, `InvalidateArrange`/
`InvalidateMeasure`.

These findings drive every mechanism decision below — each one is a
mechanical replacement for a specific WPF API this layer's reference code
calls, not a redesign.

## Decisions already made

- **Scope: minimal end-to-end slice, not the full DesignPanel.** In scope:
  `DesignSurface`/`DesignPanel` hosting a live `DesignItem.View`, the
  overlay layer (Adorner replacement), hit-testing, and a single-selection
  service wired to one highlight-rectangle adorner. Out of scope (deferred
  to Phase 4 alongside the ~56 concrete design-time behaviors): arrow-key
  nudge (`PlacementOperation`-driven), context-menu merging, Copy/Cut/
  Paste/Undo/Redo command wiring, resize thumbs, snaplines, per-container
  placement, drawing tools. Confirmed with the user over a broader
  alternative that would have also wired those commands — rejected to keep
  Phase 3 focused on the two hardest technical risks (overlay geometry,
  hit-testing) without also carrying `PlacementOperation`/`ICopyPasteService`
  real implementations that have no other consumer yet.
- **Service scope, picked deliberately narrow.** Of the six services Phase 2
  deferred (`ISelectionService`, `IErrorService`, `ITopLevelWindowService`,
  `IKeyBindingService`, `ICopyPasteService`, `ISelectionFilterService`) plus
  `ITool`/`IToolService`/`IDesignPanel`: only **`IDesignPanel`** (implemented
  by `DesignPanel` itself) and **`ISelectionService`** (`DefaultSelectionService`)
  get real implementations and get registered on `DesignContext.Services`.
  The rest are declared as interfaces (mechanical port, for later phases to
  implement against) but have no concrete class and are not registered by
  default. Confirmed with the user over also wiring
  `ISelectionFilterService` for real — rejected as unnecessary until a
  concrete lock/visibility rule exists to enforce (Phase 4).
- **Project split mirrors the reference's own split**, same rationale as
  Phase 2: new project `WinUIDesigner.Designer` (mirrors
  `WpfDesign.Designer`) holds the concrete, UI-framework-heavy pieces
  (`DesignSurface`, `DesignPanel`, the concrete `AdornerLayer`,
  `DefaultSelectionService`). The abstract pieces
  (`Adorners\{AdornerPanel,AdornerPlacement,AdornerProvider,IAdornerLayer}`,
  `Tools.cs`, `HitTestType`, `DesignPanelHitTestResult`, the service
  interfaces, `SelectionExtensionServers`) go into `WinUIDesigner.DesignModel`
  — where the reference itself keeps them (`WpfDesign` core, not
  `WpfDesign.Designer`).

## Architecture

```
D:\Project\ST4I\Research\WinUIDesigner\
  src\
    WinUIDesigner.XamlDom\               (Phase 1, unchanged)
    WinUIDesigner.DesignModel\
      ...                                 (Phase 2, unchanged)
      Adorners\
        AdornerPanel.cs                   (Panel subclass, no GetVisualChild port needed)
        AdornerPlacement.cs               (abstract + FillContent only — see "Explicitly out of scope")
        AdornerProvider.cs                (extension base class; ties into Phase 2's DefaultExtension)
        IAdornerLayer.cs                  (marker interface, unchanged from reference)
      Tools.cs                            (ITool, IToolService, IDesignPanel — interfaces only)
      HitTestType.cs
      DesignPanelHitTestResult.cs         (VisualHit retyped Visual -> UIElement)
      Services.cs                         (add: ISelectionService, IErrorService,
                                             ITopLevelWindowService, IKeyBindingService;
                                             SelectionTypes enum)
      Services\
        ICopyPasteService.cs              (interface only)
        ISelectionFilterService.cs        (interface only)
      Extensions\
        SelectionExtensionServers.cs      (PrimarySelectionExtensionServer only)
      ServiceContainer.cs                 (modify: add back .Selection convenience property)
    WinUIDesigner.Designer\
      WinUIDesigner.Designer.csproj       (net10.0-windows10.0.19041.0, UseWinUI)
      DesignSurface.cs                    (ContentControl-derived host)
      DesignPanel.cs                      (Panel subclass, implements IDesignPanel)
      Controls\
        AdornerLayer.cs                   (concrete Panel subclass, LayoutUpdated-driven)
        SelectionHighlight.cs             (minimal highlight-rectangle adorner content)
      Services\
        SelectionService.cs               (DefaultSelectionService : ISelectionService)
        SelectionAdornerProvider.cs        (AdornerProvider subclass wiring SelectionHighlight
                                             via [ExtensionServer(typeof(PrimarySelectionExtensionServer))])
  tests\
    WinUIDesigner.TestHost\               (Phase 2, extended — see "Testing")
    WinUIDesigner.Designer.Tests\
      WinUIDesigner.Designer.Tests.csproj
      HitTestTests.cs
      SelectionTests.cs
      AdornerLayerTests.cs
  docs\superpowers\specs\
    2026-09-01-winui-designer-phase3-design.md   (this file)
```

New WinUI-specific types/mechanisms introduced at this layer (mechanical
swaps from the reference, verified against the winmd above):

| WPF mechanism | WinUI replacement |
| --- | --- |
| `VisualTreeHelper.HitTest(reference, filterCallback, resultCallback, params)` | `VisualTreeHelper.FindElementsInHostCoordinates(point, subtreeRoot)` — returns a flat front-to-back `IEnumerable<UIElement>`; the reference's `HitTestFilterCallback` pruning logic (Alt-cycle skip) becomes a LINQ filter over that list instead of a mid-walk callback. |
| `Visual` (as `DesignPanelHitTestResult.VisualHit`'s type) | `UIElement` — `FindElementsInHostCoordinates` returns `UIElement`, not `Visual`. |
| `element.TransformToAncestor(ancestor)` | `element.TransformToVisual(target)` — WinUI has no ancestor/descendant distinction; `TransformToVisual` is the only transform-lookup method and works regardless of the relationship. |
| `FrameworkElement.OnRenderSizeChanged` override | `SizeChanged` event subscription (no override hook exists). |
| `System.Windows.Input.Keyboard.Modifiers` (static, read anywhere) | `PointerRoutedEventArgs.KeyModifiers` (`VirtualKeyModifiers`) — only available at the point of a pointer event. `DefaultSelectionService.SetSelectedComponents(..., SelectionTypes.Auto)`'s modifier read moves from inside the service to `DesignPanel`'s pointer handler, which resolves `Auto` to a concrete `SelectionTypes` before calling. See "Known gaps" #1. |
| `MouseButtonEventHandler`/mouse events | `PointerEventHandler`/pointer events. |
| `Decorator` with `GetVisualChild` override (WPF `DesignPanel` base) | Plain `Panel` subclass using the public `Children` collection — WinUI has no protected visual-child composition hooks, so the `_eatAllHitTestRequests` visual-child trick is dropped (see "Explicitly out of scope"). |

## In scope

**Overlay layer (`WinUIDesigner.DesignModel/Adorners/`):**
`AdornerPanel` (attached `Placement` property, `AbsoluteToRelative`/
`RelativeToAbsolute`, `MeasureOverride`/`ArrangeOverride` — ported as a
`Panel` subclass; `MeasureOverride`'s one `PlacementOperation.GetRealElementSize`
call is inlined as a small local static helper — see "Known gaps" #3, not a
`PlacementOperation` port), `AdornerOrder` (struct, unchanged logic),
`AdornerPlacement` abstract class + its one concrete `FillContent` instance
(the only placement kind the minimal selection frame needs — see
"Explicitly out of scope" for `AdornerPlacementSpace`'s other placements),
`IAdornerLayer` (marker interface, unchanged), `AdornerProvider` (extension
base class — `OnInitialized`/`OnRemove`/`Adorners` collection/`AddAdorner`/
`AddAdorners`, ties into Phase 2's `DefaultExtension` and looks up
`IDesignPanel` via `Services.GetService<IDesignPanel>()`).

**Concrete overlay host (`WinUIDesigner.Designer/Controls/AdornerLayer.cs`):**
ported from `WpfDesign.Designer/Project/Controls/AdornerLayer.cs` —
`AdornerPanelCollection`, the `_dict` (adorned element → `AdornerInfo`)
visibility/position cache, `OnLayoutUpdated`, `AddAdorner`/`RemoveAdorner`/
`AddAdornerToChildren` (z-order insertion by `AdornerOrder`),
`ArrangeOverride`'s `TransformToVisual`+`RenderTransform` repositioning
(the `GeneralTransformGroup` 3D-fallback branch is dropped — no WinUI
equivalent exists and none is needed, see "Explicitly out of scope").

**Hit-testing:** `HitTestType` (`Default`, `ElementSelection`),
`DesignPanelHitTestResult` (`VisualHit: UIElement`, `AdornerHit:
AdornerPanel`, `ModelHit: DesignItem`, value-equality unchanged),
`DesignPanel.HitTest(...)` two-overload pair ported using
`VisualTreeHelper.FindElementsInHostCoordinates` against the adorner layer
first, then the content, mirroring the reference's two-pass structure and
Alt-cycle skip bookkeeping (`hitTestElements`/`skippedHitTestElements`)
reimplemented as list filtering.

**`DesignPanel`/`DesignSurface`:** `IDesignPanel` (interface — `Context`,
`IsContentHitTestVisible`, `IsAdornerLayerHitTestVisible`, `Adorners`,
`CustomHitTestFilterBehavior`, the two `HitTest` overloads; the reference's
redundant `IInputElement`-derived event redeclarations and drag events are
dropped — `DesignPanel` is a real `UIElement` and already has
`PointerPressed`/`PointerReleased`/`DragEnter`/`DragOver`/`DragLeave`/`Drop`
natively). `DesignPanel` (`Panel` subclass implementing `IDesignPanel`,
holds the content + the concrete `AdornerLayer` as ordinary `Panel.Children`
— no `_eatAllHitTestRequests`, no `GetVisualChild` override, see
"Explicitly out of scope"). `DesignSurface` (`ContentControl`-derived,
`LoadDesigner`/`UnloadDesigner`, `DesignContext` property) — trimmed to
just enough to host `DesignPanel` and wire `IDesignPanel` into
`DesignContext.Services`; no `ZoomControl`, no command handlers (see
"Explicitly out of scope").

**Selection:** `ISelectionService` (`SelectionChanging`/`SelectionChanged`/
`PrimarySelectionChanging`/`PrimarySelectionChanged`, `IsComponentSelected`,
`SelectedItems`, `SetSelectedComponents` (both overloads),
`PrimarySelection`, `SelectionCount`), `SelectionTypes` `[Flags]` enum
(`None`, `Auto`, `Primary`, `Toggle`, `Add`, `Remove`, `Replace` — full enum ported
even though `DesignPanel`'s click handler only needs to resolve `Auto` down
to `Toggle`/`Add`/`Primary` itself, per the `Keyboard.Modifiers` gap above).
`DefaultSelectionService : ISelectionService` ported with its existing
set-difference/notification semantics kept intact (same reasoning as Phase
2 kept `UndoTransaction`'s semantics unchanged) — its one `Keyboard.Modifiers`
read is replaced by an explicit `VirtualKeyModifiers` parameter threaded in
from `DesignPanel`'s pointer handler instead of a global read (see "Known
gaps" #1). `ServiceContainer.Selection` convenience property added back
(dropped in Phase 2, same pattern as the existing `.Component` property).
`PrimarySelectionExtensionServer` only — the one `SelectionAdornerProvider`
actually consumes; the other five classes in the reference file
(`SelectionExtensionServer` itself included — it has no consumer without a
per-selected-item behavior, which is Phase 4) are dropped, see "Explicitly
out of scope". `SelectionAdornerProvider : AdornerProvider` (new, small —
this repo's minimal equivalent of wiring a highlight adorner to the
selection-driven extension-server mechanism) tagged
`[ExtensionServer(typeof(PrimarySelectionExtensionServer))]`, adding one
`SelectionHighlight`-content `AdornerPanel` via `FillContent` placement.
`SelectionHighlight` (new, minimal — a plain unstyled `Border` with a
visible stroke; not a port of the reference's `Controls/SelectionFrame.cs`,
which carries resize-thumb layout Phase 4 doesn't need yet).

**Interface-only, no implementation, not registered (declared for later
phases to implement against):** `ITool`, `IToolService`, `IErrorService`,
`ITopLevelWindow`, `ITopLevelWindowService`, `IKeyBindingService`,
`ICopyPasteService`, `ISelectionFilterService`.

## Explicitly out of scope (dropped, not deferred-but-stubbed)

- `PlacementOperation.cs` in full — only its `GetRealElementSize` static
  helper is needed (by `AdornerPanel.MeasureOverride`), and that's inlined
  directly rather than pulling in the rest of the file's stateful
  placement-behavior machinery (`Start`/`Commit`/`ContainerBehavior` etc.),
  none of which this phase uses.
- `AdornerPlacementSpace.cs`'s placement kinds beyond `FillContent`
  (relative/corner-anchored placement, used by resize-thumb layout) —
  no consumer until Phase 4's resize thumbs exist.
- `PlacementAlignment.cs` — consumed only by the placement kinds above.
- `Controls/GridAdorner.cs`, `Controls/PanelMoveAdorner.cs`,
  `Extensions/GridAdornerProvider.cs`, `Controls/SelectionFrame.cs` (the
  reference's full version, with resize thumbs) — Phase 4 concrete
  behaviors.
- `SelectionExtensionServers.cs`'s `SelectionExtensionServer`,
  `SecondarySelectionExtensionServer`, `MultipleSelectedExtensionServer`,
  `PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer`,
  `OnlyOneItemSelectedExtensionServer`, `PrimarySelectionParentExtensionServer`
  — `PrimarySelectionExtensionServer` is the only one this phase's
  `SelectionAdornerProvider` consumes; the rest have no consumer without a
  per-selected-item or multi-selection behavior, which is Phase 4.
- `DesignPanel`'s `_eatAllHitTestRequests` visual-child trick — WinUI has no
  `GetVisualChild`/`AddVisualChild` protected composition API to hang it on
  (confirmed absent from the winmd). `DesignPanel`'s own `PointerPressed`
  handler calling `Focus()` directly is the WinUI-native replacement for
  "grab focus before the content's own handlers run" — no separate
  hit-test-eating element needed.
- `AdornerLayer.ArrangeOverride`'s `GeneralTransformGroup` 3D-fallback
  branch — `GeneralTransformGroup` doesn't exist in WinUI (confirmed absent
  from the winmd); WinUI's `TransformToVisual` only ever needs the
  `MatrixTransform` path the reference already has for the 2D case.
- `DesignSurface`'s `ZoomControl`/`PART_Zoom`, all `ApplicationCommands`
  handlers (Undo/Redo/Copy/Cut/Delete/Paste/SelectAll), alignment/rotate/
  stretch commands, `ScrollIntoView`/`RequestBringIntoView` handling,
  `FocusNavigator`, context-menu merging (`AddContextMenu`/
  `RemoveContextMenu`/`ContextMenuHandler`) — all depend on services or
  concepts (`ICopyPasteService`, `UndoService` UI wiring, `PlacementOperation`,
  a real command-input pipeline) this phase doesn't implement.
- `DesignPanel`'s arrow-key nudge (`DesignPanel_KeyDown`/`KeyUp`,
  `PlacementOperation.Start`/`Commit`) — needs real `PlacementOperation`,
  Phase 4.
- `IKeyDown`/`IKeyUp` extension-interface dispatch in `DesignPanel`'s key
  handlers — no consumer without the concrete behaviors that implement them.
- `IDesignPanel`'s `MouseDown`/`MouseUp`/`DragEnter`/`DragOver`/`DragLeave`/
  `Drop` event redeclarations and its `IInputElement` base — redundant on a
  real WinUI `UIElement` (see architecture table above).
- `Extensions/DrawItemExtension.cs`, `MouseInteraction.cs` — drawing-tool
  concepts, no consumer without `IToolService`'s real tool-switching
  implementation (Phase 4/5).

## Known gaps — flagged, not guessed

1. **No live `Window`/`XamlRoot` in the current test host.**
   `WinUIDesigner.TestHost.WinUITestHost` (Phase 2) only starts
   `Application.Start` for a `DispatcherQueue` — no `Window` is ever
   created or activated. This phase's core mechanisms
   (`TransformToVisual`, `FindElementsInHostCoordinates`,
   `ActualWidth`/`ActualHeight`, `LayoutUpdated`) all require a live
   layout pass, which requires a `Window` with `Content` set and
   `Activate()` called. Not yet confirmed whether an off-screen/inactive
   window still runs layout under the `.exe` NUnitLite runner in a
   headless CI context, or whether the window must be visible on-screen.
   **Must be verified empirically as the plan's first task**, the same way
   Phase 1 verified `ISupportInitialize`/namespace URIs against a real
   `dotnet new winui3` project rather than assuming — this finding decides
   whether `WinUIDesigner.TestHost` needs a real `Window` subtype added,
   and if so, whether tests can run under CI without an interactive
   desktop session.
2. **`Keyboard.Modifiers` (WPF global static) has no WinUI equivalent.**
   Confirmed via the winmd: no static keyboard-modifier query exists;
   `VirtualKeyModifiers` is only available on a live
   `PointerRoutedEventArgs`/`KeyRoutedEventArgs`. `DefaultSelectionService.
   SetSelectedComponents(components, SelectionTypes.Auto)` in the reference
   reads `Keyboard.Modifiers` internally to resolve `Auto` into
   `Toggle`/`Add`/`Primary`. This phase's port moves that resolution to the
   call site: `DesignPanel`'s pointer handler reads
   `PointerRoutedEventArgs.KeyModifiers` and passes an already-resolved
   `SelectionTypes` (never `Auto`) into `SetSelectedComponents`. Ported
   `DefaultSelectionService` keeps the `Auto` case in its switch for
   interface completeness but it is never reached in this phase's own call
   path — flagged so it isn't mistaken for dead code removed by accident.
3. **`PlacementOperation.GetRealElementSize` is the only used member of a
   file this phase otherwise excludes wholesale.** Confirmed by reading the
   full method: it's a pure static helper over `RenderSize`/`Width`/
   `Height`/`MinWidth`/`MinHeight`, no dependency on `PlacementOperation`'s
   instance state or the rest of the placement-behavior machinery. Ported
   as a small local static helper (exact placement — inline in
   `AdornerPanel` vs. a shared internal helper class — is a plan-time
   choice, not a design one).
4. **`AdornerLayer`'s adorned-element dictionary is keyed by `UIElement`
   reference identity**, same as Phase 2's `XamlComponentService._sites`
   dictionary (Phase 2's `IdentityEqualityComparer`). Whether this phase
   needs its own identity comparer or can reuse/expose Phase 2's is a
   plan-time decision — flagged so it isn't silently reimplemented
   differently by accident.

## Data flow

`XamlDesignContext.RootItem.View` (a live `UIElement`, from Phase 2) is set
as `DesignSurface`'s content → `DesignPanel` hosts it inside its `Children`
alongside the concrete `AdornerLayer` → a pointer press on `DesignPanel`
resolves screen coordinates to panel-local coordinates → `DesignPanel.HitTest`
calls `VisualTreeHelper.FindElementsInHostCoordinates` against the adorner
layer then the content, walking each hit `UIElement` up via
`VisualTreeHelper.GetParent` to find its registered `DesignItem` (through
Phase 2's `IComponentService.GetDesignItem`) → resolved `SelectionTypes`
(never `Auto`, see gap #2) plus the hit `DesignItem` are passed to
`ISelectionService.SetSelectedComponents` → `SelectionChanged`/
`PrimarySelectionChanged` fire → `PrimarySelectionExtensionServer` (Phase 2's
`ExtensionServer` framework) reapplies `SelectionAdornerProvider` on the new
primary selection → `SelectionAdornerProvider.OnInitialized` adds its
`SelectionHighlight` `AdornerPanel` to `IDesignPanel.Adorners` → the concrete
`AdornerLayer` makes it visible and arranges it via `TransformToVisual`
against the adorned element → subsequent `LayoutUpdated`/`SizeChanged` on
the design content keeps the highlight rectangle tracking the selected
element's screen position.

## Testing / Definition of Done

Extends `WinUIDesigner.TestHost` (Phase 2) rather than building a second
host — same `.exe` NUnitLite runner requirement as Phase 1/2, since every
mechanism in this phase needs the live WinUI dispatcher thread. Gap #1
above is this phase's first empirical task: confirm whether `TestHost`
needs a real `Window` added, and whether it can run headless.

Phase 3 is done when:

- A test hosts a real `DesignItem.View` (parsed via Phase 1/2's pipeline
  from actual WinUI 3 XAML) inside a live `DesignPanel`/`DesignSurface`,
  performs a hit-test at a known screen point, and asserts the correct
  `DesignItem` is returned as `ModelHit`.
- A test simulates a click (direct method call with a resolved
  `SelectionTypes`, not a synthesized OS input event) that selects an
  element, and asserts `ISelectionService.PrimarySelection` and
  `SelectedItems` update correctly, `SelectionChanged`/
  `PrimarySelectionChanged` fire, and exactly one `SelectionHighlight` adorner
  becomes visible in `IDesignPanel.Adorners`.
- A test moves/resizes the adorned element (mutating a layout property
  through Phase 2's `DesignItemProperty.SetValue`) and asserts the
  `SelectionHighlight` adorner's arranged position tracks it after the next
  layout pass (`LayoutUpdated`).
- A test selects a second element and asserts the first element's adorner
  is removed (single-selection minimal scope — no multi-select UI, but the
  underlying `ISelectionService`/`AdornerLayer` machinery must still behave
  correctly for exactly one active selection at a time).
- Each of the four flagged gaps above has an explicit, tested resolution.

## Out of scope for Phase 3

No property-grid UI or metadata (`Metadata.cs`), no `OutlineView`, no
clipboard/copy-paste, no undo/redo command wiring (the `UndoService` engine
itself is already done, Phase 2 — only its `DesignSurface` command
exposure is deferred), no resize thumbs, snaplines, drag-move placement,
arrow-key nudge, drawing tools, or context-menu merging, no multi-selection
UI (rubber-band drag-select, Ctrl/Shift-click), no `ZoomControl`, no
`IErrorService`/`ITopLevelWindowService`/`IKeyBindingService`/
`ICopyPasteService`/`ISelectionFilterService`/`IToolService` concrete
implementations. Concrete design-time behaviors (including the reference's full
`Controls/SelectionFrame.cs` with resize thumbs) are Phase 4; the `XamlDesigner` shell
app is Phase 5 — per the original decomposition.
