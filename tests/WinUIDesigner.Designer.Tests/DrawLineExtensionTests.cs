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
            // Brief-defect correction (same one CreateComponentToolTests.cs/MoveTests.cs/
            // HitTestTests.cs/DefinitionOfDoneTests.cs already document/work around): a Panel/
            // Canvas with Background == null isn't hit-test visible over its own empty interior
            // (WPF/UWP/WinUI convention), and this loader's XAML parsing does not populate
            // Background from a plain Background="Transparent" attribute on the live view - so
            // without this, DesignPanel.HitTest never finds the Canvas at all and
            // CreateComponentTool.HandlePointerPressed's HitTest would spuriously read as "hit
            // nothing", never reaching DrawLineExtension.StartDrawItem at all. Set directly on
            // the live view (not through a DesignItemProperty), matching the established pattern.
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            canvas.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
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
                int undoCountBefore = undo.UndoActions.Count();

                var canvasForPoint = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                // Brief-defect correction (same one CreateComponentToolTests.cs's own
                // ClickWithoutDragAddsADefaultSizedButtonAtTheClickPoint documents): the Canvas
                // has an explicit Width/Height, so DesignPanel.ArrangeOverride's
                // Stretch-alignment arrange centers it within the shared test window's (much
                // larger) DesignPanel instead of placing it flush at panel-local (0,0) - a
                // hardcoded panel-local point like (50, 60) can land outside the Canvas entirely.
                // Deriving the point from the Canvas's own live TransformToVisual(DesignPanel)
                // is robust to wherever the Canvas actually got arranged.
                var point = canvasForPoint.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(50, 60));

                tool.HandlePointerPressed(surface.DesignPanel, point, Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                // Test-only seam (DrawLineExtension.Gesture, mirroring CreateComponentTool.Gesture):
                // the outer "Draw Line" changeGroup StartDrawItem opened only closes on a real
                // IDesignPanel.PointerReleased, which needs a PointerRoutedEventArgs a test can't
                // fabricate (no public constructor) - so the gesture's own internal
                // HandlePointerReleased is driven directly through this property instead, once the
                // drag below is done.
                var drawLineExtension = surface.DesignContext.RootItem.Extensions.OfType<DrawLineExtension>().Single();

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
                drawLineExtension.Gesture.HandlePointerReleased(new Windows.Foundation.Point(30, 20));
                surface.UpdateLayout();

                Assert.AreEqual(30, line.X2, 0.5);
                Assert.AreEqual(20, line.Y2, 0.5);
                Assert.AreEqual(undoCountBefore + 1, undo.UndoActions.Count(), "create + drag-to-size is ONE undo entry, not two");
            });
        }

        [Test]
        public void CancellingAnInProgressLineDrawLeavesNothingBehindAndResetsTheTool()
        {
            // Final-review coverage gap (Fix 3): none of the three Draw*Extension gestures had an
            // automated test proving the Escape / PointerCaptureLost abort path actually unwinds
            // cleanly. Both of those production paths funnel into DrawLineMouseGesture.Cancel()
            // (OnKeyDown checks VirtualKey.Escape, OnPointerCaptureLost calls it directly), which is
            // the seam driven here - neither KeyRoutedEventArgs nor PointerRoutedEventArgs has a
            // public constructor, so the real events cannot be raised from a test (Global
            // Constraints). Same seam-through-Cancel() shape CreateComponentToolTests'
            // EscapeDuringPointerPressAbortsWithoutAddingAnything already uses.
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;

                int childrenBefore = canvas.Children.Count;
                bool canUndoBefore = undo.CanUndo;
                int undoCountBefore = undo.UndoActions.Count();

                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Line));
                toolService.CurrentTool = tool;
                var point = canvas.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(50, 60));

                tool.HandlePointerPressed(surface.DesignPanel, point, Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();
                Assert.AreEqual(childrenBefore + 1, canvas.Children.Count, "sanity check: the gesture really did create something for the cancel below to have to take back");

                var drawLineExtension = surface.DesignContext.RootItem.Extensions.OfType<DrawLineExtension>().Single();
                var lineItem = surface.DesignContext.Services.Component.GetDesignItem(canvas.Children[childrenBefore]);
                var lineHandler = lineItem.Extensions.OfType<LineHandlerExtension>().Single();

                // Drag past DragListener's threshold first, so the INNER Resize transaction
                // (LineHandlerExtension.SetOperation) is genuinely open at the moment of the cancel -
                // otherwise this would only exercise the trivial "nothing was ever opened" path, and
                // Cancel()'s whole reason for closing inner-before-outer would go untested.
                lineHandler.DragListener.ExternalMove(new Windows.Foundation.Point(point.X + 30, point.Y + 20), Windows.System.VirtualKeyModifiers.None);

                drawLineExtension.Gesture.Cancel();
                surface.UpdateLayout();

                Assert.AreEqual(childrenBefore, canvas.Children.Count, "a cancelled draw must leave nothing behind in the container");
                Assert.AreEqual(canUndoBefore, undo.CanUndo, "a cancelled draw must not change whether there is anything to undo");
                Assert.AreEqual(undoCountBefore, undo.UndoActions.Count(), "a cancelled draw must add no undo entry - neither a phantom commit nor a dangling one");
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool, "a cancelled draw must reset the active tool back to PointerTool");
            });
        }
    }
}
