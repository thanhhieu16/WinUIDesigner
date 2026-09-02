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

All six Critical findings and the follow-up scope call were folded into
a rewritten spec (this document's first committed version, `2e536a5`).
Findings 3-6 above were independently re-verified there via the same
compiler-probe method Phase 4a's round 2 used.

**Round 2** sent that committed spec — the document itself, not a prose
summary — to one more expert reviewer, instructed to re-verify every
compiler claim and file:line citation independently and, critically, to
read the reference files the round-1 reviewers had only read *about*
secondhand (`StackPanelPlacementSupport.cs`, `GridPlacementSupport.cs`,
`RasterPlacementBehavior.cs`, `SnaplinePlacementBehavior.cs`,
`GridAdornerProvider.cs` — all in full). Every API/citation claim from
round 1 held up under independent re-verification. Round 2 found three
further Critical defects this document's first version still had:

7. **The StackPanel DoD bullet described behavior the reference doesn't
   have.** `StackPanelPlacementSupport.cs` doesn't constrain a resize —
   it computes an insertion index from child centers and **reorders the
   item inside the parent's `ContentProperty` collection** as it's
   dragged past siblings, plus draws a 2px insertion-point indicator.
   Corrected below (Architecture, Data flow, DoD).
8. **Three load-bearing files/members were missing entirely**:
   `DesignPanel.RasterWidth`/`UseRasterPlacement`,
   `DesignPanel.UseSnaplinePlacement`, and a **new `OptionService`**
   (doesn't exist anywhere in this port) for
   `OptionService.SnaplinePlacementRoundValues`; separately, Known gap
   #5's modifier-parameter change touches the shared
   `PlacementBehavior.cs` interface (`PlacePoint`/`BeforeSetPosition`),
   also unlisted. Added to Architecture below.
9. **`GridAdornerProvider`/`GridAdorner.cs` is an interaction system, not
   a rendering one — the first revision treated it as visuals-only.**
   Reading the reference in full: clicking a rail **creates** a new row
   or column (`RegisterComponentForDesigner(new RowDefinition())`,
   collection insert) and rewrites every child's `Grid.Row`/`RowSpan`/
   `Column`/`ColumnSpan`/margins to keep positions stable across the
   split. Dragging an existing splitter is a **second, independent drag
   controller** (its own capture/`ChangeGroup`/Escape-cancel life cycle,
   separate from `DragListener`). A third file, **`GridUnitSelector`**
   (176 lines, a small picker UI for a row/column's size-unit — `Auto`/
   `*`/pixel), is a dependency of the rail visuals and was entirely
   unlisted. `GrayOutDesignerExceptActiveArea`'s animated transition
   (`AnimateActiveAreaRectTo`, called on every `GridPlacementSupport.
   SetPosition`) also depends on WPF's `Animatable`/`RectAnimation`/
   `Geometry.BeginAnimation` — all three confirmed absent, on top of the
   `CombinedGeometry` gap round 1 already found. Folded into Architecture,
   the mechanism table, and Known gaps below.

Given finding 9, the human was asked again — now with the corrected
picture (row/column creation + a second full drag controller + a
dependent picker UI, not a visuals-only redesign) — whether to keep this
inside 4b or split it out. Answered **keep it in 4b**, a second time,
with the larger scope now disclosed.

Round 2 also found five Important gaps (all folded in below):
`DispatcherPriority.Loaded` (used by `GridAdornerProvider` specifically
*because* its delay guarantees a new row/column's layout has settled)
has no WinUI equivalent — `DispatcherQueuePriority.Normal` is not a
substitute, and the mechanism table's first-revision claim that this was
a settled type-swap was wrong; `Visibility.Hidden` is absent (used by
both `StackPanelPlacementSupport` and `GridAdorner`) and was missing
from the mechanism table; `GridPlacementSupport`'s real scope also
includes `HorizontalAlignment`/`VerticalAlignment`/`Margin`/`Width`/
`Height` resets and deletes `RowDefinition`/`ColumnDefinition`
`DesignItem`s on `LeaveContainer`, understated as "Row/Column/RowSpan/
ColumnSpan-aware"; and several more absent WPF APIs specific to the
`GridAdorner` stack (`InvalidateVisual`, `System.Windows.Input.Cursors`/
`CursorProperty.OverrideMetadata` — same already-established
`InputSystemCursorShape` replacement pattern as 4a, `SolidColorBrush.
Freeze`, `DoubleCollection(IEnumerable)`, and a `TextBox.
GetRectFromCharacterIndex` signature change) not previously called out.

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
- `Animatable` / `Geometry.BeginAnimation` / `RectAnimation` / the 3-arg
  `DoubleAnimation(fromValue, toValue, duration)` constructor — round-2
  finding, all absent. `GrayOutDesignerExceptActiveArea.
  AnimateActiveAreaRectTo` (called on every `GridPlacementSupport.
  SetPosition`, not just Canvas) uses WPF's imperative
  `Freezable.BeginAnimation(DependencyProperty, AnimationTimeline)`
  model, which WinUI doesn't have at all — WinUI's animation model is
  the declarative `Storyboard`/`Microsoft.UI.Xaml.Media.Animation.
  DoubleAnimation` (a different API shape, not a drop-in rename). See
  Known gap #2.
- `Visibility.Hidden` — round-2 finding, absent (`CS0117`; WinUI's
  `Visibility` enum has only `Visible`/`Collapsed`, same fact 4a's own
  spec already established for `ResizeThumbExtension`). Used by
  `StackPanelPlacementSupport.cs:100` and `GridAdorner.cs:81,258` — same
  `Visibility.Collapsed` replacement 4a already adopted, but not
  previously listed for these two files.
- `System.Windows.Threading.DispatcherPriority.Loaded` — round-2
  finding, absent (`CS0117`; `Microsoft.UI.Dispatching.
  DispatcherQueuePriority` has no `Loaded` member, only `Low`/`Normal`/
  `High`). `GridAdornerProvider.cs` picks `Loaded` specifically because
  the delay guarantees a newly-created row/column's layout has actually
  settled before the code reads it — `.Normal` is not a substitute for
  that guarantee, only for the type name. Corrects this document's
  first-revision mechanism-table entry, which wrongly treated this as a
  settled type swap. See Known gap #3.
- `InvalidateVisual`, `SolidColorBrush.Freeze()`, `DoubleCollection`'s
  `IEnumerable<double>` constructor, `TextBox.
  GetRectFromCharacterIndex(int)` (single-arg) — round-2 findings, all
  absent from the `GridAdorner`/`GridUnitSelector` stack specifically.
  `GetRectFromCharacterIndex` does exist but requires a second `bool`
  argument in this SDK version.

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
- `Microsoft.UI.Dispatching.DispatcherQueuePriority` — the type exists
  (different namespace than WPF's `System.Windows.Threading.
  DispatcherPriority`) with `Low`/`Normal`/`High` members, but **not**
  a `Loaded` member — see the "confirmed absent" list above and Known
  gap #3. `.Normal` compiles as a priority argument but does not carry
  the reference's "after layout has settled" guarantee, so it isn't a
  like-for-like substitute at the one call site that needs `Loaded`.
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
  spec raised at its lines 465-470 ("4b's spec needs
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
- **`DesignPanel.RasterWidth`/`UseRasterPlacement`/`UseSnaplinePlacement`
  don't exist yet (round-2 finding, grep-confirmed zero hits)** — all
  three are new properties this phase adds to
  `src/WinUIDesigner.Designer/DesignPanel.cs`, not existing surface to
  read. **`OptionService` doesn't exist anywhere in this port at all**
  (grep-confirmed) — `SnaplinePlacementRoundValues` needs a new small
  service (`Services/OptionService.cs`), registered the same way other
  Phase 2/3 services are.
- **`PlacementBehavior.cs`'s `IPlacementBehavior` interface currently
  declares `PlacePoint(Point)`/`BeforeSetPosition(PlacementOperation)`
  with no modifier parameter** (round-2 finding) — Known gap #5's
  Ctrl-modifier threading is therefore an interface change, not just an
  implementation detail inside Raster/Snapline, since every other
  `IPlacementBehavior` implementor (`DefaultPlacementBehavior`,
  `CanvasPlacementSupport`, etc.) must update its override signature to
  match.

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
- **`GridAdornerProvider`/`GridAdorner.cs` stay in scope, as a full
  three-part system, not just visuals (explicit user call, made twice —
  see Revision history).** The three parts: (1) rail visuals plus
  click-to-create a new row/column, which also rewrites every affected
  child's `Grid.Row`/`RowSpan`/`Column`/`ColumnSpan`/margins to hold
  positions stable across the split; (2) an existing splitter's drag —
  its own capture/`ChangeGroup`/Escape-cancel life cycle, independent of
  `DragListener`, resizing an existing `RowDefinition`/`ColumnDefinition`
  rather than creating a new one; (3) `GridUnitSelector`, a small picker
  UI (`Auto`/`*`/pixel) the rail visuals depend on. All three move from
  `OnRender`/`DrawingContext`/`FormattedText` to ordinary XAML `Shapes`/
  `Path`/`Control` visual-tree children, positioned via the same
  `AdornerPanel`/`AdornerPlacement` mechanism 4a's thumbs already use —
  not custom-painted controls. Unlike 4a's thumbs, which all use the
  ready-made `RelativePlacement`, the rails need their own
  `AdornerPlacement` subclasses (mirroring the reference's
  `RowSplitterPlacement`/`ColumnSplitterPlacement`, each arranging its
  rail at a specific row/column boundary rather than a fixed corner/edge
  alignment) — confirmed workable, since this port's `AdornerPlacement`
  base (`src/WinUIDesigner.DesignModel/Adorners/AdornerPlacement.cs`) is
  `public abstract`. Exact shape/visual composition is a plan-time detail
  (Known gap #4); Known gap #2 covers the gray-out effect's visual
  correctness specifically, including its animated transition (see
  below).
- **`GrayOutDesignerExceptActiveArea`'s redesign covers both its static
  geometry (Known gap #2, `PathGeometry`/`FillRule.EvenOdd`) and its
  animated transition.** `AnimateActiveAreaRectTo` — called on every
  `GridPlacementSupport.SetPosition`, not just `CanvasPlacementSupport`'s
  — uses WPF's `Freezable.BeginAnimation`/`RectAnimation`, which has no
  WinUI equivalent (round-2 finding, confirmed absent above). Replacement
  is WinUI's declarative `Storyboard`/`Microsoft.UI.Xaml.Media.Animation.
  DoubleAnimation` driven against the `PathGeometry`'s figure points —
  a different API shape from the reference's imperative call, exact
  composition a plan-time detail alongside gap #2's visual-correctness
  question.
- **`StackPanelPlacementSupport` reorders the dragged item's position in
  its parent's `ContentProperty` collection (an insertion-index change,
  driven by comparing the drag position against sibling centers) plus
  draws a thin insertion-point indicator — it does not constrain a
  resize.** (Round-2 correction — the first revision of this document
  wrongly described it as resize-constraining.) It still bases directly
  on `DefaultPlacementBehavior` (no Snapline/Raster chain — reordering
  isn't a coordinate-snapping concern), and still needs
  `ResizeThumbExtension.IsResizing` to suppress the reorder indicator
  while an actual resize (not a move) is in progress.
- **`GridPlacementSupport`'s real scope is larger than "Row/Column/
  RowSpan/ColumnSpan-aware."** (Round-2 correction.) Its `SetPosition`
  also resets `HorizontalAlignment`/`VerticalAlignment`/all four
  `Margin` sides and clears `Width`/`Height` when the item stretches to
  fill a cell; its `LeaveContainer` deletes the `RowDefinition`/
  `ColumnDefinition` `DesignItem`s a moved-away item's row/column would
  leave behind. Both port unchanged in shape, just more surface than the
  short name suggested.
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
      PlacementBehavior.cs                (modify: PlacePoint/BeforeSetPosition
                                             gain a modifier parameter —
                                             IPlacementBehavior interface
                                             change, every implementor's
                                             override signature updates
                                             to match)
      Extensions\
        SelectionExtensionServers.cs      (modify: add
                                             PrimarySelectionParentExtensionServer)
        LogicalExtensionServers.cs        (new: LogicalOrExtensionServer<A,B>)
        DefaultExtension.cs               (modify: add
                                             DefaultExtensionServer.PermanentWithDesignPanel)
    WinUIDesigner.Designer\
      DesignPanel.cs                       (modify: add RasterWidth/
                                             UseRasterPlacement/
                                             UseSnaplinePlacement properties
                                             — none exist yet, grep-confirmed)
      Services\
        OptionService.cs                  (new: doesn't exist anywhere in
                                             this port yet;
                                             SnaplinePlacementRoundValues,
                                             registered like other Phase
                                             2/3 services)
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
                                             reorders the dragged item's index
                                             in ContentProperty's collection by
                                             comparing drag position against
                                             sibling centers, does not
                                             constrain a resize; needs
                                             ResizeThumbExtension.IsResizing,
                                             a public flag 4a's ResizeThumbExtension
                                             doesn't currently expose)
        GridPlacementSupport.cs           (new: SnaplinePlacementBehavior base;
                                             Grid.Row/Column/RowSpan/ColumnSpan
                                             plus HorizontalAlignment/
                                             VerticalAlignment/Margin/Width/
                                             Height resets on placement, and
                                             deletes now-unneeded
                                             RowDefinition/ColumnDefinition
                                             DesignItems on LeaveContainer;
                                             Offset reconstructed from
                                             ActualHeight/ActualWidth running sums)
        GridAdornerProvider.cs            (new: three-part system on a selected
                                             Grid — rail visuals + click-to-
                                             create a row/column (rewrites
                                             every affected child's Grid.Row/
                                             RowSpan/Column/ColumnSpan/margins),
                                             a second independent drag
                                             controller for resizing an
                                             existing splitter, and
                                             GridUnitSelector for picking a
                                             row/column's size unit;
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
                                             to suppress its reorder indicator
                                             while a resize is in progress)
      Controls\
        DragListener.cs                   (modify: add PointerCaptureLost
                                             handling — Cancel() on capture
                                             loss, mirrors the existing
                                             PointerReleased guard)
        GridAdorner.cs                    (new, redesigned: rail visuals +
                                             row/column-creation click handling
                                             + a second, independent
                                             splitter-drag controller, all as
                                             ordinary XAML Shapes/Path/Control
                                             visual-tree children via
                                             AdornerPanel/AdornerPlacement,
                                             not OnRender/DrawingContext/
                                             FormattedText)
        GridUnitSelector.cs               (new, redesigned: Auto/*/pixel
                                             size-unit picker UI the
                                             GridAdorner rail visuals depend
                                             on; reference is 176 lines)
        GrayOutDesignerExceptActiveArea.cs (new, redesigned: PathGeometry +
                                             FillRule.EvenOdd cover-with-a-hole
                                             geometry (not CombinedGeometry/
                                             GeometryCombineMode.Exclude) plus
                                             a Storyboard/DoubleAnimation-based
                                             transition (not Animatable.
                                             BeginAnimation/RectAnimation) for
                                             AnimateActiveAreaRectTo)
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
    WinUIDesigner.DesignModel.Tests\
      LogicalExtensionServersTests.cs     (new: LogicalOrExtensionServer<A,B>)
      SelectionExtensionServersTests.cs   (modify, Phase 3 file: add
                                             PrimarySelectionParentExtensionServer
                                             coverage)
    WinUIDesigner.Designer.Tests\
      DragListenerTests.cs                (modify, Phase 4a file: add a
                                             PointerCaptureLost-mid-drag
                                             test — simulate capture loss,
                                             assert Cancel() fired)
      CanvasPlacementSupportTests.cs      (new)
      StackPanelPlacementSupportTests.cs  (new: reorder-by-drag-index
                                             assertions, not resize-constraint)
      GridPlacementSupportTests.cs        (new)
      GridAdornerProviderTests.cs         (new: covers all three parts —
                                             row/column creation, splitter
                                             drag, GridUnitSelector)
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
| `DispatcherPriority.Loaded` (`GridAdornerProvider`, delays a read until a new row/column's layout has settled) | **No equivalent** (compiler-confirmed, `DispatcherQueuePriority` has only `Low`/`Normal`/`High`) — `.Normal` does not carry the same "after layout" guarantee. Timing risk, see Known gap #3. |
| `Visibility.Hidden` (`StackPanelPlacementSupport`, `GridAdorner`) | `Visibility.Collapsed` — same replacement 4a already established for `ResizeThumbExtension`, confirmed absent again here (compiler-verified). |
| `Freezable.BeginAnimation`/`RectAnimation` (`GrayOutDesignerExceptActiveArea.AnimateActiveAreaRectTo`) | Not present (compiler-confirmed) — WinUI's declarative `Storyboard`/`Microsoft.UI.Xaml.Media.Animation.DoubleAnimation`, a different API shape (imperative-timeline vs. declarative-storyboard), driving the `PathGeometry` replacement's figure points instead of an animated `CombinedGeometry`. |

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
Inside a `StackPanel`, `StackPanelPlacementSupport` takes a different
path entirely — it does not chain through Snapline/Raster at all
(reordering isn't a coordinate-snapping concern): each drag tick compares
the pointer position against sibling elements' centers to compute a
target insertion index, updates a thin visual indicator at that index,
and — on drop — reorders the dragged item to that index inside its
parent's `ContentProperty` collection. It checks the new
`ResizeThumbExtension.IsResizing` flag (4a) first and no-ops entirely
while an actual resize (not a move) is in progress on the same item.

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

**Grid row/column editing (`GridAdornerProvider`).** Independent of
`GridPlacementSupport`'s per-item placement — this operates on the
`Grid` itself, gated by
`[ExtensionServer(typeof(LogicalOrExtensionServer<
PrimarySelectionExtensionServer, PrimarySelectionParentExtensionServer>))]`
so its rails show whether the `Grid` or one of its direct children is
selected. A rail click starts row/column *creation*: opens a
`ChangeGroup`, registers a new `RowDefinition`/`ColumnDefinition` as a
`DesignItem`, inserts it into the `Grid`'s collection at the split
point, then rewrites every affected child's `Grid.Row`/`RowSpan`/
`Column`/`ColumnSpan` (and margins) so existing content doesn't visually
shift. Dragging an *existing* splitter is a separate, independent drag
controller (its own pointer capture, its own `ChangeGroup`, its own
Escape-cancel — not routed through `DragListener`) that resizes the
adjacent `RowDefinition`/`ColumnDefinition`'s `Height`/`Width` on each
tick and commits one `ChangeGroup` per whole drag on release.
`GridUnitSelector` is a small picker control (not a drag gesture) that
sets a row/column's size to `Auto`, star (`*`), or a fixed pixel value.

## Testing / Definition of Done

Same `.exe` NUnitLite runner and live-`Window`/real-`DesignSurface`
fixture pattern Phase 3/4a established (`Services.DesignPanel` only
resolves off a real `DesignSurface`, not a bare `XamlDesignContext`).

Phase 4b is done when:

- A test resizes a selected item inside a `Canvas` and asserts
  `Canvas.Left`/`Top` (not `Width`/`Height` alone) update, and that the
  `Right`/`Bottom` fallback path is exercised when only those are set.
- A test drags a selected item inside a `StackPanel` past a sibling and
  asserts the dragged item's index in `ContentProperty`'s collection
  changes to the expected slot (not a `Width`/`Height` assertion — see
  Revision history, round 2's correction), and that a resize (via 4a's
  `ResizeThumbExtension`, `IsResizing` true) on the same item produces no
  reorder.
- A test places/resizes a selected item inside a `Grid` and asserts
  `Grid.Row`/`Column`/`RowSpan`/`ColumnSpan` update correctly, including
  at least one case that exercises the `Offset`-replacement running-sum
  math against a `Grid` with unequal row/column sizes, and a case that
  asserts `HorizontalAlignment`/`VerticalAlignment`/`Margin`/`Width`/
  `Height` reset on entry and that a now-empty `RowDefinition`/
  `ColumnDefinition` `DesignItem` is removed on exit.
- A test clicks a `GridAdornerProvider` rail to create a new row/column
  and asserts the `Grid`'s `RowDefinitions`/`ColumnDefinitions` collection
  grows by one and every affected sibling's `Grid.Row`/`RowSpan`/
  `Column`/`ColumnSpan` updates to hold its visual position — with a
  single undo reverting the whole creation, siblings included.
- A separate test drags an *existing* `GridAdornerProvider` splitter
  and asserts the corresponding `RowDefinition`/`ColumnDefinition`'s
  `Height`/`Width` updates, with a single undo reverting the whole drag
  — this is a distinct code path from row/column creation (Known gap #4).
- A test drives `GridUnitSelector` and asserts a row/column's size
  becomes `Auto`, star, or a fixed pixel value as selected.
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
- Gaps #1 and #3 below each have an explicit, tested resolution. Gap #2
  is checked by eye (visual correctness isn't unit-testable) with a
  fallback documented if it fails. Gaps #4-#6 are plan-time design
  choices, not open research questions — no test is expected to "resolve"
  them, only to cover whatever the plan settles on.

## Known gaps — flagged, not guessed

1. **Whether `LeaveContainer`→`EnterContainer` (4a-shipped, never yet
   exercised in sequence) correctly reparents a `DesignItem` when driven
   through a real `ChangeContainer` call, with both the old and new
   parent live in the same `WinUIDesigner.XamlDom` document.** The
   methods exist and read correctly by inspection, but nothing has ever
   called them one after another. **Must be verified empirically as the
   plan's first task**, same precedent as Phase 3's live-Window
   investigation and 4a's `DragListener`-bug-hunting task.
2. **Visual correctness of `GrayOutDesignerExceptActiveArea`'s redesign
   — both its static geometry and its animated transition.** The
   `PathGeometry`/`FillRule.EvenOdd` cover-with-a-hole geometry and the
   `Storyboard`/`DoubleAnimation` transition that replaces
   `AnimateActiveAreaRectTo` both compile (confirmed above); whether the
   rendered result actually looks like "everything gray except this
   rectangle," animates smoothly between rectangles, and has correct
   z-order/no seam artifacts at the hole's edge hasn't been visually
   checked. Fallback if the geometry approach doesn't render correctly:
   four separate opaque rectangles covering the four strips around the
   active area instead of one hole-punched shape (loses the animated
   transition, which would need its own fallback — e.g. a plain
   visibility swap — if `Storyboard`-driving a `PathGeometry`'s figure
   points doesn't work smoothly).
3. **Timing of `RowDefinition.ActualHeight`/`ColumnDefinition.ActualWidth`
   during a live drag or immediately after `GridAdornerProvider` creates
   a new row/column.** These are post-layout values, and — unlike the
   first revision of this document assumed — there is no
   `DispatcherPriority.Loaded` equivalent to delay a read until layout
   has settled (`DispatcherQueuePriority` has no `Loaded` member,
   compiler-confirmed). Whether reading `ActualHeight`/`ActualWidth`
   inside a `PointerMoved` handler, or immediately after inserting a new
   `RowDefinition`/`ColumnDefinition`, returns stale values — and if so,
   what WinUI mechanism (e.g. `LayoutUpdated`, `DispatcherQueue.
   TryEnqueue` at a lower priority, or awaiting a frame) reliably
   substitutes for the reference's `Loaded`-priority delay — **must be
   verified empirically**, same class of concern as Phase 3's live-
   `Window` layout-timing investigation. Elevated from the first
   revision's framing (a settled type-swap) to a real open question.
4. **Exact rail/handle/splitter/GridUnitSelector visual composition for
   `GridAdorner`'s XAML redesign** — shapes, thickness, hit-test width,
   hover/drag visual feedback for all three parts (creation rails,
   splitter drag, unit picker). A plan-time design choice once gap #1's
   reparenting question, gap #2's gray-out approach, and gap #3's
   layout-timing question are settled, not resolved here.
5. **Ctrl-modifier threading into `IPlacementBehavior.PlacePoint`/
   `BeforeSetPosition`.** Both methods' current (4a-inherited, defined on
   the shared interface in `PlacementBehavior.cs`) signatures carry no
   modifier parameter; extending them to accept one (sourced from the
   originating pointer event's `KeyModifiers`, same mechanism 4a already
   uses elsewhere) is an interface change every `IPlacementBehavior`
   implementor must follow — exact shape a plan-time detail, not a
   research risk.
6. **`OptionService`'s scope beyond `SnaplinePlacementRoundValues`.**
   This service doesn't exist anywhere in this port; the reference's own
   `OptionService` likely carries other designer-wide options beyond the
   one member 4b needs. Whether to port a narrow single-purpose service
   (mirroring 4a's "port only the members this phase calls" precedent
   for `ModelTools`/`PlacementOperation`) or the reference's fuller
   surface is a plan-time scope call, not resolved here.

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
