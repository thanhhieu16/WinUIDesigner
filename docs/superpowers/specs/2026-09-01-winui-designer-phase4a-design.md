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

**Revision history.** This spec went through two rounds of independent
adversarial review before any implementation plan was written from it.
Round 1 found the scope inventory materially incomplete (a WPF-only API
named as if WinUI's, missing files/dependencies, a WPF event-tunneling
assumption that doesn't hold in WinUI). Round 2 — run against the
round-1 fix — found that `Microsoft.UI.Xaml.Controls.Primitives.Thumb`,
the class every thumb visual was planned to subclass, is **sealed** in
this SDK version, plus a second file (`PlacementOperation.cs`) that
needed the same "port only the members this phase calls" treatment
already applied to `ModelTools.cs`/`Metadata.cs`. Both round-2 Criticals
were independently re-verified here by writing throwaway subclasses
against the actual project and reading the resulting compiler errors —
the same gold-standard method Phase 3 used to confirm `Border` is
sealed. This revision folds in every round-2 Critical and Important
finding. Round-1's fixes (documented inline in "Decisions already made"
below) all still stand; only the items this revision calls out changed.

Reference source, same two reference projects as Phase 2/3:

- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign\Project\` — abstract core
  (`PlacementOperation.cs`, `PlacementBehavior.cs`, `PlacementType.cs`,
  `PlacementAlignment.cs`, `PlacementInformation.cs`,
  `Adorners\RelativePlacement.cs`, `Extensions\BehaviorExtension.cs`,
  `DesignItem.cs`, `UIExtensions\UIHelpers.cs`, `ExtensionMethods.cs`).
- `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.Designer\Project\` —
  concrete WPF-specific pieces (`ModelTools.cs`,
  `Extensions\DefaultPlacementBehavior.cs`, `Controls\DragListener.cs`,
  `Controls\Thumbs\{DesignerThumb,ResizeThumb,RotateThumb}.cs`,
  `Extensions\{ResizeThumbExtension,RotateThumbExtension,SkewThumbExtension}.cs`).

## WinUI API surface — verified, not assumed

Checked directly against the installed Windows App SDK 1.7.250606001
reference assemblies/docs
(`~/.nuget/packages/microsoft.windowsappsdk/1.7.250606001/`), and — for
the two facts that decide this revision's biggest structural change —
against the **actual C# compiler**, by adding a throwaway probe file to
`src/WinUIDesigner.Designer/`, running `dotnet build`, reading the exact
diagnostic, and deleting the probe. Compiler verification is strictly
stronger evidence than reading IL metadata or XML docs (it's what will
actually happen to an implementer), so it's used here wherever the
question is "does this compile."

**Confirmed sealed — compiler-verified, invalidates the round-1 thumb
design.** `class X : Microsoft.UI.Xaml.Controls.Primitives.Thumb {}`
fails with `CS0509: 'X': cannot derive from sealed type 'Thumb'`. Same
situation as Phase 3's `Border` (also sealed, also caught the same way).
`Thumb`'s `DragStarted`/`DragDelta`/`DragCompleted`/`IsDragging`/
`CancelDrag` members genuinely exist (confirmed via `Microsoft.WinUI.xml`)
but are moot for this port: 4a's `DragListener` never used them anyway
(see "Decisions already made" — it wraps raw pointer events, matching
the reference's own choice), so nothing about the drag *mechanism*
changes. What changes is the thumb *visual*'s base class.

**Confirmed open — compiler-verified, the replacement base.**
`class X : Microsoft.UI.Xaml.Controls.Grid {}` compiles cleanly. `Grid`
is the same base Phase 3 already used for `SelectionHighlight`
(`src/WinUIDesigner.Designer/Controls/SelectionHighlight.cs`) for the
identical reason (need an open, unstyled visual with `Background`/
`BorderBrush`/`BorderThickness`).

**Confirmed absent — compiler-verified.** `Rect.TopLeft` and `Rect.Size`
(`CS1061`, `Windows.Foundation.Rect` has no such members — only `X`/`Y`/
`Width`/`Height`/`Left`/`Top`/`Right`/`Bottom`). `Matrix.Invert()`
(`CS1061`, `Microsoft.UI.Xaml.Media.Matrix` is a bare 6-field struct —
`M11 M12 M21 M22 OffsetX OffsetY` — with no methods).
`Microsoft.UI.Xaml.Media.Vector` (`CS0234`, no such type in that
namespace or anywhere in WinUI).

**Present, matches WPF closely.** `Control.DefaultStyleKeyProperty`
exists (moot for 4a now that thumbs base on `Grid`, not `Control` — see
above). `UIElement.RenderTransform` exists.
`PointerRoutedEventArgs.GetCurrentPoint(UIElement relativeTo)`: doc text
confirms "If null was passed as relativeTo, the coordinates are in the
frame of reference of the overall window" — window/island coordinates,
the same space Phase 3 established for `TransformToVisual(null)`.
`UIElement.AddHandler(RoutedEvent, object, bool handledEventsToo)` exists,
and `PointerPressedEvent`/`PointerMovedEvent`/`PointerReleasedEvent` are
real static `RoutedEvent` members it accepts. `GeneralTransform.Inverse`
(the type `TransformToVisual` returns) exists and returns another
`GeneralTransform` with `TransformPoint` — this is the route used in
place of `Matrix.Invert()` (see "Decisions already made"); note it
returns `null` when the transform isn't invertible, so callers must
guard it, unlike WPF's `Matrix.Invert()` which throws instead.
`UIElement.ProtectedCursor` exists and is `protected` (accessible from
any `UIElement` subclass's own code — this doesn't depend on the
`Thumb`-vs-`Grid` base-class decision, since `Cursor` visibility is a
`UIElement`-level restriction, not a `Control`-level one).
`UIElement.IsHitTestVisible` is public.

**`InputSystemCursorShape`** (`Microsoft.UI.Input`, confirmed via
`Microsoft.InteractiveExperiences.Projection.xml` — a different XML doc
than the `Microsoft.WinUI.xml` files this spec otherwise cites, so this
required a broader search than the earlier revisions used) has these
members: `Arrow, AppStarting, Cross, Hand, Help, IBeam, Person, Pin,
SizeAll, SizeNorthSouth, SizeNortheastSouthwest, SizeNorthwestSoutheast,
SizeWestEast, UniversalNo, UpArrow, Wait`. No `Scroll*` members exist.

**Also confirmed absent.** `DependencyProperty.OverrideMetadata` (moot
now — see above), `FrameworkElement.LayoutTransform`, `System.Windows.
SystemParameters` (`MinimumHorizontalDragDistance`/
`MinimumVerticalDragDistance`), `Windows.Media.ContainerVisual` (WPF
visual-tree sense), `System.Windows.Input.InputManager`/
`PostProcessInput` (no global pre-dispatch input hook), `Visibility.
Hidden` (WinUI's `Visibility` enum has exactly two members: `Visible`/
`Collapsed`), `System.Windows.Controls.Decorator` (no WinUI equivalent),
`FrameworkPropertyMetadata` (WinUI only has `PropertyMetadata`, with
exactly two constructors — `PropertyMetadata(object)` and
`PropertyMetadata(object, PropertyChangedCallback)` — and
`DependencyProperty.Register` takes `(string, TypeName, TypeName,
PropertyMetadata)`, no 3-arg overload).

These findings drive the mechanism table below, several "Decisions
already made" bullets, and the "Known gaps" section — each a mechanical
replacement or an explicit narrowing, not a redesign.

## Also verified against the current codebase

- **`ServiceContainer` (`src/WinUIDesigner.DesignModel/ServiceContainer.cs`)
  has no `.DesignPanel` convenience property** — only `.Component`,
  `.ExtensionManager`, `.Selection`. Adding one mirrors the existing
  three exactly (`GetRequiredService<IDesignPanel>()`).
- **`WinUIDesigner.Designer.DesignPanel` (`src/WinUIDesigner.Designer/
  DesignPanel.cs:17`) holds its `AdornerLayer` in a private `readonly`
  field with no public accessor.** `ModelTools.ApplyTransform` needs
  `((DesignPanel)designItem.Services.DesignPanel).AdornerLayer.
  UpdateAdornersForElement(...)` to compile — the target method itself
  already exists unchanged from Phase 3, `public` (`AdornerLayer.cs:138`).
  `IDesignPanel` is only ever registered as a service from
  `DesignSurface.LoadDesigner` — any 4a test needs a real `DesignSurface`
  in its fixture, not a bare `XamlDesignContext`, or `Services.DesignPanel`
  throws `ServiceRequiredException`.
- **`DesignItemProperty`/`DesignItemPropertyCollection` are string-keyed
  only** — no `DependencyProperty`-indexed overload. Every ported call
  site (`ModelTools`, `DefaultPlacementBehavior`, all three thumb
  extensions) uses the string form, e.g.
  `Properties.GetProperty(FrameworkElement.WidthProperty)` becomes
  `Properties.GetProperty("Width")`. `ModelTools.ApplyTransform`'s
  `DependencyProperty transformProperty = null` parameter becomes
  `string transformProperty = null`, defaulting internally to
  `"RenderTransform"`.
- **`SelectionExtensionServers.cs` currently defines only
  `PrimarySelectionExtensionServer`.** 4a adds exactly one class,
  `OnlyOneItemSelectedExtensionServer : PrimarySelectionExtensionServer`
  — see the abstract-layer decision below for why nothing else is needed.
- **`AdornerProvider`'s constructor/initialization order supports
  building adorners before `OnInitialized` runs**, confirmed by reading
  `src/WinUIDesigner.DesignModel/Adorners/AdornerProvider.cs`: the base
  constructor only allocates the adorner collection, an internal
  "visible" flag starts `false` so adding a panel during a subclass's own
  constructor is deferred rather than acted on early, and `OnInitialized`
  flips that flag and replays every already-added panel. This is exactly
  the shape `ResizeThumbExtension`'s reference constructor needs (it
  builds an `AdornerPanel`, adds 8 thumbs, and wires 8 `DragListener`s,
  all before `ExtendedItem` is available) — confirmed workable, not
  assumed.
- **`ExtensionManager.RegisterAssembly` skips any type with zero
  `[ExtensionFor]` attributes** (`src/WinUIDesigner.DesignModel/
  Extensions/ExtensionManager.cs`) — confirmed by reading the
  registration loop. `ResizeThumbExtension`/`RotateThumbExtension`/
  `SkewThumbExtension` each need `[ExtensionFor(typeof(FrameworkElement))]`
  in addition to their `[ExtensionServer(...)]` attribute (the reference
  carries both on all three); omitting `[ExtensionFor]` means the
  extension is silently never created and every DoD test would fail
  against an empty adorner layer with no compiler error to point at why.

## Decisions already made

- **Scope: resize/rotate/skew on a single selected item, generic
  container only.** In scope: the core placement engine (narrowed — see
  below), one concrete `IPlacementBehavior` (`DefaultPlacementBehavior`,
  registered for `Panel`/`Control`/`Border`/`Viewbox` generically —
  resizes via `Width`/`Height`, not `Canvas.Left`/`Top`), and the three
  thumb extensions (`ResizeThumbExtension`, `RotateThumbExtension`,
  `SkewThumbExtension`) plus their `Controls/Thumbs` visuals and the
  `DragListener` pointer-tracking helper. Out of scope, deferred to 4b:
  `GridPlacementSupport`/`CanvasPlacementSupport`/`StackPanelPlacementSupport`,
  snaplines, drag-move, container enter/leave.
- **`RelativePlacement.cs` is ported.** Every thumb (`ResizeThumbExtension.
  Place`, `RotateThumbExtension`, `SkewThumbExtension`) positions itself
  via `AdornerPanel.SetPlacement(thumb, new RelativePlacement(...))`. Read
  in full: the reference's `RelativePlacement`
  (`WpfDesign\Project\Adorners\RelativePlacement.cs`, 222 lines) operates
  only on doubles plus the already-ported `HorizontalAlignment`/
  `VerticalAlignment`/`AdornerPlacement`/`UIElement.DesiredSize`/`Arrange`/
  `Size`/`Point`/`Rect` — no further WPF-specific API surface, confirmed
  by reading its full public members, not inferred from its purpose.
- **The abstract `SelectionAdornerProvider`/`PrimarySelectionAdornerProvider`/
  `SecondarySelectionAdornerProvider` layer (reference:
  `Adorners/AdornerProviderClasses.cs`) is *not* ported.** Each of 4a's
  three thumb extensions subclasses `AdornerProvider` directly and
  carries its own `[ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]`
  attribute — exactly the pattern Phase 3's own concrete
  `SelectionAdornerProvider` (`WinUIDesigner.Designer/Services/
  SelectionAdornerProvider.cs`) already establishes (no abstract
  intermediate layer). This is confirmed behavior-preserving: the
  reference's own `SelectionAdornerProvider` is an empty class whose
  `[ExtensionServer(typeof(SelectionExtensionServer))]` attribute is
  already overridden by each concrete subclass's own
  `[ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]`
  attribute — so skipping the empty intermediate class changes nothing
  at runtime. It also avoids needing two more `ExtensionServer` classes
  (`SelectionExtensionServer`, `SecondarySelectionExtensionServer`) that
  would otherwise have no other 4a consumer, and avoids a same-name,
  different-namespace collision against Phase 3's existing concrete
  `SelectionAdornerProvider` class.
- **`ResizeThumbExtension`'s `is GridPlacementSupport` branch is dropped
  for 4a**, not stubbed — every resize sets `Width`/`Height` directly via
  `ModelTools.Resize`, even inside a `Grid`. Also fixes a latent
  `NullReferenceException` present in the reference (`operation` is
  `null` whenever `resizeBehavior` was `null` at drag-start, but the
  dropped branch dereferences `operation.CurrentContainerBehavior`
  unconditionally). Closed when 4b lands `GridPlacementSupport`.
- **`ModelTools.cs` (890 lines in the reference) ports only the four
  members 4a actually calls**: `GetWidth`, `GetHeight`, `Resize`,
  `ApplyTransform` (with its `transformProperty` parameter changed to
  `string`, per the `DesignItemProperty` decision above). Not ported:
  `WrapItemsNewContainer`, `UnwrapItemsFromContainer`, `ArrangeItems`,
  `StretchItems`, `CanDeleteComponents`, `IsInDocument`,
  `ComparePositionInModelFile` — all multi-selection/context-menu
  helpers with no 4a consumer (4c/4e territory).
- **`PlacementOperation.cs` (reference: `WpfDesign\Project\
  PlacementOperation.cs`) ports only what 4a's resize/rotate/skew
  lifecycle actually calls**: the `Start`/`Commit`/`Abort` lifecycle,
  `GetPlacementBehavior`, the static `GetRealElementSize` helper, and the
  property surface (`PlacedItems`, `Type`, `CurrentContainer`,
  `CurrentContainerBehavior`, `IsAborted`, `IsCommitted`, `Description`).
  **Not ported**: `ChangeContainer` and its private helper
  `TransformRectByMiddlePoint` (container-to-container move — needs
  `Vector`, `Rect.TopLeft`, `Rect.Size`, and
  `GeneralTransform.Transform(Point)`, none of which exist in WinUI, all
  confirmed absent above — and its only caller is drag-move-between-
  containers, 4b), `DeleteItemsAndCommit` (delete command wiring, out of
  scope per Phase 3's own "Out of scope" list), and
  `TryStartInsertNewComponents` (toolbox-drop insertion, no 4a caller).
  Same treatment as `ModelTools.cs`/`Metadata.cs` — port only the called
  surface, not the whole file.
- **`Metadata.IsPlacementDisabled` ports as a small standalone static
  helper** in a new file `WinUIDesigner.DesignModel/PlacementMetadata.cs`,
  not the full `Metadata.cs` (458 lines, a reflection-heavy design-time-
  attribute system already ruled out of scope in Phase 3). Same
  precedent as Phase 3's Known-gap-#2.
- **`DefaultPlacementBehavior.CanEnterContainer`'s `InfoTextEnterArea`
  ("hold Alt to enter container" hint UI) is dropped entirely** — both
  its use inside `CanEnterContainer` itself (unreachable in 4a: its only
  caller, `PlacementOperation.TryStartInsertNewComponents`, isn't ported)
  and its `InfoTextEnterArea.Stop(ref infoTextEnterArea)` call inside
  `EndPlacement` (this one **is** reachable — `EndPlacement` runs on
  every `PlacementOperation.Commit`/`Abort`). The ported `EndPlacement`
  simply omits the `InfoTextEnterArea` line; `CanEnterContainer` returns
  the plain `internalCanEnterContainer` result directly.
- **`internalCanEnterContainer` drops only its `ExtendedItem.Component is
  Decorator` branch** — `Decorator` doesn't exist in WinUI (confirmed
  absent above), no 4a consumer creates or resizes a decorator-shaped
  element. Its other three branches (`Expander`, `UserControl`,
  `CollectionSupport.CanCollectionAdd`) all port unchanged —
  `Microsoft.UI.Xaml.Controls.Expander` exists, `UserControl` is an open
  base, and `CollectionSupport.CanCollectionAdd(Type, IEnumerable)`
  already exists at `src/WinUIDesigner.XamlDom/CollectionSupport.cs`.
- **`Controls/SelectionFrame.cs` is *not* ported in 4a.** It is the
  rubber-band multi-select rectangle control (confirmed by searching the
  whole reference tree for its only instantiation site,
  `Extensions/PanelSelectionHandler.cs:75`), not a resize-frame as its
  name suggests. Out of scope per Phase 3's own "no multi-selection UI"//
  line, not assigned to 4a.
- **`DragListener` ports via WinUI pointer events, using
  `AddHandler(..., handledEventsToo: true)`, not a plain event
  subscription.** WPF's `DragListener` subscribes the *Preview*
  (tunneling) mouse events because `Thumb` handles the bubbling events
  itself first as part of its default look/feel; WinUI has no tunneling
  pointer events, only bubbling, so `target.AddHandler(UIElement.
  PointerPressedEvent, handler, handledEventsToo: true)` (and the
  matching `PointerMoved`/`PointerReleased`) is used to guarantee the
  handler fires regardless of the target's own default handling. This
  reasoning is unaffected by the `Thumb`→`Grid` base-class change below:
  a `Grid`-based thumb has no built-in pointer handling to route around,
  but `handledEventsToo: true` remains harmless and correct, and keeps
  `DragListener` reusable against either kind of target.
- **`DesignerThumb` (and therefore `ResizeThumb`/`RotateThumb`) bases on
  `Grid`, not `Thumb`.** `Thumb` is sealed (compiler-confirmed above) and
  cannot be subclassed — the same situation Phase 3 hit with `Border`.
  Since `DragListener` already treats the thumb as a plain pointer
  target and never used `Thumb`'s own `DragStarted`/`DragDelta`/
  `DragCompleted` API (see "WinUI API surface"), losing `Thumb` as a base
  costs nothing functionally. `DesignerThumb : Grid` keeps its
  `IsPrimarySelection`/`ThumbVisible`/`OperationMenu`/`Alignment`
  dependency properties and constructor-time appearance
  (`Width`/`Height`/`Background`/`BorderBrush` set directly, same
  "code-only, no `generic.xaml`" idiom as Phase 3's `SelectionHighlight
  : Grid`). The reference's `this.DefaultStyleKeyProperty.
  OverrideMetadata(...)`/`DefaultStyleKey` machinery is dropped entirely,
  not translated — it has no purpose without a `generic.xaml` style to
  look up, `Control`-only in the first place, and moot now that the base
  isn't `Control`.
- **`PointThumb.cs`/`MultiPointThumb.cs` are *not* ported in 4a.** Their
  only reference consumers (`Line`/`Path`/`PolyLine` handler extensions)
  are 4d, already listed as out of scope; the file list's earlier
  inclusion of both had no 4a rationale. (Porting them later also needs
  its own decision: the reference's `PointThumb`'s "Point" dependency
  property uses `PropertyMetadata(PropertyChangedCallback)` — WinUI's
  actual `PropertyMetadata` only has `PropertyMetadata(object)` and
  `PropertyMetadata(object, PropertyChangedCallback)`, both confirmed to
  exist, so that specific concern turns out to be a non-issue whenever
  4d does port them — noted here so it isn't re-investigated from
  scratch.)
- **`DragListener` gains a pointer-position query it didn't need in the
  first draft.** `RotateThumbExtension`/`SkewThumbExtension` call WPF's
  `Mouse.GetPosition(element)` *mid-drag, outside any event handler*.
  WinUI has no ambient pointer-position API — position is only available
  from a live `PointerRoutedEventArgs`. The ported `DragListener` retains
  the last `PointerPoint` sampled during `PointerMoved` (window
  coordinates) and adds `GetPosition(UIElement relativeTo)`, converting
  via `relativeTo.TransformToVisual(null).Inverse?.TransformPoint(...)`
  — guarded against `Inverse` returning `null` (confirmed possible, see
  "WinUI API surface"; WPF's `Matrix.Invert()` threw instead, so this
  guard has no WPF-side analogue to copy).
- **`DragListener.Delta`'s transform-aware math uses `GeneralTransform.
  Inverse`/`TransformPoint` on both `StartPoint` and `CurrentPoint`
  individually, subtracting the results** — algebraically equivalent to
  the reference's `Matrix.Invert()` + `Matrix.Transform(Vector)` for an
  affine transform (translation cancels out of the difference). `Delta`
  becomes a plain `(double X, double Y)` value, not a `Vector` (confirmed
  absent).
- **Cursors:** `DesignerThumb` exposes a public forwarding member for its
  own `ProtectedCursor`. WPF's `Cursors.SizeNS`/`SizeWE`/`SizeNESW`/
  `SizeNWSE` map to `InputSystemCursorShape.SizeNorthSouth`/
  `SizeWestEast`/`SizeNortheastSouthwest`/`SizeNorthwestSoutheast`
  (confirmed exact names above — no longer a plan-time hedge).
  `RotateThumbExtension`'s `Cursors.Hand` maps to
  `InputSystemCursorShape.Hand` (also confirmed present) — the rotate
  thumb gets a real system hand cursor, not a placeholder.
  `SkewThumbExtension`'s `Cursors.ScrollWE`/`ScrollNS` have no equivalent
  (confirmed absent) and fall back to `SizeWestEast`/`SizeNorthSouth` — a
  disclosed simplification. The reference's custom `.cur`-resource rotate
  cursor path (`ZoomControl.GetCursor`, itself out of scope) is not
  ported. The reference's `cursor == Cursors.SizeNS` reference-equality
  check (which would silently break against WinUI's
  `InputSystemCursor.Create()`, a fresh instance every call) is not
  ported as-is — the port passes the cursor *shape* value directly
  instead of comparing cursor objects.
- **Ctrl-modifier reads (`ResizeThumbExtension`'s aspect-ratio lock,
  `RotateThumbExtension`'s 15°-snap disable) resolve from the pointer
  event, not a global `Keyboard.IsKeyDown` query** — same choice Phase 3
  made for `SelectionTypes.Auto` resolution. `DragListener`'s `Changed`
  callback carries the originating pointer event's `KeyModifiers`.
- **`Visibility.Hidden` (used by `UpdateAdornerVisibility` to hide resize
  handles that don't apply) is replaced with `Visibility.Collapsed`**,
  not `Opacity`/`IsHitTestVisible` (a prior revision's mistake). Verified
  against `AdornerPanel.MeasureOverride`/`ArrangeOverride`
  (`src/WinUIDesigner.DesignModel/Adorners/AdornerPanel.cs`): every child
  is measured and arranged independently via its own `AdornerPlacement`,
  and the panel's own reported size comes from the *adorned element*, not
  from aggregating children — so a `Collapsed` child doesn't perturb its
  siblings' layout, unlike a WPF `StackPanel`-style layout where
  `Collapsed` removes reserved space others depend on. This is a
  *different* mechanism than the reference's own `Opacity = 0` on its
  stretched center-edge thumbs (`ResizeThumbExtension.Place`) — that one
  exists to keep a thumb invisible but still hit-testable (a hit-test
  strip), which `Visibility.Collapsed` would defeat (collapsed elements
  aren't hit-testable). The port keeps both mechanisms distinct:
  `Opacity = 0` stays for the hit-testable-but-invisible strips exactly
  as the reference uses it; `Visibility.Collapsed` replaces `Visibility.
  Hidden` for "this handle doesn't apply, don't show or hit-test it."
- **`IsPrimarySelection`/`ThumbVisible` get their visual effect wired
  directly in code**, since the "code-only appearance, no `generic.xaml`"
  decision (kept — same simplification Phase 3 used for
  `SelectionHighlight`) means the WPF style `Trigger`s that currently
  drive both properties' visible effect have nothing to port to. Each
  property's changed-callback (`PropertyMetadata(object,
  PropertyChangedCallback)`, confirmed to exist) updates the thumb's
  `Visibility`/`Background`/`BorderBrush` directly instead.
- **`ResizeThumbExtension`'s `SizeDisplayExtension`/`MarginHandleExtension`
  interaction (`ShowSizeAndHideHandles`/`HideSizeAndShowHandles`) is
  deleted outright, not null-checked.** Both methods and their two call
  sites (`drag_Started`, `drag_Completed`) reference
  `SizeDisplayExtension`/`MarginHandleExtension` by type — types that
  don't exist in this port (4e territory) — so there is no "degrade
  gracefully without them" path; the code simply doesn't compile unless
  removed.
- **`TryFindParent<T>`, used by `DesignerThumb.ReDraw` and
  `SkewThumbExtension`, ports as a small manual `VisualTreeHelper.
  GetParent` walk** — sourced from the reference's
  `UIExtensions/UIHelpers.cs:75` (confirmed present at that path with
  that member; its default `searchCompleteVisualTree = false` prefers
  `FrameworkElement.Parent` over `VisualTreeHelper.GetParent`, which
  matches both 4a call sites' actual logical/visual parent, so the
  narrower walk is safe here). Lands alongside a small `Rect.Round()`
  extension method (reference: `ExtensionMethods.cs:33`, confirmed
  present, its two siblings are `DependencyPropertyDescriptor`-based and
  correctly excluded) in a new
  `WinUIDesigner.DesignModel/GeometryExtensions.cs`.
- **`SharedInstances.BoxedTrue`** (reference path corrected:
  `WpfDesign.Designer\Project\SharedInstances.cs`, not
  `Controls/SharedInstances.cs`) is inlined as a `static readonly object`
  field directly in `DesignerThumb.cs` rather than porting a whole
  separate file for one value — moot either way, since WinUI's
  `PropertyMetadata(object)` constructor accepts the boxed value
  directly regardless of where it's declared.
- **`WindowClone`-related guard branches in `RotateThumbExtension`/
  `SkewThumbExtension` are dropped**, not translated — `WindowClone`
  isn't part of this port yet, so the guarded code paths are unreachable
  in 4a's scope.
- **Both copies of `GetRealElementSize` are kept, not consolidated.**
  Phase 3's private copy in `AdornerPanel.cs` and this spec's new public
  `PlacementOperation.GetRealElementSize` don't conflict (different
  classes, different accessibility); consolidating them is a forward-flag
  for later, not a 4a defect.
- **`DesignerThumb`'s dependency properties use WinUI's actual
  `DependencyProperty.Register` signature** (`string name, Type
  propertyType, Type ownerType, PropertyMetadata metadata`), not the
  reference's WPF 3-arg-plus-`FrameworkPropertyMetadata` form.
- **`DesignItem.GetCompleteAppliedTransformationToView()` is ported with
  its transform sources narrowed to `RenderTransform` only** — WinUI has
  no `LayoutTransform` or `ContainerVisual.Transform` (confirmed absent).
  The port walks `VisualTreeHelper.GetParent` (same pattern as Phase 3's
  `IsDescendantOf`) accumulating only `RenderTransform` into a
  `TransformGroup`. Whether re-adding a live, already-parented
  `RenderTransform` instance into a new `TransformGroup.Children`
  collection is safe in WinUI is not assumed — see Known gap #3.
- **The DoD's simulated-input seam is method-based, not args-based.**
  The reference's `DragListener.ExternalStart()`/
  `ExternalMouseMove(MouseEventArgs)`/`ExternalStop()` test seam can't
  port literally: `Microsoft.UI.Xaml.Input.PointerRoutedEventArgs` is a
  sealed WinRT type with no public constructor, so tests cannot fabricate
  one. The ported `DragListener` instead exposes position-and-modifiers-
  taking methods (e.g. `SimulateDown(Point)`, `SimulateMove(Point,
  Windows.System.VirtualKeyModifiers)`, `SimulateUp(Point)` — exact
  shape is a plan-time choice) that tests call directly, bypassing real
  pointer args entirely. Same requirement applies to the Ctrl-modifier
  decision above: a test needs to pass `VirtualKeyModifiers` explicitly
  since there's no real pointer event to read them from.
- **Forward-flag for 4b, not a 4a decision:** `DesignItem.AddBehavior`
  (last-writer-wins, no conflict detection) is safe for 4a because
  `Panel`/`Control`/`Border`/`Viewbox` are mutually disjoint in WinUI's
  type hierarchy. It stops being safe once 4b adds `GridPlacementSupport`
  (`[ExtensionFor(typeof(Grid))]`): a `Grid` then matches both it and
  `DefaultPlacementBehavior` (`[ExtensionFor(typeof(Panel))]`), and which
  one wins depends on `ExtensionAttribute.Order`, not specificity. 4b's
  spec needs `ExtensionForAttribute.OverrideExtensions` (or equivalent).

## Architecture

```
D:\Project\ST4I\Research\WinUIDesigner\
  src\
    WinUIDesigner.DesignModel\
      PlacementType.cs                    (new)
      PlacementAlignment.cs               (new)
      PlacementInformation.cs             (new)
      PlacementBehavior.cs                (new: IPlacementBehavior, IRootPlacementBehavior)
      PlacementOperation.cs               (new: Start/Commit/Abort/GetPlacementBehavior/
                                             GetRealElementSize + properties only —
                                             ChangeContainer/DeleteItemsAndCommit/
                                             TryStartInsertNewComponents NOT ported, see
                                             "Decisions already made")
      PlacementMetadata.cs                (new: IsPlacementDisabled static helper)
      GeometryExtensions.cs               (new: Rect.Round() extension, TryFindParent<T> helper)
      DesignItem.cs                       (modify: add GetCompleteAppliedTransformationToView,
                                             RenderTransform-only)
      ServiceContainer.cs                 (modify: add .DesignPanel convenience property,
                                             mirrors .Selection)
      Extensions\
        BehaviorExtension.cs              (new)
        SelectionExtensionServers.cs      (modify: add OnlyOneItemSelectedExtensionServer only)
      Adorners\
        RelativePlacement.cs              (new)
    WinUIDesigner.Designer\
      ModelTools.cs                       (new: GetWidth, GetHeight, Resize, ApplyTransform only
                                             — ApplyTransform's transformProperty param is string)
      DesignPanel.cs                      (modify: expose public AdornerLayer property,
                                             currently private field)
      Extensions\
        DefaultPlacementBehavior.cs       (new)
        ResizeThumbExtension.cs           (new: AdornerProvider subclass directly,
                                             [ExtensionFor(typeof(FrameworkElement))]
                                             [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))],
                                             SizeDisplay/MarginHandle interaction deleted)
        RotateThumbExtension.cs           (new, same attribute pattern)
        SkewThumbExtension.cs             (new, same attribute pattern)
      Controls\
        DragListener.cs                   (new: WinUI pointer events + AddHandler(handledEventsToo:true),
                                             adds GetPosition(UIElement) and Simulate*(...) test seam)
        Thumbs\
          DesignerThumb.cs                (new: bases on Grid, not Thumb — Thumb is sealed)
          ResizeThumb.cs                  (new: bases on DesignerThumb)
          RotateThumb.cs                  (new: bases on DesignerThumb)
  tests\
    WinUIDesigner.Designer.Tests\
      ResizeThumbExtensionTests.cs        (new)
      RotateThumbExtensionTests.cs        (new)
      SkewThumbExtensionTests.cs          (new)
      PlacementOperationTests.cs          (new)
  docs\superpowers\specs\
    2026-09-01-winui-designer-phase4a-design.md   (this file)
```

`PointThumb.cs`/`MultiPointThumb.cs` are **not** in this list — no 4a
consumer (see "Decisions already made").

New WinUI-specific mechanisms introduced at this layer (mechanical swaps
from the reference, verified against the assemblies/docs/compiler above):

| WPF mechanism | WinUI replacement |
| --- | --- |
| `DesignerThumb : Thumb` (+ `DefaultStyleKeyProperty.OverrideMetadata`) | `DesignerThumb : Grid` — `Thumb` confirmed sealed (`CS0509`), `Grid` confirmed open. Appearance set directly in the constructor (`Width`/`Height`/`Background`/`BorderBrush`), same idiom as Phase 3's `SelectionHighlight : Grid`. `OverrideMetadata`/`DefaultStyleKey` dropped, not translated — no purpose without a `Control` base or a `generic.xaml` style. |
| `Target.PreviewMouseLeftButtonDown/Move/Up` (`DragListener`) | `target.AddHandler(UIElement.PointerPressedEvent, handler, handledEventsToo: true)` and the `PointerMoved`/`PointerReleased` equivalents — WinUI has no tunneling events. |
| `Mouse.GetPosition(null)` / `e.GetPosition(null)` (screen coordinates) | `PointerRoutedEventArgs.GetCurrentPoint(null).Position` — window/island coordinates. `DragListener.Delta` only consumes the *difference* of two such points; `DragListener.GetPosition(element)` (new) converts a captured point via `element.TransformToVisual(null).Inverse?.TransformPoint(...)`. |
| `SystemParameters.MinimumHorizontalDragDistance`/`MinimumVerticalDragDistance` | No WinUI equivalent (confirmed absent) — local constant, exact value a plan-time choice. |
| `InputManager.Current.PostProcessInput` (global Escape-to-cancel-drag) | No WinUI equivalent (confirmed absent). Replacement mechanism is **Known gap #1**. |
| `Transform.Value` + `Matrix.Invert()` + `Matrix.Transform(Vector)` (`DragListener.Delta`) | `GeneralTransform.Inverse` (nullable — guard it) `.TransformPoint(...)` applied to `StartPoint`/`CurrentPoint` individually, subtracted component-wise. `Matrix`/`Vector` confirmed unusable/absent by compiler. |
| `Vector`, `Rect.TopLeft`, `Rect.Size`, `GeneralTransform.Transform(Point)` (`PlacementOperation.ChangeContainer`) | Not ported — `ChangeContainer` itself isn't ported (4b), see "Decisions already made". |
| `Properties.GetProperty(FrameworkElement.WidthProperty)` / `Properties[dp]` (DP-indexed) | `Properties.GetProperty("Width")` (string-indexed) — no DP-indexed overload exists in this codebase. |
| `Visibility.Hidden` | `Visibility.Collapsed` — WinUI's `Visibility` has only `Visible`/`Collapsed`, and `AdornerPanel` doesn't reserve layout space per-child, so `Collapsed` is safe here (verified by reading `AdornerPanel`'s measure/arrange). The reference's separate `Opacity = 0` idiom (hit-testable-but-invisible strips) is kept as-is, unchanged. |
| `element.Cursor = cursor` | A public forwarding member on `DesignerThumb` that sets the protected `ProtectedCursor`. `Cursors.Size*`/`Cursors.Hand` map to `InputSystemCursorShape.Size*`/`Hand` (exact names confirmed). `Cursors.ScrollWE`/`ScrollNS` have no equivalent — fall back to `SizeWestEast`/`SizeNorthSouth`. |
| `FrameworkPropertyMetadata` + 3-arg `DependencyProperty.Register` | `PropertyMetadata` (2 ctors: `(object)`, `(object, PropertyChangedCallback)`) + WinUI's 4-arg `Register(string, Type, Type, PropertyMetadata)`. |
| `element.TransformToAncestor(ancestor)` / `TranslatePoint` | `element.TransformToVisual(target)` — same replacement Phase 3 already established. |
| `Vector.AngleBetween(v1, v2)` | Manual `Math.Atan2`-based angle computation — no WinUI `Vector` type. |
| `new SkewTransform(angleX, angleY)` (parameterized ctor) | `new SkewTransform { AngleX = ..., AngleY = ... }` — WinUI transform types expose only parameterless constructors. |
| `element.TryFindParent<T>()` (`DesignerThumb.ReDraw`, `SkewThumbExtension`) | Manual `VisualTreeHelper.GetParent`/`FrameworkElement.Parent` walk (`GeometryExtensions.cs`) — no WinUI-ported equivalent exists yet, same pattern used since Phase 3's `IsDescendantOf`. |
| `FrameworkElement.LayoutTransform` + `ContainerVisual.Transform` (in `GetCompleteAppliedTransformationToView`) | Dropped — confirmed absent from WinUI's object model. Only `RenderTransform` is accumulated. |
| `ExtendedItem.Component is Decorator` (`internalCanEnterContainer`) | Branch dropped — `Decorator` confirmed absent, its three sibling branches (`Expander`/`UserControl`/`CollectionSupport`) port unchanged. |
| `DragListener.ExternalStart()`/`ExternalMouseMove(MouseEventArgs)`/`ExternalStop()` (test seam) | `SimulateDown(Point)`/`SimulateMove(Point, VirtualKeyModifiers)`/`SimulateUp(Point)` — `PointerRoutedEventArgs` has no public constructor, so an args-based seam isn't portable. |

## Data flow

Pointer press on a `DesignerThumb` (a `Grid`-based visual, child of the
`AdornerPanel` Phase 3's `AdornerLayer` positions over the selected
element, placed via the newly-ported `RelativePlacement`) →
`DragListener` (subscribed with `AddHandler(..., handledEventsToo:
true)`) captures the pointer, records the start point in window
coordinates → first move past the drag-distance threshold fires
`Started` → `ResizeThumbExtension.drag_Started` calls
`PlacementOperation.Start(extendedItemArray, PlacementType.Resize)`,
which resolves `DesignItem.GetBehavior<IPlacementBehavior>()` on the
parent (`DefaultPlacementBehavior`, registered in `OnInitialized` via
`ExtendedItem.AddBehavior`) and calls its `BeginPlacement`/`GetPosition`
→ each subsequent `PointerMoved` fires `drag_Changed`, which computes a
new width/height from the thumb's `PlacementAlignment` and the
transform-aware `Delta` (`GeneralTransform.Inverse`-based), applies it
via `ModelTools.Resize` (setting `DesignItemProperty` string-keyed
values, so each resize step is undo-tracked), and updates
`PlacementInformation.Bounds` →
`operation.CurrentContainerBehavior.SetPosition(info)` → pointer release
fires `drag_Completed`, which commits (or aborts, on cancel) the
`PlacementOperation`'s underlying `ChangeGroup` (Phase 2's `UndoService`
integration, unchanged) — one `ChangeGroup` per whole drag.
`RotateThumbExtension`/`SkewThumbExtension` follow the same
`DragListener` lifecycle but call `ModelTools.ApplyTransform` (via the
new `Services.DesignPanel` accessor) with a `RotateTransform`/
`SkewTransform` instead of resizing. `ApplyTransform` opens and commits
its **own** `ChangeGroup` on every call — once per `PointerMoved` — but
(final-review fix, findings #1/#2; corrects this spec's original text,
which had claimed the per-tick granularity below matched the
reference's own, a claim verified false against the actual reference
source) each drag is *also* bracketed in its own outer `ChangeGroup`,
opened via `DesignItem.OpenGroup` in `Drag_Started` and committed (or
aborted, on cancel) in `Drag_Completed` — `RotateThumbExtension` opens
one group for its single drag; `SkewThumbExtension` opens one
independently per handle (X and Y), matching the reference's own
per-handle granularity. Every per-tick `ApplyTransform` `ChangeGroup`
then nests inside that outer group (`UndoService.Execute` routes into
whichever transaction is currently open), so the whole gesture — however
many ticks it took — collapses into a single undo-stack entry, the same
drag-level granularity `ResizeThumbExtension`'s `PlacementOperation`
already gives resize. `ApplyTransform` also calls
`AdornerLayer.UpdateAdornersForElement` (Phase 3, unchanged, now reached
through `DesignPanel`'s new public `AdornerLayer` property) so the
selection highlight and thumb positions re-arrange after every transform
change.

## Testing / Definition of Done

Extends `WinUIDesigner.TestHost` (Phase 2/3) — same `.exe` NUnitLite
runner, same live-`Window` mechanism Phase 3's investigation established
(`WinUITestHost.RunWithWindow`). Fixtures build a real `DesignSurface`
(not a bare `XamlDesignContext`) so `Services.DesignPanel` resolves.

Phase 4a is done when:

- A test drives a corner resize thumb via `DragListener`'s `Simulate*`
  seam (not synthesized OS input — `PointerRoutedEventArgs` has no
  public constructor to fabricate) on a selected `DesignItem` and
  asserts the item's `Width`/`Height` properties update to the expected
  values, and that a single `ChangeGroup` was committed (undo reverts the
  resize in one step).
- A test drags an edge (non-corner) resize thumb and asserts only the
  corresponding dimension (`Width` or `Height`) changed.
- A test drags a rotate thumb and asserts the item's `RenderTransform`
  becomes a `RotateTransform` with the expected `Angle`, that the
  `AdornerLayer`'s thumb/highlight positions re-arrange afterward
  (`TransformToVisual`-based assertion, same pattern as Phase 3's
  `SelectedElementsAdornerPanelIsArrangedAtTheElementsTransformedPosition`
  test), and that undo reverts it — a single undo after a multi-step
  rotate drag reverts the *entire* drag in one step, the same
  single-group-per-drag granularity the resize path already has
  (final-review fix, findings #1/#2; corrects this bullet's original
  text, which had wrongly claimed a single undo only reverts the last
  step "matching the reference's own granularity" — verified false
  against the actual reference source, see `RotateThumbExtension`'s
  class remarks).
- A test drags a skew thumb and asserts a `SkewTransform` is applied.
- A test cancels an in-progress resize (via whatever Escape-cancel
  mechanism gap #1 below resolves to) and asserts the item's
  `Width`/`Height` revert to their pre-drag values and no `ChangeGroup`
  was committed.
- Each of the four flagged gaps below has an explicit, tested resolution.

## Known gaps — flagged, not guessed

1. **No WinUI equivalent for `InputManager.Current.PostProcessInput`'s
   global Escape-to-cancel-drag.** Confirmed absent from
   `Microsoft.UI.Xaml`. Candidate replacement: subscribe to `KeyDown` on
   the `DesignerThumb` itself while it holds pointer capture, or on
   `DesignPanel` (which Phase 3's spec already flagged as needing to hold
   focus for "all key handling... deferred to Phase 4" — this is that
   deferred work's first concrete instance). Whether pointer-captured
   elements still receive `KeyDown` in WinUI 3, and whether `DesignPanel`
   needs `IsTabStop`/explicit `Focus()` calls to receive it, **must be
   verified empirically as the plan's first task**, the same way Phase 3
   verified live-`Window` layout timing before building on it.
2. **Exact drag-distance threshold value.** WPF's
   `SystemParameters.MinimumHorizontalDragDistance`/
   `MinimumVerticalDragDistance` default to 4px/2px but have no WinUI
   equivalent to read at runtime. Whether to hardcode those same pixel
   values, use a different constant, or derive one from
   `DisplayInformation`/`XamlRoot.RasterizationScale` for DPI-awareness is
   a plan-time choice.
3. **Whether re-parenting a live, already-parented `RenderTransform`
   instance into a new `TransformGroup.Children` collection is safe in
   WinUI.** `GetCompleteAppliedTransformationToView` builds its result by
   adding each ancestor's *live* `RenderTransform` object into a new
   `TransformGroup`. WPF allows this because `Transform` is a shareable
   `Freezable`; WinUI's `Transform` is a plain `DependencyObject`, with
   generally stricter parenting rules. **Must be verified empirically** —
   if adding a live instance throws or silently reparents the visual
   tree in an unwanted way, the fix is to copy the transform's property
   values into a fresh instance instead of reusing the reference.
4. **Whether `ModelTools.ApplyTransform`'s `TransformGroup`-wrapping path
   works through `WinUIDesigner.XamlDom`'s component registration.**
   When an existing transform of a different type needs to become part
   of a `TransformGroup`, the reference does
   `RegisterComponentForDesigner(new TransformGroup())` then
   `tgDes.ContentProperty.CollectionElements.Add(...)` — this requires
   `DesignItem.ContentPropertyName` to resolve to `TransformGroup.
   Children` for a WinRT type. No prior phase has verified that WinRT
   `[ContentProperty]` metadata resolves correctly through this port's
   component-registration path for a non-`FrameworkElement` type like
   `TransformGroup`. **Must be verified empirically** alongside gap #3 —
   if it fails, the fallback is to detect the pre-existing-different-
   transform case and rebuild a flat replacement instead of wrapping.

## Out of scope for Phase 4a

`GridPlacementSupport`/`CanvasPlacementSupport`/`StackPanelPlacementSupport`
and all other container-specific `IPlacementBehavior` implementations,
snaplines, drag-move (`PanelMove`, `TopLeftContainerDragHandle`),
container enter/leave during a drag (`PlacementOperation.ChangeContainer`/
`TryStartInsertNewComponents`, not ported — see "Decisions already made"),
rubber-band multi-select (`SelectionFrame`'s real purpose),
`PointThumb`/`MultiPointThumb` and their actual consumers (`Line`/`Path`/
`PolyLine` handler extensions — 4d), context menus,
`SizeDisplayExtension`/`MarginHandleExtension` (4a's `ResizeThumbExtension`
has their interaction deleted outright, not stubbed), arrow-key nudge,
themed cursor resources (`.cur` files via `ZoomControl`),
`WindowClone`/`ThumbnailView` integration, and the ~50 remaining
behaviors listed in the Phase 4 brainstorming breakdown (4b-4e) — per
that decomposition, each gets its own spec once 4a ships.
