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
            // Same brief-defect correction DrawLineExtensionTests.cs documents: a Canvas with
            // Background == null isn't hit-test visible over its own empty interior, and the XAML
            // loader doesn't populate Background from a plain attribute - so without this,
            // CreateComponentTool.HandlePointerPressed's HitTest would spuriously read as "hit
            // nothing" and never reach DrawPolyLineExtension.StartDrawItem at all.
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            canvas.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
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

                var canvasForPoint = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                // Same brief-defect correction DrawLineExtensionTests.cs documents: derive the
                // click point from the Canvas's own live TransformToVisual(DesignPanel) rather
                // than a hardcoded panel-local point, robust to wherever the Canvas actually got
                // arranged within the shared test window's (larger) DesignPanel.
                Windows.Foundation.Point ToPanel(double x, double y) =>
                    canvasForPoint.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(x, y));

                var origin = ToPanel(50, 50);
                tool.HandlePointerPressed(surface.DesignPanel, origin, Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(1, canvas.Children.Count, "the created Polyline must actually be inserted into the container's children");
                var polyline = (Microsoft.UI.Xaml.Shapes.Polyline)canvas.Children[0];
                Assert.AreEqual(1, polyline.Points.Count, "one preview point right after the first click");
                Assert.AreEqual(new Windows.Foundation.Point(0, 0), polyline.Points[0]);

                Assert.AreSame(tool, surface.DesignContext.Services.GetService<IToolService>().CurrentTool, "gesture still in progress - tool not reset yet");

                // Test-only seam (DrawPolyLineExtension.Gesture, mirroring DrawLineExtension.Gesture):
                // DrawPolylineMouseGesture is a private class subscribed directly to real
                // PointerMoved/PointerPressed/DoubleTapped events, none of which a test can
                // fabricate (no public constructors on their EventArgs types - Global Constraints),
                // so the gesture's own internal Handle* methods are driven directly instead.
                var drawExtension = surface.DesignContext.RootItem.Extensions.OfType<DrawPolyLineExtension>().Single();
                var gesture = drawExtension.Gesture;
                Assert.IsNotNull(gesture);

                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int undoCountBefore = undo.UndoActions.Count();

                // Move BEFORE the second click - proves the trailing preview-point bookkeeping:
                // a move must overwrite the last point in place, never grow the collection.
                gesture.HandlePointerMoved(ToPanel(90, 50), Windows.System.VirtualKeyModifiers.None);
                Assert.AreEqual(1, polyline.Points.Count, "a move only updates the trailing preview point, it must not add one");
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), polyline.Points[0]);

                // Second click: finalizes the point the preview was tracking (local (40,0)) and
                // starts a new preview point at the same spot.
                gesture.HandlePointerPressed(ToPanel(90, 50), Windows.System.VirtualKeyModifiers.None);
                Assert.AreEqual(2, polyline.Points.Count);
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), polyline.Points[0]);
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), polyline.Points[1]);

                // Third click WITH Alt held - proves ApplyAltSnap's axis selection. Current local
                // point (90,5) relative to the last committed point (40,0): dx=50 dominates
                // dy=5, so the horizontal axis is picked and Y snaps to the last committed point's
                // Y (0) - the committed vertex ends up perfectly horizontal from its predecessor.
                gesture.HandlePointerPressed(ToPanel(140, 55), Windows.System.VirtualKeyModifiers.Menu);
                Assert.AreEqual(3, polyline.Points.Count);
                Assert.AreEqual(new Windows.Foundation.Point(90, 0), polyline.Points[1], "Alt-snap: horizontal axis picked, Y snapped to the previous point's Y");
                Assert.AreEqual(new Windows.Foundation.Point(90, 0), polyline.Points[2], "trailing preview duplicates the just-committed point");

                // Fourth click, no Alt: finalizes the third committed vertex and starts a fourth
                // (trailing) preview point that DoubleTapped below must drop.
                gesture.HandlePointerPressed(ToPanel(140, 150), Windows.System.VirtualKeyModifiers.None);
                Assert.AreEqual(4, polyline.Points.Count);
                Assert.AreEqual(new Windows.Foundation.Point(90, 100), polyline.Points[2]);

                // DoubleTapped ends the gesture: drops the trailing preview point, commits the
                // ChangeGroup as a single undo entry, and reapplies extensions.
                gesture.HandleDoubleTapped();
                surface.UpdateLayout();

                Assert.AreEqual(3, polyline.Points.Count, "the trailing preview point must be dropped on DoubleTapped");
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), polyline.Points[0]);
                Assert.AreEqual(new Windows.Foundation.Point(90, 0), polyline.Points[1]);
                Assert.AreEqual(new Windows.Foundation.Point(90, 100), polyline.Points[2]);

                Assert.AreEqual(undoCountBefore + 1, undo.UndoActions.Count(), "the whole multi-click draw gesture is ONE undo entry, not one per click");

                var polylineItem = surface.DesignContext.Services.Component.GetDesignItem(polyline);
                var handlerExtension = polylineItem.Extensions.OfType<PolyLineHandlerExtension>().Single();
                Assert.AreEqual(3, handlerExtension.PointThumbs.Count, "ReapplyAllExtensions must have rebuilt PolyLineHandlerExtension against the FINAL 3-point collection, not the stale 1-point one from StartDrawItem");
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, handlerExtension.PointThumbs.Select(t => t.Index));

                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool, "the gesture must switch the tool back to PointerTool once it ends");
            });
        }
    }
}
