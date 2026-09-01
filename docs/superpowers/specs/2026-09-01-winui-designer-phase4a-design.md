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

**Revision note (2026-09-01):** this spec's first draft was adversarially
reviewed by an independent agent before any plan was written from it. The
review re-verified every WinUI-API claim directly against the installed
SDK and cross-checked every scope decision against both the reference
source and this repo's actual current code. The four headline API claims
held up; six scope/dependency gaps did not (missing files, a WPF-only API
named as if it were WinUI's, an event-routing mismatch). This revision
folds in all Critical and Important findings — see inline call-outs below
where a decision changed as a result. Full findings are not reproduced
here; the fixes are.

Reference source, same two reference projects as Phase 2/3:

- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\` — abstract core
  (`PlacementOperation.cs`, `PlacementBehavior.cs`, `PlacementType.cs`,
  `PlacementAlignment.cs`, `PlacementInformation.cs`,
  `Adorners\RelativePlacement.cs`, `Extensions\BehaviorExtension.cs`,
  `DesignItem.cs`, `UIExtensions\UIHelpers.cs`, `ExtensionMethods.cs`).
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\` —
  concrete WPF-specific pieces (`ModelTools.cs`,
  `Extensions\DefaultPlacementBehavior.cs`, `Controls\DragListener.cs`,
  `Controls\Thumbs\*.cs`,
  `Extensions\{ResizeThumbExtension,RotateThumbExtension,SkewThumbExtension}.cs`).

## WinUI API surface — verified, not assumed

Checked directly against the installed Windows App SDK 1.7.250606001
reference assemblies/docs (same package Phase 1-3 verified against:
`~/.nuget/packages/microsoft.windowsappsdk/1.7.250606001/`). The review
re-ran every check below independently — against all three
`lib/net6.0-windows10.0.{17763,18362,22621}.0/Microsoft.WinUI.xml`
variants (byte-identical) *and* the `Microsoft.UI.Xaml.winmd` string heap
directly, not just one XML doc file — and confirmed the same results.

**Present, matches WPF closely.**
`Microsoft.UI.Xaml.Controls.Primitives.Thumb` exists with
`DragStarted`/`DragDelta`/`DragCompleted`/`IsDragging`/`CancelDrag`.
`Control.DefaultStyleKeyProperty` exists as a static
`DependencyProperty`. `UIElement.RenderTransform` exists.
`PointerRoutedEventArgs.GetCurrentPoint(UIElement relativeTo)`: doc text
confirms "If null was passed as relativeTo, the coordinates are in the
frame of reference of the overall window" — window/island coordinates,
the same space Phase 3 established for `TransformToVisual(null)`.

**Confirmed absent — corrected from the first draft.**
`DependencyProperty.OverrideMetadata` **does not exist anywhere in WinUI
3** (0 matches in the XML docs and 0 matches in the winmd string heap,
where a control string like `DefaultStyleKey` gets 5 hits — the scan
methodology is sound). The first draft of this spec wrongly asserted
`DefaultStyleKeyProperty.OverrideMetadata(...)` "is the same API WinUI
custom controls use throughout this codebase already" — a grep of `src/`
and `tests/` for `OverrideMetadata` returns zero hits; no such precedent
exists in this repo. The correct WinUI idiom, used instead: set
`this.DefaultStyleKey = typeof(DesignerThumb);` directly in the instance
constructor (`DefaultStyleKey` is a plain settable property, not a
metadata override).

**Also confirmed absent (new in this revision).**
`FrameworkElement.LayoutTransform`, `System.Windows.SystemParameters`
(`MinimumHorizontalDragDistance`/`MinimumVerticalDragDistance`),
`Windows.Media.ContainerVisual` (WPF visual-tree sense),
`System.Windows.Input.InputManager`/`PostProcessInput` (no global
pre-dispatch input hook), `Visibility.Hidden` (WinUI's `Visibility` enum
has exactly two members: `Visible`/`Collapsed`), `System.Windows.Controls.
Decorator` (no WinUI equivalent — the class doesn't exist),
`FrameworkPropertyMetadata` (WinUI only has `PropertyMetadata`, and
`DependencyProperty.Register` takes `(string, TypeName, TypeName,
PropertyMetadata)` — no 3-arg overload). `Microsoft.UI.Xaml.Media.Matrix`
exists but is a bare 6-field struct (`M11 M12 M21 M22 OffsetX OffsetY`)
with **no methods** — no `.Invert()`, and `Transform.Value` doesn't exist
on WinUI transforms. There is no `Vector` type in WinUI (only `Point`).
`UIElement.Cursor` is not public — only a **protected**
`ProtectedCursor`, so a custom control needing a settable cursor must
expose its own forwarding member.

These findings drive the mechanism table below, several "Decisions
already made" bullets, and the "Known gaps" section — each a mechanical
replacement or an explicit narrowing, not a redesign.

## Also verified against the current codebase (new in this revision)

The review checked the spec's assumptions about this repo's own code, not
just the reference and the WinUI SDK:

- **`ServiceContainer` (`src/WinUIDesigner.DesignModel/ServiceContainer.cs`)
  has no `.DesignPanel` convenience property** — only `.Component`,
  `.ExtensionManager`, `.Selection` (each a thin
  `GetRequiredService<T>()` wrapper). `ModelTools.ApplyTransform` (a
  member 4a ports) needs one, mirroring the reference's own
  `ServiceContainer.DesignPanel`.
- **`WinUIDesigner.Designer.DesignPanel` (`src/WinUIDesigner.Designer/
  DesignPanel.cs:17`) holds its `AdornerLayer` in a private
  `readonly` field with no public accessor.** `ModelTools.ApplyTransform`
  needs `((DesignPanel)designItem.Services.DesignPanel).AdornerLayer.
  UpdateAdornersForElement(...)` to compile — the target method itself,
  `AdornerLayer.UpdateAdornersForElement(UIElement, bool)`, already exists
  unchanged from Phase 3.
- **`DesignItemProperty`/`DesignItemPropertyCollection` are string-keyed
  only** (`this[string]`, `GetProperty(string)`) — there is no
  `DependencyProperty`-indexed overload (`this[DependencyProperty]`,
  `GetProperty(DependencyProperty)`, `GetAttachedProperty
  (DependencyProperty)`) the way the reference has. WinUI
  `DependencyProperty` instances have no `.Name`, so a DP-indexed overload
  cannot be added to mirror the reference — every ported call site
  (`ModelTools`, `DefaultPlacementBehavior`, all three thumb extensions)
  uses the string form instead, e.g.
  `Properties.GetProperty(FrameworkElement.WidthProperty)` becomes
  `Properties.GetProperty("Width")`.
- **`SelectionExtensionServers.cs` currently defines only
  `PrimarySelectionExtensionServer`** (confirmed: one class in the file).
  The first draft's decision to port the reference's abstract
  `SelectionAdornerProvider`/`PrimarySelectionAdornerProvider`/
  `SecondarySelectionAdornerProvider` trio would have needed two more
  `ExtensionServer` classes (`SelectionExtensionServer`,
  `SecondarySelectionExtensionServer`) neither of which exist nor has any
  4a consumer beyond that abstract layer itself — see the superseding
  decision below.

## Decisions already made

- **Scope: resize/rotate/skew on a single selected item, generic
  container only.** In scope: the core placement engine
  (`PlacementOperation`/`IPlacementBehavior`/`PlacementType`/
  `PlacementAlignment`/`PlacementInformation`/`RelativePlacement`), one
  concrete `IPlacementBehavior` (`DefaultPlacementBehavior`, registered
  for `Panel`/`Control`/`Border`/`Viewbox` generically — resizes via
  `Width`/`Height`, not `Canvas.Left`/`Top`), and the three thumb
  extensions (`ResizeThumbExtension`, `RotateThumbExtension`,
  `SkewThumbExtension`) plus their `Controls/Thumbs` visuals and the
  `DragListener` pointer-tracking helper. Out of scope, deferred to 4b:
  `GridPlacementSupport`/`CanvasPlacementSupport`/`StackPanelPlacementSupport`,
  snaplines, drag-move, container enter/leave.
- **`RelativePlacement.cs` was missing from the first draft's file list —
  added.** Every thumb (`ResizeThumbExtension.Place`,
  `RotateThumbExtension`, `SkewThumbExtension`) positions itself via
  `AdornerPanel.SetPlacement(thumb, new RelativePlacement(...))`. The
  reference's `RelativePlacement` (`WpfDesign\Project\Adorners\
  RelativePlacement.cs`, 222 lines: horizontal/vertical alignment plus
  offset/relative-size properties) ports mechanically — it operates only
  on doubles and the already-ported `HorizontalAlignment`/
  `VerticalAlignment`/`AdornerPlacement`, no new WPF-specific API surface.
- **Superseding the first draft's naming decision: the abstract
  `SelectionAdornerProvider`/`PrimarySelectionAdornerProvider`/
  `SecondarySelectionAdornerProvider` layer (reference:
  `Adorners/AdornerProviderClasses.cs`) is *not* ported for 4a.** The
  first draft chose to port it under the reference's exact names,
  accepting a namespace-disambiguation cost against Phase 3's existing
  concrete `WinUIDesigner.Designer/Services/SelectionAdornerProvider.cs`.
  The review found that cost was worse than estimated (a `using
  WinUIDesigner.Designer.Services;` alongside the abstract layer's
  namespace triggers CS0104 ambiguity on the bare name `SelectionAdornerProvider`)
  *and* that porting it needs two new `ExtensionServer` classes with no
  other 4a consumer (see above). Phase 3 already established a simpler
  pattern for exactly this situation: its own `SelectionAdornerProvider`
  subclasses `AdornerProvider` directly, with the `[ExtensionServer(...)]`
  attribute inlined on the concrete class, no abstract intermediate layer.
  4a's `ResizeThumbExtension`/`RotateThumbExtension`/`SkewThumbExtension`
  now follow that same pattern: each subclasses `AdornerProvider`
  directly and carries its own `[ExtensionServer(typeof(
  OnlyOneItemSelectedExtensionServer))]` attribute. `SelectionExtensionServers.cs`
  only needs the one addition the first draft already listed —
  `OnlyOneItemSelectedExtensionServer : PrimarySelectionExtensionServer`
  — and the namespace-collision risk is gone entirely, not just mitigated.
- **`ResizeThumbExtension`'s `is GridPlacementSupport` branch is dropped
  for 4a**, not stubbed. The reference special-cases Grid children
  (stretch-aware resize via `HorizontalAlignment`/`Margin` instead of raw
  `Width`/`Height`). Since `GridPlacementSupport` doesn't exist yet, the
  ported code always takes the `else` branch (`ModelTools.Resize`) —
  every resize sets `Width`/`Height` directly, even inside a `Grid`. The
  review noted this drop is also a small correctness improvement over the
  reference: the reference's `is GridPlacementSupport` check dereferences
  `operation.CurrentContainerBehavior` with no null guard, while
  `operation` is `null` whenever `resizeBehavior == null` at drag-start —
  a latent `NullReferenceException` in the original that the unconditional
  `ModelTools.Resize` path does not have. Closed when 4b lands
  `GridPlacementSupport`. Confirmed with the user over pulling
  `GridPlacementSupport` into 4a — rejected to keep 4a from reaching into
  4b's container-specific scope.
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
  "no property-grid UI or metadata (Metadata.cs)"). Lands in a new file
  `WinUIDesigner.DesignModel/PlacementMetadata.cs` (the first draft left
  its host file as an open plan-time choice and tentatively suggested
  `Services.cs`, which is pure interface declarations with no static
  members — not a good fit; naming it now removes that ambiguity). Same
  precedent as Phase 3's Known-gap-#2 (`PlacementOperation.
  GetRealElementSize` ported alone from a file otherwise excluded
  wholesale).
- **`DefaultPlacementBehavior.CanEnterContainer`'s `InfoTextEnterArea`
  ("hold Alt to enter container" hint UI) is dropped entirely**, not
  ported — both its use in `CanEnterContainer` (unreachable in 4a: its
  only caller, `PlacementOperation.TryStartInsertNewComponents`, isn't
  used until drag-move-between-containers lands in 4b) *and* its
  `InfoTextEnterArea.Stop(ref infoTextEnterArea)` call inside
  `EndPlacement` (**this one is reachable** — `EndPlacement` runs on every
  `PlacementOperation.Commit`/`Abort`, i.e. every resize/rotate/skew). The
  first draft only flagged the `CanEnterContainer` half; the ported
  `EndPlacement` simply omits the `InfoTextEnterArea` line, no
  replacement needed since the static field itself isn't ported.
  `CanEnterContainer` keeps its boolean contract but returns the plain
  `internalCanEnterContainer` result directly (see next bullet for that
  method's own cut).
- **`internalCanEnterContainer`'s `ExtendedItem.Component is Decorator`
  branch is dropped.** `Decorator` doesn't exist in WinUI (see "WinUI API
  surface"); there is no direct replacement type to check against, and no
  4a consumer creates or resizes a decorator-shaped element, so the branch
  is simply omitted rather than mapped to something else.
- **`Controls/SelectionFrame.cs` is *not* ported in 4a.** Reading it
  revealed it is the rubber-band multi-select rectangle control (a bare
  `Control` styled via `generic.xaml`, used during click-drag empty-space
  selection), not a resize-frame as its name suggests — confirmed by
  searching the whole reference tree for its only instantiation site,
  `Extensions/PanelSelectionHandler.cs:75`. That feature (rubber-band
  drag-select) is explicitly out of scope per Phase 3's own "Out of
  scope" list ("no multi-selection UI (rubber-band drag-select,
  Ctrl/Shift-click)") and isn't assigned to 4a.
- **`DragListener` ports via WinUI pointer events, using
  `AddHandler(..., handledEventsToo: true)`, not a plain event
  subscription.** WPF's `DragListener` deliberately subscribes the
  *Preview* (tunneling) mouse events, because a plain `Thumb` handles the
  bubbling events itself first (and marks them handled) as part of its
  own default look/feel. WinUI has no tunneling pointer events — only
  bubbling — so a plain `target.PointerPressed += ...` on a `Thumb` risks
  never firing if `Thumb`'s own base handling marks the event handled
  first. The WinUI equivalent of "see it regardless" is
  `target.AddHandler(UIElement.PointerPressedEvent, handler,
  handledEventsToo: true)` (and the matching `PointerMovedEvent`/
  `PointerReleasedEvent`). *Correction from the first draft*: it claimed
  this "reuses the same pointer-capture pattern Phase 3's `DesignPanel`
  already established" — `DesignPanel.cs` in fact only does a plain
  `this.PointerPressed += OnPointerPressed` with no capture and no
  `handledEventsToo`, so there is no such precedent; this is a new
  pattern being introduced in 4a specifically because its pointer target
  (`Thumb`) already has default handling to route around, unlike
  `DesignPanel` itself.
- **`DragListener` gains a pointer-position query it didn't need in the
  first draft.** `RotateThumbExtension`/`SkewThumbExtension` call WPF's
  `Mouse.GetPosition(element)` *mid-drag, outside any event handler*, to
  sample the pointer's current position relative to an arbitrary element
  (not just the accumulated start-to-current delta `DragListener.Delta`
  already covers). WinUI has no ambient pointer-position API — position is
  only available from a live `PointerRoutedEventArgs`. The ported
  `DragListener` retains the last `PointerPoint` sampled during
  `PointerMoved` (in window coordinates, per the confirmed
  `GetCurrentPoint(null)` semantics above) and adds a
  `GetPosition(UIElement relativeTo)` method that converts it via
  `relativeTo.TransformToVisual(null).Inverse.TransformPoint(...)` —
  the same island-space convention Phase 3 established for hit-testing.
- **`DragListener.Delta`'s transform-aware math is redone for WinUI's bare
  `Matrix` struct.** The reference inverts a WPF `Matrix` and transforms a
  `Vector`; WinUI's `Matrix` has no methods and there is no `Vector` type.
  The port instead applies `Transform.Inverse` (a `GeneralTransform`, which
  *does* have `.Inverse`/`.TransformPoint`) to `StartPoint` and
  `CurrentPoint` individually and subtracts the results component-wise —
  algebraically equivalent for an affine transform (translation cancels
  out of the difference), so the result matches the reference's vector
  math without needing a `Vector` type. `Delta` becomes a plain `(double
  X, double Y)` value, not a `Vector`.
- **Cursors: `DesignerThumb` exposes a public forwarding member for its
  own `ProtectedCursor`**, since `UIElement.Cursor` is not public in
  WinUI. WPF's `Cursors.SizeNS`/`SizeWE`/`SizeNESW`/`SizeNWSE` map to
  `InputSystemCursorShape.SizeNorthSouth`/`SizeWestEast`/
  `SizeNortheastSouthwest`/`SizeNorthwestSoutheast` — exact enum member
  names are a plan-time verification, not a design one.
  `Cursors.ScrollWE`/`ScrollNS`
  and `RotateThumbExtension`'s custom `.cur` resource (loaded via
  `ZoomControl.GetCursor`, itself out of scope) have no equivalent for
  4a — the rotate thumb gets a generic system cursor instead (a disclosed
  simplification, not a silent omission; a themed rotate cursor can be
  added once 4e or later work brings in custom cursor resources). The
  reference's `cursor == Cursors.SizeNS` reference-equality check (which
  would silently break against WinUI's `InputSystemCursor.Create()`,
  which returns a fresh instance every call) is not ported as-is — the
  port passes the `PlacementAlignment`/cursor *shape* directly to
  `CreateThumb` instead of comparing cursor objects.
- **Ctrl-modifier reads (`ResizeThumbExtension`'s aspect-ratio lock,
  `RotateThumbExtension`'s 15°-snap disable) resolve from the pointer
  event, not a global `Keyboard.IsKeyDown` query** — the same choice
  Phase 3 already made for `SelectionTypes.Auto` resolution
  (`PointerRoutedEventArgs.KeyModifiers` at the call site, no global
  keyboard-state dependency). `DragListener`'s `Changed` callback carries
  the originating pointer event's `KeyModifiers` so `drag_Changed` can
  read it without a separate query.
- **`Visibility.Hidden` (used to hide resize handles that don't apply —
  e.g. a locked axis) has no WinUI equivalent (only `Visible`/
  `Collapsed`, confirmed above) and is replaced with `Opacity = 0` plus
  `IsHitTestVisible = false`.** This isn't an invented substitute — the
  reference's own `ResizeThumbExtension.Place()` already uses `Opacity =
  0` for the same "present but not shown" need on its stretched
  center-edge thumbs, so the port applies the same idiom uniformly
  instead of introducing a second hidden-state mechanism.
- **`IsPrimarySelection`/`ThumbVisible` (dependency properties on
  `DesignerThumb`) get their visual effect wired directly in code**,
  since the first draft's "code-only appearance, no `generic.xaml`"
  decision (kept — same simplification Phase 3 used for
  `SelectionHighlight`) means the WPF style `Trigger`s that currently
  drive both properties' visible effect have nothing to port to. Each
  property's changed-callback updates the thumb's `Opacity`/
  `Background`/`BorderBrush` directly instead.
- **`TryFindParent<T>`, used by `DesignerThumb.ReDraw` and
  `SkewThumbExtension`, ports as a small manual `VisualTreeHelper.
  GetParent` walk** — sourced from the reference's
  `UIExtensions/UIHelpers.cs` (not listed in the first draft's file list;
  it has no WinUI-ported equivalent yet, same situation as
  `IsDescendantOf` in Phase 3). Lands alongside a small `Rect.Round()`
  extension method (reference: `ExtensionMethods.cs`, needed by both
  `PlacementOperation.cs` and `ResizeThumbExtension.cs`) in a new
  `WinUIDesigner.DesignModel/GeometryExtensions.cs`.
- **`SharedInstances.BoxedTrue`** (a single boxed `true` used as
  `ThumbVisibleProperty`'s default) is inlined as a `static readonly
  object` field directly in `DesignerThumb.cs` rather than porting the
  reference's separate `Controls/SharedInstances.cs` file for one value.
- **`WindowClone`-related guard branches in `RotateThumbExtension`/
  `SkewThumbExtension` are dropped**, not translated — `WindowClone`
  (the design-time preview-window thumbnail control) isn't part of this
  port yet (no Phase has touched `ThumbnailView`/`WindowClone`), so the
  guarded code paths are unreachable in 4a's scope and the guards
  themselves have nothing to check against.
- **Both copies of `GetRealElementSize` are kept, not consolidated.**
  Phase 3 already inlined a private copy in
  `WinUIDesigner.DesignModel/Adorners/AdornerPanel.cs` (its own
  `Known gap #2` resolution). This spec's new
  `PlacementOperation.GetRealElementSize` (public, static, matching the
  reference) is what `ResizeThumb.ArrangeOverride` and
  `SkewThumbExtension` call. The two don't conflict (different classes,
  different accessibility) and consolidating them is out of scope for
  4a — a forward-flag, not a defect.
- **`DesignerThumb`'s dependency properties use WinUI's actual
  `DependencyProperty.Register` signature** (`string name, Type
  propertyType, Type ownerType, PropertyMetadata metadata` — 4 args,
  `PropertyMetadata` not `FrameworkPropertyMetadata`), not the reference's
  WPF 3-arg-plus-`FrameworkPropertyMetadata` form.
- **`DesignItem.GetCompleteAppliedTransformationToView()` is ported with
  its transform sources narrowed to `RenderTransform` only.** The
  reference walks the visual tree accumulating `LayoutTransform` +
  `RenderTransform` + `ContainerVisual.Transform`; WinUI has none of the
  first or third (confirmed absent above). The port walks
  `VisualTreeHelper.GetParent` (the same manual-walk pattern as Phase 3's
  `IsDescendantOf`) accumulating only `RenderTransform` into a
  `TransformGroup`. Whether re-adding a live, already-parented
  `RenderTransform` instance into a new `TransformGroup.Children`
  collection is safe in WinUI (vs. WPF's shareable `Freezable`s) is not
  assumed here — see Known gap #3.
- **`DesignerThumb`/`ResizeThumb`/`RotateThumb`'s default appearance is
  code-only**, not a `generic.xaml` resource dictionary entry — same
  simplification Phase 3 used for `SelectionHighlight` (a plain `Grid`
  instead of a themed `Border`). `this.DefaultStyleKey =
  typeof(DesignerThumb)` is still set in the constructor (matches the
  reference's extensibility hook and doesn't block a themed style being
  added later), but the shipped default sets
  `Width`/`Height`/`Background`/`BorderBrush` directly.
- **Forward-flag for 4b, not a 4a decision:** `DesignItem.AddBehavior`
  (`_behaviorObjects[behaviorInterface] = behaviorImplementation`,
  last-writer-wins, no conflict detection) is safe for 4a because
  `Panel`/`Control`/`Border`/`Viewbox` are mutually disjoint in WinUI's
  type hierarchy — `ExtensionManager`'s `[ExtensionFor]` matching finds at
  most one of them per component. It stops being safe once 4b adds
  `GridPlacementSupport` (`[ExtensionFor(typeof(Grid))]`): a `Grid` then
  matches both it and `DefaultPlacementBehavior` (`[ExtensionFor(typeof(
  Panel))]`), and which one wins `AddBehavior(typeof(IPlacementBehavior),
  ...)` depends on `ExtensionAttribute.Order`, not specificity. 4b's spec
  needs `ExtensionForAttribute.OverrideExtensions` (or equivalent) to
  make Grid-specificity explicit — noted here so it isn't rediscovered
  from scratch.

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
      PlacementMetadata.cs                (new: IsPlacementDisabled static helper)
      GeometryExtensions.cs               (new: Rect.Round() extension)
      DesignItem.cs                       (modify: add GetCompleteAppliedTransformationToView,
                                             RenderTransform-only)
      ServiceContainer.cs                 (modify: add .DesignPanel convenience property,
                                             mirrors .Selection)
      Extensions\
        BehaviorExtension.cs              (new)
        SelectionExtensionServers.cs      (modify: add OnlyOneItemSelectedExtensionServer only
                                             — abstract SelectionAdornerProvider layer NOT ported,
                                             see "Decisions already made")
      Adorners\
        RelativePlacement.cs              (new)
    WinUIDesigner.Designer\
      ModelTools.cs                       (new: GetWidth, GetHeight, Resize, ApplyTransform only)
      DesignPanel.cs                      (modify: expose public AdornerLayer property,
                                             currently private field)
      Extensions\
        DefaultPlacementBehavior.cs       (new)
        ResizeThumbExtension.cs           (new: AdornerProvider subclass directly,
                                             [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))])
        RotateThumbExtension.cs           (new, same pattern)
        SkewThumbExtension.cs             (new, same pattern)
      Controls\
        DragListener.cs                   (new: WinUI pointer events + AddHandler(handledEventsToo:true),
                                             adds GetPosition(UIElement) beyond the reference's API)
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
| `DefaultStyleKeyProperty.OverrideMetadata(...)` (static ctor) | `this.DefaultStyleKey = typeof(X)` (instance ctor) — `OverrideMetadata` confirmed absent from WinUI 3. |
| `Target.PreviewMouseLeftButtonDown/Move/Up` (`DragListener`) | `target.AddHandler(UIElement.PointerPressedEvent, handler, handledEventsToo: true)` and the `PointerMoved`/`PointerReleased` equivalents — WinUI has no tunneling events, and `handledEventsToo` is needed because `Thumb`'s own default handling marks bubbling pointer events handled first. |
| `Mouse.GetPosition(null)` / `e.GetPosition(null)` (screen coordinates) | `PointerRoutedEventArgs.GetCurrentPoint(null).Position` — window/island coordinates (confirmed by XML doc), not screen coordinates. `DragListener.Delta` only ever consumes the *difference* of two such points, so the frame doesn't affect it; `DragListener.GetPosition(element)` (new) converts a captured point via `element.TransformToVisual(null).Inverse.TransformPoint(...)`. |
| `SystemParameters.MinimumHorizontalDragDistance`/`MinimumVerticalDragDistance` | No WinUI equivalent (confirmed absent) — replaced with a local constant. Exact value (WPF's default is 4px/2px) is a plan-time choice, not a design one. |
| `InputManager.Current.PostProcessInput` (global Escape-to-cancel-drag) | No WinUI equivalent (confirmed absent). Replacement mechanism is **Known gap #1**, below. |
| `Transform.Value` + `Matrix.Invert()` + `Matrix.Transform(Vector)` (`DragListener.Delta`) | WinUI `Matrix` is a bare struct with no methods and no `Vector` type exists. Replaced with `GeneralTransform.Inverse.TransformPoint(...)` applied to `StartPoint` and `CurrentPoint` individually, subtracted component-wise (see "Decisions already made"). |
| `Properties.GetProperty(FrameworkElement.WidthProperty)` / `Properties[dp]` (DP-indexed) | `Properties.GetProperty("Width")` (string-indexed) — `DesignItemProperty` has no DP-indexed overload in this codebase (WinUI `DependencyProperty` has no `.Name` to key off of). |
| `Visibility.Hidden` | `Opacity = 0` + `IsHitTestVisible = false` — WinUI's `Visibility` has only `Visible`/`Collapsed`. Same idiom the reference itself already uses for its stretched center-edge thumbs. |
| `element.Cursor = cursor` | A public forwarding member on `DesignerThumb` that sets the protected `ProtectedCursor` — `UIElement.Cursor` isn't public in WinUI. `Cursors.Size*` map to `InputSystemCursorShape.Size*` members (exact names verified at plan time). |
| `FrameworkPropertyMetadata` + 3-arg `DependencyProperty.Register` | `PropertyMetadata` + WinUI's 4-arg `Register(string, Type, Type, PropertyMetadata)` — the WPF overloads don't exist. |
| `element.TransformToAncestor(ancestor)` / `TranslatePoint` | `element.TransformToVisual(target)` — same replacement Phase 3 already established. |
| `Vector.AngleBetween(v1, v2)` | Manual `Math.Atan2`-based angle computation — no WinUI `Vector` type to carry the method. |
| `new SkewTransform(angleX, angleY)` (parameterized ctor) | `new SkewTransform { AngleX = ..., AngleY = ... }` — WinUI transform types expose only parameterless constructors. |
| `element.TryFindParent<T>()` (`DesignerThumb.ReDraw`, `SkewThumbExtension`) | Manual `VisualTreeHelper.GetParent` walk (new `GeometryExtensions.cs`/thumb-local helper) — no WinUI-ported equivalent exists yet, same pattern used since Phase 3's `IsDescendantOf`. |
| `FrameworkElement.LayoutTransform` + `ContainerVisual.Transform` (in `GetCompleteAppliedTransformationToView`) | Dropped — confirmed absent from WinUI's object model. Only `RenderTransform` is accumulated. |
| `ExtendedItem.Component is Decorator` (`internalCanEnterContainer`) | Branch dropped — `Decorator` confirmed absent from WinUI, no 4a consumer needs a replacement. |

## Data flow

Pointer press on a `DesignerThumb` (child of the `AdornerPanel` Phase 3's
`AdornerLayer` positions over the selected element, placed via the newly-
ported `RelativePlacement`) → `DragListener` (subscribed with
`AddHandler(..., handledEventsToo: true)`) captures the pointer, records
the start point in window coordinates → first move past the drag-distance
threshold fires `Started` → `ResizeThumbExtension.drag_Started` calls
`PlacementOperation.Start(extendedItemArray, PlacementType.Resize)`, which
resolves `DesignItem.GetBehavior<IPlacementBehavior>()` on the parent
(`DefaultPlacementBehavior`, registered in `OnInitialized` via
`ExtendedItem.AddBehavior`) and calls its `BeginPlacement`/`GetPosition` →
each subsequent `PointerMoved` fires `drag_Changed`, which computes a new
width/height from the thumb's `PlacementAlignment` and the transform-aware
`Delta` (`GeneralTransform.Inverse`-based, not WPF `Matrix` math), applies
it via `ModelTools.Resize` (setting `DesignItemProperty` string-keyed
values — Phase 2's `XamlModelProperty.SetValue`, so each resize step is
undo-tracked), and updates `PlacementInformation.Bounds` →
`operation.CurrentContainerBehavior.SetPosition(info)` → pointer release
fires `drag_Completed`, which commits (or aborts, on cancel) the
`PlacementOperation`'s underlying `ChangeGroup` (Phase 2's `UndoService`
integration, unchanged). `RotateThumbExtension`/`SkewThumbExtension`
follow the same `DragListener` lifecycle but call `ModelTools.
ApplyTransform` (via the new `Services.DesignPanel` accessor) with a
`RotateTransform`/`SkewTransform` instead of resizing, which in turn
calls `AdornerLayer.UpdateAdornersForElement` (Phase 3, unchanged, now
reached through `DesignPanel`'s new public `AdornerLayer` property) so
the selection highlight and thumb positions re-arrange after every
transform change.

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
- Each of the three flagged gaps below has an explicit, tested resolution.

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
3. **Whether re-parenting a live, already-parented `RenderTransform`
   instance into a new `TransformGroup.Children` collection is safe in
   WinUI.** `GetCompleteAppliedTransformationToView` builds its result by
   adding each ancestor's *live* `RenderTransform` object into a new
   `TransformGroup`. WPF allows this because `Transform` is a shareable
   `Freezable`; WinUI's `Transform` is a plain `DependencyObject`, and
   WinUI's collection/parenting rules for `DependencyObject`s are
   generally stricter than WPF's. **Must be verified empirically** —
   if adding a live instance throws or silently reparents the visual tree
   in an unwanted way, the fix is to copy the transform's property values
   into a fresh instance instead of reusing the reference.

## Out of scope for Phase 4a

`GridPlacementSupport`/`CanvasPlacementSupport`/`StackPanelPlacementSupport`
and all other container-specific `IPlacementBehavior` implementations,
snaplines, drag-move (`PanelMove`, `TopLeftContainerDragHandle`),
container enter/leave during a drag, rubber-band multi-select
(`SelectionFrame`'s real purpose), `MultiPointThumb`/`PointThumb`'s actual
consumers (`Line`/`Path`/`PolyLine` handler extensions — 4d), context
menus, `SizeDisplayExtension`/`MarginHandleExtension` (4a's
`ResizeThumbExtension` null-checks for these and degrades gracefully
without them), arrow-key nudge, themed cursor resources (`.cur` files via
`ZoomControl`), `WindowClone`/`ThumbnailView` integration, and the ~50
remaining behaviors listed in the Phase 4 brainstorming breakdown
(4b-4e) — per that decomposition, each gets its own spec once 4a ships.
