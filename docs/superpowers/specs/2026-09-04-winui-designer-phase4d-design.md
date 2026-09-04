# Phase 4d: Drawing tools & point-editing infra — WinUIDesigner.Designer

## Context

Phase 4c delivered context menus, merged to `main` (commit `dca7549`).
Per the five-sub-phase Phase 4 decomposition, 4a's own spec explicitly
deferred two related things to 4d: the "drawing tools" domain (click a
toolbox item, click/drag on the canvas to create a new control or shape),
and — named explicitly in 4a's "Out of scope" section —
`PointThumb`/`MultiPointThumb` and their consumers (`Line`/`Path`/
`PolyLine` handler extensions). These two are one phase, not two: the
reference's `DrawLineExtension` creates a new `Line` by re-driving the
*existing* `LineHandlerExtension`'s endpoint-drag machinery, so the
draw-tool layer cannot be built without the point-edit layer underneath
it. Phase 4d ports both together.

None of this exists yet in `WinUIDesigner`. What already exists:
`ITool`/`IToolService`/`IDesignPanel` as bare interfaces
(`src/WinUIDesigner.DesignModel/Tools.cs`), never implemented or
consumed anywhere; `DesignPanel` hardwires its own click-to-select
handling directly (`PointerPressed += OnPointerPressed` in its
constructor) with no tool-swap concept at all.

## WinUI API surface — verified, not assumed

Verified via a real throwaway probe added to
`WinUIDesigner.Designer.csproj`, built with `dotnet build`, and deleted
immediately after (same method used in 4b/4c) — not by reading docs or
assuming parity with WPF.

**Present, compiles clean (0 errors):**
- `UIElement.AllowDrop` (get/set), `UIElement.DragEnter`/`DragOver`/
  `DragLeave`/`Drop` events (type `DragEventHandler`, handler signature
  `(object, DragEventArgs)`).
- `DragEventArgs.AcceptedOperation` (get/set, type `DataPackageOperation`),
  `.DataView` (type `DataPackageView`, get-only), `.GetPosition(UIElement)`
  (returns `Point`), `.Handled` (get/set).
- `DataPackageOperation.Copy`.
- `FrameworkElement.IsLoaded` — exists in WinUI3, contrary to this
  project's usual pattern of WPF-only properties being absent. Safe to
  use directly, no substitute needed.
- `PathGeometry`, `PathFigure`, `PathFigureCollection`, `PathSegment`,
  `PathSegmentCollection`, `LineSegment`, `PolyLineSegment`,
  `BezierSegment`, `PolyBezierSegment`, `QuadraticBezierSegment`,
  `PolyQuadraticBezierSegment`, `ArcSegment` (with `.Size`,
  `.RotationAngle`, `.IsLargeArc`, `.SweepDirection` of type
  `SweepDirection`) — every `PathSegment` subtype `PathHandlerExtension`
  needs is present. This was the single biggest risk in this phase and
  it is fully cleared: `PathGeometry`/`PathFigure`/`LineSegment` were
  already confirmed running in production code in this repo
  (`GrayOutDesignerExceptActiveArea.cs`, Phase 4b); this probe confirms
  the remaining segment types WPF's `DrawPathExtension`/
  `PathHandlerExtension` also use.

**Confirmed absent (compiler error, not a guess):**
- `Shape.RenderedGeometry` / `Path.RenderedGeometry` — WPF's
  `PointTrackerPlacementSupport.Arrange` (reference file
  `WpfDesign.Designer\Project\Extensions\PointTrackerPlacementSupport.cs:72`)
  transforms a shape's logical point (`Line.X1/Y1/X2/Y2`,
  `Polygon.Points[i]`, `Polyline.Points[i]`) through
  `shape.RenderedGeometry.Transform` before arranging the point-thumb.
  No WinUI equivalent exists. **Decision** (see "Decisions already
  made"): every shape this phase's draw tools create always sets
  `Stretch = Stretch.None` (reference does this in all three
  `Draw*Extension.StartDrawItem` methods, and this phase preserves it) —
  under `Stretch.None`, WPF's own `RenderedGeometry` transform reduces to
  the identity transform for a `Line`/`Polygon`/`Polyline`'s own logical
  points (no stretch-driven scale, no bounding-box renormalization
  applies to interior coordinate values). The WinUI port's
  `PointTrackerPlacementSupport.Arrange` uses the raw logical point
  directly, with no transform step. This is a targeted, justified
  substitution scoped to this phase's actual usage — not a blanket claim
  that `RenderedGeometry` is always identity — flagged here so a future
  phase attaching point-thumbs to a `Stretch != None` shape re-derives
  this rather than copying the assumption forward.

## Also verified against the current codebase

- `ITool`, `IToolService`, `IDesignPanel` already declared as bare
  interfaces in `src/WinUIDesigner.DesignModel/Tools.cs:27-49`. `ITool`
  already types `Cursor` as `Microsoft.UI.Input.InputCursor` (correctly
  pre-adapted for WinUI, not WPF's `Cursor`). `IToolService` currently
  has only `PointerTool`/`CurrentTool`/`CurrentToolChanged` — no
  `DesignPanelHitTestFilterCallback`. `IDesignPanel` currently has only
  `Context`/`IsContentHitTestVisible`/`IsAdornerLayerHitTestVisible`/
  `Adorners`/two `HitTest` overloads — no `CustomHitTestFilterBehavior`,
  no `PointerPressed`/`PointerReleased`/`DragEnter`/`DragOver`/
  `DragLeave`/`Drop` events. Confirmed via grep across `src/` that no
  concrete `IToolService`/`ITool` implementation and no consumer exists
  anywhere yet — this is 100% unbuilt scaffolding.
- `DesignPanel.cs:25` wires `this.PointerPressed += OnPointerPressed` in
  the constructor; `OnPointerPressed` (line 234) delegates to
  `HandlePointerPressed` (line 248, `internal`, already extracted
  specifically so a test can drive it directly without a real
  `PointerRoutedEventArgs` — same pattern this phase reuses for its own
  untestable-event methods).
- `DragListener` (`src/WinUIDesigner.Designer/Controls/DragListener.cs`,
  237 lines, all read) binds itself to one fixed `Target` at
  construction time (`Target.AddHandler(PointerPressedEvent, ...)` etc.
  in the constructor) — a per-thumb drag primitive, not something an
  external gesture can "drive" without a real pointer event landing on
  that exact `Target`. It has `SimulateDown`/`SimulateMove`/`SimulateUp`/
  `RaisePointerCaptureLostForTest`, all doc-commented "Test-only entry
  point". It has **no** `ExternalStart`/`ExternalMouseMove`/
  `ExternalStop` — the reference's `DrawLineExtension.StartDrawItem`
  (`DrawLineExtension.cs:74`) calls exactly these three on
  `LineHandlerExtension.DragListener` to re-drive the *existing*
  endpoint-resize drag from a fresh mouse-down that landed on the
  *canvas*, not on the endpoint thumb itself. See "Decisions already
  made" for how this phase closes that gap.
- `ResizeThumbExtension.cs` (213 lines, all read) is 4a's precedent for
  exactly this shape of extension: `[ExtensionServer(typeof(
  OnlyOneItemSelectedExtensionServer))]`, `[ExtensionFor(typeof(
  FrameworkElement))]`, `sealed class ResizeThumbExtension :
  AdornerProvider` (not `SelectionAdornerProvider` — that WPF base class
  was never ported). Exposes `internal readonly Dictionary<
  PlacementAlignment, DragListener> DragListeners` specifically so tests
  can drive a named thumb's drag directly. Uses `DesignerThumb.
  ThumbVisible` (bool) rather than `Visibility` to hide/show a thumb —
  WinUI has no `Visibility.Hidden`, only `Collapsed`, and toggling
  `Collapsed` directly (as WPF's `Visibility="Hidden"` trigger did) would
  collapse layout differently than WPF's "hidden but still occupies
  space" semantics; `ThumbVisible` is the already-established
  abstraction over that difference.
- `DesignerThumb.cs` (75 lines, all read): `public class DesignerThumb :
  Grid` — not `Thumb` (WinUI's `Thumb` is `sealed`, noted in the file's
  own comment; `DragListener` doesn't need `Thumb`'s own drag events
  anyway). All visuals are hardcoded in the constructor
  (`Width=7,Height=7,Background=White,BorderBrush=Black,
  BorderThickness=1`) — there is no `ControlStyles.xaml`/
  `ControlTemplate`/`Trigger` equivalent anywhere in this port.
  `IsPrimarySelectionProperty`/`ThumbVisibleProperty` both re-run
  `OnAppearanceChanged` (line 60-72), which currently only flips
  `Visibility` and `BorderThickness` (2 vs 1) — it does **not** invert
  Stroke/Fill the way WPF's `DesignerThumb` style trigger does. There is
  no `IsEllipse`, `InnerRenderTransform`, `Point`, `RelativeToPoint`,
  `OperationMenu`, or `AdornerPlacement` property — none of
  `PointThumb`'s WPF-specific surface exists yet.
- `AdornerPanel.cs` (136 lines) / `AdornerProvider.cs` (91 lines), both
  read in full: `AdornerPanel`'s attached `PlacementProperty` and
  `Arrange`/`MeasureOverride` already call `GetPlacement(element).
  Arrange(this, element, finalSize)` — the exact same
  `AdornerPlacement.Arrange(AdornerPanel, UIElement, Size)` contract
  WPF's `PointTrackerPlacementSupport`/`PointThumb.PointPlacementSupport`
  implement. This phase's placement-support classes port directly
  against this existing abstraction; no changes needed to
  `AdornerPanel`/`AdornerProvider` themselves.
- `DesignItem.ReapplyAllExtensions()` (`DesignItem.cs:235`) and
  `DesignItem.ReapplyExtension(Type extensionType)` (`DesignItem.cs:249`)
  both already exist — `PathHandlerExtension.ConvertPart` and
  `PathHandlerExtension.CommitOperation` (reference) call these after a
  segment-structure change to rebuild the thumb set; the port calls the
  same two methods, no substitute needed.
- `PlacementType` (`src/WinUIDesigner.DesignModel/PlacementType.cs`, 23
  lines, all read) already registers `MovePoint`, `Resize`, `Move`,
  `MoveAndIgnoreOtherContainers`, `AddItem`, `Delete`, `PasteItem`, and
  `DragDropFromOutside` — every `PlacementType` this phase's `Draw*`/
  `*HandlerExtension` classes need (`MovePoint` for point-drag,
  `AddItem` for `CreateComponentTool`'s drop-into-container path)
  already exists. No changes needed to this file.
- `Rect.Round()` already exists
  (`src/WinUIDesigner.DesignModel/GeometryExtensions.cs:18`, rounds to
  `PlacementInformation.BoundsPrecision`) — reused as-is for
  `CreateComponentMouseGesture`'s drag-to-size rect, matching every
  other placement gesture in this codebase.
- `XamlComponentService.RaiseComponentRegisteredAndAddedToContainer(
  DesignItem)` already exists (`internal`,
  `src/WinUIDesigner.DesignModel/Xaml/XamlComponentService.cs:111`),
  reachable via the existing `InternalsVisibleTo("WinUIDesigner.Designer")`
  — no new internals-visibility change needed.
- `ExtensionForAttribute.OverrideExtensions` (plural, `Type[]`) and the
  singular `OverrideExtension` shorthand both already exist
  (`src/WinUIDesigner.DesignModel/Extensions/ExtensionForAttribute.cs:43,55`),
  already used by `GridPlacementSupport`/`CanvasPlacementSupport`/
  `StackPanelPlacementSupport` (each overriding `DefaultPlacementBehavior`
  singly). This phase is the first consumer of the plural form.
- `IKeyDown`/`IKeyUp` (the interfaces `PolyLineHandlerExtension`/
  `PathHandlerExtension` implement in the reference, for arrow-key point
  nudging) do **not** exist anywhere in `WinUIDesigner` — grep across
  `src/` finds zero matches. This matches 4a's own explicit deferral of
  "arrow-key nudge" as a whole feature area. See "Out of scope."
- Of the reference's `LineHandlerExtension`
  `OverrideExtensions = { ResizeThumbExtension,
  SelectedElementRectangleExtension, CanvasPositionExtension,
  QuickOperationMenuExtension, RotateThumbExtension,
  RenderTransformOriginExtension, SkewThumbExtension }` (7 types), only
  3 exist in this port today: `ResizeThumbExtension`, `RotateThumbExtension`,
  `SkewThumbExtension` (confirmed via grep — `SelectedElementRectangleExtension`
  has no match anywhere in `src/`). `CanvasPositionExtension`,
  `QuickOperationMenuExtension`, and `RenderTransformOriginExtension`
  are all named in 4c's own "Out of scope" section as 4e work, not yet
  ported. See "Decisions already made" for how this phase's
  `OverrideExtensions` lists are scoped.

## Decisions already made

1. **Tool-swap architecture (chat-approved).** `DesignPanel`'s existing
   click-to-select logic (`HandlePointerPressed`) is extracted into a
   new `PointerTool : ITool` — the default tool, matching the
   reference's own framing of `IToolService.PointerTool` as "the default
   tool for selecting and moving elements." `ToolService : IToolService`
   is a new concrete class; its `CurrentTool` setter calls the old
   tool's `Deactivate(designPanel)` then the new tool's
   `Activate(designPanel)`. `DesignPanel` no longer hardwires
   `OnPointerPressed` in its constructor — instead it exposes the new
   public `PointerPressed`/`PointerReleased`/`DragEnter`/`DragOver`/
   `DragLeave`/`Drop` events (added to `IDesignPanel`) by forwarding its
   own inherited `UIElement` events, and `PointerTool.Activate` is what
   subscribes `HandlePointerPressed` to them. This is a refactor of
   already-shipped, already-tested code (4a-4c's selection tests are the
   regression net), chosen over layering an `if (CurrentTool is
   CreateComponentTool)` branch into `HandlePointerPressed` because (a)
   it matches the reference's actual architecture instead of
   special-casing one tool inside another's handler, and (b) Phase 5's
   shell app will need real tool-switching UI (a toolbar), which needs
   this swap mechanism to exist cleanly regardless.
2. **`IDrawItemExtension` lives in `WinUIDesigner.DesignModel`, not
   `WinUIDesigner.Designer`.** Matches the reference's own layering —
   `DrawItemExtension.cs` lives in `WpfDesign` core (the layer with no
   concrete controls), not `WpfDesign.Designer`. The three concrete
   `Draw*Extension` implementations, `CreateComponentTool`, and the
   three `*HandlerExtension`/point-thumb classes all live in
   `WinUIDesigner.Designer` (they need `ExtensionServer`, `ChangeGroup`,
   `ModelTools`, `MoveLogic`, `DragListener` — Designer-layer
   concretions), matching the reference's own file placement
   (`WpfDesign.Designer\Project\Extensions\`).
3. **`LineExtensionBase`-equivalent inherits `AdornerProvider` directly,
   not a ported `SelectionAdornerProvider`.** The reference's
   `LineExtensionBase : SelectionAdornerProvider` — that base class was
   never ported to WinUI (confirmed: no match anywhere in `src/`), and
   4a's own `ResizeThumbExtension` already established the precedent of
   inheriting `AdornerProvider` directly for this exact shape of
   extension (adorner panel + resize/edit thumbs + selection-gated
   visibility). This phase's shared base (named `PointEditExtensionBase`
   to avoid colliding with the misleadingly-Line-specific reference
   name, since it is shared by Line/Polygon/Polyline/Path, not just
   lines) follows the same precedent: `abstract class
   PointEditExtensionBase : AdornerProvider`.
4. **`DragListener` gains a real (non-test) external-drive API.**
   `DrawLineExtension`'s whole mechanism (reusing `LineHandlerExtension`
   equivalent's already-built endpoint-drag to draw a brand-new line)
   depends on starting that drag from a mouse-down that landed on the
   *canvas*, not on the endpoint thumb. `DragListener.SimulateDown`
   already does exactly the right thing internally (`BeginAt(point,
   modifiers)`) but is doc-commented "Test-only". This phase adds a
   production-named alias: `ExternalStart(Point point, VirtualKeyModifiers
   modifiers)` / `ExternalMove(Point point, VirtualKeyModifiers
   modifiers)` / `ExternalStop(Point point)`, each a thin wrapper calling
   the same internal `BeginAt`/`MoveTo`/`EndAt` the `Simulate*` test
   entry points already call — kept as separate named methods (not a
   rename of `Simulate*`) so a reader can tell "test drives this
   artificially" from "a real production code path re-enters this
   listener's state machine" without reading call sites.
5. **Faithful port of the reference's own incompleteness in
   `PathHandlerExtension`.** The reference itself has, in the exact area
   this phase ports: `ConvertPart`'s `PolyBezierSegment`/
   `PolyQuadraticBezierSegment` branches are empty `//TODO` (never
   implemented upstream); the "is Smooth Join" / "is large Arc" /
   "Rotation Angle" / "Clockwise SweepDirection" context-menu items only
   ever set `IsChecked` — no `Click` handler is wired, so they cannot
   actually change anything; `RectangleGeometry`'s 4 corner points are
   collected into the `PathPoint` list with a `null` setter lambda, so
   they render as thumbs but cannot be dragged. This phase ports all
   three of these exactly as they are upstream — same non-functional
   menu items, same non-draggable rectangle corners, same missing
   convert-branches — rather than completing functionality the
   reference itself never finished. This is a deliberate scope
   boundary, not an oversight: completing them is new feature work
   beyond a port, and each is independently completable later without
   touching this phase's design. Each is called out with a `// Matches
   reference incompleteness — see spec` comment at the exact line so a
   future contributor doesn't mistake it for a porting bug.
6. **`KeyUpAction` behavior difference between Polyline and Path is
   preserved as-is.** In the reference, `PolyLineHandlerExtension.
   KeyUpAction` calls `CommitOperation()` directly; `PathHandlerExtension.
   KeyUpAction`'s body is entirely commented out upstream — Path instead
   commits its keyboard-driven point-move via `SelectThumb`/
   `SetOperation`/the `PrimarySelectionChanged` handler. Both arrow-key
   paths are themselves out of scope for this phase (see "Out of
   scope" — no `IKeyDown`/`IKeyUp` port), so this difference is recorded
   here for whichever phase eventually ports arrow-key point-nudging,
   not something 4d's own code needs to reconcile.
7. **`OverrideExtensions` scoped to what exists today.** Line/Polygon/
   Polyline/Path's `[ExtensionFor(..., OverrideExtensions = new[] {
   typeof(ResizeThumbExtension), typeof(RotateThumbExtension),
   typeof(SkewThumbExtension) })]` lists only the 3 extensions that
   exist in this port (dropping `SelectedElementRectangleExtension`,
   `CanvasPositionExtension`, `QuickOperationMenuExtension`,
   `RenderTransformOriginExtension` from the reference's 7-type list,
   since referencing a nonexistent type would not compile). Flagged
   here as a forward-looking note: when 4e ports those four, each one's
   own `[ExtensionFor(typeof(FrameworkElement))]` (or whatever type it
   targets) will need to also exclude `Line`/`Polygon`/`Polyline`/`Path`
   from its own applicability, mirroring what this phase's override
   list does from the other direction — 4d does not need to solve that,
   only avoid assuming it's already solved.
8. **`CreateComponentTool`'s click-drag gesture is a new, small state
   machine, not a `DragListener`.** `DragListener` binds to one fixed
   `Target` at construction and is built for "drag one specific element
   that is already on screen" (a thumb). `CreateComponentTool`'s job is
   "any click anywhere on the canvas, hit-tested fresh each time" — a
   different shape of gesture. This phase adds a small
   `CreateComponentMouseGesture` class driven directly by the new
   `IDesignPanel.PointerPressed`/`PointerReleased` events (added in
   decision 1), mirroring the reference's `ClickOrDragMouseGesture`
   click-vs-drag distinction (`SystemParameters.MinimumHorizontalDragDistance`
   equivalent — reuses the existing `DragListener.
   MinimumHorizontalDragDistance`/`MinimumVerticalDragDistance` constants
   already defined for exactly this WinUI-has-no-SystemParameters gap,
   see `DragListener.cs:26-33`) without depending on `DragListener`
   itself.
9. **Toolbox drag-drop tested via extracted `Handle*` methods, not real
   `DragEventArgs`.** `DragEventArgs` is a WinRT-projected type; like
   `PointerRoutedEventArgs`, it is expected to have no public
   constructor a test could use to fabricate a drop. `CreateComponentTool`'s
   `DragOver`/`Drop`/`DragLeave` handlers are thin wrappers that
   immediately delegate to `internal` `HandleDragOver(DesignPanel,
   Point, DataPackageOperation)`-shaped methods carrying only the
   plain-data pieces a test needs to supply directly — the same
   `Handle*`-extraction pattern `DesignPanel.HandlePointerPressed`
   already established.

## Architecture

```
WinUIDesigner.DesignModel/
  Tools.cs                          — ITool/IToolService/IDesignPanel (existing interfaces,
                                       extended: + PointerPressed/PointerReleased/DragEnter/
                                       DragOver/DragLeave/Drop events on IDesignPanel)
  Extensions/
    DrawItemExtension.cs            — new: IDrawItemExtension (CanItemBeDrawn/StartDrawItem)

WinUIDesigner.Designer/
  Services/
    ToolService.cs                  — new: ToolService : IToolService
    PointerTool.cs                  — new: PointerTool : ITool (extracted click-to-select)
    CreateComponentTool.cs          — new: CreateComponentTool : ITool
                                       + CreateComponentMouseGesture
  Controls/
    DragListener.cs                 — extended: + ExternalStart/ExternalMove/ExternalStop
    Thumbs/
      PointThumb.cs                 — new: PointThumb : DesignerThumb
      MultiPointThumb.cs            — new: MultiPointThumb : DesignerThumb
  Extensions/
    PointEditExtensionBase.cs       — new: abstract base (was LineExtensionBase in reference)
    PointTrackerPlacementSupport.cs — new: AdornerPlacement for Line/Polygon/Polyline
    LineHandlerExtension.cs         — new: [ExtensionFor(Line, OverrideExtensions=[...])]
    PolyLineHandlerExtension.cs     — new: [ExtensionFor(Polygon)] [ExtensionFor(Polyline)]
    PathHandlerExtension.cs         — new: [ExtensionFor(Path)]
    DrawLineExtension.cs            — new: [ExtensionFor(Canvas)] [ExtensionFor(Grid)] : IDrawItemExtension
    DrawPolyLineExtension.cs        — new: [ExtensionFor(Canvas)] [ExtensionFor(Grid)] : IDrawItemExtension
    DrawPathExtension.cs            — new: [ExtensionFor(Canvas)] [ExtensionFor(Grid)] : IDrawItemExtension
  DesignPanel.cs                    — modified: drop the constructor's direct
                                       PointerPressed += OnPointerPressed; expose the new
                                       IDesignPanel Pointer*/Drag* events instead
  DesignSurface.cs                  — modified: register IToolService, matching the existing
                                       CustomServiceRegisterFunctions.Add(...) pattern used
                                       for ICopyPasteService (Phase 4c)
```

`DesignItem.Position` (already exists, used by `CreateItemWithPosition`)
and `ModelTools.GetDefaultSize` (already exists per 4c's `ModelTools.cs`)
are consumed as-is — no changes.

## Mechanism table (WPF → WinUI replacements)

| WPF | WinUI replacement |
|---|---|
| `Cursor` / `Cursors.Cross` | `Microsoft.UI.Input.InputCursor` / `InputSystemCursor.Create(InputSystemCursorShape.Cross)` |
| `IInputElement`-inherited `MouseDown`/`MouseUp`/`DragEnter`/`DragOver`/`DragLeave`/`Drop` on `IDesignPanel` | Explicitly declared on `IDesignPanel` (WinUI's `IInputElement` doesn't carry them), forwarding `DesignPanel`'s own inherited `UIElement.PointerPressed`/`PointerReleased`/`DragEnter`/`DragOver`/`DragLeave`/`Drop` |
| `MouseButtonEventArgs`/`MouseEventArgs` | `PointerRoutedEventArgs` |
| `DragEventArgs.Data` (`IDataObject`) / `DragDropEffects` | `DragEventArgs.DataView` (`DataPackageView`) / `DataPackageOperation` |
| `DragDrop.DoDragDrop(...)` (toolbox-side drag start) | Out of scope this phase — no real toolbox exists yet (Phase 5); `CreateComponentTool`'s `Drop`/`DragOver` handlers are built and tested via extracted `Handle*` methods (decision 9), but nothing in 4d *initiates* a real WinUI drag |
| `Shape.RenderedGeometry` | No equivalent — substituted with the raw logical point (see "WinUI API surface", justified by `Stretch.None`) |
| `SystemParameters.MinimumHorizontalDragDistance`/`...Vertical...` | `DragListener.MinimumHorizontalDragDistance`/`MinimumVerticalDragDistance` (already-defined constants, `DragListener.cs:26-33`) |
| `ClickOrDragMouseGesture`/`MouseGestureBase` | Not ported (per 4b's own precedent) — `CreateComponentMouseGesture` is a small self-contained state machine against the new `IDesignPanel` events, not a shared gesture base class |
| `Mouse.GetPosition(null)` (ambient) | Not available — `DrawPolylineMouseGesture`/`DrawPathMouseGesture`-equivalents take position from the live `PointerRoutedEventArgs`/`DragListener` callback instead of an ambient query |
| `Matrix`/`Transform.Value.Invert()` | `GeneralTransform`/`.Inverse`/`.TransformPoint(Point)` (established in `DragListener.Delta`, `DragListener.cs:183-195`) |
| `PointCollectionConverter().ConvertToInvariantString(...)` (Polyline/Polygon points → XAML string) | `DesignItem.Properties[PointsProperty].SetValue(pointsCollection)` directly (object value, not a string round-trip) — this port's `XamlDom` layer serializes the object graph itself; no string mini-language conversion needed |
| `PathGeometry.FiguresProperty` TypeConverter string round-trip (`ConvertToInvariantString`) | `DesignItem.Properties[PathGeometry.FiguresProperty].SetValue(figuresCollection)` directly, same reasoning as above |
| `IKeyDown`/`IKeyUp` (arrow-key point nudge) | Not ported — out of scope, see "Out of scope" |

## Data flow

**1. Click-drag create a normal control** (`CreateComponentTool`, no
`IDrawItemExtension` match): tool activated (`Services.Tool.CurrentTool
= new CreateComponentTool(typeof(Button))`) → `PointerPressed` on
`DesignPanel` → hit-test via existing `IDesignPanel.HitTest` → no
`IDrawItemExtension` on the hit item claims the type → fall through to
`CreateComponentMouseGesture`: `PointerPressed` starts it,
`PointerMoved` past the drag threshold calls
`PlacementOperation.TryStartInsertNewComponents(container, [item],
[Rect], PlacementType.Resize)` and updates `Bounds` on every further
move, `PointerReleased` either commits the resize operation (if a drag
happened) or — for a bare click with no drag — falls back to
`CreateComponentTool.AddItemsWithDefaultSize` (this exact static helper
already exists conceptually in the reference and is ported as a static
helper here too, using `ModelTools.GetDefaultSize` and
`PlacementOperation.TryStartInsertNewComponents(..., PlacementType.AddItem)`).
Tool resets to `PointerTool` after either path completes.

**2. Toolbox drag-drop into the canvas** (no real toolbox source exists
yet — this flow is built and unit-tested via the extracted `Handle*`
methods per decision 9, ready for Phase 5's real toolbox to drive it):
`DragOver` → hit-test → same `IDrawItemExtension`-or-`CreateComponentMouseGesture`-equivalent
branch as click-drag, but driven by `DragOver`/`Drop`/`DragLeave` instead
of `PointerPressed`/`PointerMoved`/`PointerReleased`, using
`PlacementType.AddItem` per drop (matching `PlacementType.
DragDropFromOutside`'s existing registration for when Phase 5 wires a
real external drag source, though 4d's own `CreateComponentTool`-driven
drop, originating from *this* app's own tool selection rather than an
external source, uses `AddItem` — matching the reference's own choice at
`CreateComponentTool.cs:298`).

**3. Draw a Line** (`DrawLineExtension`, on `Canvas`/`Grid`):
`CreateComponentTool.OnMouseDown`-equivalent hit-tests the container,
finds `DrawLineExtension.CanItemBeDrawn(typeof(Line))` true →
`StartDrawItem` creates the `Line` DesignItem (`Stroke=Black`,
`StrokeThickness=2`, `Stretch=None`), inserts it via
`PlacementOperation.TryStartInsertNewComponents(..., PlacementType.
AddItem)` with a NaN-sized `Rect` at the click point (matching
reference), then looks up the just-attached `LineHandlerExtension`
(auto-attached because `[ExtensionFor(typeof(Line))]` fires the moment
the item is added) and calls `lineHandler.DragListener.ExternalStart(
startPoint, modifiers)` (decision 4) — from this point on, every further
pointer-move/up re-enters `LineHandlerExtension`'s *own* already-built
endpoint-resize `drag_Changed`/`drag_Completed` handlers exactly as if
the user had grabbed the endpoint thumb directly, ending the draw
gesture by committing a normal resize.

**4. Draw a Polyline/Polygon** (`DrawPolyLineExtension`): creates the
item with one point, then drives its own dedicated
`DrawPolylineMouseGesture`-equivalent: each click commits a new vertex
to `Points`, mouse-move live-updates a "preview" trailing point (added
then replaced on the next move, never left committed), Alt snaps the
in-progress segment to horizontal/vertical, double-click removes the
preview point and commits the final `Points` value via
`DesignItem.Properties[PointsProperty].SetValue(...)`, then
`ReapplyAllExtensions()` (matching reference) so `PolyLineHandlerExtension`
attaches fresh point-thumbs for every committed vertex.

**5. Draw a Path** (`DrawPathExtension`): creates the item with an empty
`PathFigure`/`PathGeometry`, `Path.Data` set to the geometry directly
(no `CollectionElements` attach for the figure — matches reference's own
commented-out line, the figure lives only inside `Path.Data`'s object
graph, not as a separately-tracked `DesignItem`); each click appends a
new `LineSegment` (reference always draws straight-line segments while
drawing — Bezier/Arc/etc. only ever appear via `PathHandlerExtension`'s
post-creation convert-segment context menu, not during initial drawing),
mouse-move live-updates the last segment's `Point` as a preview,
double-click removes the preview segment and commits, then
`ReapplyAllExtensions()` so `PathHandlerExtension` attaches a `PathThumb`
per anchor/control point.

**6. Post-creation point editing** (`LineHandlerExtension`/
`PolyLineHandlerExtension`/`PathHandlerExtension`, once an existing
Line/Polygon/Polyline/Path is selected): each attaches `PointThumb`/
`MultiPointThumb` instances via its own `AdornerPanel` (inherited from
`PointEditExtensionBase`), one per editable point, each backed by a
`DragListener` whose `Started`/`Changed`/`Completed` drive a
`PlacementOperation.Start(items, PlacementType.MovePoint)` (Path) or
`PlacementOperation.Start`/plain `ChangeGroup` (Polyline/Polygon,
matching reference's `resizeBehavior`-present-or-absent branch) — moving
a point updates the shape's `Points`/`Segments` collection directly,
committing on drag-complete.

## Testing / Definition of Done

- `PointerTool`/`ToolService`: swapping `CurrentTool` deactivates the
  old tool and activates the new one exactly once each; existing 4a-4c
  selection tests continue passing unmodified against the
  `PointerTool`-backed `DesignPanel` (this is the regression net for
  decision 1's refactor — no new test needed to prove it, the existing
  suite proves it by continuing to pass).
- `CreateComponentTool`: click-without-drag adds a default-sized item at
  the click point; click-and-drag adds an item sized to the drag rect;
  dropping on a container that refuses the item (`CanEnterContainer`
  false) does not add anything and leaves no dangling `ChangeGroup`
  (mirrors the existing `TryStartInsertNewComponents` null-return
  contract from Phase 4c). Toolbox drop path exercised via the extracted
  `Handle*` methods (decision 9), not real `DragEventArgs`.
  Undo/redo: each successful create is exactly one undo entry.
- `DrawLineExtension`: drawing a line end-to-end produces a `Line` with
  the expected `X1/Y1/X2/Y2` and default `Stroke`/`StrokeThickness`/
  `Stretch`; the whole gesture (create + drag endpoint) is one undo
  entry, not two (same class of bug fixed in 4c's final review for
  `RightClickContextMenuExtension` — verify explicitly here rather than
  assume it's fine).
- `DrawPolyLineExtension`: multi-click sequence produces the expected
  `Points`; double-click ends the gesture and leaves no preview point in
  the committed value; Alt-snap produces an axis-aligned segment when
  held.
- `DrawPathExtension`: multi-click sequence produces the expected
  `PathFigure.Segments` (all `LineSegment` from drawing, per decision 5);
  double-click ends the gesture cleanly.
- `LineHandlerExtension`/`PolyLineHandlerExtension`/`PathHandlerExtension`:
  dragging each point-thumb updates the correct point/segment and
  commits as one undo entry; deleting a Polygon/Polyline point via
  Shift+Ctrl+click (matching reference) removes exactly that point and
  re-indexes the remaining thumbs; `PathHandlerExtension`'s
  convert-segment context menu (`ToLineSegment`/`ToBezierSegment`/
  `ToQuadricBezierSegment`/`ToArcSegment`/`insertPoint`) produces the
  expected segment type at the expected index, with the reference's own
  incompleteness (decision 5) preserved and covered by a test asserting
  it stays a no-op, not silently regressed into "does nothing
  differently than before" by accident.
- Whole-branch review (per subagent-driven-development) explicitly
  checks: does the full user gesture connect end-to-end (toolbox
  selection → cursor change → draw → commit → point-edit), not just
  each class in isolation; is `OverrideExtensions` (decision 7)
  consistent across all three point-edit extensions; does every
  `PlacementOperation`/`ChangeGroup` opened by a `Draw*`/`*HandlerExtension`
  method have a matching `Commit()` or `Abort()` on every exit path
  (the exact defect class 4c's final review caught three times).

## Known gaps — flagged, not guessed

1. **`Shape.RenderedGeometry` absence** — see "WinUI API surface".
   Substituted with the raw logical point, justified by this phase's
   `Stretch.None` usage; not a general-purpose replacement.
2. **`PathHandlerExtension`'s own upstream incompleteness** — see
   decision 5. Ported faithfully, not completed.
3. **No real toolbox exists yet** — `CreateComponentTool`'s
   `DragOver`/`Drop`/`DragLeave` path is built and tested via extracted
   `Handle*` methods (decision 9) but has no real end-to-end exercise
   until Phase 5 builds an actual toolbox that calls
   `DragDrop`-equivalent WinUI APIs (`UIElement.StartDragAsync` or
   similar) to originate a real drag. This phase cannot verify that
   integration — flagged, not silently assumed to work.
4. **`DesignPanelHitTestFilterCallback`/`CustomHitTestFilterBehavior`** —
   confirmed present in the reference's `IToolService`/`IDesignPanel`
   but confirmed to have **zero consumers** anywhere in the entire
   reference repository (grep across all of `F:\ReferencesCode\CSharp\WpfDesigner`).
   Not needed by anything this phase builds. Not ported — if a future
   phase finds an actual need for it, it can be added then with a real
   consumer driving its design, rather than speculatively now.

## Out of scope for Phase 4d

Arrow-key point nudging (`IKeyDown`/`IKeyUp`, confirmed unported —
matches 4a's own explicit "arrow-key nudge" deferral, not a new gap this
phase introduces), a real toolbox UI and real drag-drop origination
(`DragDrop.DoDragDrop`/`StartDragAsync` — Phase 5 shell app), the four
`OverrideExtensions` types not yet ported (`SelectedElementRectangleExtension`,
`CanvasPositionExtension`, `QuickOperationMenuExtension`,
`RenderTransformOriginExtension` — 4e, see decision 7),
`DesignPanelHitTestFilterCallback`/`CustomHitTestFilterBehavior` (known
gap 4, zero reference consumers), completing `PathHandlerExtension`'s
own upstream-incomplete convert-branches and non-functional Arc/menu
items (decision 5 — faithful port only), `RectangleGeometry`/
`EllipseGeometry`/`LineGeometry`/`CombinedGeometry`/`GeometryGroup`/
`StreamGeometry` point-editing (`PathHandlerExtension`'s
`AddGeometryPoints` handles these upstream too, but `DrawPathExtension`
never creates anything but a plain `PathGeometry`/`PathFigure` with
`LineSegment`s — porting edit support for geometry shapes nothing in
this phase ever creates is speculative scope, deferred until something
actually produces one), and the remaining Phase 4 sub-phase behaviors
already named out-of-scope in 4a/4c (border decorations, size display,
render-transform-origin, in-place editor, tab-item-click, quick-operation
menu, `CanvasPositionExtension` margin-handle guides — all 4e).
