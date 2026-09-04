# Phase 4d: Drawing tools & point-editing infra — WinUIDesigner.Designer

## Revision history

Two rounds of two independent adversarial reviews each (parallel, no
shared context between the two reviewers within a round, mirroring
4a/4b's own two-round precedent) were run against this spec — round 2
specifically re-verified round 1's fixes rather than re-covering the
same ground.

### Round 1

Findings and fixes, against the first draft:

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
- User reviewed round 1's findings and chose to fix this spec in place
  rather than split it into two phases, despite the reference source
  totaling ~2897 lines across the 12 files this phase ports (this is
  still a large phase — the implementation plan should expect
  proportionally more tasks than 4a/4b/4c, not fewer).

### Round 2

Round 1's fixes introduced their own gaps and left some findings only
partially closed. Findings and fixes, against the round-1-fixed spec:

- **Critical, fixed**: round 1 added `IDesignPanel.CapturePointer`/
  `ReleasePointerCapture` and had `Draw*MouseGesture` call
  `designPanel.CapturePointer(pointer)` from inside `StartDrawItem` —
  but round 1 *also* decomposed `StartDrawItem`'s signature (decision 2)
  to drop the `Pointer` parameter for testability, leaving no way to
  actually obtain a `Pointer` object at that call site. A real gap
  created by one fix not being checked against another. Fixed: capture
  (and focus) now happens once, in `CreateComponentTool`'s own
  pointer-pressed handling — the one call site with a live `Pointer` —
  *before* branching into either `StartDrawItem` or
  `CreateComponentMouseGesture`; neither needs a `Pointer` of its own
  (see decision 2's "Where pointer capture for a draw gesture actually
  happens", decision 4's "Capture responsibility").
- **Critical, fixed**: Escape-to-cancel had no plumbing at all — no
  `KeyDown` anywhere on `IDesignPanel`, no mention of Escape for any of
  the 4 new gesture classes, despite the reference's `MouseGestureBase.
  OnKeyDown` making it a safety net every reference gesture inherits for
  free. Fixed: `IDesignPanel` gains `KeyDown`/`Focus(FocusState)`;
  every new gesture class calls `Focus` alongside `CapturePointer` at
  start (WinUI routes keyboard by focus, not capture) and treats Escape
  the same as a failed/cancelled gesture (see decision 1's
  "Escape-to-cancel").
- **Critical, fixed**: `CreateComponentMouseGesture` never addressed
  `PointerCaptureLost` (the OS revoking capture mid-gesture) — a real
  contradiction with this spec's own DoD requirement that every
  `PlacementOperation`/`ChangeGroup` have a matching `Commit()` or
  `Abort()` on every exit path. Fixed: `PointerCaptureLost` now runs the
  same abort path `KeyDown`-Escape does, for all 4 gesture classes (see
  decision 1's "`PointerCaptureLost` is not optional").
- **Critical, fixed**: `DrawPolylineMouseGesture`/`DrawPathMouseGesture`
  need double-click to end a draw gesture, but the reference's mechanism
  (a Win32 `GetDoubleClickTime()` P/Invoke inside `MouseGestureBase`) was
  never addressed — no WinUI equivalent was even considered. Fixed:
  `UIElement.DoubleTapped` (verified present via probe build) is a
  native WinUI gesture-recognizer event that replaces the whole
  mechanism outright, added to `IDesignPanel` and used directly (see
  decision 1, mechanism table).
- **Important, fixed**: decision 10's own investigation-task example (a
  `Stretch=None` `Line` from `(50,50)` to `(10,10)`) would almost
  certainly pass without testing anything, since `Stretch=None` is
  expected to already reduce `RenderedGeometry.Transform` to identity —
  exactly the case this phase's own tools always produce, not the
  `Stretch != None`/loaded-from-XAML case "Known gaps" #1 actually warns
  about. Fixed: the task now explicitly requires a `Stretch != None`
  case with an explicit `Width`/`Height` mismatched to its points'
  natural bounds.
- **Important, fixed**: decision 12's "removing a thumb from
  `adornerPanel.Children` is enough for GC" reasoning is correct .NET,
  but silently assumed no other structure references thumbs — while this
  spec's own DoD requires testing individual thumbs by position/index
  and re-indexing after delete, which `ResizeThumbExtension`'s own
  precedent (`DragListeners` dictionary, kept specifically for
  testability) shows needs exactly such a structure. Fixed: decision 12
  now requires an internal thumb index and requires it be updated in
  lockstep with every `adornerPanel.Children` change, not as an
  afterthought.
- **Important, fixed**: the `PolyLineHandlerExtension`'s
  `resizeBehavior`-null → `changeGroup`-fallback branch was confirmed to
  exist (round 1) but never resolved whether it's a real must-implement
  path or the same "unreachable given this port's container types,
  accepted" status `ResizeThumbExtension` already carries for its
  identical branch. Fixed: decision 13 rules explicitly — gate
  point-thumb visibility by `resizeBehavior` the same way
  `ResizeThumbExtension.UpdateAdornerVisibility` already does, making the
  `changeGroup` fallback the same narrow, already-accepted case, not new
  scope.
- **Important, fixed**: decision 1's "the two capture mechanisms never
  conflict because they're never active at the same time" was
  self-contradicting decision 4 (`ExternalStart` is exactly a
  `DragListener` active while the tool isn't `PointerTool`). Corrected:
  the real reason is that `ExternalStart` never calls
  `Target.CapturePointer` in the first place (bypasses `OnPointerPressed`
  entirely), not temporal exclusivity.
- **Minor, fixed**: `LineHandlerExtension.DragListener` (reference) is a
  *singular* property whose value is whichever thumb (`TopLeft` or
  `BottomRight`) was constructed *last* in `OnInitialized` — an implicit
  ordering dependency `DrawLineExtension.ExternalStart` relies on
  entirely by accident of array-initializer order. Not previously
  documented; now flagged explicitly in decision 4 with a DoD test that
  asserts *which* endpoint moves, not just that one does.
- **Minor, fixed**: `PathThumb.ContextFlyout` was attributed to
  `FrameworkElement`; winmd metadata inspection shows it's declared on
  `UIElement` (functionally identical since `Grid : FrameworkElement :
  UIElement`, but the citation was wrong).
- **Minor, acknowledged, not changed**: decision 3's `OnlyOneItemSelectedExtensionServer`
  choice hides point-editing thumbs under multi-select, a real UX
  regression from the reference's per-item gate — round 1's wording
  understated this as "not a functionality loss"; reworded to state it
  as a deliberate, acknowledged trade-off rather than a neutral one. The
  underlying decision (accept the regression, don't build a
  `SelectionExtensionServer` port) is unchanged.

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
   `PointerCaptureLost`/`KeyDown`/`DoubleTapped`/`DragEnter`/`DragOver`/
   `DragLeave`/`Drop` events (added to `IDesignPanel`, plus `bool
   CapturePointer(Pointer)`/`void ReleasePointerCapture(Pointer)`/`bool
   Focus(FocusState)`, forwarding `DesignPanel`'s own inherited
   `UIElement.CapturePointer`/`ReleasePointerCapture`/`Focus` — verified
   present via a real probe build, 0 errors) by forwarding its own
   inherited `UIElement` events, and `PointerTool.Activate` is what
   subscribes `HandlePointerPressed` to them. `PointerMoved`/
   `PointerCaptureLost`/`KeyDown`/`DoubleTapped`/the capture/focus
   methods exist specifically because the reference's `IDesignPanel :
   IInputElement` gets `MouseMove`/`CaptureMouse()`/`LostMouseCapture`
   for free from that WPF interface, and `MouseGestureBase` additionally
   subscribes `KeyDown` (Escape-to-cancel) and hand-rolls double-click
   detection via a Win32 `GetDoubleClickTime()` P/Invoke — WinUI's
   `IDesignPanel` inherits nothing of the kind, and WinUI has no
   double-click-timing API to port, but it does have a native
   `DoubleTapped` routed event (a real built-in gesture recognizer, not
   a timing hack) that replaces that whole P/Invoke mechanism outright.
   Every one of these needs an explicit `IDesignPanel` member (a gap the
   first two review rounds progressively found — see "Revision
   history"). This is a refactor of
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
   on its own thumb). The two never conflict, but *not* because they're
   temporally exclusive (round-2 review correctly caught the first draft
   of this sentence claiming that, which is false — `DrawLineExtension`'s
   `ExternalStart`, decision 4, is exactly a case of a thumb's
   `DragListener` being `IsDown` while the active tool is *not*
   `PointerTool`). The real reason is narrower: `ExternalStart` calls
   `BeginAt` directly, bypassing `DragListener`'s own `OnPointerPressed` —
   the *only* place that method ever calls `Target.CapturePointer`. So a
   `DragListener` driven via `ExternalStart` never captures its `Target`
   at all; capture during a Line-draw gesture lives entirely on
   `designPanel`, taken by `DrawLineMouseGesture` (decision 4), never on
   the endpoint thumb. The two capture mechanisms simply never both fire
   for the same gesture, not because the tools that use them are mutually
   exclusive in time.

   **Escape-to-cancel.** The reference's `MouseGestureBase.OnKeyDown`
   (Escape → `Stop()`) and `OnLostMouseCapture` (always → `Stop()`) are
   safety nets every reference gesture class inherits for free. None of
   this phase's 4 new gesture classes (`CreateComponentMouseGesture`,
   `DrawLineMouseGesture`, `DrawPolylineMouseGesture`,
   `DrawPathMouseGesture`) inherit a shared base (decision 8 — no
   `MouseGestureBase` port), so each must wire this itself, explicitly,
   following the exact pattern `DragListener.OnKeyDown`/
   `OnPointerCaptureLost` already establish in this codebase
   (`DragListener.cs:103-120`): at gesture start, alongside
   `designPanel.CapturePointer(pointer)`, also call `designPanel.
   Focus(FocusState.Pointer)` (WinUI routes keyboard input by focus, not
   by pointer capture — a gesture that only captures the pointer will
   never see a `KeyDown`) and subscribe `designPanel.KeyDown`; on `e.Key
   == VirtualKey.Escape`, run the same cancel path `PointerCaptureLost`
   triggers (abort any open `PlacementOperation`/`ChangeGroup`, remove
   any partially-created `DesignItem` — matching reference's own
   `CreateComponentMouseGesture.OnStopped`/`Draw*MouseGesture.OnStopped`,
   which always abort on every exit path, not just a successful one).
   Unsubscribe `KeyDown`/`PointerCaptureLost`/release capture on every
   exit (success, Escape, or lost capture) — a gesture that forgets to
   unsubscribe on one path leaks a live subscription into the next
   gesture's lifetime.

   **`PointerCaptureLost` is not optional for `CreateComponentMouseGesture`
   either.** The reference's `CreateComponentMouseGesture.OnStopped`
   (`CreateComponentTool.cs:431-445`) runs on *every* exit path — success,
   Escape, or the OS revoking capture — and always aborts any open
   `operation`/`changeGroup` first. This phase's `CreateComponentMouseGesture`
   follows the same shape: `PointerCaptureLost` gets the identical cancel
   path `PointerReleased`'s failure branch and `KeyDown`'s Escape branch
   both use. Skipping this would leave a `PlacementOperation` open if the
   OS ever revokes capture mid-drag (window deactivation, a system
   interruption) — exactly the defect class this spec's own DoD requires
   checking for (every `PlacementOperation`/`ChangeGroup` has a matching
   `Commit()` or `Abort()` on every exit path).
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

   **Where pointer capture for a draw gesture actually happens.**
   Round-2 review caught a real gap here: this decomposed signature has
   no `Pointer` object, but `designPanel.CapturePointer(Pointer)` needs
   one, and `Draw*MouseGesture` (decision 4) needs to capture. Rather
   than smuggling a `Pointer` through `IDrawItemExtension`'s interface
   (which would re-couple it to a live WinRT event object, undoing the
   whole point of decomposing the signature), the fix mirrors the
   reference's own separation: `DrawLineExtension.cs:76`'s
   `DrawLineMouseGesture` constructor never receives the mouse event
   either — capture happens one level up, in `MouseGestureBase.Start
   (panel, e)`, called *after* construction. This phase's
   `CreateComponentTool.OnMouseDown`-equivalent — the one real call site
   that has the live `PointerRoutedEventArgs`/`e.Pointer` — captures the
   pointer and takes focus on `designPanel` itself (`designPanel.
   CapturePointer(e.Pointer)`, `designPanel.Focus(FocusState.Pointer)`)
   *before* calling `StartDrawItem`, exactly the same way decision 8's
   `CreateComponentMouseGesture` already does for the no-`IDrawItemExtension`
   path. By the time `StartDrawItem`/`Draw*MouseGesture` run, capture is
   already in place — neither needs a `Pointer` object of its own, only
   `designPanel`'s already-forwarded `PointerMoved`/`PointerReleased`/
   `PointerCaptureLost`/`KeyDown` events to track the rest of the
   gesture.
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
   sit alongside (confirmed via `SelectionExtensionServers.cs:89-92`:
   `ShouldApplyExtensions` requires `PrimarySelection == extendedItem &&
   SelectionCount < 2`). **This is a deliberate, acknowledged UX
   regression from the reference, not a neutral choice**: selecting two
   Lines at once hides point-editing thumbs on *both* (matching
   `ResizeThumbExtension`'s already-accepted behavior for
   bounding-box-resize thumbs since 4a) — where the reference's
   per-item `SelectionExtensionServer` would keep each Line's own
   endpoint thumbs live independently of what else is selected.
   Point-editing is more plausibly a per-item operation a user expects
   to still work under multi-select than bounding-box resize is, so this
   deviation carries more real cost than the `ResizeThumbExtension`
   precedent it's borrowed from. Accepted anyway because building a real
   `SelectionExtensionServer` port is out-of-scope net-new work this
   phase's stated goals don't require, not because the UX cost is zero.
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
   its `Draw*Extension.StartDrawItem`. Capture itself was **already**
   taken by `CreateComponentTool`'s pointer-pressed handling *before*
   `StartDrawItem` was even called (decision 2's "Where pointer capture
   for a draw gesture actually happens" — the same `designPanel.
   CapturePointer(e.Pointer)`/`designPanel.Focus(FocusState.Pointer)`
   call decision 8's `CreateComponentMouseGesture` already makes for the
   no-draw-tool path), so none of these three gesture classes needs a
   `Pointer` object of its own; each only subscribes `designPanel.
   PointerMoved`/`PointerReleased`/`PointerCaptureLost`/`KeyDown` for the
   gesture's duration, translating each move into `lineHandler.
   DragListener.ExternalMove(point, modifiers)` (Line) or driving its own
   dedicated logic directly (Polyline/Path, which don't reuse an existing
   `DragListener` the way Line does — see Data flow #4/#5), and
   releasing capture + unsubscribing on `PointerReleased`/
   `PointerCaptureLost`/`KeyDown`-Escape/`DoubleTapped`-commit (Escape
   and `PointerCaptureLost` both required — see decision 1's
   "Escape-to-cancel" and "`PointerCaptureLost` is not optional" — and
   both must abort any `PlacementOperation`/`ChangeGroup` this gesture
   opened, matching `Draw*MouseGesture.OnStopped` in the reference,
   which aborts unconditionally on every exit path). Because capture is
   on the *panel*, not the tiny endpoint thumb, this works regardless of
   where over the canvas the pointer actually is — matching the
   reference's own choice to capture on `DesignPanel` inside
   `MouseGestureBase.Start`, not on whatever `positionRelativeTo` happens
   to be.

   **Endpoint identity depends on thumb-creation order — port this
   ordering, don't just port the method calls.** The reference's
   `LineHandlerExtension.DragListener` is a *singular* property
   (`LineHandlerExtension.cs:45`), reassigned by every call to
   `CreateThumb` — its final value is whichever thumb was constructed
   *last*. `OnInitialized` (`LineHandlerExtension.cs:229-233`) creates
   `TopLeft` (`X1`/`Y1`) first, then `BottomRight` (`X2`/`Y2`) second, so
   `DragListener` ends up pointing at the `BottomRight`/`X2`,`Y2` thumb —
   purely as a side effect of array-initializer order, not because of
   any name or type distinguishing the two. `DrawLineExtension.cs:74`
   depends on exactly this: `ExternalStart` on `lineHandler.DragListener`
   must land on `X2`/`Y2` (the endpoint that should track the mouse while
   drawing), never `X1`/`Y1` (the fixed start point). If this phase's
   `LineHandlerExtension`-equivalent's `OnInitialized` creates its two
   thumbs in the opposite order, `DrawLineExtension`-equivalent silently
   drags the wrong endpoint — no compiler error, no test failure unless
   a test specifically asserts which endpoint moved. Port the exact
   creation order (`TopLeft` first, `BottomRight` second), not just the
   two `CreateThumb` calls.
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
   `IDesignPanel.PointerPressed`/`PointerMoved`/`PointerReleased`/
   `PointerCaptureLost`/`KeyDown` events and `CapturePointer`/
   `ReleasePointerCapture`/`Focus` (all added in decision 1) — see
   decision 1's "Escape-to-cancel" and "`PointerCaptureLost` is not
   optional" for the cancel path every exit from this gesture must run,
   mirroring the reference's `ClickOrDragMouseGesture`
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
    WinUI runtime. **Round-2 review caught a real flaw in this task's
    original test case**: a `Line` from `(50,50)` to `(10,10)` with
    `Stretch=None` (the only kind this phase's own draw tools ever
    create — decision 5) almost certainly proves nothing, because WPF's
    `RenderedGeometry.Transform` is expected to reduce to identity
    exactly when `Stretch=None`, by the same reasoning that motivated the
    substitution in the first place — a test built entirely from
    `Stretch=None` shapes is circular, not a real check. The actual risk
    (per "Known gaps" #1) lives in Line/Polygon/Polyline **already
    present in a loaded XAML document**, which may carry any `Stretch`
    value and explicit `Width`/`Height` this phase's own tools never
    produce. The investigation task must therefore include **at least
    one shape with an explicit non-`None` `Stretch` and explicit
    `Width`/`Height` different from its points' natural bounds** (e.g. a
    `Polyline` with `Stretch="Fill"` and a `Width`/`Height` larger than
    the span of its `Points`), not only a freshly-drawn `Stretch=None`
    shape, or the investigation will pass without having tested the
    thing it exists to test. If the raw-point substitute turns out wrong
    for the `Stretch != None` case, every later task in this phase that
    places a point-thumb depends on the fix — exactly the same
    "investigate the biggest empirical risk first" shape as Phase 3's
    Task 1 and Phase 4a's Task 1.
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
    detach method, *provided nothing else keeps its own index of
    thumbs*.** `PolyLineHandlerExtension`'s Shift+Alt (insert a point
    mid-drag, creating a new thumb+`DragListener` while another thumb's
    drag is still in progress) and Shift+Ctrl+click (delete a point,
    removing its thumb) both create/destroy `MultiPointThumb` instances
    at runtime — unlike `ResizeThumbExtension`'s 8 fixed thumbs created
    once in `OnInitialized` and never removed. Removing a thumb from
    `adornerPanel.Children` alone is sufficient to make it (and its
    `DragListener`) eligible for GC (`.NET`'s tracing collector reclaims
    the `Target`↔`DragListener` reference cycle once nothing external
    roots either side — confirmed correct, this is not a reference-count
    scheme) **only if `adornerPanel.Children` is the only place a thumb
    is referenced from.**

    **This phase's own DoD requires an index, so state explicitly that
    one exists and must stay in sync.** Testing "dragging each
    point-thumb" and "deleting a point... re-indexes the remaining
    thumbs" (this spec's own Testing/DoD section) is not practical
    against `adornerPanel.Children` alone — `ResizeThumbExtension`
    already established the precedent this phase follows for exactly
    this reason: `internal readonly Dictionary<PlacementAlignment,
    DragListener> DragListeners` (`ResizeThumbExtension.cs:40-41`),
    kept specifically so a test can drive a *named* thumb's drag
    directly. `PolyLineHandlerExtension`/`PathHandlerExtension`-equivalents
    need the same for the same reason — an index (e.g. `internal
    readonly List<MultiPointThumb> PointThumbs`, ordered to match
    `Points`/`Segments`) that a test can address by position, and that
    the re-indexing logic (Shift+Ctrl-delete decrementing every later
    thumb's `Index`) reads and writes. **This index must be updated in
    lockstep with every `adornerPanel.Children.Add`/`.Remove` call** —
    inserting a thumb (`OnInitialized`'s per-point loop, or Shift+Alt's
    mid-drag insert) adds to both; removing one (Shift+Ctrl+click)
    removes from both. An implementation that updates `adornerPanel.
    Children` but forgets the index leaves a stale reference alive in
    the index, defeating the GC reasoning above and leaving the index
    out of sync with the real thumb set — exactly the kind of bug this
    spec's own DoD ("re-indexes the remaining thumbs") is written to
    catch, so implement and test the index update as one atomic step
    with the visual-tree update, never as two separate ones.
13. **`resizeBehavior`-null fallback for Polyline/Polygon's point-drag
    commit: same "unreachable in this port's container types, accepted
    gap" status as `ResizeThumbExtension`'s, not a must-implement new
    path.** The reference's `PolyLineHandlerExtension.SetOperation`
    (`PolyLineHandlerExtension.cs:142-154`) branches: `operation =
    PlacementOperation.Start(...)` when `resizeBehavior != null`, else
    `changeGroup = ExtendedItem.Context.OpenGroup(...)` as a fallback.
    `PathHandlerExtension` has no such branch — it always calls
    `PlacementOperation.Start` (`PathHandlerExtension.cs:384-395`),
    matching what this spec's Data flow #6 already says. Round-2 review
    correctly flagged that round-1's fix only confirmed the branch
    exists without saying whether this port's `PolyLineHandlerExtension`-
    equivalent gates point-thumb *visibility* on `resizeBehavior` the
    way `LineExtensionBase`'s `UpdateAdornerVisibility`
    (`LineExtensionBase.cs`, ported into `PointEditExtensionBase`) and
    `ResizeThumbExtension.UpdateAdornerVisibility`
    (`ResizeThumbExtension.cs:170-211`) both already do for their own
    resize thumbs — which determines whether the `changeGroup` fallback
    is a real, reachable, must-implement path or the same
    "currently-unreachable-given-this-port's-container-types, accepted"
    status `ResizeThumbExtension` already carries for its own identical
    branch. **Ruling: yes, gate it the same way.** `PointEditExtensionBase`
    ports `UpdateAdornerVisibility` faithfully (it's inherited from
    `LineExtensionBase` in the reference, used by all three handler
    extensions) — a point-thumb is hidden via `ThumbVisible = false`
    exactly when `resizeBehavior.CanPlace(..., PlacementType.MovePoint,
    ...)` (or the container's real placement-check) refuses it, the same
    condition that gates `ResizeThumbExtension`'s corner thumbs. Given
    every `IPlacementBehavior` currently in this port (`CanvasPlacementSupport`,
    `GridPlacementSupport`, `StackPanelPlacementSupport`,
    `DefaultPlacementBehavior`) is attached to its *container*, not to
    the dragged item, `resizeBehavior` (looked up via `PlacementOperation.
    GetPlacementBehavior` on the Line/Polygon/Polyline/Path's *parent*)
    is null only when the shape sits in a container with no placement
    behavior at all — the same narrow, already-accepted "low priority,
    effectively unreachable for now" case `ResizeThumbExtension` already
    documents for its own identical fallback. The `changeGroup` fallback
    is ported for reference-fidelity (so a future container type that
    genuinely lacks a placement behavior still works) but is not this
    phase's responsibility to make reachable or to test as a primary
    path — consistent with, not a new decision beyond, the precedent
    `ResizeThumbExtension` already set.

## Architecture

```
WinUIDesigner.DesignModel/
  Tools.cs                          — ITool/IToolService/IDesignPanel (existing interfaces,
                                       extended: + PointerPressed/PointerMoved/PointerReleased/
                                       PointerCaptureLost/KeyDown/DoubleTapped/DragEnter/
                                       DragOver/DragLeave/Drop events + CapturePointer/
                                       ReleasePointerCapture/Focus on IDesignPanel — decision 1,
                                       verified present on UIElement via probe build)
  Extensions/
    DrawItemExtension.cs            — new: IDrawItemExtension (CanItemBeDrawn/StartDrawItem,
                                       decomposed-primitive signature, no Pointer param —
                                       decision 2)

WinUIDesigner.Designer/
  Services/
    ToolService.cs                  — new: ToolService : IToolService (constructor activates
                                       PointerTool immediately — decision 1)
    PointerTool.cs                  — new: PointerTool : ITool (extracted click-to-select)
    CreateComponentTool.cs          — new: CreateComponentTool : ITool
                                       + CreateComponentMouseGesture (captures pointer +
                                       takes focus on IDesignPanel at gesture start, before
                                       calling IDrawItemExtension.StartDrawItem when a draw
                                       tool claims the click — decision 2/4/8; handles
                                       Escape via KeyDown and OS-revoked capture via
                                       PointerCaptureLost on every exit path — decision 1)
  Controls/
    DragListener.cs                 — extended: + ExternalStart/ExternalMove/ExternalStop
                                       (decision 4 — these do not themselves capture; capture
                                       already happened in CreateComponentTool before
                                       StartDrawItem ran — decision 2)
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
                                       (declared on `UIElement`, confirmed via winmd metadata
                                       inspection — every `DesignerThumb` already has it via
                                       `Grid : FrameworkElement : UIElement`) — distinct from
                                       and unrelated to 4c's per-`DesignItem` composite
                                       `ContextFlyout` merge system in `DesignPanel`, since a
                                       `PathPoint` is not itself a `DesignItem`
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
                                       + DrawLineMouseGesture (decision 4 — no Pointer/capture
                                       of its own, capture already done by CreateComponentTool;
                                       drives DragListener.ExternalMove/ExternalStop from
                                       IDesignPanel.PointerMoved/PointerReleased, aborts via
                                       KeyDown-Escape/PointerCaptureLost on every exit)
    DrawPolyLineExtension.cs        — new: [ExtensionFor(Canvas)] [ExtensionFor(Grid)] : IDrawItemExtension
                                       + DrawPolylineMouseGesture (own click/move state
                                       machine — does not reuse DragListener, see Data flow #4;
                                       ends via DoubleTapped, not a hand-rolled double-click
                                       timer; aborts via KeyDown-Escape/PointerCaptureLost)
    DrawPathExtension.cs            — new: [ExtensionFor(Canvas)] [ExtensionFor(Grid)] : IDrawItemExtension
                                       + DrawPathMouseGesture (own click/move state
                                       machine — does not reuse DragListener, see Data flow #5;
                                       ends via DoubleTapped; aborts via KeyDown-Escape/
                                       PointerCaptureLost)
  DesignPanel.cs                    — modified: drop the constructor's direct
                                       PointerPressed += OnPointerPressed; expose the new
                                       IDesignPanel Pointer*/Drag*/Capture*/Focus/KeyDown/
                                       DoubleTapped members instead (forwarding the panel's
                                       own inherited UIElement members)
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
| `MouseGestureBase.OnKeyDown` (Escape → `Stop()`) | `IDesignPanel.KeyDown`, checked for `VirtualKey.Escape` by each of the 4 new gesture classes directly — same pattern `DragListener.OnKeyDown` (`DragListener.cs:114-120`) already establishes in this codebase. Requires `designPanel.Focus(FocusState.Pointer)` at gesture start (WinUI routes keyboard by focus, not by pointer capture) — see decision 1's "Escape-to-cancel" |
| `MouseButtonHelper`'s `GetDoubleClickTime()` P/Invoke (manual double-click detection) | `UIElement.DoubleTapped` — a native WinUI gesture-recognizer routed event, verified present via probe build; replaces the whole P/Invoke mechanism, not just a 1:1 API swap |

## Data flow

**1. Click-drag create a normal control** (`CreateComponentTool`, no
`IDrawItemExtension` match): tool activated (`Services.Tool.CurrentTool
= new CreateComponentTool(typeof(Button))`) → `PointerPressed` on
`DesignPanel` → hit-test via `IDesignPanel.HitTest(point, testAdorners:
false, testDesignSurface: true, HitTestType.Default)` (matching
reference's own `CreateComponentTool.cs:332` call exactly — adorners
left over from a prior selection must not intercept a draw-tool click)
→ on a hit, `designPanel.CapturePointer(e.Pointer)` and `designPanel.
Focus(FocusState.Pointer)` happen **once, here**, before branching into
either `IDrawItemExtension.StartDrawItem` (data flow #3/#4/#5) or the
no-match path below — the single call site with the real `Pointer`
object (decision 2's "Where pointer capture for a draw gesture actually
happens") → no `IDrawItemExtension` on the hit item claims the type →
fall through to `CreateComponentMouseGesture`: `PointerPressed` starts
it, `PointerMoved` past the drag threshold calls
`PlacementOperation.TryStartInsertNewComponents(container, [item],
[Rect], PlacementType.Resize)` and updates `Bounds` on every further
move, `PointerReleased` either commits the resize operation (if a drag
happened) or — for a bare click with no drag — falls back to
`CreateComponentTool.AddItemsWithDefaultSize` (this exact static helper
already exists conceptually in the reference and is ported as a static
helper here too, using `ModelTools.GetDefaultSize` and
`PlacementOperation.TryStartInsertNewComponents(..., PlacementType.AddItem)`).
`KeyDown`-Escape and `PointerCaptureLost` both run the same cancel path
(abort any open `PlacementOperation`, matching reference's
`CreateComponentMouseGesture.OnStopped` — decision 1's "`PointerCaptureLost`
is not optional"). `designPanel.ReleasePointerCapture(...)` releases the
capture taken at gesture start on every exit; tool resets to
`PointerTool` after any path completes.

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
(same `testAdorners: false` call as data flow #1, same capture+focus
call already taken before this branch — decision 2), finds
`DrawLineExtension.CanItemBeDrawn(typeof(Line))` true → `StartDrawItem`
creates the `Line` DesignItem (`Stroke=Black`, `StrokeThickness=2`,
`Stretch=None`), inserts it via `PlacementOperation.
TryStartInsertNewComponents(..., PlacementType.AddItem)` with a
NaN-sized `Rect` at the click point (matching reference), then looks up
the just-attached `LineHandlerExtension` (auto-attached because
`[ExtensionFor(typeof(Line))]` fires the moment the item is added, its
`X1`/`Y1`-then-`X2`/`Y2` thumb creation order load-bearing — decision
4's "Endpoint identity depends on thumb-creation order"), constructs a
`DrawLineMouseGesture(lineHandler, changeGroup)` (no `Pointer`/capture
parameter — capture already happened one level up) and calls
`lineHandler.DragListener.ExternalStart(startPoint, modifiers)` — from
this point on, `designPanel.PointerMoved` drives `DragListener.
ExternalMove(point, modifiers)` and `designPanel.PointerReleased`/
`PointerCaptureLost`/`KeyDown`-Escape all drive `DragListener.
ExternalStop(point)` (Escape and lost-capture both abort rather than
commit — matching `Draw*MouseGesture.OnStopped` in the reference),
re-entering `LineHandlerExtension`'s *own* already-built endpoint-resize
`drag_Changed`/`drag_Completed` handlers exactly as if the user had
grabbed the endpoint thumb directly, ending the draw gesture by
committing a normal resize and releasing the panel's pointer capture.

**4. Draw a Polyline/Polygon** (`DrawPolyLineExtension`): creates the
item with one point (capture/focus already taken by `CreateComponentTool`
before this branch — decision 2; this gesture does not reuse
`DragListener`/`ExternalStart` the way Line's does, since there's no
existing per-point drag handler to re-enter yet during initial drawing),
then drives its own dedicated `DrawPolylineMouseGesture`: each click
commits a new vertex to `Points`, mouse-move live-updates a "preview"
trailing point (added then replaced on the next move, never left
committed), Alt snaps the in-progress segment to horizontal/vertical,
`designPanel.DoubleTapped` removes the preview point and commits the
final `Points` value via `DesignItem.Properties[PointsProperty].
SetValue(...)` (`DoubleTapped` replaces the reference's hand-rolled
`GetDoubleClickTime()` detection outright — see mechanism table),
`KeyDown`-Escape/`PointerCaptureLost` both abort instead (removing the
in-progress `DesignItem`, matching `Draw*MouseGesture.OnStopped`),
releases the panel's pointer capture on every exit, then
`ReapplyAllExtensions()` on a successful commit (matching reference) so
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
graph, not as a separately-tracked `DesignItem`; capture/focus already
taken by `CreateComponentTool` before this branch — decision 2, same
reasoning as Polyline — no existing drag handler to re-enter during
initial drawing) then drives its own `DrawPathMouseGesture`: each click
appends a new `LineSegment` (reference always draws straight-line
segments while drawing — Bezier/Arc/etc. only ever appear via
`PathHandlerExtension`'s post-creation convert-segment context menu, not
during initial drawing), mouse-move live-updates the last segment's
`Point` as a preview, `designPanel.DoubleTapped` removes the preview
segment and commits, `KeyDown`-Escape/`PointerCaptureLost` both abort
instead (removing the in-progress `DesignItem`), releases the panel's
pointer capture on every exit, then `ReapplyAllExtensions()` on a
successful commit so `PathHandlerExtension` attaches a `PathThumb` per
anchor/control point.

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
  Undo/redo: each successful create is exactly one undo entry. **Escape
  mid-drag aborts the create and adds nothing** (`KeyDown` with
  `VirtualKey.Escape`, matching reference's `MouseGestureBase.OnKeyDown`);
  **`PointerCaptureLost` mid-drag also aborts and adds nothing**
  (simulating the OS revoking capture — matching reference's
  `CreateComponentMouseGesture.OnStopped`, which runs unconditionally on
  every exit path); both leave no dangling `ChangeGroup`/
  `PlacementOperation` and reset `CurrentTool` back to `PointerTool`.
- `DrawLineExtension`: drawing a line end-to-end produces a `Line` with
  the expected `X1/Y1/X2/Y2` and default `Stroke`/`StrokeThickness`/
  `Stretch`; the whole gesture (create + drag endpoint) is one undo
  entry, not two (same class of bug fixed in 4c's final review for
  `RightClickContextMenuExtension` — verify explicitly here rather than
  assume it's fine); Escape/`PointerCaptureLost` mid-draw abort and leave
  no `Line` in the tree (not a half-drawn one, not an undo entry).
- `DrawPolyLineExtension`: multi-click sequence produces the expected
  `Points`; `DoubleTapped` ends the gesture and leaves no preview point
  in the committed value; Alt-snap produces an axis-aligned segment when
  held; Escape/`PointerCaptureLost` mid-sequence abort and leave no
  `Polyline`/`Polygon` in the tree.
- `DrawPathExtension`: multi-click sequence produces the expected
  `PathFigure.Segments` (all `LineSegment` from drawing, per decision 5);
  `DoubleTapped` ends the gesture cleanly; Escape/`PointerCaptureLost`
  mid-sequence abort and leave no `Path` in the tree.
- `LineHandlerExtension`/`PolyLineHandlerExtension`/`PathHandlerExtension`:
  dragging each point-thumb updates the correct point/segment and
  commits as one undo entry; drawing then dragging a `Line`'s *start*
  point moves `X1/Y1` and dragging its *end* point moves `X2/Y2` —
  explicitly asserting which endpoint moved, not just that "a" point
  moved, to catch a thumb-creation-order swap (decision 4's "Endpoint
  identity depends on thumb-creation order") that would otherwise pass
  silently; deleting a Polygon/Polyline point via Shift+Ctrl+click
  (matching reference) removes exactly that point, re-indexes the
  remaining thumbs, **and** removes the corresponding entry from
  whatever internal thumb-index this port maintains (decision 12) — a
  test asserting the index and `adornerPanel.Children` agree in count
  and order after the delete, not just that the visual thumb is gone;
  Shift+Alt while dragging a point inserts a new point immediately
  before it, mid-drag, and hands the drag to the newly-inserted point's
  own new thumb, updating the same index (decision 12); a
  Line/Polygon/Polyline/Path in a container with no `IPlacementBehavior`
  hides all its point-thumbs (`ThumbVisible = false`, decision 13),
  matching `ResizeThumbExtension`'s own already-tested behavior for the
  same condition;
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
