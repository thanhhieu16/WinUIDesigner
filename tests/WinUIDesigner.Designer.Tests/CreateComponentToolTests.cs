using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;
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

            // Brief-defect correction (same one MoveTests.cs documents for its own TargetCanvas
            // fixture): a Panel/Canvas with Background == null isn't hit-test visible over its own
            // empty interior (WPF/UWP/WinUI convention), and this loader's XAML parsing does not
            // populate Background from a plain Background="Transparent" attribute on the live view
            // (confirmed empirically via a throwaway diagnostic test: ModelHit still came back null
            // for a point well inside the Canvas's own measured bounds until this line was added) -
            // so without this, DesignPanel.HitTest never finds the Canvas at all and every
            // click-to-create test below would spuriously read as "hit nothing". Set directly on
            // the live view (not through a DesignItemProperty), matching MoveTests.cs's own
            // established pattern - this is test-fixture plumbing, not model state under test.
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            canvas.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

            surface.UpdateLayout();
            return surface;
        }

        [Test]
        public void ADeclinedClickReportsItWasNotHandledSoOtherPanelSubscribersStillSeeIt()
        {
            // Final-review regression test (Fix 1). OnPointerPressed used to set e.Handled = true
            // unconditionally, one line BEFORE calling HandlePointerPressed - which declines the
            // click outright whenever a multi-click draw gesture is already running (_drawing) or
            // the hit test found nothing. WinUI's routed-event dispatch skips every handler
            // registered without handledEventsToo once e.Handled is true, including handlers on the
            // SAME element registered after this one (Controls/DragListener.cs's constructor works
            // around exactly this with AddHandler(..., handledEventsToo: true)) - so
            // DrawPolylineMouseGesture/DrawPathMouseGesture, which subscribe their own
            // OnPointerPressed to this same IDesignPanel.PointerPressed with a plain +=, never
            // received clicks 2, 3, 4... of their own gesture at all: multi-click drawing could not
            // work against a real routed event.
            //
            // The real routed path is not directly reachable from a test (PointerRoutedEventArgs has
            // no public constructor and WinUI has no UIElement.RaiseEvent - Global Constraints), so
            // what is pinned here is the decision HandlePointerPressed now reports back and
            // OnPointerPressed now gates e.Handled on: declined => false, acted => true.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Polyline));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;

                var canvasForPoint = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                var point = canvasForPoint.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(50, 60));

                // Click 1 starts DrawPolyLineExtension's own multi-click gesture: the tool DID act,
                // so the real click is legitimately marked Handled.
                Assert.IsTrue(tool.HandlePointerPressed(surface.DesignPanel, point, Windows.System.VirtualKeyModifiers.None),
                    "the click that starts a draw gesture is genuinely consumed by this tool");

                // Click 2 (and 3, 4, ...) belong to the draw gesture's OWN PointerPressed handler.
                // This tool must decline them AND report that, so the routed event stays unhandled
                // and reaches that later-registered handler.
                Assert.IsFalse(tool.HandlePointerPressed(surface.DesignPanel, point, Windows.System.VirtualKeyModifiers.None),
                    "a click declined because a draw gesture is already running must NOT be reported as handled");

                // A click that hits nothing is declined for a different reason, and must report the
                // same thing - nothing was consumed, so nothing should be marked Handled.
                var freshTool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Controls.Button));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = freshTool;
                Assert.IsFalse(freshTool.HandlePointerPressed(surface.DesignPanel, new Windows.Foundation.Point(-50, -50), Windows.System.VirtualKeyModifiers.None),
                    "a click that hit-tests to nothing must NOT be reported as handled");
            });
        }

        [Test]
        public void ClickWithoutDragAddsADefaultSizedButtonAtTheClickPoint()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Controls.Button));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;

                var canvasBefore = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                // Brief-defect correction (same one MoveTests.cs/HitTestTests.cs/
                // DefinitionOfDoneTests.cs already document/work around): the Canvas has an
                // explicit Width/Height, so DesignPanel.ArrangeOverride's Stretch-alignment arrange
                // centers it within the shared test window's (much larger) DesignPanel instead of
                // placing it flush at panel-local (0,0) - a hardcoded panel-local point like
                // (50, 60) can land outside the Canvas entirely. Deriving the point from the
                // Canvas's own live TransformToVisual(surface.DesignPanel), the same pattern those
                // other suites use, is robust to wherever the Canvas actually got arranged.
                var point = canvasBefore.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(50, 60));

                tool.HandlePointerPressed(surface.DesignPanel, point, Windows.System.VirtualKeyModifiers.None);
                // HandlePointerPressed only creates the item and starts the click-vs-drag gesture
                // (CreateComponentMouseGesture.Start just subscribes to further pointer events) -
                // the actual container insertion happens on PointerReleased. A real
                // PointerRoutedEventArgs can't be fabricated in a test (no public constructor -
                // Global Constraints), so this drives the gesture's own internal
                // HandlePointerReleased directly through CreateComponentTool.Gesture, the same
                // "Handle*-extracted, internal test seam" pattern this codebase already
                // establishes for DesignPanel.HandlePointerPressed/DragListener.SimulateUp.
                tool.Gesture.HandlePointerReleased();
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

                var canvasForPoint = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                // See ClickWithoutDragAddsADefaultSizedButtonAtTheClickPoint's own comment: the
                // Canvas is centered within the shared window's DesignPanel, not flush at (0,0).
                var point = canvasForPoint.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(50, 60));

                tool.HandlePointerPressed(surface.DesignPanel, point, Windows.System.VirtualKeyModifiers.None);
                // Simulate Escape by driving the same cancel path a real KeyDown would - the
                // gesture's Cancel() is internal but reachable through CreateComponentTool.Gesture
                // (see ClickWithoutDragAddsADefaultSizedButtonAtTheClickPoint's own comment for why
                // a direct internal call is used instead of a real routed KeyDown event).
                tool.Gesture.Cancel();
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(0, canvas.Children.Count, "a cancelled gesture must not add anything");
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool, "a genuine cancel resets the active tool back to PointerTool");
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
                // No dangling ChangeGroup: since HitTest found nothing, HandlePointerPressed must
                // have returned before ever opening one - UndoService.Undo() throws on ANY empty
                // stack (WinUIDesigner.DesignModel/Services/UndoService.cs:255-260, checked before
                // its own "transaction still running" check), so the absence of even a *committed*
                // undo entry is asserted directly via CanUndo instead of calling Undo() itself.
                Assert.IsFalse(surface.DesignContext.Services.GetService<UndoService>()?.CanUndo ?? false);
            });
        }
    }
}
