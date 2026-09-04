# Phase 4d: Drawing tools & point-editing infra — WinUIDesigner.Designer

## Revision history

Two independent adversarial reviews (parallel, no shared context between
them, mirroring 4a/4b's own two-round precedent) were run against the
first draft of this spec. Findings and fixes:

- **Critical, fixed**: the tool-swap architecture (decision 1) extended
  `IDesignPanel` with only `PointerPressed`/`PointerReleased`/drag events
  — missing `PointerMoved` and any pointer-capture mechanism, both of
  which the reference gets for free from `IInputElement` but WinUI's
  `IDesignPanel` does not inherit. Without these, neither
  `CreateComponentMouseGesture` nor `DragListener.ExternalStart` (decision
  4) could actually receive move events after the initial press. Fixed:
  `IDesignPanel` now also exposes `PointerMoved`/`PointerCaptureLost` and
  `CapturePointer`/`ReleasePointerCapture`, forwarding `DesignPanel`'s own
  inherited `UIElement` members (see decision 1).
- **Critical, fixed**: nothing in the original draft made `PointerTool`
  the active tool at startup — `ToolService`'s constructor now performs
  that activation itself, so it can't be forgotten by whichever code
  constructs it (see decision 1).
- **Critical, fixed**: the `Shape.RenderedGeometry` → raw-point
  substitution was originally framed as "a future phase's problem" for
  `Stretch != None` shapes. A reviewer correctly pointed out
  `[ExtensionFor(typeof(Line))]` etc. attach unconditionally to *any*
  selected Line/Polygon/Polyline/Path — including ones loaded from an
  existing XAML document, never touched by this phase's draw tools. This
  is a present-phase risk, not a deferred one. Fixed: reframed as the
  single biggest open empirical question in this phase, explicitly
  required to be the implementation plan's first investigation task (see
  "Known gaps" #1 and decision 10), matching this project's own
  established precedent (Phase 3's Task 1, Phase 4a's Task 1 — investigate
  the biggest empirical risk before building on top of it).
- **Important, fixed**: no `[ExtensionServer(...)]` was chosen for the
  point-edit extensions (decision 3 extended); the `ZoomControl`/
  `zoom.CurrentZoom` dependency in the reference's drag-delta math was
  missing entirely (decision 11 added); dynamic thumb creation/removal
  mid-gesture (Polyline's Shift+Alt insert-point, Shift+Ctrl delete-point)
  had no lifecycle story against a `DragListener` that has no detach
  method (decision 12 added); `PathHandlerExtension`'s per-point context
  menu (`BuildMenu(PathPoint)`) had no home in the Architecture section
  (added, `PathThumb.cs`); the Shift+Alt mid-drag insert-point feature was
  missing from Data flow and DoD entirely (added); `IDrawItemExtension.
  StartDrawItem`'s WinUI signature was undecided (decision 2 extended);
  the "zero consumers" claim about `DesignPanelHitTestFilterCallback` was
  too strong — real plumbing exists in the reference, it's just never
  *called with* a real value anywhere (Known gap #4 corrected); decision
  3's citation of `SelectionAdornerProvider` as "never ported" was wrong
  evidence for a right conclusion — a class with that exact name already
  exists in this port, but it's an unrelated sealed selection-highlight
  class, not a port of the reference's abstract marker (corrected);
  `Handle*`-extraction testability (decision 9) cannot exercise the real
  coordinate-extraction code path (`GetPosition`/`DataView` reading) since
  WinRT event-args types have no public test constructor — flagged
  explicitly as a "checked by eye" gap (matching 4b's Known gap #2
  precedent) rather than left implicit.
- User reviewed both rounds of findings and chose to fix this spec in
  place rather than split it into two phases, despite the reference
  source totaling ~2897 lines across the 12 files this phase ports (this
  is still a large phase — the implementation plan should expect
  proportionally more tasks than 4a/4b/4c, not fewer).

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
  No WinUI equivalent exists, and no live WPF runtime is available from
  this environment to empirically characterize what that transform
  actually contains under `Stretch.None` (identity, a pen-alignment
  half-thickness offset, or a bounding-box-normalizing translation for
  shapes with negative-relative interior coordinates — e.g. a `Line` from
  `(50,50)` to `(10,10)`). **This is an open, unverified risk, not a
  resolved decision** — see decision 10 and "Known gaps" #1 for how the
  implementation plan must treat it (a mandatory first investigation
  task against the real WinUI runtime, before any other task builds on
  `PointTrackerPlacementSupport`).

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
  AdornerProvider`. Note: a class literally named `SelectionAdornerProvider`
  *does* exist in this port
  (`src/WinUIDesigner.Designer/Services/SelectionAdornerProvider.cs`,
  `[ExtensionFor(typeof(UIElement))] [ExtensionServer(typeof(
  PrimarySelectionExtensionServer))] sealed class SelectionAdornerProvider
  : AdornerProvider`) — but it is an unrelated selection-highlight-border
  renderer, not a port of the reference's abstract marker base class
  (`WpfDesign\Project\Adorners\AdornerProviderClasses.cs:39`, `[
  ExtensionServer(typeof(SelectionExtensionServer))] public abstract
  class SelectionAdornerProvider : AdornerProvider {}` — an empty marker
  whose only job is to make `ExtensionServerAttribute`'s `Inherited=true`
  behavior hand every subclass the `SelectionExtensionServer` gate for
  free). It's also `sealed`, so it couldn't be inherited from even if it
  were the right class. `SelectionExtensionServer` itself (the reference's
  real gate — attaches to every selected item of a matching type, not
  just when exactly one is selected) does not exist in this port either
  — only `OnlyOneItemSelectedExtensionServer`, `PrimarySelectionExtensionServer`,
  and `PrimarySelectionButOnlyWhenMultipleSelectedExtensionServer` do.
  See decision 3 for which one this phase's point-edit extensions use.
  Exposes `internal readonly Dictionary<
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
- `ZoomControl` — the reference's `LineHandlerExtension.drag_Changed` and
  `PolyLineHandlerExtension.SetOperation`/`drag_Changed` both divide
  `drag.Delta` by `zoom.CurrentZoom` (found via `TryFindParent<ZoomControl>()`)
  before applying it. `ZoomControl` does not exist anywhere in
  `WinUIDesigner` (grep across `src/` finds zero matches) — this is not a
  new gap 4d introduces; 4a/4b's own `RotateThumbExtension.cs`/
  `PanelMoveAdorner.cs` already carry comments acknowledging the same
  not-yet-ported `ZoomControl` and already use `drag.Delta` directly, with
  no zoom-scaling division. This phase's point-edit `drag_Changed`
  handlers do the same — consistent with, not a deviation from, how the
  rest of this codebase already handles this gap.
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
   public `PointerPressed`/`PointerMoved`/`PointerReleased`/
   `PointerCaptureLost`/`DragEnter`/`DragOver`/`DragLeave`/`Drop` events
   (added to `IDesignPanel`, plus `bool CapturePointer(Pointer)`/`void
   ReleasePointerCapture(Pointer)`, forwarding `DesignPanel`'s own
   inherited `UIElement.CapturePointer`/`ReleasePointerCapture` — already
   proven-public members, since `DragListener` already calls them
   directly on its `Target`) by forwarding its own inherited `UIElement`
   events, and `PointerTool.Activate` is what subscribes
   `HandlePointerPressed` to them. `PointerMoved`/`PointerCaptureLost`/
   the capture methods exist specifically because the reference's
   `IDesignPanel : IInputElement` gets `MouseMove`/`CaptureMouse()`/
   `LostMouseCapture` for free from that WPF interface — WinUI's
   `IDesignPanel` inherits nothing of the kind, so every one of those
   needs an explicit member (a gap the first draft of this spec missed
   entirely — see "Revision history"). This is a refactor of
   already-shipped, already-tested code (4a-4c's selection tests are the
   regression net), chosen over layering an `if (CurrentTool is
   CreateComponentTool)` branch into `HandlePointerPressed` because (a)
   it matches the reference's actual architecture instead of
   special-casing one tool inside another's handler, and (b) Phase 5's
   shell app will need real tool-switching UI (a toolbar), which needs
   this swap mechanism to exist cleanly regardless.

   **Initial activation.** Nothing else in this phase's design ever calls
   `CurrentTool = PointerTool` on its own, and `DesignSurface`'s existing
   `CustomServiceRegisterFunctions.Add(...)` pattern (used for
   `ICopyPasteService` in 4c) only *constructs and registers* a service —
   it has no lifecycle hook that runs after registration. To avoid this
   being silently forgotten (which would leave `HandlePointerPressed`
   never subscribed and break every 4a-4c selection test), `ToolService`'s
   own constructor sets `CurrentTool = PointerTool` itself, immediately,
   using the same setter every later swap uses — activation is
   self-contained in the class that owns it, not something the caller
   that constructs `ToolService` needs to remember to do.

   **Pointer capture responsibility.** `DesignPanel.CapturePointer`/
   `ReleasePointerCapture` (added above) are for tools that capture on
   the *panel itself* (`CreateComponentMouseGesture`, decision 8, and the
   new `Draw*MouseGesture` classes, decision 4) — this is distinct from
   `DragListener`'s own existing per-`Target` capture (`DragListener.cs`'s
   real `OnPointerPressed` already calls `Target.CapturePointer(e.Pointer)`
   on its own thumb). The two never conflict because they capture
   different elements for different gestures that are never active at
   the same time (a tool other than `PointerTool` is active, or a
   `DragListener`-backed thumb drag is in progress — never both, since
   `PointerTool` is what's active when the user could grab a thumb at
   all).
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

   **`StartDrawItem`'s WinUI signature.** The reference's
   `void StartDrawItem(DesignItem clickedOn, Type createItemType,
   IDesignPanel panel, MouseEventArgs e, Action<DesignItem>
   drawItemCallback)` only ever gets called from `CreateComponentTool.
   OnMouseDown` (a real click on the canvas while a draw tool is
   active) — never from the toolbox-drop path, which never checks
   `IDrawItemExtension` at all in the reference (it inserts the dropped
   item as a plain component, no draw-gesture). So this phase's
   signature only needs to serve the click path: `void StartDrawItem(
   DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point
   point, VirtualKeyModifiers modifiers, Action<DesignItem>
   drawItemCallback)` — decomposed primitives extracted by
   `CreateComponentTool`'s own pointer-pressed handling before calling
   in, matching the established `Handle*`-extraction testability pattern
   (decision 9) rather than passing a raw `PointerRoutedEventArgs`
   through an interface a test would need to construct one to call.
3. **`LineExtensionBase`-equivalent inherits `AdornerProvider` directly,
   not a ported `SelectionAdornerProvider`.** The reference's
   `LineExtensionBase : SelectionAdornerProvider` — the reference's
   `SelectionAdornerProvider` is an empty abstract marker
   (`WpfDesign\Project\Adorners\AdornerProviderClasses.cs:39`) whose only
   job is to carry `[ExtensionServer(typeof(SelectionExtensionServer))]`
   down to every subclass via `ExtensionServerAttribute`'s
   `Inherited=true`. Neither that marker class nor `SelectionExtensionServer`
   exist in this port (a class named `SelectionAdornerProvider` does
   exist, `src/WinUIDesigner.Designer/Services/SelectionAdornerProvider.cs`,
   but it's an unrelated, `sealed`, selection-highlight-border renderer —
   see "Also verified against the current codebase" for the full
   correction). 4a's own `ResizeThumbExtension` already established the
   precedent of inheriting `AdornerProvider` directly for this exact
   shape of extension (adorner panel + resize/edit thumbs +
   selection-gated visibility). This phase's shared base (named
   `PointEditExtensionBase` to avoid colliding with the
   misleadingly-Line-specific reference name, since it is shared by
   Line/Polygon/Polyline/Path, not just lines) follows the same
   precedent: `abstract class PointEditExtensionBase : AdornerProvider`.

   **`ExtensionServer` choice.** The reference's `SelectionExtensionServer`
   (attaches to *every* selected item of a matching type, not just when
   exactly one is selected) has no port and building one is out of scope
   — the only reason the reference used it instead of something more
   restrictive is that it's the reference's own general-purpose default,
   not a requirement specific to point-editing. `LineHandlerExtension`/
   `PolyLineHandlerExtension`/`PathHandlerExtension`-equivalents use
   `[ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]` —
   the same gate `ResizeThumbExtension` already uses for the sibling
   resize/rotate/skew thumb extensions this phase's point-edit thumbs
   sit alongside. Point-editing multiple selected shapes at once was
   never a stated requirement anywhere in this phase's scope, and
   matching the already-established sibling extensions' gating is the
   least-surprising choice, not a functionality loss relative to any
   actual requirement.
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

   **Capture responsibility (who calls `ExternalMove`).** `BeginAt`/
   `MoveTo`/`EndAt` never touch pointer capture — only `DragListener`'s
   own real `OnPointerPressed` does (`Target.CapturePointer(e.Pointer)`),
   and `ExternalStart` bypasses that method entirely. This means
   `ExternalStart` alone does **not** make further pointer-moves reach
   the endpoint thumb; something else must capture the pointer and
   forward moves in. That "something else" is a new small class per
   drawable shape type — `DrawLineMouseGesture`, `DrawPolylineMouseGesture`,
   `DrawPathMouseGesture` (see Architecture) — each constructed inside
   its `Draw*Extension.StartDrawItem`, each calling `designPanel.
   CapturePointer(pointer)` (the `IDesignPanel` member added in decision
   1) at start, subscribing `designPanel.PointerMoved`/`PointerReleased`/
   `PointerCaptureLost` for the gesture's duration, translating each move
   into `lineHandler.DragListener.ExternalMove(point, modifiers)` (Line)
   or driving its own dedicated logic directly (Polyline/Path, which
   don't reuse an existing `DragListener` the way Line does — see Data
   flow #4/#5), and releasing capture + unsubscribing on
   `PointerReleased`/`PointerCaptureLost`/double-click-commit. Because
   capture is on the *panel*, not the tiny endpoint thumb, this works
   regardless of where over the canvas the pointer actually is —
   matching the reference's own choice to capture on `DesignPanel`
   inside `MouseGestureBase.Start`, not on whatever `positionRelativeTo`
   happens to be.
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
   `IDesignPanel.PointerPressed`/`PointerMoved`/`PointerReleased` events
   and `CapturePointer`/`ReleasePointerCapture` (all added in decision
   1), mirroring the reference's `ClickOrDragMouseGesture`
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
   already established. **This does not cover the real coordinate/data
   extraction code** (`e.GetPosition(designPanel)`, `e.DataView`) — see
   "Known gaps" for why that gap is accepted rather than solved.
10. **`Shape.RenderedGeometry` substitute requires a real-runtime
    investigation task, not an assumption.** Since no live WPF runtime is
    reachable from this environment to characterize what
    `RenderedGeometry.Transform` actually contains (see "WinUI API
    surface"), and the substitute (raw logical point, no transform)
    affects every selected Line/Polygon/Polyline — including ones this
    phase never draws, loaded from existing XAML — the implementation
    plan's **first task** must empirically verify point-thumb placement
    accuracy against a real `Line`/`Polygon`/`Polyline` in the actual
    WinUI runtime, using coordinates chosen specifically to expose a
    bounding-box-normalizing translation if one exists (e.g. a `Line`
    from `(50,50)` to `(10,10)`, where the two endpoints' relationship to
    the element's own top-left differs from a simple `(0,0)`-anchored
    case). If the raw-point substitute turns out wrong, every later
    task in this phase that places a point-thumb depends on the fix —
    exactly the same "investigate the biggest empirical risk first"
    shape as Phase 3's Task 1 and Phase 4a's Task 1.
11. **`ZoomControl`'s missing zoom-scaling division is accepted as-is,
    not solved by this phase.** See "Also verified against the current
    codebase" — `ZoomControl` doesn't exist anywhere in this port, and
    4a/4b's `RotateThumbExtension.cs:17`/`PanelMoveAdorner.cs:11,13`
    already carry comments acknowledging the same gap with the same
    resolution (use `drag.Delta` directly, no division). This phase's
    `LineHandlerExtension`/`PolyLineHandlerExtension`-equivalents'
    `drag_Changed` handlers do the same, for consistency with the rest
    of the codebase rather than inventing a different convention.
12. **Dynamic point-thumb creation/removal needs no `DragListener`
    detach method.** `PolyLineHandlerExtension`'s Shift+Alt (insert a
    point mid-drag, creating a new thumb+`DragListener` while another
    thumb's drag is still in progress) and Shift+Ctrl+click (delete a
    point, removing its thumb) both create/destroy `MultiPointThumb`
    instances at runtime — unlike `ResizeThumbExtension`'s 8 fixed
    thumbs created once in `OnInitialized` and never removed. No new
    `DragListener` API is needed for this: removing a thumb from
    `adornerPanel.Children` is sufficient — the `DragListener` instance
    built for that thumb becomes unreferenced (nothing outside the
    removed thumb's own closure holds it) and is eligible for GC along
    with the thumb itself. Its `Target.AddHandler(..., handledEventsToo:
    true)` subscriptions (`DragListener.cs:59-63`) keep `Target` alive
    only for as long as `Target` itself is reachable — once the thumb is
    unparented and nothing else references it, both become collectible
    together. Adding a new point mid-drag (Shift+Alt) constructs a new
    `MultiPointThumb`+`DragListener` the same way `OnInitialized`'s
    per-point loop already does, just called again later — not a new
    code path, only a new call site for existing thumb-creation logic.

## Architecture

```
WinUIDesigner.DesignModel/
  Tools.cs                          — ITool/IToolService/IDesignPanel (existing interfaces,
                                       extended: + PointerPressed/PointerMoved/PointerReleased/
                                       PointerCaptureLost/DragEnter/DragOver/DragLeave/Drop
                                       events + CapturePointer/ReleasePointerCapture on
                                       IDesignPanel — decision 1)
  Extensions/
    DrawItemExtension.cs            — new: IDrawItemExtension (CanItemBeDrawn/StartDrawItem,
                                       decomposed-primitive signature — decision 2)

WinUIDesigner.Designer/
  Services/
    ToolService.cs                  — new: ToolService : IToolService (constructor activates
                                       PointerTool immediately — decision 1)
    PointerTool.cs                  — new: PointerTool : ITool (extracted click-to-select)
    CreateComponentTool.cs          — new: CreateComponentTool : ITool
                                       + CreateComponentMouseGesture (captures/releases via
                                       IDesignPanel.CapturePointer — decision 1)
  Controls/
    DragListener.cs                 — extended: + ExternalStart/ExternalMove/ExternalStop
                                       (decision 4 — these do not themselves capture; the
                                       calling Draw*MouseGesture captures on the panel)
    Thumbs/
      PointThumb.cs                 — new: PointThumb : DesignerThumb (+ nested
                                       PointPlacementSupport, used by Path)
      MultiPointThumb.cs            — new: MultiPointThumb : DesignerThumb (used by
                                       Polygon/Polyline, created/removed dynamically —
                                       decision 12)
      PathThumb.cs                  — new: PathThumb : PointThumb — adds the per-point
                                       context menu (`BuildMenu(PathPoint)` in reference:
                                       convert-segment items, IsClosed/IsStroked toggles),
                                       set directly on the thumb's own `ContextFlyout`
                                       (a `FrameworkElement` property every `DesignerThumb`
                                       already has via `Grid`) — distinct from and unrelated
                                       to 4c's per-`DesignItem` composite `ContextFlyout`
                                       merge system in `DesignPanel`, since a `PathPoint`
                                       is not itself a `DesignItem`
  Extensions/
    PointEditExtensionBase.cs       — new: abstract base (was LineExtensionBase in reference)
    PointTrackerPlacementSupport.cs — new: AdornerPlacement for Line/Polygon/Polyline
                                       (raw-point placement — pending decision 10's
                                       investigation task)
    LineHandlerExtension.cs         — new: [ExtensionFor(Line, OverrideExtensions=[...])]
                                       [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
                                       (decision 3)
    PolyLineHandlerExtension.cs     — new: [ExtensionFor(Polygon)] [ExtensionFor(Polyline)]
                                       [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    PathHandlerExtension.cs         — new: [ExtensionFor(Path)]
                                       [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    DrawLineExtension.cs            — new: [ExtensionFor(Canvas)] [ExtensionFor(Grid)] : IDrawItemExtension
                                       + DrawLineMouseGesture (decision 4 — drives
                                       DragListener.ExternalMove/ExternalStop from
                                       IDesignPanel.PointerMoved/PointerReleased)
    DrawPolyLineExtension.cs        — new: [ExtensionFor(Canvas)] [ExtensionFor(Grid)] : IDrawItemExtension
                                       + DrawPolylineMouseGesture (own click/move state
                                       machine — does not reuse DragListener, see Data flow #4)
    DrawPathExtension.cs            — new: [ExtensionFor(Canvas)] [ExtensionFor(Grid)] : IDrawItemExtension
                                       + DrawPathMouseGesture (own click/move state
                                       machine — does not reuse DragListener, see Data flow #5)
  DesignPanel.cs                    — modified: drop the constructor's direct
                                       PointerPressed += OnPointerPressed; expose the new
                                       IDesignPanel Pointer*/Drag*/Capture* members instead
                                       (forwarding the panel's own inherited UIElement members)
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
| `IInputElement`-inherited `MouseDown`/`MouseUp`/`MouseMove`/`DragEnter`/`DragOver`/`DragLeave`/`Drop` on `IDesignPanel` | Explicitly declared on `IDesignPanel` (WinUI's `IInputElement` doesn't carry them), forwarding `DesignPanel`'s own inherited `UIElement.PointerPressed`/`PointerMoved`/`PointerReleased`/`DragEnter`/`DragOver`/`DragLeave`/`Drop` |
| `IInputElement.CaptureMouse()`/`ReleaseMouseCapture()`/`LostMouseCapture` | `IDesignPanel.CapturePointer(Pointer)`/`ReleasePointerCapture(Pointer)`/`PointerCaptureLost` event, forwarding `UIElement`'s own members (already proven-public — `DragListener` already calls them on its `Target`) |
| `ZoomControl`/`zoom.CurrentZoom` (drag-delta scaling) | Not ported (no `ZoomControl` exists anywhere in this port) — `drag.Delta` used directly, unscaled, matching 4a/4b's own already-accepted resolution of the same gap (`RotateThumbExtension.cs:17`, `PanelMoveAdorner.cs:11,13`) |
| `MouseButtonEventArgs`/`MouseEventArgs` | `PointerRoutedEventArgs` |
| `DragEventArgs.Data` (`IDataObject`) / `DragDropEffects` | `DragEventArgs.DataView` (`DataPackageView`) / `DataPackageOperation` |
| `DragDrop.DoDragDrop(...)` (toolbox-side drag start) | Out of scope this phase — no real toolbox exists yet (Phase 5); `CreateComponentTool`'s `Drop`/`DragOver` handlers are built and tested via extracted `Handle*` methods (decision 9), but nothing in 4d *initiates* a real WinUI drag |
| `Shape.RenderedGeometry` | No equivalent — tentatively substituted with the raw logical point, pending decision 10's mandatory investigation task (see "WinUI API surface", "Known gaps" #1) |
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
`DesignPanel` → hit-test via `IDesignPanel.HitTest(point, testAdorners:
false, testDesignSurface: true, HitTestType.Default)` (matching
reference's own `CreateComponentTool.cs:332` call exactly — adorners
left over from a prior selection must not intercept a draw-tool click)
→ no `IDrawItemExtension` on the hit item claims the type → capture the
pointer via `designPanel.CapturePointer(e.Pointer)` and fall through to
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
`designPanel.ReleasePointerCapture(...)` releases the capture taken at
gesture start; tool resets to `PointerTool` after either path completes.

**2. Toolbox drag-drop into the canvas** (no real toolbox source exists
yet — this flow is built and unit-tested via the extracted `Handle*`
methods per decision 9, ready for Phase 5's real toolbox to drive it):
`DragOver` → hit-test (same `testAdorners: false` call as data flow #1,
matching reference's `designPanel_DragOver`, `CreateComponentTool.cs:100`)
→ same `IDrawItemExtension`-or-`CreateComponentMouseGesture`-equivalent
branch as click-drag, but driven by `DragOver`/`Drop`/`DragLeave` instead
of `PointerPressed`/`PointerMoved`/`PointerReleased`, using
`PlacementType.AddItem` per drop (matching `PlacementType.
DragDropFromOutside`'s existing registration for when Phase 5 wires a
real external drag source, though 4d's own `CreateComponentTool`-driven
drop, originating from *this* app's own tool selection rather than an
external source, uses `AddItem` — matching the reference's own choice at
`CreateComponentTool.cs:298`).

**3. Draw a Line** (`DrawLineExtension`, on `Canvas`/`Grid`):
`CreateComponentTool.OnMouseDown`-equivalent hit-tests the container
(same `testAdorners: false` call as data flow #1), finds
`DrawLineExtension.CanItemBeDrawn(typeof(Line))` true → `StartDrawItem`
creates the `Line` DesignItem (`Stroke=Black`, `StrokeThickness=2`,
`Stretch=None`), inserts it via `PlacementOperation.
TryStartInsertNewComponents(..., PlacementType.AddItem)` with a
NaN-sized `Rect` at the click point (matching reference), then looks up
the just-attached `LineHandlerExtension` (auto-attached because
`[ExtensionFor(typeof(Line))]` fires the moment the item is added),
constructs a `DrawLineMouseGesture(lineHandler, designPanel, pointer,
changeGroup)`, which captures the pointer on `designPanel` (decision 4 —
*not* on the endpoint thumb) and calls `lineHandler.DragListener.
ExternalStart(startPoint, modifiers)` — from this point on,
`designPanel.PointerMoved` drives `DragListener.ExternalMove(point,
modifiers)` and `designPanel.PointerReleased`/`PointerCaptureLost`
drives `DragListener.ExternalStop(point)`, re-entering
`LineHandlerExtension`'s *own* already-built endpoint-resize
`drag_Changed`/`drag_Completed` handlers exactly as if the user had
grabbed the endpoint thumb directly, ending the draw gesture by
committing a normal resize and releasing the panel's pointer capture.

**4. Draw a Polyline/Polygon** (`DrawPolyLineExtension`): creates the
item with one point, captures the pointer on `designPanel` (decision 4
— this gesture does not reuse `DragListener`/`ExternalStart` the way
Line's does, since there's no existing per-point drag handler to
re-enter yet during initial drawing), then drives its own dedicated
`DrawPolylineMouseGesture`: each click commits a new vertex to `Points`,
mouse-move live-updates a "preview" trailing point (added then replaced
on the next move, never left committed), Alt snaps the in-progress
segment to horizontal/vertical, double-click removes the preview point
and commits the final `Points` value via `DesignItem.
Properties[PointsProperty].SetValue(...)`, releases the panel's pointer
capture, then `ReapplyAllExtensions()` (matching reference) so
`PolyLineHandlerExtension` attaches fresh point-thumbs for every
committed vertex.

**Post-creation only** (not part of the draw gesture above —
`PolyLineHandlerExtension`, once the shape is selected and a point-thumb
is being dragged): Shift+Alt held while dragging a point inserts a
duplicate of that point immediately before it in `Points`, creating a
new `MultiPointThumb`+`DragListener` for it mid-drag (decision 12) so
the newly-inserted point becomes the one actively dragging; Shift+Ctrl+
click on a point-thumb deletes that point, removing its thumb and
re-indexing every thumb after it. Both are reference behaviors on the
already-created shape's point-thumbs, not part of `DrawPolyLineExtension`'s
own initial-draw gesture.

**5. Draw a Path** (`DrawPathExtension`): creates the item with an empty
`PathFigure`/`PathGeometry`, `Path.Data` set to the geometry directly
(no `CollectionElements` attach for the figure — matches reference's own
commented-out line, the figure lives only inside `Path.Data`'s object
graph, not as a separately-tracked `DesignItem`); captures the pointer
on `designPanel` (decision 4, same reasoning as Polyline — no existing
drag handler to re-enter during initial drawing) then drives its own
`DrawPathMouseGesture`: each click appends a new `LineSegment` (reference
always draws straight-line segments while drawing — Bezier/Arc/etc. only
ever appear via `PathHandlerExtension`'s post-creation convert-segment
context menu, not during initial drawing), mouse-move live-updates the
last segment's `Point` as a preview, double-click removes the preview
segment and commits, releases the panel's pointer capture, then
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
  re-indexes the remaining thumbs; Shift+Alt while dragging a point
  inserts a new point immediately before it, mid-drag, and hands the
  drag to the newly-inserted point's own new thumb (decision 12);
  `PathHandlerExtension`'s convert-segment context menu
  (`ToLineSegment`/`ToBezierSegment`/`ToQuadricBezierSegment`/
  `ToArcSegment`/`insertPoint`, reached via `PathThumb`'s own
  `ContextFlyout`) produces the expected segment type at the expected
  index, with the reference's own incompleteness (decision 5) preserved
  and covered by a test asserting it stays a no-op, not silently
  regressed into "does nothing differently than before" by accident.
- **Not unit-testable, checked by eye instead** (matching 4b's Known gap
  #2 precedent): the real coordinate/data-extraction code paths
  (`PointerRoutedEventArgs.GetCurrentPoint(...)`, `DragEventArgs.
  GetPosition(...)`/`.DataView`) that feed the `Handle*`-extracted
  methods (decision 9) — WinRT event-args types have no public
  constructor, so no unit test can fabricate one to drive this code
  path end-to-end. The `Handle*` methods themselves are fully unit
  tested; only the thin wrapper that extracts primitives from a *real*
  event and calls into them is not. Verify this thin layer by running
  the actual built `.exe` and drawing/dropping by hand at least once
  per gesture (Line, Polyline, Path, plain-control create, toolbox
  drop) before considering the phase done — this project has been
  burned by exactly this class of extraction bug before (`DesignPanel.
  HitTest`'s `TransformToVisual(null)` finding, `DesignPanel.cs:148-152`).
- Whole-branch review (per subagent-driven-development) explicitly
  checks: does the full user gesture connect end-to-end (toolbox
  selection → cursor change → draw → commit → point-edit), not just
  each class in isolation; is `OverrideExtensions` (decision 7)
  consistent across all three point-edit extensions; does every
  `PlacementOperation`/`ChangeGroup` opened by a `Draw*`/`*HandlerExtension`
  method have a matching `Commit()` or `Abort()` on every exit path
  (the exact defect class 4c's final review caught three times).

## Known gaps — flagged, not guessed

1. **`Shape.RenderedGeometry` absence — the single biggest open risk in
   this phase, not a resolved decision.** See "WinUI API surface" and
   decision 10. Tentatively substituted with the raw logical point; the
   implementation plan's first task must empirically verify this against
   a real `Line`/`Polygon`/`Polyline` in the actual WinUI runtime before
   anything else in the plan builds on `PointTrackerPlacementSupport`.
   This affects every selected Line/Polygon/Polyline in the whole
   design surface, not just shapes this phase's own tools draw.
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
   confirmed present in the reference's `IToolService`/`IDesignPanel`,
   with real plumbing (`DesignPanel.cs:145-146,231`, `ToolService.cs:39-41`
   in the reference) — but grepping specifically for a real *assignment*
   of a callback (`DesignPanelHitTestFilterCallback =`) across the whole
   reference repository finds zero results: the plumbing exists but
   nothing in the reference ever actually uses it. Not needed by
   anything this phase builds. Not ported — if a future phase finds an
   actual need for it, it can be added then with a real consumer driving
   its design, rather than speculatively now.
5. **`ZoomControl`'s missing zoom-scaling division** — see decision 11.
   Not a new gap; already accepted by 4a/4b for the sibling
   rotate/move-drag handlers this phase's point-edit handlers sit next
   to.
6. **Real coordinate/data-extraction code is not unit-testable** — see
   Testing/DoD's "Not unit-testable, checked by eye instead" entry.
   WinRT event-args types (`PointerRoutedEventArgs`, `DragEventArgs`)
   have no public constructor; the `Handle*`-extraction pattern
   (decision 9) tests everything downstream of extraction, not the
   extraction call itself. Mitigated by required manual verification via
   the built `.exe`, not by a unit test.

## Out of scope for Phase 4d

Arrow-key point nudging (`IKeyDown`/`IKeyUp`, confirmed unported —
matches 4a's own explicit "arrow-key nudge" deferral, not a new gap this
phase introduces), a real toolbox UI and real drag-drop origination
(`DragDrop.DoDragDrop`/`StartDragAsync` — Phase 5 shell app), the four
`OverrideExtensions` types not yet ported (`SelectedElementRectangleExtension`,
`CanvasPositionExtension`, `QuickOperationMenuExtension`,
`RenderTransformOriginExtension` — 4e, see decision 7),
`DesignPanelHitTestFilterCallback`/`CustomHitTestFilterBehavior` (known
gap 4 — real plumbing exists in the reference, but nothing ever assigns
it a real callback), completing `PathHandlerExtension`'s
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
