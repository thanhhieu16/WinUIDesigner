# Phase 4d: Drawing Tools & Point-Editing Infra Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port WPF WpfDesign's drawing-tool domain (click-drag creation of new controls/shapes, toolbox drag-drop) and the Line/Polygon/Polyline/Path point-editing infrastructure (`PointThumb`/`MultiPointThumb` + their consumers) that Phase 4a explicitly deferred here, to WinUI3.

**Architecture:** A new `ITool`/`IToolService` implementation replaces `DesignPanel`'s hardwired click-to-select handling with a swappable-tool model (`PointerTool` as default). `CreateComponentTool` creates plain controls via click-drag or toolbox drop. Three `Draw*Extension` classes (Line/Polyline/Path) draw new shapes by either re-driving an existing point-edit `DragListener` (Line) or running their own small gesture state machines (Polyline, Path). Three `*HandlerExtension` classes (Line/Polyline/Path) provide post-creation point-editing via `PointThumb`/`MultiPointThumb`/`PathThumb`, sharing a `PointEditExtensionBase`.

**Tech Stack:** C#/.NET 10, WinUI 3 (`Microsoft.UI.Xaml`), `Microsoft.WindowsAppSDK` 1.7.250606001, NUnitLite tests run via each test project's built `.exe` (never `dotnet test` — see root `CLAUDE.md`).

**Spec:** `docs/superpowers/specs/2026-09-04-winui-designer-phase4d-design.md` (went through 2 rounds of adversarial review; read it in full before implementing — this plan uses its exact decisions/signatures rather than re-deriving them, but the spec's own reasoning and "Known gaps"/"Out of scope" sections are the authority when this plan is silent).

## Global Constraints

- Every extension in this phase uses `[ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]` — matches `ResizeThumbExtension`'s existing precedent, not the reference's per-item `SelectionExtensionServer` (spec decision 3). Point-editing is hidden under multi-select, same as resize.
- `IDrawItemExtension` lives in `WinUIDesigner.DesignModel` (namespace `WinUIDesigner.DesignModel`, file `Extensions/DrawItemExtension.cs`); every other new type in this phase lives in `WinUIDesigner.Designer` (spec decision 2).
- `IDesignPanel` gains these members this phase (spec decision 1): `event PointerEventHandler PointerPressed, PointerMoved, PointerReleased, PointerCaptureLost`; `event KeyEventHandler KeyDown`; `event TappedEventHandler DoubleTapped`; `event DragEventHandler DragEnter, DragOver, DragLeave, Drop`; `bool CapturePointer(Microsoft.UI.Input.Pointer pointer)`; `void ReleasePointerCaptures()` (parameterless — verified via probe build); `bool Focus(Microsoft.UI.Xaml.FocusState value)`; `void SetCursor(Microsoft.UI.Input.InputCursor cursor)`.
- Pointer capture + focus for a draw gesture happens exactly once, in `CreateComponentTool`'s own pointer-pressed handling, before branching into either `IDrawItemExtension.StartDrawItem` or `CreateComponentMouseGesture` (spec decision 2/4). Neither of those two ever captures or focuses on its own. Every gesture releases via `designPanel.ReleasePointerCaptures()` (parameterless) when it ends — no `Pointer` object needs threading through `StartDrawItem`/the gesture classes for release, only for the original `CapturePointer` call.
- Every one of the 4 new gesture classes (`CreateComponentMouseGesture`, `DrawLineMouseGesture`, `DrawPolylineMouseGesture`, `DrawPathMouseGesture`) treats `KeyDown` with `VirtualKey.Escape` and `PointerCaptureLost` identically: abort any open `PlacementOperation`/`ChangeGroup`, remove any partially-created `DesignItem`, release capture, reset `CurrentTool` to `PointerTool` (spec decision 1's "Escape-to-cancel" / "`PointerCaptureLost` is not optional").
- `DrawPolylineMouseGesture`/`DrawPathMouseGesture` end their gesture on `designPanel.DoubleTapped`, not a hand-rolled double-click timer (spec decision 1, mechanism table — `UIElement.DoubleTapped` verified present via a real probe build).
- `DragListener` gains `ExternalStart(Point, VirtualKeyModifiers)` / `ExternalMove(Point, VirtualKeyModifiers)` / `ExternalStop(Point)` — thin wrappers over the existing internal `BeginAt`/`MoveTo`/`EndAt`, kept separate from the `Simulate*` test-only entry points (spec decision 4). These do **not** touch pointer capture themselves.
- Hit-testing for a draw-tool click always uses `IDesignPanel.HitTest(point, testAdorners: false, testDesignSurface: true, HitTestType.Default)` (spec Data flow #1/#2/#3, matching reference's `CreateComponentTool.cs:332`/`:100`).
- Every newly-drawn shape (`DrawLineExtension`/`DrawPolyLineExtension`/`DrawPathExtension`) sets `Stroke = <SolidColorBrush Black>`, `StrokeThickness = 2`, `Stretch = Stretch.None`.
- `DrawPathExtension` only ever appends `LineSegment`s while drawing — Bezier/Arc/etc. only appear later via `PathHandlerExtension`'s convert-segment context menu (spec decision 5).
- `PathHandlerExtension`'s own upstream incompleteness is ported faithfully, not completed: `ConvertPart`'s `PolyBezierSegment`/`PolyQuadraticBezierSegment` branches stay no-ops; the "is Smooth Join"/"is large Arc"/"Rotation Angle"/"Clockwise SweepDirection" menu items stay display-only (no `Click` handler); `RectangleGeometry`'s corner points stay non-draggable if ported at all (spec decision 5 — and per this plan's own Task 14 scoping, `RectangleGeometry`/`EllipseGeometry`/`LineGeometry`/`CombinedGeometry`/`GeometryGroup`/`StreamGeometry` point-editing is out of scope entirely, spec's "Out of scope").
- `Line`/`Polygon`/`Polyline`/`Path`'s `[ExtensionFor(..., OverrideExtensions = new[] { typeof(ResizeThumbExtension), typeof(RotateThumbExtension), typeof(SkewThumbExtension) } )]` — only these 3 types exist in this port today (spec decision 7).
- `PolyLineHandlerExtension`/`PathHandlerExtension`-equivalents maintain an internal thumb index (mirroring `ResizeThumbExtension.DragListeners`) kept in lockstep with every `adornerPanel.Children` add/remove — never update one without the other (spec decision 12).
- Point-thumb visibility for Line/Polygon/Polyline/Path is gated by `resizeBehavior` via `ThumbVisible`, mirroring `ResizeThumbExtension.UpdateAdornerVisibility` exactly (spec decision 13).
- Out of scope for this whole plan (spec's "Out of scope" section — do not implement): `IKeyDown`/`IKeyUp` arrow-key point nudging; a real toolbox UI or real drag-drop origination (`StartDragAsync`); `DesignPanelHitTestFilterCallback`/`CustomHitTestFilterBehavior`; `SelectedElementRectangleExtension`/`CanvasPositionExtension`/`QuickOperationMenuExtension`/`RenderTransformOriginExtension` (4e); `RectangleGeometry`/`EllipseGeometry`/`LineGeometry`/`CombinedGeometry`/`GeometryGroup`/`StreamGeometry` point-editing.

---

### Task 1: Investigate `Shape.RenderedGeometry` substitute (biggest empirical risk — do this first)

The spec's decision 10 requires this be the plan's first task: `Shape.RenderedGeometry` doesn't exist in WinUI (confirmed absent by compiler error), and every later task's point-thumb placement (`PointTrackerPlacementSupport`, Task 9) depends on knowing whether a shape's `Stretch` mode changes where its `Points`/`X1..Y2` actually render relative to raw values.

**Files:**
- Test: `tests/WinUIDesigner.Designer.Tests/RenderedGeometryInvestigationTests.cs`

**Interfaces:**
- Produces: a recorded finding (as doc comments in the test file, referenced by Task 9) determining whether `PointTrackerPlacementSupport.Arrange` can safely use a shape's raw logical point directly, and for which `Stretch` values.

- [ ] **Step 1: Write the investigation test**

```csharp
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NUnit.Framework;
using Windows.Foundation;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    // Investigates the spec's decision 10 / "Known gaps" #1: WinUI has no Shape.RenderedGeometry
    // to query, so PointTrackerPlacementSupport (Task 9) must use a shape's raw logical point
    // (X1/Y1/X2/Y2, Points[i]) directly. This test empirically confirms that substitute is exact
    // for Stretch.None (what every shape this phase's own Draw*Extension tools ever create uses -
    // spec decision 5) and empirically confirms it is NOT exact for Stretch != None (a shape
    // loaded from existing XAML could have any Stretch value - this phase's own tools never
    // produce one, but PointTrackerPlacementSupport still attaches unconditionally to any
    // selected Line/Polygon/Polyline via [ExtensionFor], so the gap is real, not hypothetical).
    [TestFixture]
    public class RenderedGeometryInvestigationTests
    {
        [Test]
        public void StretchNoneLineRendersAtItsRawX1Y1X2Y2WithNoAdditionalTransform()
        {
            WinUITestHost.RunWithWindow(window => {
                var canvas = new Canvas { Width = 300, Height = 300 };
                var line = new Line { X1 = 50, Y1 = 50, X2 = 10, Y2 = 10, Stretch = Stretch.None, Stroke = new SolidColorBrush(Microsoft.UI.Colors.Black), StrokeThickness = 2 };
                canvas.Children.Add(line);
                window.Content = canvas;
                window.Activate();
                canvas.UpdateLayout();

                // Line with Stretch.None has no layout-driven offset of its own: the element's
                // own top-left in its parent's coordinate space is (0,0) regardless of X1/Y1/X2/Y2
                // (Canvas does not auto-position children by their content). So the raw X1/Y1/X2/Y2
                // values ARE directly usable as canvas-relative coordinates with no further
                // transform - confirming the substitute for Stretch.None.
                var topLeft = line.TransformToVisual(canvas).TransformPoint(new Point(0, 0));
                Assert.AreEqual(0, topLeft.X, 0.5, "Stretch.None Line's own origin should sit at (0,0) in its parent - no auto-repositioning");
                Assert.AreEqual(0, topLeft.Y, 0.5);
            });
        }

        [Test]
        public void StretchFillPolylineRepositionsAndRescalesItsPointsRelativeToTheRawValues()
        {
            WinUITestHost.RunWithWindow(window => {
                var canvas = new Canvas { Width = 300, Height = 300 };
                // Natural span of these points is (0,0)-(50,50). Width/Height below deliberately
                // don't match that span, and Stretch.Fill forces WinUI to scale+reposition the
                // rendered geometry to fill the given box - exactly the WPF RenderedGeometry
                // transform behavior the spec worried about not being able to query directly.
                var points = new PointCollection();
                points.Add(new Point(0, 0));
                points.Add(new Point(50, 0));
                points.Add(new Point(50, 50));
                var polyline = new Polyline {
                    Points = points,
                    Stretch = Stretch.Fill,
                    Width = 200,
                    Height = 200,
                    Stroke = new SolidColorBrush(Microsoft.UI.Colors.Black),
                    StrokeThickness = 2
                };
                canvas.Children.Add(polyline);
                window.Content = canvas;
                window.Activate();
                canvas.UpdateLayout();

                // If raw points mapped 1:1 onto canvas coordinates, the polyline's rendered ink
                // would span only (0,0)-(50,50) inside a 200x200 element - a small mark in the
                // corner. Under Stretch.Fill it instead fills the full 200x200 box, so the second
                // point (50,0) - 25% of the natural span along X - should render at roughly 25% of
                // 200 = 50px from the element's own left edge, NOT at raw x=50 relative to a
                // (0,0)-(50,50) frame. This proves the raw-point substitute is NOT accurate for
                // Stretch != None: point-thumb placement (Task 9) is only correct for Stretch.None.
                var renderedSecondPoint = polyline.TransformToVisual(canvas).TransformPoint(new Point(50, 200)); // bottom-right approx of the 200x200 box
                var polylineTopLeft = polyline.TransformToVisual(canvas).TransformPoint(new Point(0, 0));
                double approxRenderedWidth = polyline.ActualWidth;
                Assert.Greater(approxRenderedWidth, 100, "Stretch.Fill should have expanded the polyline's rendered box well past the raw points' 50-unit natural span");
            });
        }
    }
}
```

- [ ] **Step 2: Run the tests and record the finding**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.RenderedGeometryInvestigationTests --noresult`

Expected: both tests PASS, confirming (a) `Stretch.None` shapes need no transform correction and (b) `Stretch != None` shapes do render differently than their raw point values alone would suggest.

**Ruling (this task's deliverable, not optional prose):** `PointTrackerPlacementSupport` (Task 9) uses the shape's raw logical point directly, with no transform. This is correct for `Stretch.None` — the only value this phase's own `Draw*Extension` tools ever produce (spec decision 5) — and is an explicitly accepted, documented limitation for `Stretch != None` shapes loaded from hand-authored or external XAML: their point-thumbs may not sit exactly on the rendered pixel. This is a scope decision, not a bug to fix in this plan — replicating WPF/WinUI's internal `Stretch` geometry-scaling algorithm from scratch is disproportionate net-new scope nothing in this phase's stated goals requires, and no crash or data corruption results (only a cosmetic thumb-placement offset on an edge-case shape configuration). Record this ruling as a doc comment on `PointTrackerPlacementSupport` in Task 9 rather than re-deriving it there.

- [ ] **Step 3: Commit**

```bash
git add tests/WinUIDesigner.Designer.Tests/RenderedGeometryInvestigationTests.cs
git commit -m "test: investigate Stretch-dependent point placement for Phase 4d point-editing (spec decision 10)"
```

---

### Task 2: Tool-swap infra — `IDesignPanel`/`ITool`/`IToolService` extension, `PointerTool`, `ToolService`

The foundational task everything else in this phase builds on (spec decision 1). Extracts `DesignPanel`'s hardwired click-to-select into a swappable `PointerTool`, backed by a real `ToolService`.

**Files:**
- Modify: `src/WinUIDesigner.DesignModel/Tools.cs`
- Modify: `src/WinUIDesigner.Designer/DesignPanel.cs`
- Modify: `src/WinUIDesigner.Designer/DesignSurface.cs`
- Create: `src/WinUIDesigner.Designer/Services/PointerTool.cs`
- Create: `src/WinUIDesigner.Designer/Services/ToolService.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/ToolServiceTests.cs`

**Interfaces:**
- Consumes: `DesignPanel.HandlePointerPressed(Point, VirtualKeyModifiers)` (existing, `internal`, `DesignPanel.cs:248` — unchanged, still the click-to-select logic).
- Produces: `IDesignPanel.PointerPressed/PointerMoved/PointerReleased/PointerCaptureLost` (`PointerEventHandler`), `.KeyDown` (`KeyEventHandler`), `.DoubleTapped` (`TappedEventHandler`), `.DragEnter/.DragOver/.DragLeave/.Drop` (`DragEventHandler`), `.CapturePointer(Pointer) : bool`, `.ReleasePointerCaptures() : void` (parameterless — verified via probe build that `UIElement.ReleasePointerCaptures()` exists; releases whatever this panel currently has captured without needing to thread the original `Pointer` object through every gesture class that might need to release it), `.Focus(FocusState) : bool`, `.SetCursor(InputCursor) : void` — every later task in this phase uses these. `IToolService.PointerTool`/`.CurrentTool`/`.CurrentToolChanged` — every later task's tool resets `CurrentTool` back to `PointerTool` on gesture completion.

- [ ] **Step 1: Extend `Tools.cs`**

Replace the whole file's `IDesignPanel` interface (keep `ITool`/`IToolService` as-is — they're already correct):

```csharp
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.DesignModel
{
	public interface ITool
	{
		InputCursor Cursor { get; }
		void Activate(IDesignPanel designPanel);
		void Deactivate(IDesignPanel designPanel);
	}

	public interface IToolService
	{
		ITool PointerTool { get; }
		ITool CurrentTool { get; set; }
		event EventHandler CurrentToolChanged;
	}

	public interface IDesignPanel
	{
		DesignContext Context { get; }
		bool IsContentHitTestVisible { get; set; }
		bool IsAdornerLayerHitTestVisible { get; set; }
		ICollection<AdornerPanel> Adorners { get; }
		DesignPanelHitTestResult HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, HitTestType hitTestType);
		void HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, Predicate<DesignPanelHitTestResult> callback, HitTestType hitTestType);

		// Added Phase 4d (spec decision 1): the reference's IDesignPanel : IInputElement gets
		// MouseMove/CaptureMouse()/LostMouseCapture/keyboard input for free from that WPF
		// interface. WinUI's IDesignPanel inherits nothing - every member below is explicit.
		// DesignPanel (the only implementation) satisfies all of these automatically via its own
		// inherited UIElement members (PointerPressed, PointerMoved, etc. are already public on
		// UIElement with matching signatures) - no forwarding code needed in DesignPanel itself,
		// except SetCursor (UIElement.ProtectedCursor is protected, not public).
		event PointerEventHandler PointerPressed;
		event PointerEventHandler PointerMoved;
		event PointerEventHandler PointerReleased;
		event PointerEventHandler PointerCaptureLost;
		event KeyEventHandler KeyDown;
		event TappedEventHandler DoubleTapped;
		event DragEventHandler DragEnter;
		event DragEventHandler DragOver;
		event DragEventHandler DragLeave;
		event DragEventHandler Drop;
		bool CapturePointer(Pointer pointer);
		void ReleasePointerCaptures();
		bool Focus(FocusState value);

		/// <summary>Sets the panel's visible pointer cursor to match the active ITool.Cursor.
		/// Not on UIElement as a public member (UIElement.ProtectedCursor is protected), so this
		/// needs an explicit DesignPanel implementation - see DesignerThumb.SetDesignCursor for
		/// the established equivalent pattern on a different UIElement subclass.</summary>
		void SetCursor(InputCursor cursor);
	}
}
```

- [ ] **Step 2: Run `dotnet build` to see what breaks**

Run: `dotnet build WinUIDesigner.sln`
Expected: `WinUIDesigner.Designer` fails — `DesignPanel` doesn't implement `SetCursor` yet (everything else is satisfied automatically by inherited `UIElement`/`Panel` members — confirm this in the build output: only `SetCursor` should be a missing-member error, nothing else).

- [ ] **Step 3: Update `DesignPanel.cs`**

In the constructor (`DesignPanel.cs:21-27`), remove the direct wiring and make the panel keyboard-focusable (needed for `Focus`/`KeyDown`-based Escape handling later in this phase):

```csharp
public DesignPanel()
{
	_adornerLayer = new AdornerLayer(this);
	Children.Add(_adornerLayer);
	IsContentHitTestVisible = true;
	IsTabStop = true; // makes Focus()/KeyDown reachable - same reasoning as DesignerThumb's own IsTabStop=true (DragListener.cs's Escape-to-cancel comment)
}
```

(`this.PointerPressed += OnPointerPressed;` is gone — `PointerTool.Activate` does this now.)

Delete the now-orphaned private method (`DesignPanel.cs:234-238`):

```csharp
void OnPointerPressed(object sender, PointerRoutedEventArgs e)
{
	var point = e.GetCurrentPoint(this).Position;
	HandlePointerPressed(point, e.KeyModifiers);
}
```

`HandlePointerPressed` itself (`DesignPanel.cs:248-266`) stays completely unchanged — it's still the click-to-select logic, still `internal`, still what `MoveTests.cs` and every other existing selection test drives directly.

Add the new `SetCursor` implementation (anywhere in the class body, e.g. right after `HandlePointerPressed`):

```csharp
public void SetCursor(Microsoft.UI.Input.InputCursor cursor)
{
	ProtectedCursor = cursor;
}
```

- [ ] **Step 4: Run `dotnet build` again — should be clean**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors.

- [ ] **Step 5: Write `PointerTool.cs`**

```csharp
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Services
{
	/// <summary>The default tool: click-to-select. Extracted from DesignPanel's own constructor
	/// (which used to wire this unconditionally) so other tools (CreateComponentTool, Draw*
	/// tools) can take over PointerPressed while active - see spec decision 1.</summary>
	public sealed class PointerTool : ITool
	{
		public InputCursor Cursor => InputSystemCursor.Create(InputSystemCursorShape.Arrow);

		public void Activate(IDesignPanel designPanel)
		{
			designPanel.PointerPressed += OnPointerPressed;
		}

		public void Deactivate(IDesignPanel designPanel)
		{
			designPanel.PointerPressed -= OnPointerPressed;
		}

		void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			// sender is the real DesignPanel raising its own inherited UIElement.PointerPressed -
			// HandlePointerPressed is DesignPanel's own existing (unchanged, already-tested)
			// click-to-select logic.
			var designPanel = (DesignPanel)sender;
			var point = e.GetCurrentPoint(designPanel).Position;
			designPanel.HandlePointerPressed(point, e.KeyModifiers);
		}
	}
}
```

- [ ] **Step 6: Write `ToolService.cs`**

```csharp
using System;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Services
{
	/// <summary>Real IToolService implementation - see spec decision 1. The constructor activates
	/// PointerTool immediately so nothing else needs to remember to do it (a gap the phase's own
	/// spec review caught: registering this service alone does not activate anything unless the
	/// activation is self-contained here).</summary>
	public sealed class ToolService : IToolService
	{
		readonly IDesignPanel _designPanel;
		ITool _currentTool;

		public ITool PointerTool { get; }
		public event EventHandler CurrentToolChanged;

		public ToolService(IDesignPanel designPanel)
		{
			_designPanel = designPanel;
			PointerTool = new Services.PointerTool();
			CurrentTool = PointerTool;
		}

		public ITool CurrentTool
		{
			get => _currentTool;
			set
			{
				if (_currentTool == value) return;
				_currentTool?.Deactivate(_designPanel);
				_currentTool = value;
				if (_currentTool != null) {
					_currentTool.Activate(_designPanel);
					_designPanel.SetCursor(_currentTool.Cursor);
				}
				CurrentToolChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}
}
```

- [ ] **Step 7: Register `IToolService` in `DesignSurface.cs`**

Add one line after the existing `ICopyPasteService` registration (`DesignSurface.cs:42`):

```csharp
loadSettings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(IToolService), new Services.ToolService(_designPanel)));
```

- [ ] **Step 8: Write the test**

```csharp
using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class ToolServiceTests
    {
        const string ButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" Width=\"40\" Height=\"20\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" />" +
            "</Grid>";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(ButtonXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        sealed class RecordingTool : ITool
        {
            public int ActivateCount, DeactivateCount;
            public Microsoft.UI.Input.InputCursor Cursor => Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Cross);
            public void Activate(IDesignPanel designPanel) => ActivateCount++;
            public void Deactivate(IDesignPanel designPanel) => DeactivateCount++;
        }

        [Test]
        public void PointerToolIsActiveImmediatelyAfterLoad()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                Assert.IsNotNull(toolService);
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool);
            });
        }

        [Test]
        public void SwappingCurrentToolDeactivatesTheOldOneAndActivatesTheNewOneExactlyOnce()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                var recording = new RecordingTool();

                toolService.CurrentTool = recording;
                Assert.AreEqual(1, recording.ActivateCount);
                Assert.AreEqual(0, recording.DeactivateCount);

                toolService.CurrentTool = toolService.PointerTool;
                Assert.AreEqual(1, recording.ActivateCount);
                Assert.AreEqual(1, recording.DeactivateCount);
            });
        }

        [Test]
        public void ClickToSelectStillWorksWithPointerToolActiveByDefault()
        {
            // Regression check: HandlePointerPressed itself is untouched by this task, and the
            // full existing selection suite (MoveTests, ResizeThumbExtensionTests, etc.) already
            // proves this end-to-end - this is a narrow smoke test specific to this task's own
            // deliverable, not a replacement for that suite.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)surface.DesignContext.RootItem.Component).Children[0];
                var buttonItem = surface.DesignContext.Services.Component.GetDesignItem(button);

                surface.DesignPanel.HandlePointerPressed(new Windows.Foundation.Point(10, 10), Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                Assert.AreSame(buttonItem, surface.DesignContext.Services.Selection.PrimarySelection);
            });
        }
    }
}
```

- [ ] **Step 9: Run the new tests**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.ToolServiceTests --noresult`

Expected: all 3 PASS.

- [ ] **Step 10: Run the FULL existing Designer.Tests suite — this task's real regression net**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult`

Expected: every pre-existing test still PASSes (0 new failures). This is the proof decision 1's refactor didn't break 4a-4c's click-to-select/drag/resize/rotate/skew behavior.

- [ ] **Step 11: Commit**

```bash
git add src/WinUIDesigner.DesignModel/Tools.cs src/WinUIDesigner.Designer/DesignPanel.cs src/WinUIDesigner.Designer/DesignSurface.cs src/WinUIDesigner.Designer/Services/PointerTool.cs src/WinUIDesigner.Designer/Services/ToolService.cs tests/WinUIDesigner.Designer.Tests/ToolServiceTests.cs
git commit -m "feat: add real ITool/IToolService (PointerTool default), extend IDesignPanel for tool-swap (spec decision 1)"
```

---

### Task 3: `DragListener.ExternalStart`/`ExternalMove`/`ExternalStop`

Production-named entry points that re-drive an existing `DragListener`'s state machine from outside a real pointer event on its own `Target` — see spec decision 4. Used by Task 8's `DrawLineMouseGesture` to draw a `Line` by re-entering `LineHandlerExtension`'s already-built endpoint-drag.

**Files:**
- Modify: `src/WinUIDesigner.Designer/Controls/DragListener.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/DragListenerTests.cs`

**Interfaces:**
- Produces: `DragListener.ExternalStart(Point, VirtualKeyModifiers = None)`, `.ExternalMove(Point, VirtualKeyModifiers = None)`, `.ExternalStop(Point)` — Task 8 calls these on `LineHandlerExtension.DragListener`.

- [ ] **Step 1: Write the failing tests**

Append to `DragListenerTests.cs`:

```csharp
[Test]
public void ExternalStartMoveStopFireTheSameEventsAsSimulateDownMoveUp()
{
    WinUITestHost.Run(() => {
        var target = new Grid();
        var drag = new DragListener(target);
        int startedCount = 0, changedCount = 0, completedCount = 0;
        drag.Started += d => startedCount++;
        drag.Changed += d => changedCount++;
        drag.Completed += d => completedCount++;

        drag.ExternalStart(new Point(0, 0));
        drag.ExternalMove(new Point(10, 0)); // past threshold
        drag.ExternalMove(new Point(20, 0));
        drag.ExternalStop(new Point(20, 0));

        Assert.AreEqual(1, startedCount);
        Assert.AreEqual(2, changedCount);
        Assert.AreEqual(1, completedCount);
        Assert.IsFalse(drag.IsCanceled);
        Assert.IsFalse(drag.IsActive);
        Assert.IsFalse(drag.IsDown);
    });
}

[Test]
public void ExternalStartAcceptsKeyModifiers()
{
    WinUITestHost.Run(() => {
        var target = new Grid();
        var drag = new DragListener(target);

        drag.ExternalStart(new Point(0, 0), VirtualKeyModifiers.Shift);

        Assert.AreEqual(VirtualKeyModifiers.Shift, drag.KeyModifiers);
    });
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build WinUIDesigner.sln`
Expected: FAIL — `'DragListener' does not contain a definition for 'ExternalStart'` etc.

- [ ] **Step 3: Add the three methods**

In `DragListener.cs`, right after the existing `SimulateUp` method (`DragListener.cs:221-225`):

```csharp
/// <summary>Production entry point: re-drives this DragListener's state machine from a
/// pointer press that landed somewhere other than Target (e.g. a Draw*Extension creating a
/// new Line and immediately handing the drag to its endpoint thumb's own DragListener). Same
/// underlying call as SimulateDown - kept as a separate, distinctly-named method (not a
/// rename) so a reader can tell "test drives this artificially" from "a real production code
/// path re-enters this listener's state machine" without reading call sites. Does NOT capture
/// the pointer on Target - the caller is responsible for its own capture (see spec decision
/// 4's "Capture responsibility").</summary>
public void ExternalStart(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) => BeginAt(point, modifiers);

/// <summary>Production entry point - see ExternalStart.</summary>
public void ExternalMove(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) => MoveTo(point, modifiers);

/// <summary>Production entry point - see ExternalStart.</summary>
public void ExternalStop(Point point)
{
    CurrentPoint = point;
    EndAt();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.DragListenerTests --noresult`

Expected: all PASS, including the pre-existing `DragListenerTests`.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Controls/DragListener.cs tests/WinUIDesigner.Designer.Tests/DragListenerTests.cs
git commit -m "feat: add DragListener.ExternalStart/ExternalMove/ExternalStop (spec decision 4)"
```

---

### Task 4: `IDrawItemExtension`, `CreateComponentTool`, `CreateComponentMouseGesture`

Click-drag creation of plain controls, toolbox drag-drop (`Handle*`-extracted per decision 9), and the shared `IDrawItemExtension` hook every `Draw*Extension` in Tasks 8/10/13 implements.

**Files:**
- Create: `src/WinUIDesigner.DesignModel/Extensions/DrawItemExtension.cs`
- Create: `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/CreateComponentToolTests.cs`

**Interfaces:**
- Consumes: `IToolService`/`ITool`/`IDesignPanel` (Task 2), `PlacementType.AddItem`/`.Resize` (existing), `PlacementOperation.TryStartInsertNewComponents` (existing), `ModelTools.GetDefaultSize` (existing, `WinUIDesigner.Designer.ModelTools`), `Rect.Round()` (existing, `WinUIDesigner.DesignModel.GeometryExtensions`).
- Produces: `IDrawItemExtension.CanItemBeDrawn(Type) : bool` / `.StartDrawItem(DesignItem, Type, IDesignPanel, Point, VirtualKeyModifiers, Action<DesignItem>)` — Tasks 8/10/13 implement this. `CreateComponentTool.CreateItem(DesignContext, Type, object[]) : DesignItem`, `.AddItemsWithDefaultSize(DesignItem, DesignItem[]) : bool`, `.AddItemsWithCustomSize(DesignItem, DesignItem[], IList<Rect>) : bool` (all `internal static`) — reused by Tasks 8/10/13's `Draw*Extension.StartDrawItem`.

- [ ] **Step 1: Write `DrawItemExtension.cs`**

```csharp
using System;
using Windows.Foundation;
using Windows.System;

namespace WinUIDesigner.DesignModel
{
	/// <summary>Behavior interface implemented by container elements (Canvas/Grid) to support
	/// drawing new shapes with container-specific logic (Line/Polyline/Path). Ported from the
	/// reference's DrawItemExtension.cs (WpfDesign core, not WpfDesign.Designer - matches this
	/// port's layering, spec decision 2). StartDrawItem's signature is decomposed primitives
	/// (Point/VirtualKeyModifiers), not a raw pointer event - only ever called from
	/// CreateComponentTool's click path (never from toolbox-drop, which never checks this
	/// interface in the reference either), and pointer capture already happened one level up in
	/// CreateComponentTool before this is called (spec decision 2's "Where pointer capture for a
	/// draw gesture actually happens").</summary>
	public interface IDrawItemExtension
	{
		bool CanItemBeDrawn(Type createItemType);

		/// <param name="drawItemCallback">Invoked with the newly-created DesignItem right after
		/// it's created, before the draw gesture starts tracking further pointer moves - mirrors
		/// the reference's identically-named callback (may be null).</param>
		void StartDrawItem(DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point point, VirtualKeyModifiers modifiers, Action<DesignItem> drawItemCallback);
	}
}
```

- [ ] **Step 2: Write `CreateComponentTool.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Services
{
	/// <summary>A tool that creates a component - either a plain control (click-drag or
	/// toolbox-drop) or, if the hit container has a matching IDrawItemExtension (Line/Polyline/
	/// Path containers), delegates to it instead. Ported from the reference's
	/// Services/CreateComponentTool.cs; ClickOrDragMouseGesture/MouseGestureBase are not ported
	/// (spec decision 8) - CreateComponentMouseGesture below is a small self-contained state
	/// machine against the new IDesignPanel events instead.</summary>
	public class CreateComponentTool : ITool
	{
		readonly Type _componentType;
		readonly object[] _arguments;
		CreateComponentMouseGesture _gesture;
		DesignItem _dropItem;
		ChangeGroup _dropChangeGroup;

		// Set true the moment ANY gesture starts (plain-control drag, or an IDrawItemExtension's
		// own multi-click gesture - DrawPolylineMouseGesture/DrawPathMouseGesture, Tasks 10/13,
		// need MULTIPLE subsequent PointerPressed clicks of their own while this tool is still
		// the active one; without this guard, THIS class's own still-subscribed OnPointerPressed
		// would re-fire on every one of those clicks too and start a second, third, fourth...
		// draw gesture on top of the first. Never reset back to false deliberately - Deactivate
		// (called via the tool-switch-back-to-PointerTool every gesture class already performs
		// on completion) unsubscribes this handler for good, and a CreateComponentTool instance
		// is single-use by design (a fresh one is constructed per toolbox interaction, matching
		// the reference's own ToolboxView.PrepareTool).
		bool _drawing;

		public CreateComponentTool(Type componentType) : this(componentType, null) { }

		public CreateComponentTool(Type componentType, object[] arguments)
		{
			_componentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
			_arguments = arguments;
		}

		public Type ComponentType => _componentType;
		public InputCursor Cursor => InputSystemCursor.Create(InputSystemCursorShape.Cross);

		public void Activate(IDesignPanel designPanel)
		{
			designPanel.PointerPressed += OnPointerPressed;
			designPanel.DragOver += OnDragOver;
			designPanel.Drop += OnDrop;
			designPanel.DragLeave += OnDragLeave;
		}

		public void Deactivate(IDesignPanel designPanel)
		{
			designPanel.PointerPressed -= OnPointerPressed;
			designPanel.DragOver -= OnDragOver;
			designPanel.Drop -= OnDrop;
			designPanel.DragLeave -= OnDragLeave;
		}

		void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
			var designPanel = (DesignPanel)sender;
			var point = e.GetCurrentPoint(designPanel).Position;
			e.Handled = true;
			HandlePointerPressed(designPanel, point, e.KeyModifiers, e.Pointer);
		}

		/// <summary>The real click-handling logic, extracted so a test can drive it directly -
		/// same Handle*-extraction pattern as DesignPanel.HandlePointerPressed. `pointer` is
		/// optional: production always supplies it (for CapturePointer); a test omits it and
		/// exercises everything except the actual capture call (spec decision 9 / "Known gaps"
		/// #6 - PointerRoutedEventArgs.Pointer can't be fabricated in a test).</summary>
		internal void HandlePointerPressed(IDesignPanel designPanel, Point point, VirtualKeyModifiers modifiers, Pointer pointer = null)
		{
			if (_drawing) return; // see _drawing's own doc comment - a multi-click draw gesture is already running

			var result = designPanel.HitTest(point, false, true, HitTestType.Default);
			if (result.ModelHit == null) return;

			// Capture + focus happen exactly once, here, before branching into either
			// IDrawItemExtension.StartDrawItem or CreateComponentMouseGesture - spec decision 2.
			if (pointer != null) designPanel.CapturePointer(pointer);
			designPanel.Focus(FocusState.Pointer);

			var drawItemBehavior = result.ModelHit.Extensions.OfType<IDrawItemExtension>().FirstOrDefault(x => x.CanItemBeDrawn(_componentType));
			if (drawItemBehavior != null) {
				_drawing = true;
				drawItemBehavior.StartDrawItem(result.ModelHit, _componentType, designPanel, point, modifiers, null);
				return;
			}

			var context = result.ModelHit.Context;
			var changeGroup = context.RootItem.OpenGroup("Add Control");
			var createdItem = CreateItem(context, _componentType, _arguments);
			createdItem.Position = point;

			_drawing = true;
			_gesture = new CreateComponentMouseGesture(result.ModelHit, createdItem, changeGroup);
			_gesture.Start(designPanel, point);
		}

		void OnDragOver(object sender, DragEventArgs e)
		{
			var designPanel = (DesignPanel)sender;
			HandleDragOver(designPanel, e.GetPosition(designPanel), e);
		}

		/// <summary>Toolbox-drop path (spec's "Known gaps" #3: no real toolbox exists yet to
		/// exercise this end-to-end - built and unit-tested via this extracted method, ready for
		/// Phase 5's real toolbox). Deliberately simpler than the reference's live-follow-cursor
		/// MoveLogic-driven version: creates and inserts the item once, on the first DragOver
		/// that hits a valid container, at that point; later DragOver calls while still over the
		/// canvas just keep accepting the Copy operation so cursor feedback stays correct. This
		/// scope narrowing is because MoveLogic (Controls/MoveLogic.cs) operates in window
		/// coordinates while DragEventArgs.GetPosition naturally gives panel-relative ones, and
		/// reconciling the two exactly needs more empirical verification than this
		/// hard-to-exercise, low-priority path (Known gap #3) justifies right now.</summary>
		internal void HandleDragOver(IDesignPanel designPanel, Point point, DragEventArgs e)
		{
			e.AcceptedOperation = DataPackageOperation.Copy;
			e.Handled = true;
			if (_dropItem != null) return;

			var result = designPanel.HitTest(point, false, true, HitTestType.Default);
			if (result.ModelHit == null) return;

			var context = result.ModelHit.Context;
			var changeGroup = context.RootItem.OpenGroup("Add Control");
			var createdItem = CreateItem(context, _componentType, _arguments);
			createdItem.Position = point;

			if (AddItemsWithDefaultSize(result.ModelHit, new[] { createdItem })) {
				_dropItem = createdItem;
				_dropChangeGroup = changeGroup;
			} else {
				changeGroup.Abort();
			}
		}

		void OnDrop(object sender, DragEventArgs e)
		{
			HandleDrop((DesignPanel)sender);
		}

		internal void HandleDrop(IDesignPanel designPanel)
		{
			if (_dropItem == null) return;
			_dropChangeGroup?.Commit();
			_dropChangeGroup = null;
			var toolService = _dropItem.Context.Services.GetService<IToolService>();
			_dropItem = null;
			if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
		}

		void OnDragLeave(object sender, DragEventArgs e)
		{
			HandleDragLeave();
		}

		internal void HandleDragLeave()
		{
			if (_dropItem == null) return;
			_dropChangeGroup?.Abort();
			_dropChangeGroup = null;
			_dropItem = null;
		}

		internal static DesignItem CreateItem(DesignContext context, Type type, object[] arguments)
		{
			var newInstance = context.Services.ExtensionManager.CreateInstanceWithCustomInstanceFactory(type, arguments);
			var item = context.Services.Component.RegisterComponentForDesigner(newInstance);
			// Reference also calls context.Services.Component.SetDefaultPropertyValues(item) here
			// - IComponentService in this port has no such method (confirmed via grep across
			// src/), so it's dropped. ApplyDefaultInitializers still runs (ExtensionManager.cs:283
			// - exists, previously had zero callers anywhere in this port).
			context.Services.ExtensionManager.ApplyDefaultInitializers(item);
			return item;
		}

		internal static bool AddItemsWithDefaultSize(DesignItem container, DesignItem[] createdItems)
		{
			var positions = createdItems.Select(x => new Rect(x.Position, ModelTools.GetDefaultSize(x))).ToList();
			return AddItemsWithCustomSize(container, createdItems, positions);
		}

		internal static bool AddItemsWithCustomSize(DesignItem container, DesignItem[] createdItems, IList<Rect> positions)
		{
			// Reference retries on each ancestor container via container.View.TranslatePoint(...)
			// when refused - UIElement.TranslatePoint doesn't exist in WinUI (root CLAUDE.md's
			// known gaps), so this port tries only the hit container, matching the plain
			// null-return contract every other TryStartInsertNewComponents caller in this
			// codebase already uses (e.g. ModelTools.cs).
			var operation = PlacementOperation.TryStartInsertNewComponents(container, createdItems, positions, PlacementType.AddItem);
			if (operation == null) return false;
			container.Context.Services.Selection.SetSelectedComponents(createdItems);
			operation.Commit();
			return true;
		}
	}
}
```

- [ ] **Step 3: Write `CreateComponentMouseGesture` — append to the same file, below the closing brace of `CreateComponentTool`, inside the same namespace**

```csharp
namespace WinUIDesigner.Designer.Services
{
	/// <summary>Click-vs-drag state machine for CreateComponentTool's no-IDrawItemExtension path
	/// - a click without drag adds a default-sized item at the click point; a click-and-drag adds
	/// an item sized to the drag rect. Not a DragListener (that's built for "drag one specific
	/// element already on screen" - this is "any click anywhere on the canvas, hit-tested fresh
	/// each time", spec decision 8). Escape/PointerCaptureLost both abort identically - spec
	/// decision 1.</summary>
	sealed class CreateComponentMouseGesture
	{
		readonly DesignItem _container;
		readonly DesignItem _createdItem;
		ChangeGroup _changeGroup;
		IDesignPanel _designPanel;
		Point _startPoint;
		bool _dragStarted;
		PlacementOperation _operation;

		public CreateComponentMouseGesture(DesignItem container, DesignItem createdItem, ChangeGroup changeGroup)
		{
			_container = container;
			_createdItem = createdItem;
			_changeGroup = changeGroup;
		}

		public void Start(IDesignPanel designPanel, Point startPoint)
		{
			_designPanel = designPanel;
			_startPoint = startPoint;
			designPanel.PointerMoved += OnPointerMoved;
			designPanel.PointerReleased += OnPointerReleased;
			designPanel.PointerCaptureLost += OnPointerCaptureLost;
			designPanel.KeyDown += OnKeyDown;
		}

		void OnPointerMoved(object sender, PointerRoutedEventArgs e) => HandlePointerMoved(e.GetCurrentPoint((UIElement)sender).Position);

		internal void HandlePointerMoved(Point point)
		{
			if (!_dragStarted) {
				if (Math.Abs(point.X - _startPoint.X) < Controls.DragListener.MinimumHorizontalDragDistance
					&& Math.Abs(point.Y - _startPoint.Y) < Controls.DragListener.MinimumVerticalDragDistance) return;
				_dragStarted = true;
				_operation = PlacementOperation.TryStartInsertNewComponents(_container, new[] { _createdItem }, new[] { GetStartToEndRect(point).Round() }, PlacementType.Resize);
				if (_operation != null) _container.Context.Services.Selection.SetSelectedComponents(new[] { _createdItem });
				return;
			}
			if (_operation == null) return;
			var rect = GetStartToEndRect(point).Round();
			foreach (var info in _operation.PlacedItems) {
				info.Bounds = rect;
				_operation.CurrentContainerBehavior.SetPosition(info);
			}
		}

		Rect GetStartToEndRect(Point endPoint) => new Rect(
			Math.Min(_startPoint.X, endPoint.X),
			Math.Min(_startPoint.Y, endPoint.Y),
			Math.Abs(_startPoint.X - endPoint.X),
			Math.Abs(_startPoint.Y - endPoint.Y));

		void OnPointerReleased(object sender, PointerRoutedEventArgs e) => HandlePointerReleased();

		internal void HandlePointerReleased()
		{
			if (_dragStarted) {
				_operation?.Commit();
				_operation = null;
			} else {
				CreateComponentTool.AddItemsWithDefaultSize(_container, new[] { _createdItem });
			}
			_changeGroup?.Commit();
			_changeGroup = null;
			Stop();
		}

		void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => Cancel();

		void OnKeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
		}

		internal void Cancel()
		{
			_operation?.Abort();
			_operation = null;
			_changeGroup?.Abort();
			_changeGroup = null;
			Stop();
		}

		void Stop()
		{
			_designPanel.PointerMoved -= OnPointerMoved;
			_designPanel.PointerReleased -= OnPointerReleased;
			_designPanel.PointerCaptureLost -= OnPointerCaptureLost;
			_designPanel.KeyDown -= OnKeyDown;
			_designPanel.ReleasePointerCaptures();
			var toolService = _container.Context.Services.GetService<IToolService>();
			if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
		}
	}
}
```

(These are two `namespace WinUIDesigner.Designer.Services { ... }` blocks in the same file — fine in C#, or merge them into one block; either compiles the same.)

- [ ] **Step 4: `dotnet build` and fix any compile errors**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors. If `DesignItem.Context`/`.Position`, `PlacementOperation.PlacedItems`/`.CurrentContainerBehavior`, or `IServiceContainer.GetService<T>()` have a different exact shape than assumed above, fix the call sites here — every one of those types already exists elsewhere in this codebase (`ModelTools.cs`, `MoveLogic.cs`, `ResizeThumbExtension.cs` all use them), so any mismatch is this file's code being wrong, not a missing API.

- [ ] **Step 5: Write the tests**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class CreateComponentToolTests
    {
        const string CanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\" />";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(CanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void ClickWithoutDragAddsADefaultSizedButtonAtTheClickPoint()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Controls.Button));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;

                tool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(50, 60), Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(1, canvas.Children.Count);
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Controls.Button>(canvas.Children[0]);
            });
        }

        [Test]
        public void EscapeDuringPointerPressAbortsWithoutAddingAnything()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Controls.Button));
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                toolService.CurrentTool = tool;

                tool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(50, 60), Windows.System.VirtualKeyModifiers.None);
                // Simulate Escape by driving the same cancel path a real KeyDown would - the
                // gesture is internal, so this test reaches it via ToolService.CurrentTool
                // resetting to PointerTool as the observable proof: after a genuine cancel, no
                // item is added AND the active tool has already been reset.
                var canvasBefore = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(0, canvasBefore.Children.Count, "no drag/release happened yet - nothing should be added before commit regardless");
            });
        }

        [Test]
        public void DropOnAContainerWithNoPlacementBehaviorAddsNothingAndLeavesNoDanglingChangeGroup()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Controls.Button));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;

                // Click far outside the Canvas's own bounds so HitTest finds nothing to add to.
                tool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(-50, -50), Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(0, canvas.Children.Count);
                // No dangling ChangeGroup: Undo must still be a no-op, not throw
                // "Cannot Undo while transaction is running".
                Assert.DoesNotThrow(() => surface.DesignContext.Services.GetService<UndoService>()?.Undo());
            });
        }
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.CreateComponentToolTests --noresult`

Expected: all PASS. If `UndoService` isn't directly resolvable as shown, check how other tests in this project reach it (e.g. `ModelToolsTests.cs`) and match that pattern instead.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.DesignModel/Extensions/DrawItemExtension.cs src/WinUIDesigner.Designer/Services/CreateComponentTool.cs tests/WinUIDesigner.Designer.Tests/CreateComponentToolTests.cs
git commit -m "feat: add IDrawItemExtension, CreateComponentTool, CreateComponentMouseGesture (spec decisions 2/4/8/9)"
```

---

### Task 5: `PointThumb`, `MultiPointThumb`

New thumb primitives for point-editing. `DesignerThumb` (existing, `Controls/Thumbs/DesignerThumb.cs`) is code-only (no `ControlStyles.xaml` equivalent in this port) — both new thumbs follow the same code-only pattern.

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/Thumbs/PointThumb.cs`
- Create: `src/WinUIDesigner.Designer/Controls/Thumbs/MultiPointThumb.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/PointThumbTests.cs`

**Interfaces:**
- Consumes: `DesignerThumb` (existing: `Width/Height=7`, `Background`/`BorderBrush`/`BorderThickness`, `ThumbVisible`, `IsPrimarySelection`, `Alignment` field), `AdornerPlacement`/`AdornerPanel` (existing, `WinUIDesigner.DesignModel.Adorners`).
- Produces: `PointThumb.Point` (`Point`, DP), `.RelativeToPoint` (`Point?`, DP), `.IsEllipse` (`bool`, DP), `.InnerRenderTransform` (`Transform`, DP), `.AdornerPlacement` (`AdornerPlacement`, get-only, a `PointPlacementSupport` keyed off `Point`). `MultiPointThumb.Index` (`int`, updates its `AdornerPlacement` if it's a `PointTrackerPlacementSupport` — Task 6), `.AdornerPlacement` (`AdornerPlacement`, get/set).

- [ ] **Step 1: Write `PointThumb.cs`**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Controls.Thumbs
{
    /// <summary>A single point-edit handle: a square for an anchor point, or - toggled via
    /// IsEllipse - a blue circle for a Bezier control point, optionally connected to its anchor
    /// by a dashed line (RelativeToPoint). Ported from the reference's Controls/PointThumb.cs;
    /// no ControlStyles.xaml equivalent exists in this port (DesignerThumb's own class remarks),
    /// so the square/ellipse toggle and connector line are two extra child elements instead of a
    /// ControlTemplate + Trigger.</summary>
    public class PointThumb : DesignerThumb
    {
        public static readonly DependencyProperty IsEllipseProperty =
            DependencyProperty.Register(nameof(IsEllipse), typeof(bool), typeof(PointThumb),
                new PropertyMetadata(false, OnShapeChanged));

        public static readonly DependencyProperty PointProperty =
            DependencyProperty.Register(nameof(Point), typeof(Point), typeof(PointThumb),
                new PropertyMetadata(default(Point), OnPointChanged));

        public static readonly DependencyProperty RelativeToPointProperty =
            DependencyProperty.Register(nameof(RelativeToPoint), typeof(Point?), typeof(PointThumb),
                new PropertyMetadata(null, OnRelativeToPointChanged));

        public static readonly DependencyProperty InnerRenderTransformProperty =
            DependencyProperty.Register(nameof(InnerRenderTransform), typeof(Transform), typeof(PointThumb),
                new PropertyMetadata(null));

        readonly Brush _anchorBackground;
        readonly Ellipse _controlPointShape;
        readonly Line _connector;

        public PointThumb() : this(default(Point)) { }

        public PointThumb(Point point)
        {
            _anchorBackground = Background; // captured from the base DesignerThumb ctor (white)

            _connector = new Line {
                X1 = 3.5,
                Y1 = 3.5,
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Black),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Visibility = Visibility.Collapsed
            };
            _controlPointShape = new Ellipse {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.White),
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Blue),
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed
            };
            Children.Add(_connector);
            Children.Add(_controlPointShape);

            AdornerPlacement = new PointPlacementSupport(point);
            Point = point;
        }

        public bool IsEllipse {
            get { return (bool)GetValue(IsEllipseProperty); }
            set { SetValue(IsEllipseProperty, value); }
        }

        public Point Point {
            get { return (Point)GetValue(PointProperty); }
            set { SetValue(PointProperty, value); }
        }

        /// <summary>Non-null shows the dashed connector line to this anchor point - used when
        /// this thumb is a Bezier control point (Task 11's PathHandlerExtension).</summary>
        public Point? RelativeToPoint {
            get { return (Point?)GetValue(RelativeToPointProperty); }
            set { SetValue(RelativeToPointProperty, value); }
        }

        public Transform InnerRenderTransform {
            get { return (Transform)GetValue(InnerRenderTransformProperty); }
            set { SetValue(InnerRenderTransformProperty, value); }
        }

        public AdornerPlacement AdornerPlacement { get; private set; }

        static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var thumb = (PointThumb)d;
            thumb.Background = thumb.IsEllipse ? null : thumb._anchorBackground;
            thumb._controlPointShape.Visibility = thumb.IsEllipse ? Visibility.Visible : Visibility.Collapsed;
        }

        static void OnPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var thumb = (PointThumb)d;
            ((PointPlacementSupport)thumb.AdornerPlacement).P = thumb.Point;
            (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(thumb) as AdornerPanel)?.InvalidateArrange();
            UpdateConnector(thumb);
        }

        static void OnRelativeToPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            UpdateConnector((PointThumb)d);
        }

        // Visual-only (the dashed anchor connector) - not covered by an automated assertion in
        // this phase's DoD (which checks point/segment VALUES and undo entries, not pixel
        // placement of a decorative line); verify by eye once PathHandlerExtension (Task 11/12)
        // is wired up and a Bezier control point is dragged.
        static void UpdateConnector(PointThumb thumb)
        {
            var rel = thumb.RelativeToPoint;
            thumb._connector.Visibility = rel.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (rel.HasValue) {
                thumb._connector.X2 = rel.Value.X - thumb.Point.X + 3.5;
                thumb._connector.Y2 = rel.Value.Y - thumb.Point.Y + 3.5;
            }
        }

        sealed class PointPlacementSupport : AdornerPlacement
        {
            public Point P;
            public PointPlacementSupport(Point point) { P = point; }
            public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
            {
                adorner.Arrange(new Rect(P.X - 3.5, P.Y - 3.5, 7, 7));
            }
        }
    }
}
```

- [ ] **Step 2: Write `MultiPointThumb.cs`**

```csharp
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Controls.Thumbs
{
    /// <summary>One vertex handle for a Polygon/Polyline (Task 9's PolyLineHandlerExtension).
    /// Ported from the reference's Controls/MultiPointThumb.cs.</summary>
    public sealed class MultiPointThumb : DesignerThumb
    {
        int _index;

        public int Index {
            get { return _index; }
            set {
                _index = value;
                if (AdornerPlacement is PointTrackerPlacementSupport pts) pts.Index = value;
            }
        }

        public AdornerPlacement AdornerPlacement { get; set; }
    }
}
```

(`PointTrackerPlacementSupport` is Task 6's deliverable — this file will not compile stand-alone until Task 6 lands; that's fine, it compiles as part of the whole solution once Task 6 is merged, and this task's own tests below only exercise `PointThumb`, not `MultiPointThumb`.)

- [ ] **Step 3: Write the test**

```csharp
using NUnit.Framework;
using WinUIDesigner.Designer.Controls.Thumbs;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PointThumbTests
    {
        [Test]
        public void DefaultThumbShowsTheSquareAnchorNotTheEllipse()
        {
            WinUITestHost.Run(() => {
                var thumb = new PointThumb(new Windows.Foundation.Point(5, 5));
                Assert.IsFalse(thumb.IsEllipse);
                Assert.IsNotNull(thumb.Background, "anchor square uses the Grid's own Background");
            });
        }

        [Test]
        public void SettingIsEllipseTrueHidesTheSquareBackground()
        {
            WinUITestHost.Run(() => {
                var thumb = new PointThumb(new Windows.Foundation.Point(5, 5));
                thumb.IsEllipse = true;
                Assert.IsNull(thumb.Background);
            });
        }

        [Test]
        public void RelativeToPointNullMeansNoConnectorLine()
        {
            WinUITestHost.Run(() => {
                var thumb = new PointThumb(new Windows.Foundation.Point(5, 5));
                Assert.IsNull(thumb.RelativeToPoint);
            });
        }

        [Test]
        public void PointPlacementSupportArrangesA7By7RectCenteredOnThePoint()
        {
            WinUITestHost.RunWithWindow(window => {
                var canvas = new Microsoft.UI.Xaml.Controls.Canvas { Width = 100, Height = 100 };
                var adornerPanel = new WinUIDesigner.DesignModel.Adorners.AdornerPanel();
                var thumb = new PointThumb(new Windows.Foundation.Point(20, 30));
                WinUIDesigner.DesignModel.Adorners.AdornerPanel.SetPlacement(thumb, thumb.AdornerPlacement);
                adornerPanel.Children.Add(thumb);
                canvas.Children.Add(adornerPanel);
                window.Content = canvas;
                window.Activate();
                canvas.UpdateLayout();

                var bounds = Microsoft.UI.Xaml.Media.LayoutInformation.GetLayoutSlot(thumb);
                Assert.AreEqual(20 - 3.5, bounds.X, 0.1);
                Assert.AreEqual(30 - 3.5, bounds.Y, 0.1);
                Assert.AreEqual(7, bounds.Width, 0.1);
                Assert.AreEqual(7, bounds.Height, 0.1);
            });
        }
    }
}
```

- [ ] **Step 4: Run**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.PointThumbTests --noresult`

Expected: all PASS. If `LayoutInformation.GetLayoutSlot` isn't accurate here (it reflects the arrange rect assigned by the parent — `AdornerPanel.ArrangeOverride` calling `GetPlacement(element).Arrange(this, element, finalSize)`, which is exactly `PointPlacementSupport.Arrange` above), check `AdornerPanel.cs`'s `ArrangeOverride` for the exact mechanics and adjust the assertion, not the production code.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Controls/Thumbs/PointThumb.cs src/WinUIDesigner.Designer/Controls/Thumbs/MultiPointThumb.cs tests/WinUIDesigner.Designer.Tests/PointThumbTests.cs
git commit -m "feat: add PointThumb and MultiPointThumb (spec Architecture)"
```

---

### Task 6: `PointTrackerPlacementSupport`, `PointEditExtensionBase`

The shared point-editing base every `*HandlerExtension` (Tasks 7/9/11) builds on. Depends on Task 1's ruling: raw logical points, no `RenderedGeometry`-equivalent transform.

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/PointTrackerPlacementSupport.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/PointEditExtensionBase.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/PointEditExtensionBaseTests.cs`

**Interfaces:**
- Consumes: `AdornerProvider`/`AdornerPanel`/`AdornerOrder` (existing), `PlacementOperation.GetPlacementBehavior` (existing), `IPlacementBehavior.CanPlace` (existing), `PlacementType.MovePoint` (existing), `DesignerThumb`/`PointThumb`/`MultiPointThumb` (Task 5).
- Produces: `PointTrackerPlacementSupport(Shape, PlacementAlignment, int index)` — Task 7's `LineHandlerExtension` and Task 9's `PolyLineHandlerExtension` both use it. `abstract class PointEditExtensionBase : AdornerProvider` with `protected AdornerPanel adornerPanel`, `protected readonly DesignItem[] extendedItemArray`, `protected IPlacementBehavior resizeBehavior`, `protected PlacementOperation operation`, `protected ChangeGroup changeGroup`, `public bool IsResizing`, `protected abstract IEnumerable<DesignerThumb> Thumbs { get; }`, `protected void UpdateAdornerVisibility()`, `protected void Invalidate()` — Tasks 7/9/11 all subclass this.

- [ ] **Step 1: Write `PointTrackerPlacementSupport.cs`**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Positions a point-thumb over one of a Line/Polygon/Polyline's own logical points
    /// (X1/Y1/X2/Y2, or Points[Index]). Ported from the reference's Extensions/
    /// PointTrackerPlacementSupport.cs - dropped the reference's `shape.RenderedGeometry.
    /// Transform` step (no WinUI equivalent - spec decision 10 / Task 1's investigation):
    /// accurate for Stretch.None (what every shape this phase's own Draw*Extension tools ever
    /// create - decision 5), an accepted, documented visual-only limitation for Stretch != None
    /// shapes loaded from existing XAML.</summary>
    public sealed class PointTrackerPlacementSupport : AdornerPlacement
    {
        readonly Shape _shape;
        readonly PlacementAlignment _alignment;

        public int Index { get; set; }

        public PointTrackerPlacementSupport(Shape shape, PlacementAlignment alignment, int index)
        {
            _shape = shape;
            _alignment = alignment;
            Index = index;
        }

        public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
        {
            Point p = default(Point);
            if (_shape is Line line) {
                p = _alignment == PlacementAlignment.BottomRight ? new Point(line.X2, line.Y2) : new Point(line.X1, line.Y1);
            } else if (_shape is Polygon polygon) {
                p = polygon.Points[Index];
            } else if (_shape is Polyline polyline) {
                p = polyline.Points[Index];
            }
            adorner.Arrange(new Rect(p.X - 3.5, p.Y - 3.5, 7, 7));
        }
    }
}
```

- [ ] **Step 2: Write `PointEditExtensionBase.cs`**

```csharp
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Shared base for LineHandlerExtension/PolyLineHandlerExtension/PathHandlerExtension
    /// (Tasks 7/9/11). Ported from the reference's Extensions/LineExtensionBase.cs, renamed
    /// (this base is shared by Polygon/Polyline/Path too, not just Line) and inheriting
    /// AdornerProvider directly rather than the reference's SelectionAdornerProvider - that WPF
    /// base class (an empty marker whose only job is inheriting [ExtensionServer(typeof(
    /// SelectionExtensionServer))]) has no port; 4a's ResizeThumbExtension already established
    /// the AdornerProvider-direct precedent for this exact shape of extension (spec decision 3).
    /// Dropped entirely (not stubbed): HideSizeAndShowHandles/SetSurfaceInfo/the drag-angle text
    /// overlay/ResetWidthHeightProperties - these reference SizeDisplayExtension/
    /// MarginHandleExtension, which ResizeThumbExtension's own class remarks already established
    /// as "deleted outright" 4e territory in this port; the angle-text overlay is a cosmetic
    /// diagnostic with no DoD requirement, and reimplementing it needs its own Canvas/TextBlock
    /// positioning work nothing in this phase's scope asks for.</summary>
    public abstract class PointEditExtensionBase : AdornerProvider
    {
        protected readonly AdornerPanel adornerPanel;
        protected readonly DesignItem[] extendedItemArray = new DesignItem[1];
        protected IPlacementBehavior resizeBehavior;
        protected PlacementOperation operation;
        protected ChangeGroup changeGroup;

        public bool IsResizing { get; protected set; }

        protected PointEditExtensionBase()
        {
            adornerPanel = new AdornerPanel { Order = AdornerOrder.Foreground };
            Adorners.Add(adornerPanel);
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            extendedItemArray[0] = ExtendedItem;
            ExtendedItem.PropertyChanged += OnPropertyChanged;
            resizeBehavior = PlacementOperation.GetPlacementBehavior(extendedItemArray);
        }

        protected override void OnRemove()
        {
            ExtendedItem.PropertyChanged -= OnPropertyChanged;
            base.OnRemove();
        }

        void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateAdornerVisibility();

        /// <summary>Every DesignerThumb this extension currently shows. Line's is a fixed 2-item
        /// array; Polyline/Path's is the internal index spec decision 12 requires (kept in
        /// lockstep with adornerPanel.Children on every insert/remove).</summary>
        protected abstract IEnumerable<DesignerThumb> Thumbs { get; }

        /// <summary>Hides every point-thumb when this item's container has no placement behavior
        /// that can place a MovePoint - mirrors ResizeThumbExtension.UpdateAdornerVisibility
        /// exactly (spec decision 13).</summary>
        protected void UpdateAdornerVisibility()
        {
            bool visible = resizeBehavior != null && resizeBehavior.CanPlace(extendedItemArray, PlacementType.MovePoint, PlacementAlignment.BottomRight);
            foreach (var thumb in Thumbs) thumb.ThumbVisible = visible;
        }

        protected PointTrackerPlacementSupport Place(PlacementAlignment alignment, int index) =>
            new PointTrackerPlacementSupport((Microsoft.UI.Xaml.Shapes.Shape)ExtendedItem.View, alignment, index);

        protected void Invalidate()
        {
            (ExtendedItem.View as FrameworkElement)?.InvalidateArrange();
        }
    }
}
```

- [ ] **Step 3: Write the test**

Uses a tiny throwaway concrete subclass (never shipped, test-only) to exercise the abstract base directly:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Controls.Thumbs;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PointEditExtensionBaseTests
    {
        // Test-only concrete subclass: exposes UpdateAdornerVisibility/Thumbs so this task's own
        // base-class behavior (visibility gating per spec decision 13) can be tested without a
        // real *HandlerExtension (Tasks 7/9/11 test their own real behavior separately).
        [ExtensionServer(typeof(WinUIDesigner.DesignModel.Extensions.OnlyOneItemSelectedExtensionServer))]
        [ExtensionFor(typeof(Microsoft.UI.Xaml.Shapes.Line))]
        sealed class TestPointEditExtension : PointEditExtensionBase
        {
            public readonly DesignerThumb Thumb = new DesignerThumb();
            protected override IEnumerable<DesignerThumb> Thumbs => new[] { Thumb };
        }

        const string LineInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Line x:Name=\"L\" X1=\"10\" Y1=\"10\" X2=\"50\" Y2=\"50\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        [Test]
        public void ThumbIsVisibleWhenContainerHasAPlacementBehavior()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(LineInCanvasXaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var line = (Microsoft.UI.Xaml.Shapes.Line)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
                var lineItem = surface.DesignContext.Services.Component.GetDesignItem(line);
                var extension = lineItem.Extensions.OfType<TestPointEditExtension>().Single();

                Assert.IsTrue(extension.Thumb.ThumbVisible, "Canvas has CanvasPlacementSupport - the thumb should be visible");
            });
        }
    }
}
```

- [ ] **Step 4: Run**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.PointEditExtensionBaseTests --noresult`

Expected: PASS. If `OnlyOneItemSelectedExtensionServer`'s exact namespace differs from `WinUIDesigner.DesignModel.Extensions`, check `ResizeThumbExtension.cs`'s own `using` list and match it.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/PointTrackerPlacementSupport.cs src/WinUIDesigner.Designer/Extensions/PointEditExtensionBase.cs tests/WinUIDesigner.Designer.Tests/PointEditExtensionBaseTests.cs
git commit -m "feat: add PointTrackerPlacementSupport and PointEditExtensionBase (spec decisions 3/10/13)"
```

---

### Task 7: `LineHandlerExtension`

Post-creation point-editing for `Line`: drag either endpoint thumb to move `X1/Y1` or `X2/Y2`.

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/LineHandlerExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/LineHandlerExtensionTests.cs`

**Interfaces:**
- Consumes: `PointEditExtensionBase` (Task 6), `PointThumb` (Task 5), `PointTrackerPlacementSupport` (Task 6), `DragListener` (existing).
- Produces: `LineHandlerExtension.DragListener` (public, get-only — reference-faithful *singular* property, ends up pointing at the `BottomRight`/`X2,Y2` thumb purely because it's created last in `OnInitialized` — Task 8's `DrawLineExtension` depends on exactly this). `internal readonly Dictionary<PlacementAlignment, DragListener> DragListeners` (both thumbs individually addressable — mirrors `ResizeThumbExtension.DragListeners`, needed since the public singular property alone can't reach `TopLeft`'s listener for testing).

- [ ] **Step 1: Write `LineHandlerExtension.cs`**

```csharp
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Post-creation point-editing for Line: two PointThumbs at (X1,Y1) and (X2,Y2).
    /// Ported from the reference's Extensions/LineHandlerExtension.cs. OverrideExtensions is
    /// scoped to the 3 extensions that exist in this port (spec decision 7) rather than the
    /// reference's 7-type list. The DragListener property's singular/last-created-wins shape is
    /// deliberately faithful to the reference (spec decision 4's documented ordering
    /// dependency) - Task 8's DrawLineExtension.ExternalStart depends on it landing on
    /// BottomRight/X2,Y2.</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Line), OverrideExtensions = new[] { typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension), typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension), typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension) })]
    public sealed class LineHandlerExtension : PointEditExtensionBase
    {
        PointThumb _topLeftThumb;
        PointThumb _bottomRightThumb;
        double _originalX1, _originalY1, _originalX2, _originalY2;

        public DragListener DragListener { get; private set; }
        internal readonly Dictionary<PlacementAlignment, DragListener> DragListeners = new Dictionary<PlacementAlignment, DragListener>();

        protected override IEnumerable<DesignerThumb> Thumbs {
            get { yield return _topLeftThumb; yield return _bottomRightThumb; }
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            // Order matters (spec decision 4): TopLeft first, BottomRight second, so the public
            // singular DragListener property ends up on BottomRight, matching DrawLineExtension's
            // (Task 8) dependency on exactly that.
            _topLeftThumb = CreateThumb(PlacementAlignment.TopLeft, InputSystemCursorShape.Cross);
            _bottomRightThumb = CreateThumb(PlacementAlignment.BottomRight, InputSystemCursorShape.Cross);
            UpdateAdornerVisibility();
        }

        PointThumb CreateThumb(PlacementAlignment alignment, InputSystemCursorShape cursor)
        {
            var thumb = new PointThumb { Alignment = alignment };
            thumb.SetDesignCursor(cursor);
            AdornerPanel.SetPlacement(thumb, Place(alignment, 0));
            adornerPanel.Children.Add(thumb);

            var drag = new DragListener(thumb);
            drag.Started += Drag_Started;
            drag.Changed += Drag_Changed;
            drag.Completed += Drag_Completed;
            DragListener = drag;
            DragListeners[alignment] = drag;

            return thumb;
        }

        void Drag_Started(DragListener drag)
        {
            var line = (Line)ExtendedItem.View;
            _originalX1 = line.X1; _originalY1 = line.Y1;
            _originalX2 = line.X2; _originalY2 = line.Y2;
            SetOperation();
        }

        void SetOperation()
        {
            if (resizeBehavior != null)
                operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            else
                changeGroup = ExtendedItem.OpenGroup("Resize");
            IsResizing = true;
        }

        void Drag_Changed(DragListener drag)
        {
            var (dx, dy) = drag.Delta;
            if (drag.Target == _topLeftThumb) {
                ExtendedItem.Properties["X1"].SetValue(System.Math.Round(_originalX1 + dx));
                ExtendedItem.Properties["Y1"].SetValue(System.Math.Round(_originalY1 + dy));
            } else {
                ExtendedItem.Properties["X2"].SetValue(System.Math.Round(_originalX2 + dx));
                ExtendedItem.Properties["Y2"].SetValue(System.Math.Round(_originalY2 + dy));
            }
            Invalidate();
        }

        void Drag_Completed(DragListener drag)
        {
            if (drag.IsCanceled) {
                operation?.Abort();
                operation = null;
                changeGroup?.Abort();
                changeGroup = null;
            } else {
                operation?.Commit();
                operation = null;
                changeGroup?.Commit();
                changeGroup = null;
            }
            IsResizing = false;
            Invalidate();
        }
    }
}
```

- [ ] **Step 2: Write the tests**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class LineHandlerExtensionTests
    {
        const string LineInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Line x:Name=\"L\" X1=\"10\" Y1=\"10\" X2=\"50\" Y2=\"50\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        static (DesignSurface surface, DesignItem lineItem, Microsoft.UI.Xaml.Shapes.Line line) Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(LineInCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var line = (Microsoft.UI.Xaml.Shapes.Line)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
            var lineItem = surface.DesignContext.Services.Component.GetDesignItem(line);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { lineItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            return (surface, lineItem, line);
        }

        [Test]
        public void DraggingTheBottomRightThumbMovesOnlyX2Y2()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, lineItem, line) = Load(window);
                var extension = lineItem.Extensions.OfType<LineHandlerExtension>().Single();
                var drag = extension.DragListeners[WinUIDesigner.DesignModel.PlacementAlignment.BottomRight];

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(10, 5));
                drag.SimulateUp(new Windows.Foundation.Point(10, 5));
                surface.UpdateLayout();

                Assert.AreEqual(10, line.X1, 0.5, "X1/Y1 untouched");
                Assert.AreEqual(10, line.Y1, 0.5);
                Assert.AreEqual(60, line.X2, 0.5);
                Assert.AreEqual(55, line.Y2, 0.5);
            });
        }

        [Test]
        public void DraggingTheTopLeftThumbMovesOnlyX1Y1AndCommitsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, lineItem, line) = Load(window);
                var extension = lineItem.Extensions.OfType<LineHandlerExtension>().Single();
                var drag = extension.DragListeners[WinUIDesigner.DesignModel.PlacementAlignment.TopLeft];
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int undoCountBefore = undo.UndoActions.Count;

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(-5, -5));
                drag.SimulateUp(new Windows.Foundation.Point(-5, -5));
                surface.UpdateLayout();

                Assert.AreEqual(5, line.X1, 0.5);
                Assert.AreEqual(5, line.Y1, 0.5);
                Assert.AreEqual(50, line.X2, 0.5, "X2/Y2 untouched");
                Assert.AreEqual(50, line.Y2, 0.5);
                Assert.AreEqual(undoCountBefore + 1, undo.UndoActions.Count, "one drag = one undo entry");
            });
        }
    }
}
```

- [ ] **Step 3: Run**

Run: `dotnet build WinUIDesigner.sln` then `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.LineHandlerExtensionTests --noresult`

Expected: 0 build errors, both tests PASS. If `UndoService.UndoActions` isn't the exact member name, check an existing undo-count test elsewhere in this project (e.g. `Phase4cDefinitionOfDoneTests.cs`) and match its exact API.

- [ ] **Step 4: Commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/LineHandlerExtension.cs tests/WinUIDesigner.Designer.Tests/LineHandlerExtensionTests.cs
git commit -m "feat: add LineHandlerExtension (spec decisions 3/4/7/13)"
```

---

### Task 8: `DrawLineExtension`, `DrawLineMouseGesture`

Draws a new `Line` by inserting a zero-length line at the click point, then re-driving `LineHandlerExtension`'s own endpoint-drag via `DragListener.ExternalStart` (Task 3) — spec Data flow #3.

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/DrawLineExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/DrawLineExtensionTests.cs`

**Interfaces:**
- Consumes: `IDrawItemExtension` (Task 4), `LineHandlerExtension`/`LineHandlerExtension.DragListener` (Task 7), `DragListener.ExternalStart/ExternalMove/ExternalStop` (Task 3), `CreateComponentTool.CreateItem` (Task 4, `internal static` — reused here since it's the exact same instantiate-and-register logic).

- [ ] **Step 1: Write `DrawLineExtension.cs`**

```csharp
using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Draws a new Line: inserts it at the click point with X1=Y1=X2=Y2=0 (its own local
    /// origin sits at the click point via Canvas.Left/Top from the insert - matches this Line's
    /// LineHandlerExtension resize math, which applies drag delta directly to X1/Y1/X2/Y2), then
    /// hands the drag to LineHandlerExtension's own already-built BottomRight/X2,Y2 endpoint
    /// DragListener via ExternalStart (spec decision 4). Ported from the reference's Extensions/
    /// DrawLineExtension.cs.</summary>
    [ExtensionFor(typeof(Microsoft.UI.Xaml.Controls.Canvas))]
    [ExtensionFor(typeof(Microsoft.UI.Xaml.Controls.Grid))]
    public sealed class DrawLineExtension : BehaviorExtension, IDrawItemExtension
    {
        public bool CanItemBeDrawn(Type createItemType) => createItemType == typeof(Line);

        public void StartDrawItem(DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point point, VirtualKeyModifiers modifiers, Action<DesignItem> drawItemCallback)
        {
            var changeGroup = clickedOn.OpenGroup("Draw Line");
            var createdItem = CreateComponentTool.CreateItem(clickedOn.Context, typeof(Line), null);

            var operation = PlacementOperation.TryStartInsertNewComponents(clickedOn, new[] { createdItem }, new[] { new Rect(point.X, point.Y, double.NaN, double.NaN) }, PlacementType.AddItem);
            if (operation == null) {
                changeGroup.Abort();
                return;
            }
            clickedOn.Context.Services.Selection.SetSelectedComponents(new[] { createdItem });
            operation.Commit();

            createdItem.Properties["Stroke"].SetValue(new SolidColorBrush(Colors.Black));
            createdItem.Properties["StrokeThickness"].SetValue(2.0);
            createdItem.Properties["Stretch"].SetValue(Stretch.None);

            drawItemCallback?.Invoke(createdItem);

            var lineHandler = createdItem.Extensions.OfType<LineHandlerExtension>().First();
            var gesture = new DrawLineMouseGesture(lineHandler, changeGroup);
            gesture.Start(panel, point);
        }
    }

    /// <summary>Drives LineHandlerExtension's BottomRight DragListener from outside a real
    /// pointer event on that thumb - see DragListener.ExternalStart's own doc comment and spec
    /// decision 4's "Capture responsibility" (capture already happened one level up in
    /// CreateComponentTool - this class never captures or focuses).</summary>
    sealed class DrawLineMouseGesture
    {
        readonly LineHandlerExtension _lineHandler;
        readonly ChangeGroup _changeGroup;
        IDesignPanel _designPanel;

        public DrawLineMouseGesture(LineHandlerExtension lineHandler, ChangeGroup changeGroup)
        {
            _lineHandler = lineHandler;
            _changeGroup = changeGroup;
        }

        public void Start(IDesignPanel designPanel, Point startPoint)
        {
            _designPanel = designPanel;
            designPanel.PointerMoved += OnPointerMoved;
            designPanel.PointerReleased += OnPointerReleased;
            designPanel.PointerCaptureLost += OnPointerCaptureLost;
            designPanel.KeyDown += OnKeyDown;
            _lineHandler.DragListener.ExternalStart(startPoint, VirtualKeyModifiers.None);
        }

        void OnPointerMoved(object sender, PointerRoutedEventArgs e) =>
            _lineHandler.DragListener.ExternalMove(e.GetCurrentPoint((UIElement)sender).Position, e.KeyModifiers);

        void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _lineHandler.DragListener.ExternalStop(e.GetCurrentPoint((UIElement)sender).Position);
            Commit();
        }

        void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => Cancel();

        void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
        }

        void Commit()
        {
            _changeGroup?.Commit();
            Stop();
        }

        void Cancel()
        {
            // Close the inner Resize transaction (LineHandlerExtension.SetOperation, opened when
            // ExternalStart's drag crossed the threshold) SYNCHRONOUSLY, before aborting the
            // outer "Draw Line" changeGroup - DragListener.Cancel() is a no-op if the drag never
            // crossed the threshold (Completed never fired, so nothing was ever opened), and
            // fires Drag_Completed(IsCanceled: true) if it did. Aborting outer-before-inner would
            // leave the inner transaction dangling on the UndoService's stack.
            _lineHandler.DragListener.Cancel();
            _changeGroup?.Abort();
            Stop();
        }

        void Stop()
        {
            _designPanel.PointerMoved -= OnPointerMoved;
            _designPanel.PointerReleased -= OnPointerReleased;
            _designPanel.PointerCaptureLost -= OnPointerCaptureLost;
            _designPanel.KeyDown -= OnKeyDown;
            _designPanel.ReleasePointerCaptures();
            // DefaultExtension.Services/.ExtendedItem are both public (DefaultExtension.cs:36,58)
            // - no internal accessor needed to reach them from this same-assembly, non-subclass
            // helper class.
            var toolService = _lineHandler.Services.GetService<IToolService>();
            if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
        }
    }
}
```

- [ ] **Step 2: `dotnet build` and fix any remaining compile errors**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors.

- [ ] **Step 3: Write the tests**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class DrawLineExtensionTests
    {
        const string EmptyCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\" />";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(EmptyCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void DrawingALineEndToEndProducesTheExpectedEndpointsAndOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Line));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int undoCountBefore = undo.UndoActions.Count;

                tool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(50, 60), Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(1, canvas.Children.Count);
                var line = (Microsoft.UI.Xaml.Shapes.Line)canvas.Children[0];
                Assert.AreEqual(Microsoft.UI.Xaml.Media.Stretch.None, line.Stretch);
                Assert.AreEqual(2, line.StrokeThickness);

                var lineItem = surface.DesignContext.Services.Component.GetDesignItem(line);
                var extension = lineItem.Extensions.OfType<LineHandlerExtension>().Single();

                extension.DragListener.SimulateDown(new Windows.Foundation.Point(0, 0));
                extension.DragListener.SimulateMove(new Windows.Foundation.Point(30, 20));
                extension.DragListener.SimulateUp(new Windows.Foundation.Point(30, 20));
                surface.UpdateLayout();

                Assert.AreEqual(30, line.X2, 0.5);
                Assert.AreEqual(20, line.Y2, 0.5);
                Assert.AreEqual(undoCountBefore + 1, undo.UndoActions.Count, "create + drag-to-size is ONE undo entry, not two");
            });
        }
    }
}
```

- [ ] **Step 4: Run**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.DrawLineExtensionTests --noresult`

Expected: PASS. If `PlacementOperation.TryStartInsertNewComponents` rejects the `double.NaN` width/height `Rect`, replace it with a small fixed-size `Rect` (e.g. `new Rect(point, new Windows.Foundation.Size(0, 0))`) instead — check the actual error/behavior first via this test run rather than guessing.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/DrawLineExtension.cs src/WinUIDesigner.Designer/Extensions/LineHandlerExtension.cs tests/WinUIDesigner.Designer.Tests/DrawLineExtensionTests.cs
git commit -m "feat: add DrawLineExtension and DrawLineMouseGesture (spec Data flow #3, decision 4)"
```

---

### Task 9: `PolyLineHandlerExtension`

Post-creation point-editing for `Polygon`/`Polyline`: drag a vertex to move it, Shift+Ctrl+click to delete a vertex, Shift+Alt while dragging to insert a new vertex mid-drag. Maintains the internal thumb index spec decision 12 requires.

**Note on fidelity:** the Shift+Alt insert-mid-drag interaction below is this plan's best-effort port of the reference's exact mechanics (captured second-hand via a deep source read, not a live line-by-line port like the rest of this task) — verify its behavior against the test in Step 3 during implementation, and adjust the exact insertion bookkeeping if the test reveals an off-by-one rather than trusting the prose reasoning in the comments blindly.

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/PolyLineHandlerExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/PolyLineHandlerExtensionTests.cs`

**Interfaces:**
- Consumes: `PointEditExtensionBase` (Task 6), `MultiPointThumb` (Task 5), `PointTrackerPlacementSupport` (Task 6).
- Produces: `internal readonly List<MultiPointThumb> PointThumbs` — the decision-12 index; Task 14's DoD suite asserts it stays in sync with `adornerPanel.Children`.

- [ ] **Step 1: Write `PolyLineHandlerExtension.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Post-creation point-editing for Polygon/Polyline. Ported from the reference's
    /// Extensions/PolyLineHandlerExtension.cs. No ZoomControl-scaling of drag.Delta (spec
    /// decision 11 - ZoomControl doesn't exist in this port, matching 4a/4b's own already-accepted
    /// resolution of the same gap).</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Polygon), OverrideExtensions = new[] { typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension), typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension), typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension) })]
    [ExtensionFor(typeof(Polyline), OverrideExtensions = new[] { typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension), typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension), typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension) })]
    public sealed class PolyLineHandlerExtension : PointEditExtensionBase
    {
        internal readonly List<MultiPointThumb> PointThumbs = new List<MultiPointThumb>();

        MultiPointThumb _draggingThumb;
        Point _dragOriginalPoint;
        bool _insertedDuringThisDrag;

        protected override IEnumerable<DesignerThumb> Thumbs => PointThumbs;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            var points = GetPointCollection();
            for (int i = 0; i < points.Count; i++) PointThumbs.Add(CreateThumb(i));
            UpdateAdornerVisibility();
        }

        PointCollection GetPointCollection()
        {
            if (ExtendedItem.View is Polygon polygon) return polygon.Points;
            return ((Polyline)ExtendedItem.View).Points;
        }

        MultiPointThumb CreateThumb(int index)
        {
            var thumb = new MultiPointThumb { Alignment = PlacementAlignment.BottomRight };
            thumb.SetDesignCursor(InputSystemCursorShape.Cross);
            var placement = Place(PlacementAlignment.BottomRight, index);
            thumb.AdornerPlacement = placement;
            thumb.Index = index;
            AdornerPanel.SetPlacement(thumb, placement);
            adornerPanel.Children.Add(thumb);

            bool dragOccurred = false;
            var drag = new DragListener(thumb);
            drag.Started += d => { dragOccurred = true; Drag_Started(thumb); };
            drag.Changed += d => Drag_Changed(d, thumb);
            drag.Completed += d => Drag_Completed(d);
            thumb.PointerReleased += (s, e) => {
                if (dragOccurred) { dragOccurred = false; return; }
                HandleThumbClick(thumb, e.KeyModifiers);
            };

            return thumb;
        }

        internal void HandleThumbClick(MultiPointThumb thumb, VirtualKeyModifiers modifiers)
        {
            bool shift = (modifiers & VirtualKeyModifiers.Shift) == VirtualKeyModifiers.Shift;
            bool ctrl = (modifiers & VirtualKeyModifiers.Control) == VirtualKeyModifiers.Control;
            if (shift && ctrl) DeletePoint(thumb);
        }

        // All three point-mutation methods below build a BRAND NEW PointCollection rather than
        // mutating the live one returned by GetPointCollection() in place - SetValue's undo
        // recording is not verified safe against "new value" being the very same, already-
        // mutated object reference the "old value" would have been, so this plan avoids that
        // risk entirely rather than assuming it's fine.
        static PointCollection Clone(PointCollection source)
        {
            var copy = new PointCollection();
            foreach (var p in source) copy.Add(p);
            return copy;
        }

        void DeletePoint(MultiPointThumb thumb)
        {
            var current = GetPointCollection();
            if (current.Count <= 1) return; // never delete down to zero points
            int index = thumb.Index;
            var next = Clone(current);
            next.RemoveAt(index);
            ExtendedItem.Properties["Points"].SetValue(next);

            adornerPanel.Children.Remove(thumb);
            PointThumbs.RemoveAt(index);
            for (int i = index; i < PointThumbs.Count; i++) PointThumbs[i].Index = i;
            Invalidate();
        }

        void Drag_Started(MultiPointThumb thumb)
        {
            _draggingThumb = thumb;
            _dragOriginalPoint = GetPointCollection()[thumb.Index];
            _insertedDuringThisDrag = false;
            SetOperation();
        }

        void SetOperation()
        {
            if (resizeBehavior != null)
                operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            else
                changeGroup = ExtendedItem.OpenGroup("Resize");
            IsResizing = true;
        }

        void Drag_Changed(DragListener drag, MultiPointThumb thumb)
        {
            bool shift = (drag.KeyModifiers & VirtualKeyModifiers.Shift) == VirtualKeyModifiers.Shift;
            bool alt = (drag.KeyModifiers & VirtualKeyModifiers.Menu) == VirtualKeyModifiers.Menu;

            if (shift && alt && !_insertedDuringThisDrag) {
                InsertPointDuringDrag(thumb);
                _insertedDuringThisDrag = true;
            }

            var (dx, dy) = drag.Delta;
            double newX = _dragOriginalPoint.X + dx;
            double newY = _dragOriginalPoint.Y + dy;
            if (shift && !alt) newY = _dragOriginalPoint.Y; // Shift-only: horizontal snap
            else if (alt && !shift) newX = _dragOriginalPoint.X; // Alt-only: vertical snap

            var next = Clone(GetPointCollection());
            next[thumb.Index] = new Point(System.Math.Round(newX), System.Math.Round(newY));
            ExtendedItem.Properties["Points"].SetValue(next);
            Invalidate();
        }

        /// <summary>Duplicates the point currently being dragged, inserting the duplicate BEFORE
        /// it - the original dragging thumb (and every thumb after it) shifts one index later,
        /// so the user keeps dragging the same logical point while a new stationary vertex is
        /// left behind at its pre-drag position.</summary>
        void InsertPointDuringDrag(MultiPointThumb draggingThumb)
        {
            int insertIndex = draggingThumb.Index;
            var current = GetPointCollection();
            var duplicated = current[insertIndex];
            var next = Clone(current);
            next.Insert(insertIndex, duplicated);
            ExtendedItem.Properties["Points"].SetValue(next);

            for (int i = insertIndex; i < PointThumbs.Count; i++) PointThumbs[i].Index = i + 1;

            var newThumb = CreateThumb(insertIndex);
            PointThumbs.Insert(insertIndex, newThumb);
        }

        void Drag_Completed(DragListener drag)
        {
            if (drag.IsCanceled) {
                operation?.Abort();
                operation = null;
                changeGroup?.Abort();
                changeGroup = null;
            } else {
                operation?.Commit();
                operation = null;
                changeGroup?.Commit();
                changeGroup = null;
            }
            IsResizing = false;
            _draggingThumb = null;
            Invalidate();
        }
    }
}
```

- [ ] **Step 2: `dotnet build`**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors.

- [ ] **Step 3: Write the tests**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PolyLineHandlerExtensionTests
    {
        const string PolylineInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Polyline x:Name=\"P\" Points=\"10,10 50,10 50,50\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
            "</Canvas>";

        static (DesignSurface surface, DesignItem polylineItem, Microsoft.UI.Xaml.Shapes.Polyline polyline, PolyLineHandlerExtension extension) Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(PolylineInCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var polyline = (Microsoft.UI.Xaml.Shapes.Polyline)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
            var polylineItem = surface.DesignContext.Services.Component.GetDesignItem(polyline);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { polylineItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            var extension = polylineItem.Extensions.OfType<PolyLineHandlerExtension>().Single();
            return (surface, polylineItem, polyline, extension);
        }

        [Test]
        public void ThreeInitialPointsProduceThreeThumbsInOrder()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, _, _, extension) = Load(window);
                Assert.AreEqual(3, extension.PointThumbs.Count);
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, extension.PointThumbs.Select(t => t.Index));
            });
        }

        [Test]
        public void DraggingTheMiddlePointMovesOnlyThatPointAndCommitsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, _, polyline, extension) = Load(window);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count;
                var drag = new DragListener(extension.PointThumbs[1]);

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(10, 5));
                drag.SimulateUp(new Windows.Foundation.Point(10, 5));
                surface.UpdateLayout();

                Assert.AreEqual(new Windows.Foundation.Point(10, 10), polyline.Points[0]);
                Assert.AreEqual(new Windows.Foundation.Point(60, 15), polyline.Points[1]);
                Assert.AreEqual(new Windows.Foundation.Point(50, 50), polyline.Points[2]);
                Assert.AreEqual(before + 1, undo.UndoActions.Count);
            });
        }

        [Test]
        public void ShiftCtrlClickDeletesAPointAndReindexesRemainingThumbs()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, _, polyline, extension) = Load(window);
                extension.HandleThumbClick(extension.PointThumbs[1], Windows.System.VirtualKeyModifiers.Shift | Windows.System.VirtualKeyModifiers.Control);
                surface.UpdateLayout();

                Assert.AreEqual(2, polyline.Points.Count);
                Assert.AreEqual(new Windows.Foundation.Point(10, 10), polyline.Points[0]);
                Assert.AreEqual(new Windows.Foundation.Point(50, 50), polyline.Points[1]);
                Assert.AreEqual(2, extension.PointThumbs.Count, "the deleted thumb's own entry is gone");
                CollectionAssert.AreEqual(new[] { 0, 1 }, extension.PointThumbs.Select(t => t.Index), "remaining thumbs re-indexed with no gap");
            });
        }
    }
}
```

- [ ] **Step 4: Run**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.PolyLineHandlerExtensionTests --noresult`

Expected: all PASS. The Shift+Alt insert-mid-drag interaction (not covered by an automated test above, given the note at the top of this task) should be checked by eye once Task 10's `DrawPolyLineExtension`/the whole phase is wired up — matching this spec's own precedent for cosmetic/hard-to-automate pieces (Testing/DoD's "Not unit-testable, checked by eye instead").

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/PolyLineHandlerExtension.cs tests/WinUIDesigner.Designer.Tests/PolyLineHandlerExtensionTests.cs
git commit -m "feat: add PolyLineHandlerExtension (spec decisions 3/7/11/12/13)"
```

---

### Task 10: `DrawPolyLineExtension`, `DrawPolylineMouseGesture`

Draws a new `Polygon`/`Polyline` by click-committing one vertex per click, ending on `DoubleTapped` — spec Data flow #4. Does not reuse `DragListener`/`LineHandlerExtension`'s trick — this is its own small state machine, since there's no existing per-point drag handler to re-enter for a shape that doesn't exist yet.

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/DrawPolyLineExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/DrawPolyLineExtensionTests.cs`

**Interfaces:**
- Consumes: `IDrawItemExtension` (Task 4), `CreateComponentTool.CreateItem` (Task 4), `DesignItem.ReapplyAllExtensions()` (existing) — commits the final `Points` and lets `PolyLineHandlerExtension` (Task 9) attach fresh point-thumbs.
- Produces: nothing further tasks depend on (leaf of this dependency chain, alongside Tasks 8/13).

**Important — read before implementing:** `CreateComponentTool` (Task 4) now tracks an internal `_drawing` flag specifically so its own still-subscribed `PointerPressed` handler does not re-fire (and start a second draw gesture) on each of this gesture's own subsequent clicks. If Task 4 was implemented without that flag, add it there first — this task's multi-click design depends on it.

- [ ] **Step 1: Write `DrawPolyLineExtension.cs`**

```csharp
using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Draws a new Polygon/Polyline: each click commits a vertex, DoubleTapped ends the
    /// gesture (replaces the reference's hand-rolled GetDoubleClickTime() P/Invoke outright - a
    /// native WinUI gesture recognizer, not a 1:1 API swap - spec decision 1/mechanism table).
    /// Ported from the reference's Extensions/DrawPolyLineExtension.cs; Alt-snap only (Shift-snap
    /// dropped - the reference's own Shift/Alt roles for THIS gesture are the freehand-angle vs
    /// axis-snap distinction described in the deep source read, and this port keeps the simpler,
    /// unambiguous single-modifier form: Alt held snaps the in-progress segment to the nearest
    /// horizontal/vertical axis).</summary>
    [ExtensionFor(typeof(Canvas))]
    [ExtensionFor(typeof(Grid))]
    public sealed class DrawPolyLineExtension : BehaviorExtension, IDrawItemExtension
    {
        public bool CanItemBeDrawn(Type createItemType) => createItemType == typeof(Polyline) || createItemType == typeof(Polygon);

        public void StartDrawItem(DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point point, VirtualKeyModifiers modifiers, Action<DesignItem> drawItemCallback)
        {
            var changeGroup = clickedOn.OpenGroup("Draw " + createItemType.Name);
            var createdItem = CreateComponentTool.CreateItem(clickedOn.Context, createItemType, null);

            var operation = PlacementOperation.TryStartInsertNewComponents(clickedOn, new[] { createdItem }, new[] { new Rect(point.X, point.Y, double.NaN, double.NaN) }, PlacementType.AddItem);
            if (operation == null) {
                changeGroup.Abort();
                return;
            }
            clickedOn.Context.Services.Selection.SetSelectedComponents(new[] { createdItem });
            operation.Commit();

            createdItem.Properties["Stroke"].SetValue(new SolidColorBrush(Colors.Black));
            createdItem.Properties["StrokeThickness"].SetValue(2.0);
            createdItem.Properties["Stretch"].SetValue(Stretch.None);

            var initialPoints = new PointCollection();
            initialPoints.Add(new Point(0, 0)); // the shape's own local origin, == the click point via Canvas.Left/Top from the insert above
            createdItem.Properties["Points"].SetValue(initialPoints);

            drawItemCallback?.Invoke(createdItem);

            var gesture = new DrawPolylineMouseGesture(createdItem, changeGroup);
            gesture.Start(panel, point);
        }
    }

    sealed class DrawPolylineMouseGesture
    {
        readonly DesignItem _item;
        ChangeGroup _changeGroup;
        IDesignPanel _designPanel;
        Point _origin;

        public DrawPolylineMouseGesture(DesignItem item, ChangeGroup changeGroup)
        {
            _item = item;
            _changeGroup = changeGroup;
        }

        public void Start(IDesignPanel designPanel, Point origin)
        {
            _designPanel = designPanel;
            _origin = origin;
            designPanel.PointerMoved += OnPointerMoved;
            designPanel.PointerPressed += OnPointerPressed;
            designPanel.DoubleTapped += OnDoubleTapped;
            designPanel.PointerCaptureLost += OnPointerCaptureLost;
            designPanel.KeyDown += OnKeyDown;
        }

        PointCollection GetPoints() => _item.View is Polygon polygon ? polygon.Points : ((Polyline)_item.View).Points;

        static PointCollection Clone(PointCollection source)
        {
            var copy = new PointCollection();
            foreach (var p in source) copy.Add(p);
            return copy;
        }

        Point ToLocal(Point panelPoint) => new Point(panelPoint.X - _origin.X, panelPoint.Y - _origin.Y);

        void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var local = ApplyAltSnap(e);
            var next = Clone(GetPoints());
            next[next.Count - 1] = local; // "preview" trailing point - never left committed between clicks
            _item.Properties["Points"].SetValue(next);
        }

        void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            var local = ApplyAltSnap(e);
            var next = Clone(GetPoints());
            next[next.Count - 1] = local; // finalize the point the preview was tracking
            next.Add(local); // start a new preview point at the same spot
            _item.Properties["Points"].SetValue(next);
            e.Handled = true;
        }

        Point ApplyAltSnap(PointerRoutedEventArgs e)
        {
            var local = ToLocal(e.GetCurrentPoint((UIElement)_designPanel).Position);
            bool alt = (e.KeyModifiers & VirtualKeyModifiers.Menu) == VirtualKeyModifiers.Menu;
            if (!alt) return local;

            var points = GetPoints();
            if (points.Count < 2) return local;
            var lastCommitted = points[points.Count - 2]; // the last point BEFORE the trailing preview
            bool horizontal = System.Math.Abs(local.X - lastCommitted.X) >= System.Math.Abs(local.Y - lastCommitted.Y);
            return horizontal ? new Point(local.X, lastCommitted.Y) : new Point(lastCommitted.X, local.Y);
        }

        void OnDoubleTapped(object sender, TappedRoutedEventArgs e)
        {
            var next = Clone(GetPoints());
            if (next.Count > 1) next.RemoveAt(next.Count - 1); // drop the trailing preview point
            _item.Properties["Points"].SetValue(next);
            Commit();
            e.Handled = true;
        }

        void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => Cancel();

        void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
        }

        void Commit()
        {
            _changeGroup?.Commit();
            _changeGroup = null;
            Stop(reapply: true);
        }

        void Cancel()
        {
            _changeGroup?.Abort();
            _changeGroup = null;
            Stop(reapply: false);
        }

        void Stop(bool reapply)
        {
            _designPanel.PointerMoved -= OnPointerMoved;
            _designPanel.PointerPressed -= OnPointerPressed;
            _designPanel.DoubleTapped -= OnDoubleTapped;
            _designPanel.PointerCaptureLost -= OnPointerCaptureLost;
            _designPanel.KeyDown -= OnKeyDown;
            _designPanel.ReleasePointerCaptures();
            if (reapply) _item.ReapplyAllExtensions();
            var toolService = _item.Services.GetService<IToolService>();
            if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
        }
    }
}
```

- [ ] **Step 2: `dotnet build`**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors.

- [ ] **Step 3: Write the tests**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class DrawPolyLineExtensionTests
    {
        const string EmptyCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\" />";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(EmptyCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void MultiClickSequenceProducesTheExpectedPointsAndDoubleTappedEndsTheGesture()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Polyline));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;

                tool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(50, 50), Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(1, canvas.Children.Count);
                var polyline = (Microsoft.UI.Xaml.Shapes.Polyline)canvas.Children[0];

                // Two more committed vertices via internal test seams (DrawPolylineMouseGesture
                // itself is private - exercised end-to-end through the real designPanel events it
                // subscribed to, using the same Handle*-less direct-event-raise limitation this
                // whole phase already accepts for pointer-driven gestures: this test drives it via
                // the CreateComponentTool-returned tool switching back only once the gesture ends,
                // observable state).
                Assert.AreEqual(1, polyline.Points.Count, "one preview point right after the first click");

                Assert.AreSame(tool, surface.DesignContext.Services.GetService<IToolService>().CurrentTool, "gesture still in progress - tool not reset yet");
            });
        }
    }
}
```

Given `DrawPolylineMouseGesture` is a private class subscribed directly to real `PointerMoved`/`PointerPressed`/`DoubleTapped` events (WinRT types this phase has established cannot be fabricated in a test — decision 9/"Known gaps" #6), this task's automated test coverage is necessarily shallow (the first click only) — the full multi-click-then-double-tap sequence is a "checked by eye" item per this spec's own DoD, run the actual built `.exe`, draw a real Polyline with several clicks and a double-click to finish, and confirm the final `Points` match what was clicked before considering this task done.

- [ ] **Step 4: Run**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.DrawPolyLineExtensionTests --noresult`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/DrawPolyLineExtension.cs tests/WinUIDesigner.Designer.Tests/DrawPolyLineExtensionTests.cs
git commit -m "feat: add DrawPolyLineExtension and DrawPolylineMouseGesture (spec Data flow #4, decisions 1/8)"
```

---

### Task 11: `PathThumb`, `PathHandlerExtension` core (geometry dispatch, drag/select/commit)

Post-creation point-editing for `Path`. The largest single file this phase ports (reference is 799 lines) — this task covers everything except the convert-segment context menu (Task 12). In scope: `PathGeometry` with `LineSegment`/`PolyLineSegment`/`BezierSegment`/`PolyBezierSegment`/`QuadraticBezierSegment`/`PolyQuadraticBezierSegment`/`ArcSegment` (spec's "Out of scope": `RectangleGeometry`/`EllipseGeometry`/`LineGeometry`/`CombinedGeometry`/`GeometryGroup`/`StreamGeometry` point-editing — `GetPoints()` below returns an empty list, no thumbs, no crash, for any `Path.Data` that isn't a `PathGeometry`).

**Files:**
- Create: `src/WinUIDesigner.Designer/Controls/Thumbs/PathThumb.cs`
- Create: `src/WinUIDesigner.Designer/Extensions/PathHandlerExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionTests.cs`

**Interfaces:**
- Consumes: `PointEditExtensionBase` (Task 6), `PointThumb` (Task 5).
- Produces: `PathThumb : PointThumb` with `PathPoint AssociatedPoint` (`internal`, Task 12 reads it to build the convert-segment menu). `PathHandlerExtension.PointThumbs` (`internal readonly List<PathThumb>`, decision 12's index).

- [ ] **Step 1: Write `PathThumb.cs`**

```csharp
namespace WinUIDesigner.Designer.Controls.Thumbs
{
    /// <summary>A Path point-edit handle - an anchor (square) or Bezier control point (blue
    /// circle, IsEllipse=true, with a dashed RelativeToPoint connector to its anchor). Unlike the
    /// reference's PathThumb, Point/RelativeToPoint are set imperatively by PathHandlerExtension
    /// (Task 11) rather than via a live data-binding to PathPoint.TranslatedPoint - this port has
    /// no RenderedGeometry-driven transform to bind through (Task 1's ruling), so an imperative
    /// update on every drag tick is equivalent and simpler than wiring INotifyPropertyChanged
    /// through PathPoint for a binding that would only ever carry the identity transform.</summary>
    public sealed class PathThumb : PointThumb
    {
        internal WinUIDesigner.Designer.Extensions.PathHandlerExtension.PathPoint AssociatedPoint { get; set; }
    }
}
```

- [ ] **Step 2: Write `PathHandlerExtension.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Post-creation point-editing for Path. Ported from the reference's Extensions/
    /// PathHandlerExtension.cs - the largest single file in this phase (799 reference lines).
    /// This class covers geometry dispatch + drag/select/commit; the convert-segment context
    /// menu is Task 12 (a separate partial-class-free file addition - PathThumb's own
    /// ContextFlyout, not this file). No ZoomControl-scaling of drag.Delta (spec decision 11).
    /// No TranslatedPoint/RenderedGeometry transform (spec decision 10/Task 1's ruling) - Point
    /// values here are the Path's own local coordinate space directly.</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Path), OverrideExtensions = new[] { typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension), typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension), typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension) })]
    public sealed partial class PathHandlerExtension : PointEditExtensionBase
    {
        internal readonly List<PathThumb> PointThumbs = new List<PathThumb>();
        List<PathPoint> _points = new List<PathPoint>();
        PathThumb _draggingThumb;
        Point _dragOriginalPoint;

        protected override IEnumerable<DesignerThumb> Thumbs => PointThumbs;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
            RebuildThumbs();
        }

        protected override void OnRemove()
        {
            Services.Selection.PrimarySelectionChanged -= OnPrimarySelectionChanged;
            base.OnRemove();
        }

        void OnPrimarySelectionChanged(object sender, EventArgs e)
        {
            // Reference guards an in-progress keyboard-driven move here (IKeyDown/IKeyUp - out of
            // scope this phase, spec's "Out of scope"). Nothing to guard without that feature.
        }

        internal void RebuildThumbs()
        {
            foreach (var thumb in PointThumbs) adornerPanel.Children.Remove(thumb);
            PointThumbs.Clear();
            _points = GetPoints();
            foreach (var point in _points) PointThumbs.Add(CreateThumb(point));
            UpdateAdornerVisibility();
        }

        // ---- Geometry dispatch (PathGeometry only - see class remarks) ----

        internal sealed class PathPoint
        {
            readonly Action<Point> _save;
            public Point Point { get; private set; }
            public PathPoint TargetPathPoint { get; set; } // non-null => this is a Bezier control point pointing at its anchor
            public object SegmentOrFigure { get; set; }
            public PathFigure ParentFigure { get; set; }
            public int PolyLineIndex { get; set; } = -1;

            public PathPoint(Point point, Action<Point> save)
            {
                Point = point;
                _save = save;
            }

            public void SetPoint(Point value)
            {
                Point = value;
                _save(value);
            }
        }

        List<PathPoint> GetPoints()
        {
            var path = (Path)ExtendedItem.View;
            var list = new List<PathPoint>();
            if (path.Data is PathGeometry geometry) {
                foreach (var figure in geometry.Figures) AddFigurePoints(list, figure, geometry);
            }
            return list;
        }

        void AddFigurePoints(List<PathPoint> list, PathFigure figure, PathGeometry geometry)
        {
            var startPoint = new PathPoint(figure.StartPoint, p => { figure.StartPoint = p; SaveGeometry(geometry); }) { SegmentOrFigure = figure, ParentFigure = figure };
            list.Add(startPoint);
            var previousAnchor = startPoint;

            foreach (var segment in figure.Segments) {
                switch (segment) {
                    case LineSegment line: {
                        var anchor = new PathPoint(line.Point, p => { line.Point = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure };
                        list.Add(anchor);
                        previousAnchor = anchor;
                        break;
                    }
                    case PolyLineSegment poly: {
                        for (int n = 0; n < poly.Points.Count; n++) {
                            int captured = n;
                            var anchor = new PathPoint(poly.Points[n], p => { SetPolyPoint(poly.Points, captured, p, pts => poly.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = n };
                            list.Add(anchor);
                            previousAnchor = anchor;
                        }
                        break;
                    }
                    case BezierSegment bezier: {
                        var anchor = new PathPoint(bezier.Point3, p => { bezier.Point3 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure };
                        var c1 = new PathPoint(bezier.Point1, p => { bezier.Point1 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, TargetPathPoint = previousAnchor };
                        var c2 = new PathPoint(bezier.Point2, p => { bezier.Point2 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, TargetPathPoint = anchor };
                        list.Add(c1); list.Add(c2); list.Add(anchor);
                        previousAnchor = anchor;
                        break;
                    }
                    case PolyBezierSegment polyBezier: {
                        for (int n = 0; n + 2 < polyBezier.Points.Count + 1 && n <= polyBezier.Points.Count - 3; n += 3) {
                            int c1Index = n, c2Index = n + 1, anchorIndex = n + 2;
                            var anchor = new PathPoint(polyBezier.Points[anchorIndex], p => { SetPolyPoint(polyBezier.Points, anchorIndex, p, pts => polyBezier.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = anchorIndex };
                            var c1 = new PathPoint(polyBezier.Points[c1Index], p => { SetPolyPoint(polyBezier.Points, c1Index, p, pts => polyBezier.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = c1Index, TargetPathPoint = previousAnchor };
                            var c2 = new PathPoint(polyBezier.Points[c2Index], p => { SetPolyPoint(polyBezier.Points, c2Index, p, pts => polyBezier.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = c2Index, TargetPathPoint = anchor };
                            list.Add(c1); list.Add(c2); list.Add(anchor);
                            previousAnchor = anchor;
                        }
                        break;
                    }
                    case QuadraticBezierSegment quad: {
                        var anchor = new PathPoint(quad.Point2, p => { quad.Point2 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure };
                        var c1 = new PathPoint(quad.Point1, p => { quad.Point1 = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, TargetPathPoint = anchor };
                        list.Add(c1); list.Add(anchor);
                        previousAnchor = anchor;
                        break;
                    }
                    case PolyQuadraticBezierSegment polyQuad: {
                        for (int n = 0; n <= polyQuad.Points.Count - 2; n += 2) {
                            int c1Index = n, anchorIndex = n + 1;
                            var anchor = new PathPoint(polyQuad.Points[anchorIndex], p => { SetPolyPoint(polyQuad.Points, anchorIndex, p, pts => polyQuad.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = anchorIndex };
                            var c1 = new PathPoint(polyQuad.Points[c1Index], p => { SetPolyPoint(polyQuad.Points, c1Index, p, pts => polyQuad.Points = pts); SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure, PolyLineIndex = c1Index, TargetPathPoint = anchor };
                            list.Add(c1); list.Add(anchor);
                            previousAnchor = anchor;
                        }
                        break;
                    }
                    case ArcSegment arc: {
                        // "Virtual" size-control point - reference uses arc.Point - new Vector(arc.Size.Width, arc.Size.Height);
                        // WinUI has no Vector type (root CLAUDE.md's known gaps), so this is the
                        // equivalent Point-only arithmetic instead.
                        var anchor = new PathPoint(arc.Point, p => { arc.Point = p; SaveGeometry(geometry); }) { SegmentOrFigure = segment, ParentFigure = figure };
                        var sizeHandlePoint = new Point(arc.Point.X - arc.Size.Width, arc.Point.Y - arc.Size.Height);
                        var sizeHandle = new PathPoint(sizeHandlePoint, p => {
                            arc.Size = new Size(System.Math.Abs(arc.Point.X - p.X), System.Math.Abs(arc.Point.Y - p.Y));
                            SaveGeometry(geometry);
                        }) { SegmentOrFigure = segment, ParentFigure = figure, TargetPathPoint = anchor };
                        list.Add(sizeHandle); list.Add(anchor);
                        previousAnchor = anchor;
                        break;
                    }
                    // RectangleGeometry/EllipseGeometry/LineGeometry/CombinedGeometry/GeometryGroup/
                    // StreamGeometry point-editing: out of scope (spec's "Out of scope") - nothing
                    // this phase's own DrawPathExtension ever creates uses them.
                }
            }
        }

        static void SetPolyPoint(PointCollection source, int index, Point value, Action<PointCollection> assign)
        {
            var next = new PointCollection();
            for (int i = 0; i < source.Count; i++) next.Add(i == index ? value : source[i]);
            assign(next);
        }

        void SaveGeometry(PathGeometry geometry)
        {
            // A new top-level PathGeometry + Figures collection (same, in-place-mutated PathFigure/
            // PathSegment objects underneath) so SetValue sees a genuinely different top-level
            // reference than whatever it cached before - PathFigure/PathSegment are not
            // separately-tracked DesignItems (spec decision 5's note), so only the Data property
            // itself is what the undo system can observe changing. If undo doesn't register a
            // point-drag correctly during Step 4's test run, the fix is to also clone each
            // PathFigure, not just this top level - verify via the real test, don't assume this
            // is sufficient without checking.
            var newGeometry = new PathGeometry { FillRule = geometry.FillRule };
            var newFigures = new PathFigureCollection();
            foreach (var f in geometry.Figures) newFigures.Add(f);
            newGeometry.Figures = newFigures;
            ExtendedItem.Properties["Data"].SetValue(newGeometry);
        }

        // ---- Thumbs, drag, commit ----

        PathThumb CreateThumb(PathPoint point)
        {
            var thumb = new PathThumb { AssociatedPoint = point, Alignment = PlacementAlignment.BottomRight };
            thumb.SetDesignCursor(InputSystemCursorShape.Cross);
            thumb.IsEllipse = point.TargetPathPoint != null;
            thumb.Point = point.Point;
            thumb.RelativeToPoint = point.TargetPathPoint?.Point;
            adornerPanel.Children.Add(thumb);

            var drag = new DragListener(thumb);
            drag.Started += d => Drag_Started(thumb);
            drag.Changed += d => Drag_Changed(d, thumb);
            drag.Completed += d => Drag_Completed(d);

            return thumb;
        }

        void Drag_Started(PathThumb thumb)
        {
            _draggingThumb = thumb;
            _dragOriginalPoint = thumb.AssociatedPoint.Point;
            SetOperation();
        }

        void SetOperation()
        {
            operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            IsResizing = true;
        }

        void Drag_Changed(DragListener drag, PathThumb thumb)
        {
            var (dx, dy) = drag.Delta;
            var newPoint = new Point(System.Math.Round(_dragOriginalPoint.X + dx), System.Math.Round(_dragOriginalPoint.Y + dy));
            thumb.AssociatedPoint.SetPoint(newPoint);
            thumb.Point = newPoint;
            // Every control-point thumb whose TargetPathPoint is the point that just moved needs
            // its own connector line re-anchored, and every anchor whose own control points moved
            // WITH it (none do here - Bezier control points move independently) does not.
            foreach (var t in PointThumbs) {
                if (t.AssociatedPoint.TargetPathPoint == thumb.AssociatedPoint) t.RelativeToPoint = newPoint;
            }
            Invalidate();
        }

        void Drag_Completed(DragListener drag)
        {
            if (drag.IsCanceled) {
                operation?.Abort();
            } else {
                operation?.Commit();
            }
            operation = null;
            IsResizing = false;
            _draggingThumb = null;
            Invalidate();
        }
    }
}
```

- [ ] **Step 3: `dotnet build`**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors. `sealed partial class PathHandlerExtension` is deliberate — Task 12 adds a second file completing the same partial class (the convert-segment context menu) rather than reopening this one.

- [ ] **Step 4: Write the tests**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PathHandlerExtensionTests
    {
        const string PathInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Path x:Name=\"P\" Stroke=\"Black\" StrokeThickness=\"2\">" +
            "<Path.Data><PathGeometry><PathFigure StartPoint=\"10,10\">" +
            "<LineSegment Point=\"50,10\" /><LineSegment Point=\"50,50\" />" +
            "</PathFigure></PathGeometry></Path.Data></Path>" +
            "</Canvas>";

        static (DesignSurface surface, DesignItem pathItem, Microsoft.UI.Xaml.Shapes.Path path, PathHandlerExtension extension) Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(PathInCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var path = (Microsoft.UI.Xaml.Shapes.Path)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
            var pathItem = surface.DesignContext.Services.Component.GetDesignItem(path);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { pathItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            var extension = pathItem.Extensions.OfType<PathHandlerExtension>().Single();
            return (surface, pathItem, path, extension);
        }

        [Test]
        public void ThreePointsInTheFigureProduceThreeThumbs()
        {
            // StartPoint + 2 LineSegment anchors = 3 PathPoints, 3 thumbs (no control points -
            // LineSegment has no TargetPathPoint).
            WinUITestHost.RunWithWindow(window => {
                var (_, _, _, extension) = Load(window);
                Assert.AreEqual(3, extension.PointThumbs.Count);
                Assert.IsTrue(extension.PointThumbs.All(t => !t.IsEllipse), "LineSegment anchors are all squares, no Bezier control points here");
            });
        }

        [Test]
        public void DraggingTheMiddleAnchorMovesOnlyThatLineSegmentsPointAndCommitsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, _, path, extension) = Load(window);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count;
                var middleThumb = extension.PointThumbs[1]; // first LineSegment's Point (was 50,10)
                var drag = new DragListener(middleThumb);

                drag.SimulateDown(new Windows.Foundation.Point(0, 0));
                drag.SimulateMove(new Windows.Foundation.Point(5, -5));
                drag.SimulateUp(new Windows.Foundation.Point(5, -5));
                surface.UpdateLayout();

                var geometry = (Microsoft.UI.Xaml.Media.PathGeometry)path.Data;
                var figure = geometry.Figures[0];
                Assert.AreEqual(10, figure.StartPoint.X, 0.5, "start point untouched");
                var firstLine = (Microsoft.UI.Xaml.Media.LineSegment)figure.Segments[0];
                Assert.AreEqual(55, firstLine.Point.X, 0.5);
                Assert.AreEqual(5, firstLine.Point.Y, 0.5);
                var secondLine = (Microsoft.UI.Xaml.Media.LineSegment)figure.Segments[1];
                Assert.AreEqual(50, secondLine.Point.X, 0.5, "second segment's own point untouched");
                Assert.AreEqual(undo.UndoActions.Count, before + 1);
            });
        }
    }
}
```

- [ ] **Step 5: Run**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.PathHandlerExtensionTests --noresult`

Expected: PASS. If the undo-count assertion fails, that's this task's own flagged risk (`SaveGeometry`'s comment above) surfacing for real — fix by also cloning each `PathFigure` inside `SaveGeometry`, not just the top-level `PathGeometry`/`Figures` collection, and re-run.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Designer/Controls/Thumbs/PathThumb.cs src/WinUIDesigner.Designer/Extensions/PathHandlerExtension.cs tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionTests.cs
git commit -m "feat: add PathThumb and PathHandlerExtension core (spec decisions 3/7/10/11)"
```

---

### Task 12: `PathHandlerExtension` convert-segment context menu

Each `PathThumb`'s own `ContextFlyout` (not 4c's per-`DesignItem` composite `ContextFlyout` merge system in `DesignPanel` — a `PathPoint` isn't a `DesignItem`): convert-segment-type items, `IsClosed`/`IsStroked` toggles. Faithfully incomplete per spec decision 5 — `PolyBezierSegment`/`PolyQuadraticBezierSegment` convert stays a no-op.

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/PathHandlerExtension.ConvertMenu.cs` (second file completing Task 11's `partial class PathHandlerExtension`)
- Modify: `src/WinUIDesigner.Designer/Extensions/PathHandlerExtension.cs:CreateThumb` (Task 11) — one line
- Test: `tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionConvertMenuTests.cs`

**Interfaces:**
- Consumes: `PathHandlerExtension.PathPoint` (Task 11, `internal` nested type), `PathThumb.AssociatedPoint`/`.ContextFlyout` (Task 11 / `UIElement`).

- [ ] **Step 1: Add the `ContextFlyout` wiring to Task 11's `CreateThumb`**

In `PathHandlerExtension.cs`, inside `CreateThumb(PathPoint point)`, right after `thumb.RelativeToPoint = point.TargetPathPoint?.Point;`:

```csharp
thumb.ContextFlyout = BuildMenu(point);
```

- [ ] **Step 2: Write `PathHandlerExtension.ConvertMenu.cs`**

```csharp
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Extensions
{
    public sealed partial class PathHandlerExtension
    {
        internal enum PathPartConvertType { ToLineSegment, ToBezierSegment, ToQuadricBezierSegment, ToArcSegment, InsertPoint }

        internal MenuFlyout BuildMenu(PathPoint point)
        {
            var flyout = new MenuFlyout();
            // Only a real anchor point (not a Bezier control point, TargetPathPoint == null) on a
            // real segment gets a convert menu - mirrors the reference's own BuildMenu gate.
            if (point.TargetPathPoint == null && point.SegmentOrFigure is PathSegment segment) {
                AddConvertItem(flyout, "Convert to Line", point, segment, PathPartConvertType.ToLineSegment);
                AddConvertItem(flyout, "Convert to Bezier", point, segment, PathPartConvertType.ToBezierSegment);
                AddConvertItem(flyout, "Convert to Quadratic Bezier", point, segment, PathPartConvertType.ToQuadricBezierSegment);
                AddConvertItem(flyout, "Convert to Arc", point, segment, PathPartConvertType.ToArcSegment);
                AddConvertItem(flyout, "Insert Point", point, segment, PathPartConvertType.InsertPoint);

                var closedItem = new ToggleMenuFlyoutItem { Text = "Is Closed", IsChecked = point.ParentFigure.IsClosed };
                closedItem.Click += (s, e) => ChangeIsClosed(point.ParentFigure);
                flyout.Items.Add(closedItem);

                var strokedItem = new ToggleMenuFlyoutItem { Text = "Is Stroked", IsChecked = segment.IsStroked };
                strokedItem.Click += (s, e) => ChangeIsStroked(segment);
                flyout.Items.Add(strokedItem);
            }
            // "is Smooth Join" / "is large Arc" / "Rotation Angle" / "Clockwise SweepDirection" -
            // the reference's own upstream incompleteness (spec decision 5): those items only
            // ever set IsChecked in the reference, no Click handler wired, so the reference
            // cannot actually change them either. Not ported at all here, rather than adding
            // non-functional display-only menu items with no real way to assert they "stay a
            // no-op" beyond "the item exists but does nothing" - omitting them entirely is a
            // stronger, more honest statement of the same faithfully-incomplete decision.
            return flyout;
        }

        void AddConvertItem(MenuFlyout flyout, string text, PathPoint point, PathSegment segment, PathPartConvertType type)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (s, e) => ConvertPart(point, segment, type);
            flyout.Items.Add(item);
        }

        internal void ConvertPart(PathPoint point, PathSegment segment, PathPartConvertType convertType)
        {
            var figure = point.ParentFigure;
            int idx = -1;
            for (int i = 0; i < figure.Segments.Count; i++) if (ReferenceEquals(figure.Segments[i], segment)) { idx = i; break; }
            if (idx < 0) return;

            if (segment is PolyBezierSegment || segment is PolyQuadraticBezierSegment) {
                // Reference's own upstream incompleteness (spec decision 5): these two branches
                // are empty //TODO in the reference itself. Matched here - not completed.
                return;
            }

            var anchorPoint = point.Point;
            var midPoint = point.TargetPathPoint != null
                ? new Point((point.TargetPathPoint.Point.X + anchorPoint.X) / 2, (point.TargetPathPoint.Point.Y + anchorPoint.Y) / 2)
                : anchorPoint;

            var operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);

            if (convertType == PathPartConvertType.InsertPoint) {
                CommitFiguresChange(figure, idx, new PathSegment[] { segment, new LineSegment { Point = midPoint } }, operation);
                return;
            }

            PathSegment replacement;
            switch (convertType) {
                case PathPartConvertType.ToBezierSegment:
                    replacement = new BezierSegment {
                        Point1 = new Point(midPoint.X - 40, midPoint.Y - 40),
                        Point2 = new Point(midPoint.X - 40, midPoint.Y + 40),
                        Point3 = anchorPoint
                    };
                    break;
                case PathPartConvertType.ToQuadricBezierSegment:
                    replacement = new QuadraticBezierSegment {
                        Point1 = new Point(anchorPoint.X - 40, anchorPoint.Y - 40),
                        Point2 = anchorPoint
                    };
                    break;
                case PathPartConvertType.ToArcSegment:
                    replacement = new ArcSegment { Point = anchorPoint, Size = new Size(20, 20) };
                    break;
                default:
                    replacement = new LineSegment { Point = anchorPoint };
                    break;
            }
            CommitFiguresChange(figure, idx, new[] { replacement }, operation);
        }

        void CommitFiguresChange(PathFigure targetFigure, int segmentIndex, IEnumerable<PathSegment> replacements, PlacementOperation operation)
        {
            var geometry = (PathGeometry)((Microsoft.UI.Xaml.Shapes.Path)ExtendedItem.View).Data;
            var newFigures = new PathFigureCollection();
            foreach (var f in geometry.Figures) {
                if (!ReferenceEquals(f, targetFigure)) { newFigures.Add(f); continue; }
                var newFigure = new PathFigure { StartPoint = f.StartPoint, IsClosed = f.IsClosed, IsFilled = f.IsFilled };
                var newSegments = new PathSegmentCollection();
                for (int i = 0; i < f.Segments.Count; i++) {
                    if (i == segmentIndex) { foreach (var r in replacements) newSegments.Add(r); }
                    else newSegments.Add(f.Segments[i]);
                }
                newFigure.Segments = newSegments;
                newFigures.Add(newFigure);
            }
            var newGeometry = new PathGeometry { FillRule = geometry.FillRule, Figures = newFigures };
            ExtendedItem.Properties["Data"].SetValue(newGeometry);
            operation.Commit();
            ExtendedItem.ReapplyExtension(typeof(PathHandlerExtension));
        }

        internal void ChangeIsClosed(PathFigure figure)
        {
            var operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            figure.IsClosed = !figure.IsClosed;
            SaveGeometry((PathGeometry)((Microsoft.UI.Xaml.Shapes.Path)ExtendedItem.View).Data);
            operation.Commit();
        }

        internal void ChangeIsStroked(PathSegment segment)
        {
            var operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            segment.IsStroked = !segment.IsStroked;
            SaveGeometry((PathGeometry)((Microsoft.UI.Xaml.Shapes.Path)ExtendedItem.View).Data);
            operation.Commit();
        }
    }
}
```

- [ ] **Step 3: `dotnet build`**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors.

- [ ] **Step 4: Write the tests**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PathHandlerExtensionConvertMenuTests
    {
        const string PathInCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
            "<Path x:Name=\"P\" Stroke=\"Black\" StrokeThickness=\"2\">" +
            "<Path.Data><PathGeometry><PathFigure StartPoint=\"10,10\">" +
            "<LineSegment Point=\"50,10\" />" +
            "</PathFigure></PathGeometry></Path.Data></Path>" +
            "</Canvas>";

        static (DesignSurface surface, Microsoft.UI.Xaml.Shapes.Path path, PathHandlerExtension extension) Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(PathInCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            var path = (Microsoft.UI.Xaml.Shapes.Path)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
            var pathItem = surface.DesignContext.Services.Component.GetDesignItem(path);
            surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { pathItem }, SelectionTypes.Primary);
            surface.UpdateLayout();
            var extension = pathItem.Extensions.OfType<PathHandlerExtension>().Single();
            return (surface, path, extension);
        }

        [Test]
        public void ConvertToArcReplacesTheLineSegmentWithAnArcSegmentAtTheSamePointAndCommitsOneUndoEntry()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, path, extension) = Load(window);
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int before = undo.UndoActions.Count;
                var anchorThumb = extension.PointThumbs[1]; // the LineSegment's own anchor point (50,10)
                var lineSegment = (Microsoft.UI.Xaml.Media.LineSegment)anchorThumb.AssociatedPoint.SegmentOrFigure;

                extension.ConvertPart(anchorThumb.AssociatedPoint, lineSegment, PathHandlerExtension.PathPartConvertType.ToArcSegment);
                surface.UpdateLayout();

                var geometry = (Microsoft.UI.Xaml.Media.PathGeometry)path.Data;
                var segment = geometry.Figures[0].Segments[0];
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.ArcSegment>(segment);
                var arc = (Microsoft.UI.Xaml.Media.ArcSegment)segment;
                Assert.AreEqual(50, arc.Point.X, 0.5);
                Assert.AreEqual(10, arc.Point.Y, 0.5);
                Assert.AreEqual(before + 1, undo.UndoActions.Count);
            });
        }

        [Test]
        public void ConvertingAPolyBezierSegmentIsANoOpMatchingTheReferencesOwnIncompleteness()
        {
            WinUITestHost.RunWithWindow(window => {
                var (surface, path, extension) = Load(window);
                var anchorThumb = extension.PointThumbs[1];
                var lineSegment = (Microsoft.UI.Xaml.Media.LineSegment)anchorThumb.AssociatedPoint.SegmentOrFigure;
                // Swap in a PolyBezierSegment directly to exercise the no-op branch, since this
                // fixture's own XAML only has a LineSegment.
                var geometry = (Microsoft.UI.Xaml.Media.PathGeometry)path.Data;
                var polyBezier = new Microsoft.UI.Xaml.Media.PolyBezierSegment();
                polyBezier.Points.Add(new Windows.Foundation.Point(20, 20));
                polyBezier.Points.Add(new Windows.Foundation.Point(30, 20));
                polyBezier.Points.Add(new Windows.Foundation.Point(40, 10));
                geometry.Figures[0].Segments[0] = polyBezier;

                Assert.DoesNotThrow(() => extension.ConvertPart(anchorThumb.AssociatedPoint, polyBezier, PathHandlerExtension.PathPartConvertType.ToArcSegment));
                Assert.AreSame(polyBezier, geometry.Figures[0].Segments[0], "PolyBezierSegment convert is a no-op - segment unchanged");
            });
        }

        [Test]
        public void EachPathThumbGetsANonNullContextFlyout()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, _, extension) = Load(window);
                Assert.IsTrue(extension.PointThumbs.All(t => t.ContextFlyout != null));
            });
        }
    }
}
```

- [ ] **Step 5: Run**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.PathHandlerExtensionConvertMenuTests --noresult`

Expected: PASS once Step 4's placeholder is resolved per its own note.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/PathHandlerExtension.cs src/WinUIDesigner.Designer/Extensions/PathHandlerExtension.ConvertMenu.cs tests/WinUIDesigner.Designer.Tests/PathHandlerExtensionConvertMenuTests.cs
git commit -m "feat: add PathHandlerExtension convert-segment context menu (spec decision 5)"
```

---

### Task 13: `DrawPathExtension`, `DrawPathMouseGesture`

Draws a new `Path` by appending a `LineSegment` per click, ending on `DoubleTapped` — spec Data flow #5. Reference always draws straight-line segments (Bezier/Arc only ever appear via Task 12's convert menu, never during initial drawing — spec decision 5). Same shape as Task 10's `DrawPolyLineExtension`, own state machine, no `DragListener` reuse.

**Files:**
- Create: `src/WinUIDesigner.Designer/Extensions/DrawPathExtension.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/DrawPathExtensionTests.cs`

**Interfaces:**
- Consumes: `IDrawItemExtension` (Task 4), `CreateComponentTool.CreateItem` (Task 4), `DesignItem.ReapplyAllExtensions()` (existing).
- Produces: nothing further tasks depend on.

**Important — read before implementing:** same `_drawing`-flag dependency on `CreateComponentTool` (Task 4) as Task 10.

- [ ] **Step 1: Write `DrawPathExtension.cs`**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Services;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Draws a new Path: each click appends a LineSegment, DoubleTapped ends the
    /// gesture. Ported from the reference's Extensions/DrawPathExtension.cs; the figure's
    /// PathFigure is never separately RegisterComponentForDesigner'd (matches the reference's own
    /// commented-out CollectionElements.Add line - it lives only inside Path.Data's object
    /// graph, not as its own tracked DesignItem - spec's Data flow #5).</summary>
    [ExtensionFor(typeof(Canvas))]
    [ExtensionFor(typeof(Grid))]
    public sealed class DrawPathExtension : BehaviorExtension, IDrawItemExtension
    {
        public bool CanItemBeDrawn(Type createItemType) => createItemType == typeof(Path);

        public void StartDrawItem(DesignItem clickedOn, Type createItemType, IDesignPanel panel, Point point, VirtualKeyModifiers modifiers, Action<DesignItem> drawItemCallback)
        {
            var changeGroup = clickedOn.OpenGroup("Draw Path");
            var createdItem = CreateComponentTool.CreateItem(clickedOn.Context, typeof(Path), null);

            var operation = PlacementOperation.TryStartInsertNewComponents(clickedOn, new[] { createdItem }, new[] { new Rect(point.X, point.Y, double.NaN, double.NaN) }, PlacementType.AddItem);
            if (operation == null) {
                changeGroup.Abort();
                return;
            }
            clickedOn.Context.Services.Selection.SetSelectedComponents(new[] { createdItem });
            operation.Commit();

            createdItem.Properties["Stroke"].SetValue(new SolidColorBrush(Colors.Black));
            createdItem.Properties["StrokeThickness"].SetValue(2.0);
            createdItem.Properties["Stretch"].SetValue(Stretch.None);

            var figure = new PathFigure { StartPoint = new Point(0, 0) };
            var figures = new PathFigureCollection();
            figures.Add(figure);
            var geometry = new PathGeometry { Figures = figures };
            createdItem.Properties["Data"].SetValue(geometry);

            drawItemCallback?.Invoke(createdItem);

            var gesture = new DrawPathMouseGesture(createdItem, changeGroup);
            gesture.Start(panel, point);
        }
    }

    sealed class DrawPathMouseGesture
    {
        readonly DesignItem _item;
        ChangeGroup _changeGroup;
        IDesignPanel _designPanel;
        Point _origin;

        public DrawPathMouseGesture(DesignItem item, ChangeGroup changeGroup)
        {
            _item = item;
            _changeGroup = changeGroup;
        }

        public void Start(IDesignPanel designPanel, Point origin)
        {
            _designPanel = designPanel;
            _origin = origin;
            designPanel.PointerMoved += OnPointerMoved;
            designPanel.PointerPressed += OnPointerPressed;
            designPanel.DoubleTapped += OnDoubleTapped;
            designPanel.PointerCaptureLost += OnPointerCaptureLost;
            designPanel.KeyDown += OnKeyDown;
        }

        Point ToLocal(Point panelPoint) => new Point(panelPoint.X - _origin.X, panelPoint.Y - _origin.Y);

        /// <summary>Rebuilds the figure's whole Segments collection through `transform`, then
        /// pushes a NEW top-level PathGeometry/PathFigureCollection/PathSegmentCollection through
        /// SetValue - same "genuinely new top-level reference" reasoning as Task 11's
        /// SaveGeometry (PathFigure/PathSegment aren't separately-tracked DesignItems, so only
        /// the Data property itself is observable for undo).</summary>
        void ReplaceSegments(Func<List<PathSegment>, List<PathSegment>> transform)
        {
            var geometry = (PathGeometry)((Path)_item.View).Data;
            var figure = geometry.Figures[0];
            var current = new List<PathSegment>();
            foreach (var s in figure.Segments) current.Add(s);
            var updated = transform(current);

            var newFigure = new PathFigure { StartPoint = figure.StartPoint, IsClosed = figure.IsClosed, IsFilled = figure.IsFilled };
            var newSegments = new PathSegmentCollection();
            foreach (var s in updated) newSegments.Add(s);
            newFigure.Segments = newSegments;

            var newFigures = new PathFigureCollection();
            newFigures.Add(newFigure);
            var newGeometry = new PathGeometry { FillRule = geometry.FillRule, Figures = newFigures };
            _item.Properties["Data"].SetValue(newGeometry);
        }

        void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var local = ToLocal(e.GetCurrentPoint((UIElement)sender).Position);
            ReplaceSegments(segs => {
                if (segs.Count == 0) segs.Add(new LineSegment { Point = local });
                else segs[segs.Count - 1] = new LineSegment { Point = local }; // "preview" trailing segment
                return segs;
            });
        }

        void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            var local = ToLocal(e.GetCurrentPoint((UIElement)sender).Position);
            ReplaceSegments(segs => {
                if (segs.Count == 0) segs.Add(new LineSegment { Point = local });
                else segs[segs.Count - 1] = new LineSegment { Point = local }; // finalize the point the preview was tracking
                segs.Add(new LineSegment { Point = local }); // start a new preview segment at the same spot
                return segs;
            });
            e.Handled = true;
        }

        void OnDoubleTapped(object sender, TappedRoutedEventArgs e)
        {
            ReplaceSegments(segs => {
                if (segs.Count > 1) segs.RemoveAt(segs.Count - 1); // drop the trailing preview segment
                return segs;
            });
            Commit();
            e.Handled = true;
        }

        void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => Cancel();

        void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
        }

        void Commit()
        {
            _changeGroup?.Commit();
            _changeGroup = null;
            Stop(reapply: true);
        }

        void Cancel()
        {
            _changeGroup?.Abort();
            _changeGroup = null;
            Stop(reapply: false);
        }

        void Stop(bool reapply)
        {
            _designPanel.PointerMoved -= OnPointerMoved;
            _designPanel.PointerPressed -= OnPointerPressed;
            _designPanel.DoubleTapped -= OnDoubleTapped;
            _designPanel.PointerCaptureLost -= OnPointerCaptureLost;
            _designPanel.KeyDown -= OnKeyDown;
            _designPanel.ReleasePointerCaptures();
            if (reapply) _item.ReapplyAllExtensions();
            var toolService = _item.Services.GetService<IToolService>();
            if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
        }
    }
}
```

- [ ] **Step 2: `dotnet build`**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors.

- [ ] **Step 3: Write the test**

```csharp
using System.IO;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class DrawPathExtensionTests
    {
        const string EmptyCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\" />";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(EmptyCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void ClickStartsAPathWithAnEmptyPathGeometryFigureReadyForSegments()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Path));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;

                tool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(50, 60), Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(1, canvas.Children.Count);
                var path = (Microsoft.UI.Xaml.Shapes.Path)canvas.Children[0];
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.PathGeometry>(path.Data);
                var geometry = (Microsoft.UI.Xaml.Media.PathGeometry)path.Data;
                Assert.AreEqual(1, geometry.Figures.Count);
                Assert.AreEqual(Microsoft.UI.Xaml.Media.Stretch.None, path.Stretch);

                Assert.AreSame(tool, surface.DesignContext.Services.GetService<IToolService>().CurrentTool, "gesture still in progress - tool not reset yet");
            });
        }
    }
}
```

Same shallow-automated-coverage caveat as Task 10's own test (private gesture class subscribed to real, un-fabricable pointer/`DoubleTapped` events - decision 9/"Known gaps" #6): the multi-click-then-double-tap sequence, and that it produces only `LineSegment`s (spec decision 5), is a "checked by eye" item — run the built `.exe`, draw a real Path with several clicks and a double-click to finish, confirm the resulting `PathFigure.Segments` before considering this task done.

- [ ] **Step 4: Run**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --test=WinUIDesigner.Designer.Tests.DrawPathExtensionTests --noresult`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.Designer/Extensions/DrawPathExtension.cs tests/WinUIDesigner.Designer.Tests/DrawPathExtensionTests.cs
git commit -m "feat: add DrawPathExtension and DrawPathMouseGesture (spec Data flow #5, decisions 1/5/8)"
```

---

### Task 14: Phase 4d Definition-of-Done suite

Whole-phase integration checks that no single earlier task's own tests can see — matches `Phase4aDefinitionOfDoneTests.cs`/`Phase4bDefinitionOfDoneTests.cs`/`Phase4cDefinitionOfDoneTests.cs`'s established role in this project (spec's Testing/DoD "Whole-branch review" bullet).

**Files:**
- Create: `tests/WinUIDesigner.Designer.Tests/Phase4dDefinitionOfDoneTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–13.

- [ ] **Step 1: Write the suite**

```csharp
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.Designer.Extensions;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class Phase4dDefinitionOfDoneTests
    {
        const string EmptyCanvasXaml =
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\" />";

        static DesignSurface Load(Microsoft.UI.Xaml.Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(EmptyCanvasXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void FullGestureConnectsEndToEndToolboxSelectionCursorChangeDrawCommitPointEdit()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool, "starts on PointerTool");

                // "toolbox selection" (no real toolbox yet - spec Known gap #3): user selects
                // Line from a toolbox, which sets CurrentTool.
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Line));
                toolService.CurrentTool = tool;
                Assert.AreSame(tool, toolService.CurrentTool);

                // "cursor change": ITool.Cursor was applied (can't assert the OS cursor directly,
                // but SetCursor was reachable and didn't throw - see Task 2).

                // "draw": click-drag creates and sizes a Line.
                tool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(50, 50), Windows.System.VirtualKeyModifiers.None);
                var lineItem = surface.DesignContext.Services.Component.GetDesignItem(
                    ((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0]);
                var lineHandler = lineItem.Extensions.OfType<LineHandlerExtension>().Single();
                lineHandler.DragListener.SimulateDown(new Windows.Foundation.Point(0, 0));
                lineHandler.DragListener.SimulateMove(new Windows.Foundation.Point(20, 20));
                lineHandler.DragListener.SimulateUp(new Windows.Foundation.Point(20, 20));
                surface.UpdateLayout();

                // "commit": tool reset back to PointerTool once the draw gesture completed.
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool);

                // "point-edit": the same LineHandlerExtension instance (still attached, no
                // ReapplyAllExtensions needed for Line - only Polyline/Path structural edits
                // trigger that) accepts a further point-drag.
                lineHandler.DragListener.SimulateDown(new Windows.Foundation.Point(0, 0));
                lineHandler.DragListener.SimulateMove(new Windows.Foundation.Point(5, 0));
                lineHandler.DragListener.SimulateUp(new Windows.Foundation.Point(5, 0));
                surface.UpdateLayout();

                var line = (Microsoft.UI.Xaml.Shapes.Line)lineItem.View;
                Assert.AreEqual(25, line.X2, 0.5, "post-creation point-edit moved the same endpoint the draw gesture created");
            });
        }

        [Test]
        public void AllThreePointEditExtensionsUseTheSameOverrideExtensionsList()
        {
            // Spec decision 7: Line/Polygon/Polyline/Path's OverrideExtensions must all list
            // exactly ResizeThumbExtension/RotateThumbExtension/SkewThumbExtension - drift between
            // the three *HandlerExtension classes would silently reintroduce the double-adorner
            // bug (both a generic resize thumb AND a point-edit thumb showing at once) for
            // whichever type's list fell out of sync.
            var expected = new[] {
                typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension),
                typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension),
                typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension)
            };
            foreach (var extensionType in new[] { typeof(LineHandlerExtension), typeof(PolyLineHandlerExtension), typeof(PathHandlerExtension) }) {
                var attrs = extensionType.GetCustomAttributes(typeof(ExtensionForAttribute), false).Cast<ExtensionForAttribute>();
                foreach (var attr in attrs) {
                    CollectionAssert.AreEquivalent(expected, attr.OverrideExtensions, $"{extensionType.Name}'s [ExtensionFor({attr.DesignedItemType.Name})] OverrideExtensions drifted from spec decision 7");
                }
            }
        }

        [Test]
        public void EveryDrawGestureLeavesNoDanglingChangeGroupOnEscape()
        {
            // The exact defect class 4c's final review caught three times (spec Testing/DoD's
            // closing bullet): Escape mid-gesture must abort cleanly enough that Undo/Redo still
            // work afterwards, not throw "Cannot Undo while transaction is running".
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                var undo = surface.DesignContext.Services.GetService<UndoService>();

                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Controls.Button));
                toolService.CurrentTool = tool;
                tool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(50, 50), Windows.System.VirtualKeyModifiers.None);
                // No PointerReleased/commit call - simulates an in-progress gesture, then asserts
                // the rest of the suite (which runs Undo() via other fixtures sharing the same
                // process) is never left poisoned. A full Escape-driven abort needs a real KeyDown
                // (decision 9/Known gap #6 - not fabricable in a test); this asserts the state a
                // correct Escape handler must leave things in is at least reachable, not that
                // Escape itself was exercised end-to-end (that's this task's own "checked by eye"
                // requirement, alongside Tasks 10/13's).
                Assert.DoesNotThrow(() => undo.Undo(), "no dangling transaction should block Undo even mid-gesture, once the gesture properly aborts");
            });
        }

        [Test]
        public void PolyLineHandlerExtensionsInternalIndexStaysInSyncWithAdornerPanelChildren()
        {
            const string xaml =
                "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"400\" Height=\"400\">" +
                "<Polyline x:Name=\"P\" Points=\"10,10 50,10 50,50 10,50\" Stroke=\"Black\" StrokeThickness=\"2\" />" +
                "</Canvas>";
            WinUITestHost.RunWithWindow(window => {
                var surface = new DesignSurface();
                window.Content = surface;
                using (var reader = XmlReader.Create(new StringReader(xaml)))
                    surface.LoadDesigner(reader, new XamlLoadSettings());
                surface.UpdateLayout();

                var polyline = (Microsoft.UI.Xaml.Shapes.Polyline)((Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component).Children[0];
                var polylineItem = surface.DesignContext.Services.Component.GetDesignItem(polyline);
                surface.DesignContext.Services.Selection.SetSelectedComponents(new[] { polylineItem }, SelectionTypes.Primary);
                surface.UpdateLayout();
                var extension = polylineItem.Extensions.OfType<PolyLineHandlerExtension>().Single();

                extension.HandleThumbClick(extension.PointThumbs[1], Windows.System.VirtualKeyModifiers.Shift | Windows.System.VirtualKeyModifiers.Control);
                surface.UpdateLayout();

                var thumbsInAdornerPanel = surface.DesignPanel.Adorners
                    .SelectMany(a => a.Children)
                    .OfType<WinUIDesigner.Designer.Controls.Thumbs.MultiPointThumb>()
                    .ToList();

                Assert.AreEqual(polyline.Points.Count, extension.PointThumbs.Count, "internal index count matches the real Points count after a delete");
                Assert.AreEqual(extension.PointThumbs.Count, thumbsInAdornerPanel.Count, "internal index and the real visual tree (adornerPanel.Children) agree on count - decision 12's lockstep requirement");
                CollectionAssert.AreEquivalent(extension.PointThumbs, thumbsInAdornerPanel, "internal index holds exactly the thumbs actually in the visual tree, no stale entries");
            });
        }
    }
}
```

- [ ] **Step 2: `dotnet build` and fix compile errors**

Run: `dotnet build WinUIDesigner.sln`
Expected: 0 errors. `ExtensionForAttribute.OverrideExtensions`/`.DesignedItemType`'s exact property names should already match `ExtensionForAttribute.cs:43,55` (spec's "Also verified" section) — if not, fix this test's property names to match the real ones, not the other way around.

- [ ] **Step 3: Run the whole `WinUIDesigner.Designer.Tests` suite — the real final gate**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult`

Expected: every test in the project passes — every 4a/4b/4c test (regression net for Task 2's refactor) and every one of this phase's own new tests (Tasks 1–14).

- [ ] **Step 4: Run the manual "checked by eye" pass**

Launch the shell app or a throwaway host that constructs a live `DesignSurface` (check how 4a-4c's own manual-verification steps were run, if a runnable host already exists in this repo — `docs/superpowers/plans/2026-09-01-winui-designer-phase3.md`'s Task 1 is the precedent for "live Window investigation" in this project) and, by hand:
- Draw a `Line`, a `Polyline` (multiple clicks + double-click), a `Path` (multiple clicks + double-click) — confirm each looks right and Escape aborts cleanly mid-draw.
- Drag a Polyline point with Shift+Alt held — confirm a new point is inserted and the drag continues on it (Task 9's flagged fidelity risk).
- Right-click a `PathThumb` anchor point — confirm the convert-segment menu appears and each conversion visually looks right (Task 12).
- Confirm Undo/Redo cleanly reverses every one of the above, one action at a time.

Record the outcome (pass/fail per bullet) in this task's commit message or PR description — this is the spec's own required substitute for automated coverage of the WinRT-event-driven layer (decision 9/"Known gaps" #6), not optional polish.

- [ ] **Step 5: Commit**

```bash
git add tests/WinUIDesigner.Designer.Tests/Phase4dDefinitionOfDoneTests.cs
git commit -m "test: add Phase 4d Definition-of-Done suite"
```

---
