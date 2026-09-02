# Phase 4b: Container placement, snaplines, drag-move — WinUIDesigner.Designer

## Context

Phase 4a delivered resize/rotate/skew thumbs on the primary selection
(single item, generic container only) and merged to `main` (commit
`862e85a`). Per the five-sub-phase decomposition its own spec recorded
(`docs/superpowers/specs/2026-09-01-winui-designer-phase4a-design.md`,
lines 18-25), 4b is next: **container-specific placement (Canvas/Grid/
StackPanel), snaplines, and drag-move (including cross-container
reparenting)**.

Reference source, same two reference projects as prior phases:

- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\` — abstract
  core (`PlacementOperation.cs`'s `ChangeContainer`, `Extensions\
  LogicalExtensionServers.cs`, `SelectionExtensionServers.cs`'s
  `PrimarySelectionParentExtensionServer`).
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\` —
  concrete WPF-specific pieces (`Extensions\{CanvasPlacementSupport,
  StackPanelPlacementSupport,GridPlacementSupport,GridAdornerProvider,
  RasterPlacementBehavior,SnaplinePlacementBehavior,PanelMove,
  TopLeftContainerDragHandle,CanvasPositionExtension}.cs`, `Controls\
  {GridAdorner,PanelMoveAdorner,GrayOutDesignerExceptActiveArea}.cs`,
  `Services\{MoveLogic,DragMoveMouseGesture,ClickOrDragMouseGesture,
  MouseGestureBase}.cs`).

## Revision history

Brainstormed 2026-09-02. First scope question: keep 4b as one bucket
(container placement + snaplines + drag-move together) or split it —
answered **keep as one bucket** (snaplines hook both resize and move
lifecycle callbacks, so splitting them apart from drag-move would create
a cross-spec dependency rather than removing one).

The first design pass (file list + task order) was then reviewed by two
independent adversarial subagents in parallel, each instructed to verify
every claim against the real reference source, the real repo, and the
real WinUI3 SDK rather than trust the design's prose — the same method
Phase 4a's own two review rounds used. Both converged independently on
the same six Critical defects:

1. **Inheritance chain was backwards.** The design had `Snapline`
   scheduled last, subclassing `DefaultPlacementBehavior` directly. The
   real chain is `CanvasPlacementSupport`/`GridPlacementSupport :
   SnaplinePlacementBehavior : RasterPlacementBehavior :
   DefaultPlacementBehavior` (`SnaplinePlacementBehavior.cs:33`,
   `RasterPlacementBehavior.cs:29`, `CanvasPlacementSupport.cs:31`,
   `GridPlacementSupport.cs:32` — `StackPanelPlacementSupport.cs:35`
   really is direct off `DefaultPlacementBehavior`, that part of the
   design was right). `RasterPlacementBehavior.cs` was missing from the
   file list entirely, and the task order needed reversing: Raster and
   Snapline are base classes for Canvas/Grid, not later additions.
2. **`LogicalOrExtensionServer<A,B>` is not ported.** The design claimed
   Phase 2 already ported it; zero hits confirmed across `src/` and
   `tests/`. It is a small standalone reference file
   (`WpfDesign/Project/Extensions/LogicalExtensionServers.cs`).
   (`PrimarySelectionParentExtensionServer`'s deferral-to-Phase-4 claim
   *was* correct — Phase 3's spec, line 321.)
3. **`RowDefinition.Offset`/`ColumnDefinition.Offset` don't exist in
   WinUI** — load-bearing throughout `GridPlacementSupport.cs` (6 call
   sites) and `GridAdornerProvider.cs`/`GridAdorner.cs`. Confirmed by
   compiler probe below, not just SDK metadata.
4. **`Controls/GridAdorner.cs` (672 lines) was missing from the file
   list.** It draws the row/column splitter rails via `OnRender`/
   `DrawingContext`/`FormattedText`/`CombinedGeometry` — WPF immediate-
   mode rendering with no WinUI equivalent (confirmed by compiler probe:
   `CombinedGeometry`/`GeometryCombineMode` don't exist). This needs a
   redesign, not a port.
5. **"No reparent helper exists" was false, but not the whole story.**
   Phase 4a already shipped `DefaultPlacementBehavior.LeaveContainer`/
   `EnterContainer` (`src/WinUIDesigner.Designer/Extensions/
   DefaultPlacementBehavior.cs:61-70,96-108`), which do exactly this —
   remove a `DesignItem` from one parent's collection property, add it
   to another's. Verified directly (see "Also verified against the
   current codebase" below): those two methods are **dead code in 4a**
   — nothing calls them, since `PlacementOperation.ChangeContainer`
   (their only caller) wasn't ported. So the primitive exists but has
   never been exercised in sequence. Task ordering below reflects the
   narrower real question.
6. **`TopLeftContainerDragHandle`/`PanelMove` don't reuse `DragListener`/
   `DesignerThumb`** the way the design claimed. The reference builds
   them on `ContainerDragHandle : Control` + `DragMoveMouseGesture`, a
   whole unlisted stack (`MouseGestureBase`, `ClickOrDragMouseGesture`,
   `DragMoveMouseGesture`, `MoveLogic` — verified directly, see below).
   One reviewer additionally found concrete gaps in reusing `DragListener`
   as-is: no `PointerCaptureLost` handler (an OS-initiated capture loss
   mid-drag would leave `IsDown` stuck `true` forever, stranding a
   `ChangeGroup` mid-reparent), and a coordinate-space mismatch between
   `DragListener` (window-relative) and `DesignPanel.HitTest`
   (panel-relative).

A follow-up question — given `GridAdornerProvider`/`GridAdorner.cs` is
far larger than first estimated, cut it to its own sub-phase or keep it
in 4b — was answered **keep it in 4b**: `GridPlacementSupport` alone
(placing an item into an existing cell) is a crippled result without a
way to create/resize/remove the rows and columns to place it into.

All six Critical findings and the follow-up scope call are folded into
this revision. Findings 3-6 above were then independently re-verified
here (not just trusted from the reviewers) via the same compiler-probe
method Phase 4a's round 2 used: a throwaway file added to
`src/WinUIDesigner.Designer/`, `dotnet build`, the exact diagnostics
read, the probe deleted — see "WinUI API surface" below.

## WinUI API surface — verified, not assumed

Checked directly against the actual C# compiler (Windows App SDK
1.7.250606001, same environment Phase 4a verified against), by adding
`src/WinUIDesigner.Designer/__Probe4b.cs`, running `dotnet build`,
reading the exact diagnostics, then deleting the probe. Compiler
verification is strictly stronger evidence than reading SDK XML docs or
IL metadata (both of which the two review subagents used and which
independently agreed with every result below).

**Confirmed absent — compiler-verified (`CS1061`/`CS0103`/`CS0246`):**
- `Rect.Inflate(double, double)` — `Windows.Foundation.Rect` has no such
  member.
- `Brushes` (the static `SolidColorBrush`-shortcut class) — doesn't
  exist in `Microsoft.UI.Xaml.Media`. Every reference use (e.g.
  `Brushes.Red`) becomes `new SolidColorBrush(Colors.Red)`.
- `UIElement.TranslatePoint(Point, UIElement)` — no such member. Same
  replacement Phase 3/4a already established:
  `element.TransformToVisual(target).TransformPoint(point)`.
- `RowDefinition.Offset` / `ColumnDefinition.Offset` — no such members.
  `GridPlacementSupport`/`GridAdornerProvider`'s row/column math must be
  reconstructed as a running sum of `RowDefinition.ActualHeight`/
  `ColumnDefinition.ActualWidth` (confirmed present, see below) instead
  of reading a precomputed offset.
- `CombinedGeometry` / `GeometryCombineMode` — neither type exists in
  `Microsoft.UI.Xaml.Media`. `GrayOutDesignerExceptActiveArea`'s
  "gray out everything except this rect" effect (built on
  `GeometryCombineMode.Exclude`) needs a different WinUI-native
  construction — see Known gap #2.

**Confirmed present — compiler-verified:**
- `UIElement.PointerCaptureLost` event — compiles clean. `DragListener`
  (4a) currently has no handler for it (see below); adding one is a
  small, mechanical fix, not a research risk.
- `PathGeometry.FillRule = FillRule.EvenOdd` — compiles clean. A
  candidate WinUI-native replacement for `GrayOutDesignerExceptActiveArea`'s
  `CombinedGeometry`/`Exclude` effect: one `PathGeometry` containing both
  the full-cover rectangle and the "hole" rectangle as separate figures,
  even-odd-filled, renders as cover-with-a-hole without needing geometry
  boolean ops. Visual correctness is still a Known gap — the API exists,
  the resulting rendering hasn't been eyeballed.
- `Microsoft.UI.Dispatching.DispatcherQueuePriority` — exists (different
  namespace/type than WPF's `System.Windows.Threading.DispatcherPriority`,
  but a `.Normal` member is available for the one reference call site
  that needs a priority argument).
- `RowDefinition.ActualHeight` / `ColumnDefinition.ActualWidth`, read
  through a live `Grid`'s `RowDefinitions[i]`/`ColumnDefinitions[i]` —
  compiles clean, confirming the running-sum reconstruction above is
  buildable.

## Also verified against the current codebase

- **`DefaultPlacementBehavior.LeaveContainer`/`EnterContainer` already
  exist (Phase 4a) but are dead code.** `LeaveContainer`
  (`DefaultPlacementBehavior.cs:61-70`) removes each placed item from its
  `ParentProperty`'s collection (or resets a non-collection parent
  property); `EnterContainer` (`:96-108`) adds it to the new container's
  `ContentProperty` collection (or sets it directly for a non-collection
  content property). Both are part of `IPlacementBehavior` and were
  ported as part of 4a's interface implementation, but their only two
  reference callers — `PlacementOperation.ChangeContainer` and
  `TryStartInsertNewComponents` — were explicitly *not* ported in 4a
  (its spec, "Decisions already made"). No 4a test exercises them in
  sequence. 4b's `ChangeContainer` is therefore the first real caller;
  Known gap #1 below is about whether the sequence behaves correctly
  when actually wired up and driven through `WinUIDesigner.XamlDom`, not
  about writing new reparenting code.
- **`DragListener` (`src/WinUIDesigner.Designer/Controls/DragListener.cs`)
  already handles Escape-cancel and pointer release** (`OnKeyDown`,
  `OnPointerReleased` → `EndAt`/`ReleaseCapture`), but has no
  `PointerCaptureLost` handler. If the OS revokes capture without a
  matching `PointerReleased` (e.g. a system interruption), `IsDown` stays
  `true` forever and no `Completed` event ever fires — for a plain
  resize/rotate/skew this strands a `ChangeGroup`; for a move gesture
  that has already called `ChangeContainer` mid-drag, it strands a
  partially-reparented item too. Fix: subscribe `PointerCaptureLost` the
  same way `PointerReleased` is subscribed, and call `Cancel()` when it
  fires for the captured pointer — mechanically identical to the
  existing `OnPointerReleased` guard, confirmed compilable above.
- **`LogicalOrExtensionServer<A,B>` — zero hits** across `src/` and
  `tests/` (both `grep`- and reviewer-confirmed). Its reference file
  (`WpfDesign/Project/Extensions/LogicalExtensionServers.cs`) is small
  and self-contained (an `ExtensionServer` that returns true if either
  of two other `ExtensionServer` types would).
- **`ExtensionForAttribute` already has both `OverrideExtension`
  (singular) and `OverrideExtensions` (`Type[]`)**
  (`src/WinUIDesigner.DesignModel/Extensions/ExtensionForAttribute.cs:43,55`),
  and `ExtensionManager` already honors it when building each type's
  `ExtensionEntry` (`ExtensionManager.cs:211`). This is exactly the
  mechanism the reference's `CanvasPlacementSupport`/`GridPlacementSupport`
  need (`[ExtensionFor(typeof(Canvas), OverrideExtension =
  typeof(DefaultPlacementBehavior))]`, confirmed present at
  `CanvasPlacementSupport.cs:30`) and exactly the forward-flag 4a's own
  spec raised at its line 462-469 ("4b's spec needs
  `ExtensionForAttribute.OverrideExtensions` or equivalent") — already
  built, nothing to add.
- **`DesignItemProperty.GetAttachedProperty` is `(Type ownerType, string
  name)`-keyed** (`src/WinUIDesigner.DesignModel/DesignItemProperty.cs:89`),
  same string-keyed convention 4a established for regular properties.
  Every reference call like
  `info.Item.Properties.GetAttachedProperty(Canvas.LeftProperty)`
  becomes `info.Item.Properties.GetAttachedProperty(typeof(Canvas),
  "Left")`.
- **`DefaultExtensionServer.Permanent` exists**
  (`src/WinUIDesigner.DesignModel/Extensions/DefaultExtension.cs:131`);
  its sibling `PermanentWithDesignPanel` (the reference's base for
  `PanelMove`, needed so the move-affordance extension skips applying
  itself when no `IDesignPanel` is registered — e.g. in a bare unit-test
  fixture) does not. The reference implementation is two lines
  (`DefaultExtension.cs:141-147` in the reference project — `return
  Services.GetService(typeof(IDesignPanel)) != null;`) and mirrors
  `Permanent` almost exactly — a small, low-risk addition, not a design
  question.
- **`UIElement.ReadLocalValue`/`DependencyProperty.UnsetValue`** — both
  present (reviewer-confirmed against SDK metadata), needed by
  `CanvasPlacementSupport`'s "is Canvas.Left explicitly set, or should we
  fall back to computing from Canvas.Right" check.

## Decisions already made

- **Scope: single selected item only, matching Phase 3/4a precedent.**
  The reference's `MoveLogic`/`ChangeContainer` machinery is built for
  multi-selection (`selectedItems` plural, sorted via
  `ModelTools.ComparePositionInModelFile`) — not ported; 4b's move logic
  operates on exactly one `DesignItem`, so no sort helper is needed.
  `MoveLogic.HandleDoubleClick`'s "double-click creates an event handler"
  behavior (`IEventHandlerService`, not built anywhere in this port) is
  dropped entirely, not stubbed.
- **Cross-container reparenting (`ChangeContainer`) is in scope.** Port
  narrowed to what 4b's move gesture actually needs: guard
  `CanLeaveContainer`, call `LeaveContainer` on the old container's
  behavior, reassign `CurrentContainer`/`CurrentContainerBehavior`, call
  `EnterContainer` on the new one, `Abort()` the whole
  `PlacementOperation` if anything throws along the way (matches the
  reference's own `ChangeContainer` control flow, verified by reading
  it in full). The coordinate-transform part of `ChangeContainer` uses
  `TransformToVisual(...).TransformPoint(...)` (already the established
  replacement, used three other places in this port); its private
  `TransformRectByMiddlePoint` helper — the part that actually needs a
  `Vector`/`Rect.TopLeft`/`Rect.Size` substitute — becomes plain
  component-wise `double` arithmetic on a new `Rect`.
- **Undo: do NOT copy Phase 4a's Rotate/Skew `OpenGroup` fix verbatim.**
  That fix was needed because `ModelTools.ApplyTransform` opens its own
  `ChangeGroup` per call with nothing bracketing the whole gesture.
  `PlacementOperation.Start` (already ported, 4a) already opens exactly
  one `ChangeGroup` for the entire operation up front — resize already
  gets one-entry-per-gesture undo for free, and move will too as long as
  the whole gesture (including any `ChangeContainer` calls) stays inside
  that single operation. The real task is verifying `ChangeContainer`'s
  own exception/abort path can't leave `UndoService`'s transaction stack
  in a state that trips its LIFO nesting assertion — a test, not a new
  `OpenGroup` wrapper.
- **`GridAdornerProvider`/`GridAdorner.cs` stay in scope (explicit user
  call, see Revision history), redesigned rather than ported.** The
  splitter-rail visuals move from `OnRender`/`DrawingContext`/
  `FormattedText` to XAML `Shapes`/`Path` elements arranged via the same
  `AdornerPanel`/`AdornerPlacement` mechanism 4a's thumbs already use —
  the rails become normal WinUI visual-tree children like every other
  adorner in this port, not a custom-painted control. Exact shape
  composition is a plan-time detail; Known gap #2 covers the remaining
  visual-correctness question for the gray-out effect specifically.
- **`TopLeftContainerDragHandle`/`PanelMove` are rebuilt on the existing
  `DragListener` primitive (extended with `PointerCaptureLost` handling
  — see above), not on a ported `MouseGestureBase`/`ClickOrDragMouseGesture`/
  `DragMoveMouseGesture` stack.** This matches 4a's own precedent
  (`DragListener` was already a from-scratch WinUI replacement for the
  reference's `Thumb`-based dragging, not a port of anything WPF-mouse-
  specific). `TopLeftContainerDragHandle` keeps the reference's actual
  visual idea (a small corner handle, `RelativePlacement`-positioned,
  `OnlyOneItemSelectedExtensionServer`-gated) but drives it through
  `DragListener` like every 4a thumb does. `PanelMove` keeps its
  reference role (make a container's own body draggable without needing
  to hit a corner handle) but the same way.
- **`CanvasPositionExtension.cs` (margin-handle visual guides from a
  Canvas-child's edges to the Canvas's edges) stays excluded from 4b,
  assigned to 4e** (Phase 4a spec's own decomposition list, "remaining
  small behaviors... margin handle") — confirmed no
  `CanvasPlacementSupport` code path references it.
- **`PlacementOperation.PasteOffset` and every `PlacementType.PasteItem`
  branch are not ported** — no clipboard/paste feature exists anywhere
  in this port yet; the reference's `CanvasPlacementSupport.EnterContainer`
  has one such branch, dropped rather than stubbed.
- **`ModelTools.ComparePositionInModelFile` stays unported** (4a already
  excluded it as multi-selection-only; 4b's single-item scope confirms
  it still has no caller).

## Architecture

```
D:\Project\ST4I\Research\WinUIDesigner\
  src\
    WinUIDesigner.DesignModel\
      PlacementOperation.cs               (modify: add ChangeContainer,
                                             using TransformToVisual-based
                                             coordinate math, not Vector/
                                             Rect.TopLeft/Rect.Size)
      Extensions\
        SelectionExtensionServers.cs      (modify: add
                                             PrimarySelectionParentExtensionServer)
        LogicalExtensionServers.cs        (new: LogicalOrExtensionServer<A,B>)
        DefaultExtension.cs               (modify: add
                                             DefaultExtensionServer.PermanentWithDesignPanel)
    WinUIDesigner.Designer\
      Extensions\
        RasterPlacementBehavior.cs        (new: DefaultPlacementBehavior base
                                             for snap-to-grid; needs
                                             DesignPanel.UseRasterPlacement/
                                             RasterWidth)
        SnaplinePlacementBehavior.cs      (new: RasterPlacementBehavior base;
                                             BeginPlacement/EndPlacement hooks
                                             fire for resize AND move; needs
                                             DesignPanel.UseSnaplinePlacement,
                                             OptionService.SnaplinePlacementRoundValues)
        CanvasPlacementSupport.cs         (new: SnaplinePlacementBehavior base,
                                             [ExtensionFor(typeof(Canvas),
                                             OverrideExtension=typeof(DefaultPlacementBehavior))])
        StackPanelPlacementSupport.cs     (new: DefaultPlacementBehavior base
                                             directly — no Snapline chain;
                                             needs ResizeThumbExtension.IsResizing,
                                             a public flag 4a's ResizeThumbExtension
                                             doesn't currently expose)
        GridPlacementSupport.cs           (new: SnaplinePlacementBehavior base;
                                             Grid.Row/Column/RowSpan/ColumnSpan-aware;
                                             Offset reconstructed from
                                             ActualHeight/ActualWidth running sums)
        GridAdornerProvider.cs            (new: row/column splitter rails on a
                                             selected Grid;
                                             [ExtensionServer(typeof(LogicalOrExtensionServer<
                                             PrimarySelectionExtensionServer,
                                             PrimarySelectionParentExtensionServer>))])
        TopLeftContainerDragHandle.cs     (new: corner move handle, every
                                             FrameworkElement, built on
                                             DragListener/RelativePlacement/
                                             OnlyOneItemSelectedExtensionServer
                                             — same pattern as 4a's thumbs)
        PanelMove.cs                      (new: click-container-body-to-move,
                                             [ExtensionServer(typeof(DefaultExtensionServer.
                                             PermanentWithDesignPanel))],
                                             targets Panel/Border/ContentControl/Viewbox)
        ResizeThumbExtension.cs           (modify: expose public IsResizing,
                                             read by StackPanelPlacementSupport
                                             to suppress its own drag feedback
                                             while a resize is in progress)
      Controls\
        DragListener.cs                   (modify: add PointerCaptureLost
                                             handling — Cancel() on capture
                                             loss, mirrors the existing
                                             PointerReleased guard)
        GridAdorner.cs                    (new, redesigned: XAML Shapes/Path
                                             rail visuals via AdornerPanel/
                                             AdornerPlacement, not OnRender/
                                             DrawingContext/FormattedText)
        GrayOutDesignerExceptActiveArea.cs (new, redesigned: PathGeometry +
                                             FillRule.EvenOdd cover-with-a-hole,
                                             not CombinedGeometry/
                                             GeometryCombineMode.Exclude)
        ContainerDragHandle.cs            (new: small square visual for
                                             TopLeftContainerDragHandle,
                                             same "code-only, no generic.xaml"
                                             idiom as Controls/Thumbs)
        MoveLogic.cs                      (new, narrowed to single item: per-tick
                                             hit-test-for-container-switch +
                                             PlacementInformation.Bounds update,
                                             shared by TopLeftContainerDragHandle
                                             and PanelMove — DragListener-driven,
                                             not the reference's MouseGestureBase)
  tests\
    WinUIDesigner.Designer.Tests\
      DragListenerTests.cs                (modify, Phase 4a file: add a
                                             PointerCaptureLost-mid-drag
                                             test — simulate capture loss,
                                             assert Cancel() fired)
      CanvasPlacementSupportTests.cs      (new)
      StackPanelPlacementSupportTests.cs  (new)
      GridPlacementSupportTests.cs        (new)
      GridAdornerProviderTests.cs         (new)
      SnaplinePlacementBehaviorTests.cs   (new)
      MoveTests.cs                        (new: TopLeftContainerDragHandle +
                                             PanelMove + cross-container
                                             ChangeContainer)
      Phase4bDefinitionOfDoneTests.cs     (new)
  docs\superpowers\specs\
    2026-09-02-winui-designer-phase4b-design.md   (this file)
```

## Mechanism table (WPF → WinUI replacements)

| WPF mechanism | WinUI replacement |
| --- | --- |
| `RowDefinition.Offset`/`ColumnDefinition.Offset` | Not present (compiler-confirmed) — reconstruct as a running sum of `RowDefinition.ActualHeight`/`ColumnDefinition.ActualWidth` across the Grid's definitions. |
| `Rect.Inflate(dx, dy)` | Not present (compiler-confirmed) — construct a new `Rect` with manually expanded bounds. |
| `Brushes.X` (static brush shortcuts) | Not present (compiler-confirmed) — `new SolidColorBrush(Colors.X)`. |
| `UIElement.TranslatePoint(Point, UIElement)` | Not present (compiler-confirmed) — `element.TransformToVisual(target).TransformPoint(point)`, same replacement Phase 3/4a already use. |
| `CombinedGeometry`/`GeometryCombineMode.Exclude` (`GrayOutDesignerExceptActiveArea`) | Not present (compiler-confirmed) — a `PathGeometry` with `FillRule.EvenOdd` containing both the cover rectangle and the hole rectangle as separate figures. Visual correctness is Known gap #2. |
| `Control.OnRender(DrawingContext)` + `FormattedText` (`GridAdorner`) | No immediate-mode rendering override with this shape in WinUI — rails become ordinary XAML `Shapes`/`Path` visual-tree children, positioned the same `AdornerPanel`/`AdornerPlacement` way every other adorner in this port already is. |
| `Vector`, `Rect.TopLeft`, `Rect.Size`, `GeneralTransform.Transform(Point)` (`PlacementOperation.ChangeContainer`'s `TransformRectByMiddlePoint`) | Plain component-wise `double` arithmetic on a new `Rect`; the main coordinate transform already uses the `TransformToVisual(...).TransformPoint(...)` pattern this port established in Phase 3. |
| `Properties.GetAttachedProperty(Canvas.LeftProperty)` (DP-indexed) | `Properties.GetAttachedProperty(typeof(Canvas), "Left")` (`Type, string`-indexed) — same string-keyed convention 4a established for `Properties.GetProperty`. |
| `Keyboard.IsKeyDown(Key.LeftCtrl)` (Raster/Snapline override) | Not read ambiently — `PlacePoint`/`BeforeSetPosition` gain a modifier parameter threaded from the originating pointer event's `KeyModifiers`, same choice 4a made for resize's aspect-lock and rotate's 15°-snap-disable. |
| `MouseGestureBase`/`ClickOrDragMouseGesture`/`DragMoveMouseGesture` (`TopLeftContainerDragHandle`/`PanelMove`) | The existing `DragListener` primitive (4a), extended with a `PointerCaptureLost` handler. |
| `System.Windows.Threading.DispatcherPriority` (`GridAdornerProvider`) | `Microsoft.UI.Dispatching.DispatcherQueuePriority` — different type, `.Normal` member confirmed present. |

## Data flow

**Container-specific resize/place.** Unchanged from 4a's data flow up
through `PlacementOperation.Start` resolving
`DesignItem.GetBehavior<IPlacementBehavior>()` — the difference is which
behavior resolves. Inside a `Canvas`, `[ExtensionFor(typeof(Canvas),
OverrideExtension=typeof(DefaultPlacementBehavior))]` makes
`CanvasPlacementSupport` win over the generic `DefaultPlacementBehavior`
4a registered for `Panel` (already-supported mechanism, see "Also
verified"); its `GetPosition`/`SetPosition` read/write `Canvas.Left/Top`
(or `Right`/`Bottom`, whichever was already set) instead of `Width`/
`Height`-only resize. Inside a `Grid`, `GridPlacementSupport` similarly
overrides, reading/writing `Grid.Row`/`Column`/`RowSpan`/`ColumnSpan`.
Both chain through `SnaplinePlacementBehavior`'s `BeginPlacement`/
`EndPlacement` (creates/tears down a snap-line overlay surface for the
duration of the operation) and `RasterPlacementBehavior`'s `PlacePoint`
(rounds a proposed point to the nearest raster cell when raster snapping
is enabled) — both fire for resize operations exactly the same as move,
since they're `PlacementOperation`-lifecycle hooks, not move-specific.

**Drag-move.** Pointer press on `TopLeftContainerDragHandle`'s corner
handle (or anywhere inside a container's own body, via `PanelMove`) →
`DragListener` (same capture/threshold/Escape-cancel machinery 4a's
thumbs use, now also handling `PointerCaptureLost`) → `Started` fires
`MoveLogic.Start`, which resolves the item's `IPlacementBehavior` and
calls `PlacementOperation.Start(item, PlacementType.Move)` (opens the
gesture's single `ChangeGroup`) → each `PointerMoved` fires
`MoveLogic.Move`: first checks `CurrentContainerBehavior.CanLeaveContainer`
and, if the pointer has moved over an unselected element via
`DesignPanel.HitTest` (Phase 3, unchanged) whose own `IPlacementBehavior.
CanEnterContainer` allows it, calls `operation.ChangeContainer(newParent)`
— which calls the old behavior's `LeaveContainer`, reassigns
`CurrentContainer`/`CurrentContainerBehavior`, calls the new behavior's
`EnterContainer` (first real exercise of these two 4a-shipped methods,
Known gap #1) — then updates `PlacementInformation.Bounds` from the
drag delta and calls `CurrentContainerBehavior.SetPosition` → pointer
release (or `PointerCaptureLost`) commits or aborts the single
`ChangeGroup` `PlacementOperation.Start` opened at the beginning —
whichever container the item ended up in, however many times it
switched mid-drag, the whole gesture is one undo-stack entry.

## Testing / Definition of Done

Same `.exe` NUnitLite runner and live-`Window`/real-`DesignSurface`
fixture pattern Phase 3/4a established (`Services.DesignPanel` only
resolves off a real `DesignSurface`, not a bare `XamlDesignContext`).

Phase 4b is done when:

- A test resizes a selected item inside a `Canvas` and asserts
  `Canvas.Left`/`Top` (not `Width`/`Height` alone) update, and that the
  `Right`/`Bottom` fallback path is exercised when only those are set.
- A test resizes a selected item inside a `StackPanel` and asserts the
  cross-axis dimension is constrained the way the reference constrains
  it (exact assertion is a plan-time detail, driven by reading
  `StackPanelPlacementSupport.cs` in full at plan time).
- A test places/resizes a selected item inside a `Grid` and asserts
  `Grid.Row`/`Column`/`RowSpan`/`ColumnSpan` update correctly, including
  at least one case that exercises the `Offset`-replacement running-sum
  math against a `Grid` with unequal row/column sizes.
- A test drags a `GridAdornerProvider` row or column splitter rail and
  asserts the corresponding `RowDefinition`/`ColumnDefinition`'s
  `Height`/`Width` updates, with a single undo reverting the whole drag.
- A test drags an item via `TopLeftContainerDragHandle` from one
  container into a different one (e.g. `Canvas` into `Grid`) and asserts
  both the `DesignItem`'s new parent and its `IPlacementBehavior`-specific
  position properties (`Canvas.Left/Top` or `Grid.Row/Column`) are
  correct on the new side, and that a single undo reverts both the
  reparent and the position change together.
- A test drags an item close to another item's edge with snaplines
  enabled and asserts the dragged item's position snaps to align with
  it; a corresponding raster-only test (snaplines off, raster on)
  asserts rounding to the raster cell instead.
- A test starts a drag-move, then simulates `PointerCaptureLost` (not a
  real `PointerReleased`) mid-gesture, and asserts the operation aborts
  cleanly — item stays in its original container at its original
  position, no `ChangeGroup` committed.
- Each of the five flagged gaps below has an explicit, tested resolution.

## Known gaps — flagged, not guessed

1. **Whether `LeaveContainer`→`EnterContainer` (4a-shipped, never yet
   exercised in sequence) correctly reparents a `DesignItem` when driven
   through a real `ChangeContainer` call, with both the old and new
   parent live in the same `WinUIDesigner.XamlDom` document.** The
   methods exist and read correctly by inspection, but nothing has ever
   called them one after another. **Must be verified empirically as the
   plan's first task**, same precedent as Phase 3's live-Window
   investigation and 4a's `DragListener`-bug-hunting task.
2. **Visual correctness of the `PathGeometry`/`FillRule.EvenOdd`
   replacement for `GrayOutDesignerExceptActiveArea`'s cover-with-a-hole
   effect.** The API compiles (confirmed above); whether the rendered
   result actually looks like "everything gray except this rectangle" —
   correct z-order against the design surface's content, no seam
   artifacts at the hole's edge — hasn't been visually checked. Fallback
   if it doesn't: four separate opaque rectangles covering the four
   strips around the active area instead of one hole-punched shape.
3. **Timing of `RowDefinition.ActualHeight`/`ColumnDefinition.ActualWidth`
   during a live drag.** These are post-layout values; the reference's
   `.Offset` was presumably kept in sync by WPF's own layout system on a
   similar cadence. Whether reading `ActualHeight`/`ActualWidth` inside
   a `PointerMoved` handler (mid-drag, potentially between layout passes)
   returns stale values needs empirical confirmation — same class of
   concern as Phase 3's live-`Window` layout-timing investigation.
4. **Exact rail/handle visual composition for `GridAdorner`'s XAML
   redesign** — shapes, thickness, hit-test width, hover/drag visual
   feedback. A plan-time design choice once gap #1's reparenting
   question and gap #2's gray-out approach are settled, not resolved
   here.
5. **Ctrl-modifier threading into `RasterPlacementBehavior`/
   `SnaplinePlacementBehavior`'s `PlacePoint`/`BeforeSetPosition`.** Both
   methods' current (4a-inherited) signatures carry no modifier
   parameter; extending them to accept one (sourced from the
   originating pointer event's `KeyModifiers`, same mechanism 4a already
   uses elsewhere) is a signature change whose exact shape is a
   plan-time detail, not a research risk.

## Out of scope for Phase 4b

Multi-item move (`ModelTools.ComparePositionInModelFile` and
`MoveLogic`'s multi-selection sort, not ported — single item only, same
scope narrowing as 4a), double-click-to-create-event-handler
(`IEventHandlerService` doesn't exist in this port), paste/clipboard
(`PlacementOperation.PasteOffset` and every `PlacementType.PasteItem`
branch, not ported), `CanvasPositionExtension.cs` (margin-handle visual
guides — 4e), rubber-band multi-select (`SelectionFrame`'s real purpose,
already out of scope per Phase 3), `TryStartInsertNewComponents`
(toolbox-drop insertion — no caller anywhere in this port yet), context
menus (4c), drawing tools (4d), and the remaining Phase 4 sub-phase
behaviors (border decorations, size display, render-transform-origin,
in-place editor, tab-item-click, quick-operation menu — 4e).
