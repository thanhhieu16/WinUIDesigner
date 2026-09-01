# Phase 4a: Resize/Rotate/Skew thumbs — WinUIDesigner.Designer

## Context

Phase 3 delivered `DesignSurface`/`DesignPanel`, hit-testing, and a
single-selection highlight adorner. Its spec deferred the ~56 concrete
design-time behaviors (resize thumbs, snaplines, per-container placement,
drawing tools, context menus) to "Phase 4" as one bucket, per the original
five-phase decomposition (see
`docs/superpowers/specs/2026-08-31-winui-xamldom-phase1-design.md`).

That bucket is too large for one spec. Brainstorming this phase (2026-09-01)
counted the reference source
(`F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\Extensions\`,
53 files, plus `Controls\Thumbs\` and `Controls\SelectionFrame.cs` — the
~56 figure) and split it into five independently-shippable sub-phases:

- **4a** (this spec) — resize/rotate/skew thumbs on the primary selection.
- **4b** — container placement (Canvas/Grid/StackPanel-specific behaviors,
  snaplines, drag-move).
- **4c** — context menus.
- **4d** — drawing tools (line/path/polyline).
- **4e** — remaining small behaviors (border decorations, margin handle,
  size display, render-transform-origin, in-place editor, tab-item-click,
  quick-operation menu).

4a was picked first: it is the highest technical-risk slice (adorner
geometry, drag tracking) and builds directly on Phase 3's
`AdornerLayer`/`SelectionHighlight`/`ISelectionService`.

Reference source, same two reference projects as Phase 2/3:

- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\` — abstract core
  (`PlacementOperation.cs`, `PlacementBehavior.cs`, `PlacementType.cs`,
  `PlacementAlignment.cs`, `PlacementInformation.cs`,
  `Extensions\BehaviorExtension.cs`, `Adorners\AdornerProviderClasses.cs`,
  `DesignItem.cs`).
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\` —
  concrete WPF-specific pieces (`ModelTools.cs`,
  `Extensions\DefaultPlacementBehavior.cs`, `Controls\DragListener.cs`,
  `Controls\Thumbs\*.cs`,
  `Extensions\{ResizeThumbExtension,RotateThumbExtension,SkewThumbExtension}.cs`).

## WinUI API surface — verified, not assumed

Checked directly against the installed Windows App SDK 1.7.250606001
reference assemblies/docs (same package Phase 1-3 verified against:
`~/.nuget/packages/microsoft.windowsappsdk/1.7.250606001/`), not assumed:

**Present, matches WPF closely.**
`Microsoft.UI.Xaml.Controls.Primitives.Thumb` exists with
`DragStarted`/`DragDelta`/`DragCompleted`/`IsDragging`/`CancelDrag` —
confirmed via `Microsoft.WinUI.xml` member docs
(`lib/net6.0-windows10.0.22621.0/Microsoft.WinUI.xml`). `Control`'s
`DefaultStyleKeyProperty`/`OverrideMetadata` pattern used by
`DesignerThumb`'s static constructor is the same API WinUI custom controls
use throughout this codebase already.

**Present, different semantics than WPF's `null`-relative-to.**
`PointerRoutedEventArgs.GetCurrentPoint(UIElement relativeTo)`: the XML doc
states "If null was passed as relativeTo, the coordinates are in the frame
of reference of the overall window" — i.e. window/island coordinates, the
same coordinate space Phase 3 already established for
`TransformToVisual(null)`. This replaces WPF's `Mouse.GetPosition(null)`
(screen coordinates) directly — the reference's `DragListener` computes a
drag delta as a *difference* of two such points, so the absolute frame
(window vs. screen) does not change the result as long as both samples use
the same frame, which they do here.

**Confirmed absent.** `System.Windows.Media.LayoutTransform`
(`FrameworkElement.LayoutTransform`) — zero matches in
`Microsoft.WinUI.xml`. WinUI 3 has no layout-transform concept (only
`RenderTransform`, confirmed present). `System.Windows.SystemParameters`
(`MinimumHorizontalDragDistance`/`MinimumVerticalDragDistance`) — zero
matches; WinUI has no drag-threshold system parameter. `Windows.Media.
ContainerVisual` in the WPF visual-tree sense — not part of
`Microsoft.UI.Xaml`'s object model at all (WinUI's composition layer is a
separate, lower-level API this project does not touch). `System.Windows.
Input.InputManager`/`PostProcessInput` — no WinUI equivalent; there is no
global pre-dispatch input hook in this framework.

These findings drive the mechanism table below and the "Known gaps"
section — each a mechanical replacement or an explicit narrowing, not a
redesign.

## Decisions already made

- **Scope: resize/rotate/skew on a single selected item, generic
  container only.** In scope: the core placement engine
  (`PlacementOperation`/`IPlacementBehavior`/`PlacementType`/
  `PlacementAlignment`/`PlacementInformation`), one concrete
  `IPlacementBehavior` (`DefaultPlacementBehavior`, registered for
  `Panel`/`Control`/`Border`/`Viewbox` generically — resizes via
  `Width`/`Height`, not `Canvas.Left`/`Top`), and the three thumb
  extensions (`ResizeThumbExtension`, `RotateThumbExtension`,
  `SkewThumbExtension`) plus their `Controls/Thumbs` visuals and the
  `DragListener` pointer-tracking helper. Out of scope, deferred to 4b:
  `GridPlacementSupport`/`CanvasPlacementSupport`/`StackPanelPlacementSupport`,
  snaplines, drag-move, container enter/leave.
- **`ResizeThumbExtension`'s `is GridPlacementSupport` branch is dropped
  for 4a**, not stubbed. The reference special-cases Grid children
  (stretch-aware resize via `HorizontalAlignment`/`Margin` instead of raw
  `Width`/`Height`). Since `GridPlacementSupport` doesn't exist yet, the
  ported code always takes the `else` branch (`ModelTools.Resize`) —
  every resize sets `Width`/`Height` directly, even inside a `Grid`. This
  is a known, disclosed behavior gap for a `Grid`-child element, closed
  when 4b lands `GridPlacementSupport`. Confirmed with the user over
  pulling `GridPlacementSupport` into 4a — rejected to keep 4a from
  reaching into 4b's container-specific scope.
- **`ModelTools.cs` (890 lines in the reference) ports only the four
  members 4a actually calls**: `GetWidth`, `GetHeight`, `Resize`,
  `ApplyTransform`. Not ported: `WrapItemsNewContainer`,
  `UnwrapItemsFromContainer`, `ArrangeItems`, `StretchItems`,
  `CanDeleteComponents`, `IsInDocument`, `ComparePositionInModelFile` —
  all multi-selection/context-menu helpers with no 4a consumer (4c/4e
  territory).
- **`Metadata.IsPlacementDisabled` ports as a small standalone static
  helper**, not the full `Metadata.cs` (458 lines, a reflection-heavy
  design-time-attribute system already ruled out of scope in Phase 3:
  "no property-grid UI or metadata (Metadata.cs)"). Same precedent as
  Phase 3's Known-gap-#2 (`PlacementOperation.GetRealElementSize` ported
  alone from a file otherwise excluded wholesale).
- **`DefaultPlacementBehavior.CanEnterContainer`'s `InfoTextEnterArea`
  ("hold Alt to enter container" hint UI) is dropped**, not ported. It has
  no 4a consumer (nothing in 4a calls `EnterContainer`/`CanEnterContainer`
  on a still-outside-the-container drag — that path belongs to 4b's
  drag-move-between-containers). `CanEnterContainer` keeps its boolean
  contract but returns the plain `internalCanEnterContainer` result.
- **`Controls/SelectionFrame.cs` is *not* ported in 4a.** Reading it
  revealed it is the rubber-band multi-select rectangle control (a bare
  `Control` styled via `generic.xaml`, used during click-drag empty-space
  selection), not a resize-frame as its name suggests. That feature
  (rubber-band drag-select) is explicitly out of scope per Phase 3's own
  "Out of scope" list ("no multi-selection UI (rubber-band drag-select,
  Ctrl/Shift-click)") and isn't assigned to 4a.
- **The abstract `SelectionAdornerProvider`/`PrimarySelectionAdornerProvider`/
  `SecondarySelectionAdornerProvider` base classes (reference:
  `Adorners/AdornerProviderClasses.cs`) port under their reference names**,
  even though `WinUIDesigner.Designer/Services/SelectionAdornerProvider.cs`
  (Phase 3) already uses the name `SelectionAdornerProvider` for an
  unrelated concrete class (the highlight-rectangle provider). Different
  namespaces (`WinUIDesigner.DesignModel.Adorners` for the abstract bases
  vs. `WinUIDesigner.Designer.Services` for Phase 3's concrete class) mean
  no compiler collision. Confirmed with the user over renaming the
  abstract bases for clarity — rejected to keep 1:1 naming fidelity with
  the reference for future cross-referencing; callers always qualify or
  `using`-alias as needed.
- **`DragListener` ports via WinUI pointer events
  (`PointerPressed`/`PointerMoved`/`PointerReleased` + `CapturePointer`),
  not `Thumb.DragDelta`.** The reference itself doesn't use `Thumb`'s own
  drag events either — `DesignerThumb : Thumb` exists only for its
  default-style visual (cursor, primary-selection look); all drag tracking
  goes through `DragListener` wrapping the thumb as a plain
  `IInputElement`/pointer target. Porting preserves that separation and
  reuses the same pointer-capture pattern Phase 3's `DesignPanel` already
  established.
- **`DesignItem.GetCompleteAppliedTransformationToView()` is ported with
  its transform sources narrowed to `RenderTransform` only.** The
  reference walks the visual tree accumulating `LayoutTransform` +
  `RenderTransform` + `ContainerVisual.Transform`; WinUI has none of the
  first or third (see "WinUI API surface" above). The port walks
  `VisualTreeHelper.GetParent` (the same manual-walk pattern as Phase 3's
  `IsDescendantOf`) accumulating only `RenderTransform` into a
  `TransformGroup`.
- **`DesignerThumb`/`ResizeThumb`/`RotateThumb`'s default appearance is
  code-only**, not a `generic.xaml` resource dictionary entry — same
  simplification Phase 3 used for `SelectionHighlight` (a plain `Grid`
  instead of a themed `Border`). `DefaultStyleKeyProperty.OverrideMetadata`
  is still called (matches the reference's extensibility hook and doesn't
  block a themed style being added later), but the shipped default sets
  `Width`/`Height`/`Background`/`BorderBrush` directly in the constructor.

## Architecture

```
D:\Project\ST4I\Research\WinUIDesigner\
  src\
    WinUIDesigner.DesignModel\
      PlacementType.cs                    (new)
      PlacementAlignment.cs               (new)
      PlacementInformation.cs             (new)
      PlacementBehavior.cs                (new: IPlacementBehavior, IRootPlacementBehavior)
      PlacementOperation.cs               (new)
      DesignItem.cs                       (modify: add GetCompleteAppliedTransformationToView)
      Extensions\
        BehaviorExtension.cs              (new)
        SelectionExtensionServers.cs      (modify: add OnlyOneItemSelectedExtensionServer)
      Adorners\
        AdornerProviderClasses.cs         (new: SelectionAdornerProvider,
                                             PrimarySelectionAdornerProvider,
                                             SecondarySelectionAdornerProvider — abstract)
      Services.cs                         (modify: add IsPlacementDisabled static helper
                                             — exact host type is a plan-time choice)
    WinUIDesigner.Designer\
      ModelTools.cs                       (new: GetWidth, GetHeight, Resize, ApplyTransform only)
      Extensions\
        DefaultPlacementBehavior.cs       (new)
        ResizeThumbExtension.cs           (new)
        RotateThumbExtension.cs           (new)
        SkewThumbExtension.cs             (new)
      Controls\
        DragListener.cs                   (new, ported to WinUI pointer events)
        Thumbs\
          DesignerThumb.cs                (new)
          ResizeThumb.cs                  (new)
          RotateThumb.cs                  (new)
          PointThumb.cs                   (new)
          MultiPointThumb.cs              (new)
  tests\
    WinUIDesigner.Designer.Tests\
      ResizeThumbExtensionTests.cs        (new)
      RotateThumbExtensionTests.cs        (new)
      SkewThumbExtensionTests.cs          (new)
      PlacementOperationTests.cs          (new)
  docs\superpowers\specs\
    2026-09-01-winui-designer-phase4a-design.md   (this file)
```

New WinUI-specific mechanisms introduced at this layer (mechanical swaps
from the reference, verified against the assemblies/docs above):

| WPF mechanism | WinUI replacement |
| --- | --- |
| `Target.PreviewMouseLeftButtonDown/Move/Up` (`DragListener`) | `UIElement.PointerPressed`/`PointerMoved`/`PointerReleased`, with `CapturePointer`/`ReleasePointerCapture` replacing implicit WPF mouse capture — same pattern as Phase 3's `DesignPanel` pointer handling. |
| `Mouse.GetPosition(null)` / `e.GetPosition(null)` (screen coordinates) | `PointerRoutedEventArgs.GetCurrentPoint(null).Position` — window/island coordinates (confirmed by XML doc), not screen coordinates, but `DragListener` only ever consumes the *difference* between two such points, so the frame doesn't affect `Delta`. |
| `SystemParameters.MinimumHorizontalDragDistance`/`MinimumVerticalDragDistance` | No WinUI equivalent (confirmed absent) — replaced with a local constant. Exact value (WPF's default is 4px/2px) is a plan-time choice, not a design one. |
| `InputManager.Current.PostProcessInput` (global Escape-to-cancel-drag) | No WinUI equivalent (confirmed absent, no global pre-dispatch input hook). Replacement mechanism is a **Known gap**, below. |
| `FrameworkElement.LayoutTransform` + `ContainerVisual.Transform` (in `GetCompleteAppliedTransformationToView`) | Dropped — confirmed absent from WinUI's object model. Only `RenderTransform` is accumulated, walked manually via `VisualTreeHelper.GetParent`. |
| `element.TryFindParent<T>()` (`DesignerThumb.ReDraw`) | No WinUI-ported equivalent exists yet — replaced with a manual `VisualTreeHelper.GetParent` walk (same pattern used repeatedly since Phase 3). |

## Data flow

Pointer press on a `DesignerThumb` (child of the `AdornerPanel` Phase 3's
`AdornerLayer` positions over the selected element) → `DragListener`
captures the pointer, records the start point in window coordinates →
first move past the drag-distance threshold fires `Started` →
`ResizeThumbExtension.drag_Started` calls
`PlacementOperation.Start(extendedItemArray, PlacementType.Resize)`, which
resolves `DesignItem.GetBehavior<IPlacementBehavior>()` on the parent
(`DefaultPlacementBehavior`, registered in `OnInitialized` via
`ExtendedItem.AddBehavior`) and calls its `BeginPlacement`/`GetPosition` →
each subsequent `PointerMoved` fires `drag_Changed`, which computes a new
width/height from the thumb's `PlacementAlignment` and the transform-aware
`Delta`, applies it via `ModelTools.Resize` (setting
`DesignItemProperty` values — Phase 2's `XamlModelProperty.SetValue`, so
each resize step is undo-tracked), and updates
`PlacementInformation.Bounds` → `operation.CurrentContainerBehavior.
SetPosition(info)` → pointer release fires `drag_Completed`, which commits
(or aborts, on cancel) the `PlacementOperation`'s underlying `ChangeGroup`
(Phase 2's `UndoService` integration, unchanged). `RotateThumbExtension`/
`SkewThumbExtension` follow the same `DragListener` lifecycle but call
`ModelTools.ApplyTransform` with a `RotateTransform`/`SkewTransform`
instead of resizing, which in turn calls
`AdornerLayer.UpdateAdornersForElement` (Phase 3, unchanged) so the
selection highlight and thumb positions re-arrange after every transform
change.

## Testing / Definition of Done

Extends `WinUIDesigner.TestHost` (Phase 2/3) — same `.exe` NUnitLite
runner, same live-`Window` mechanism Phase 3's investigation established
(`WinUITestHost.RunWithWindow`).

Phase 4a is done when:

- A test drags a corner resize thumb on a selected `DesignItem` (direct
  `DragListener`-equivalent method calls simulating pointer
  down/move/up — not synthesized OS input — matching Phase 3's
  `SelectionTypes`-resolved-not-`Auto` precedent for simulated input) and
  asserts the item's `Width`/`Height` properties update to the expected
  values, and that a single `ChangeGroup` was committed (undo reverts the
  resize in one step).
- A test drags an edge (non-corner) resize thumb and asserts only the
  corresponding dimension (`Width` or `Height`) changed.
- A test drags a rotate thumb and asserts the item's `RenderTransform`
  becomes a `RotateTransform` with the expected `Angle`, and that the
  `AdornerLayer`'s thumb/highlight positions are re-arranged afterward
  (`TransformToVisual`-based assertion, same pattern as Phase 3's
  `SelectedElementsAdornerPanelIsArrangedAtTheElementsTransformedPosition`
  test).
- A test drags a skew thumb and asserts a `SkewTransform` is applied.
- A test cancels an in-progress resize (via whatever Escape-cancel
  mechanism gap #1 below resolves to) and asserts the item's
  `Width`/`Height` revert to their pre-drag values and no `ChangeGroup`
  was committed.
- Each of the two flagged gaps below has an explicit, tested resolution.

## Known gaps — flagged, not guessed

1. **No WinUI equivalent for `InputManager.Current.PostProcessInput`'s
   global Escape-to-cancel-drag.** Confirmed absent from
   `Microsoft.UI.Xaml` (see "WinUI API surface"). Candidate replacement:
   subscribe to `KeyDown` on the `DesignerThumb` itself while it holds
   pointer capture, or on `DesignPanel` (which Phase 3's spec already
   flagged as needing to hold focus for "all key handling... deferred to
   Phase 4" — this is that deferred work's first concrete instance).
   Whether pointer-captured elements still receive `KeyDown` in WinUI 3,
   and whether `DesignPanel` needs `IsTabStop`/explicit `Focus()` calls to
   receive it, **must be verified empirically as the plan's first task**,
   the same way Phase 3 verified live-`Window` layout timing before
   building on it.
2. **Exact drag-distance threshold value.** WPF's
   `SystemParameters.MinimumHorizontalDragDistance`/
   `MinimumVerticalDragDistance` default to 4px/2px but have no WinUI
   equivalent to read at runtime (confirmed absent). Whether to hardcode
   those same pixel values, use a different constant, or derive one from
   `DisplayInformation`/`XamlRoot.RasterizationScale` for DPI-awareness is
   a plan-time choice, not a design one — flagged so it isn't picked
   silently.

## Out of scope for Phase 4a

`GridPlacementSupport`/`CanvasPlacementSupport`/`StackPanelPlacementSupport`
and all other container-specific `IPlacementBehavior` implementations,
snaplines, drag-move (`PanelMove`, `TopLeftContainerDragHandle`),
container enter/leave during a drag, rubber-band multi-select
(`SelectionFrame`'s real purpose), `MultiPointThumb`/`PointThumb`'s actual
consumers (`Line`/`Path`/`PolyLine` handler extensions — 4d), context
menus, `SizeDisplayExtension`/`MarginHandleExtension` (4a's
`ResizeThumbExtension` null-checks for these and degrades gracefully
without them), arrow-key nudge, and the ~50 remaining behaviors listed in
the Phase 4 brainstorming breakdown (4b-4e) — per that decomposition, each
gets its own spec once 4a ships.
