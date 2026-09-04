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
            // Same brief-defect correction DrawLineExtensionTests.cs/DrawPolyLineExtensionTests.cs
            // document: a Canvas with Background == null isn't hit-test visible over its own empty
            // interior, and the XAML loader doesn't populate Background from a plain attribute - so
            // without this, CreateComponentTool.HandlePointerPressed's HitTest would spuriously read
            // as "hit nothing" and never reach DrawPathExtension.StartDrawItem at all. Not called out
            // as one of this task's two corrections (those were scoped to Step 1's OnDoubleTapped
            // signature), but needed here for the same reason it was needed in every earlier
            // Canvas/Grid-drawing test in this phase - confirmed necessary by first running this test
            // WITHOUT it and observing the single-click assertions fail (canvas.Children.Count == 0).
            var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
            canvas.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            surface.UpdateLayout();
            return surface;
        }

        /// <summary>Re-reads the LineSegments from the Path's live Data on demand - ReplaceSegments
        /// (DrawPathExtension.cs) always publishes a brand-new PathGeometry/PathFigureCollection/
        /// PathSegmentCollection through SetValue, so any previously-fetched geometry/figure/segment
        /// reference is stale the instant the next click or move runs. Also asserts every segment is
        /// a LineSegment inline - spec decision 5: DrawPathExtension must never emit Bezier/Arc
        /// segments during initial drawing, only Task 12's convert-menu ever introduces those.</summary>
        static System.Collections.Generic.List<Microsoft.UI.Xaml.Media.LineSegment> Segments(Microsoft.UI.Xaml.Shapes.Path path)
        {
            var geometry = (Microsoft.UI.Xaml.Media.PathGeometry)path.Data;
            Assert.AreEqual(1, geometry.Figures.Count);
            var figure = geometry.Figures[0];
            var list = new System.Collections.Generic.List<Microsoft.UI.Xaml.Media.LineSegment>();
            foreach (var segment in figure.Segments) {
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.LineSegment>(segment, "spec decision 5 - only straight LineSegments during initial drawing, never Bezier/Arc");
                list.Add((Microsoft.UI.Xaml.Media.LineSegment)segment);
            }
            return list;
        }

        [Test]
        public void ClickStartsAPathWithAnEmptyPathGeometryFigureReadyForSegments()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Path));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;

                var canvasForPoint = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                // Same brief-defect correction DrawLineExtensionTests.cs/DrawPolyLineExtensionTests.cs
                // document: derive the click point from the Canvas's own live
                // TransformToVisual(DesignPanel) rather than a hardcoded panel-local point, robust to
                // wherever the Canvas actually got arranged within the shared test window's (larger)
                // DesignPanel.
                Windows.Foundation.Point ToPanel(double x, double y) =>
                    canvasForPoint.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(x, y));

                var origin = ToPanel(50, 60);
                tool.HandlePointerPressed(surface.DesignPanel, origin, Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(1, canvas.Children.Count);
                var path = (Microsoft.UI.Xaml.Shapes.Path)canvas.Children[0];
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Media.PathGeometry>(path.Data);
                var geometry = (Microsoft.UI.Xaml.Media.PathGeometry)path.Data;
                Assert.AreEqual(1, geometry.Figures.Count);
                Assert.AreEqual(new Windows.Foundation.Point(0, 0), geometry.Figures[0].StartPoint);
                // Unlike DrawPolyLineExtension (which pre-seeds Points with one committed vertex in
                // StartDrawItem), DrawPathExtension's StartDrawItem creates the PathFigure with an
                // EMPTY Segments collection - the very first LineSegment only appears once the
                // gesture's own PointerMoved/PointerPressed handlers run (traced by hand from
                // DrawPathExtension.cs's StartDrawItem, which never calls ReplaceSegments itself).
                Assert.AreEqual(0, geometry.Figures[0].Segments.Count, "no segment exists yet - only the gesture's own PointerMoved/PointerPressed ever add one");
                Assert.AreEqual(Microsoft.UI.Xaml.Media.Stretch.None, path.Stretch);
                Assert.AreEqual(2, path.StrokeThickness);

                Assert.AreSame(tool, surface.DesignContext.Services.GetService<IToolService>().CurrentTool, "gesture still in progress - tool not reset yet");
            });
        }

        [Test]
        public void MultiClickSequenceProducesOnlyLineSegmentsAndDoubleTappedEndsTheGesture()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                var tool = new CreateComponentTool(typeof(Microsoft.UI.Xaml.Shapes.Path));
                surface.DesignContext.Services.GetService<IToolService>().CurrentTool = tool;
                var undo = surface.DesignContext.Services.GetService<UndoService>();
                int undoCountBefore = undo.UndoActions.Count();

                var canvasForPoint = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Windows.Foundation.Point ToPanel(double x, double y) =>
                    canvasForPoint.TransformToVisual(surface.DesignPanel).TransformPoint(new Windows.Foundation.Point(x, y));

                // First click: creates the Path (StartDrawItem). Its own local (0,0) - the
                // PathFigure's StartPoint - coincides with this click point via Canvas.Left/Top from
                // the insert Rect, same mechanism DrawLineExtension/DrawPolyLineExtension use.
                var origin = ToPanel(50, 50);
                tool.HandlePointerPressed(surface.DesignPanel, origin, Windows.System.VirtualKeyModifiers.None);
                surface.UpdateLayout();

                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)surface.DesignContext.RootItem.Component;
                Assert.AreEqual(1, canvas.Children.Count, "the created Path must actually be inserted into the container's children");
                var path = (Microsoft.UI.Xaml.Shapes.Path)canvas.Children[0];
                Assert.AreEqual(0, Segments(path).Count, "StartDrawItem never adds a segment itself");

                // Test-only seam (DrawPathExtension.Gesture, mirroring DrawPolyLineExtension.Gesture/
                // DrawLineExtension.Gesture): DrawPathMouseGesture is a private class subscribed
                // directly to real PointerMoved/PointerPressed/DoubleTapped events, none of which a
                // test can fabricate (no public constructors on their EventArgs types - Global
                // Constraints), so the gesture's own internal HandlePointerMoved/HandlePointerPressed/
                // HandleDoubleTapped are driven directly instead.
                var drawExtension = surface.DesignContext.RootItem.Extensions.OfType<DrawPathExtension>().Single();
                var gesture = drawExtension.Gesture;
                Assert.IsNotNull(gesture);

                // Move BEFORE the second click, with no segment yet: ReplaceSegments' `segs.Count ==
                // 0` branch ADDS a new (preview) segment rather than overwriting a nonexistent last
                // element - traced by hand from DrawPathExtension.cs's OnPointerMoved/HandlePointerMoved.
                gesture.HandlePointerMoved(ToPanel(90, 50));
                var segs1 = Segments(path);
                Assert.AreEqual(1, segs1.Count, "a move with zero committed segments must ADD a preview segment");
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), segs1[0].Point);

                // Second click (first real PointerPressed on the gesture): `segs.Count == 1` (not 0)
                // takes the `else` branch - finalizes the trailing preview segment at the click
                // point - then unconditionally appends a NEW preview segment at the same spot. Both
                // segments end up with the same Point (40,0): the first is now committed, the second
                // is the fresh preview.
                gesture.HandlePointerPressed(ToPanel(90, 50));
                var segs2 = Segments(path);
                Assert.AreEqual(2, segs2.Count);
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), segs2[0].Point, "first committed LineSegment");
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), segs2[1].Point, "new trailing preview duplicates the just-committed point");

                // Third click at a new location: finalizes the trailing preview (index 1) to (90,5)
                // and appends yet another new preview at (90,5). The already-committed first segment
                // (index 0) must survive untouched.
                gesture.HandlePointerPressed(ToPanel(140, 55));
                var segs3 = Segments(path);
                Assert.AreEqual(3, segs3.Count);
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), segs3[0].Point, "the first committed segment must still be untouched");
                Assert.AreEqual(new Windows.Foundation.Point(90, 5), segs3[1].Point);
                Assert.AreEqual(new Windows.Foundation.Point(90, 5), segs3[2].Point, "trailing preview duplicates the just-committed point");

                // Fourth click: finalizes the third committed segment (index 2, to (90,100)) and
                // starts a fourth (trailing) preview that DoubleTapped below must drop.
                gesture.HandlePointerPressed(ToPanel(140, 150));
                var segs4 = Segments(path);
                Assert.AreEqual(4, segs4.Count);
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), segs4[0].Point);
                Assert.AreEqual(new Windows.Foundation.Point(90, 5), segs4[1].Point);
                Assert.AreEqual(new Windows.Foundation.Point(90, 100), segs4[2].Point);
                Assert.AreEqual(new Windows.Foundation.Point(90, 100), segs4[3].Point, "trailing preview duplicates the just-committed point");

                // DoubleTapped ends the gesture: drops the trailing preview segment (index 3),
                // commits the ChangeGroup as a single undo entry, and reapplies extensions.
                gesture.HandleDoubleTapped();
                surface.UpdateLayout();

                var finalSegs = Segments(path);
                Assert.AreEqual(3, finalSegs.Count, "the trailing preview segment must be dropped on DoubleTapped");
                Assert.AreEqual(new Windows.Foundation.Point(40, 0), finalSegs[0].Point);
                Assert.AreEqual(new Windows.Foundation.Point(90, 5), finalSegs[1].Point);
                Assert.AreEqual(new Windows.Foundation.Point(90, 100), finalSegs[2].Point);

                var finalGeometry = (Microsoft.UI.Xaml.Media.PathGeometry)path.Data;
                Assert.AreEqual(new Windows.Foundation.Point(0, 0), finalGeometry.Figures[0].StartPoint, "StartPoint must survive every ReplaceSegments rebuild unchanged");

                Assert.AreEqual(undoCountBefore + 1, undo.UndoActions.Count(), "the whole multi-click draw gesture is ONE undo entry, not one per click");

                var pathItem = surface.DesignContext.Services.Component.GetDesignItem(path);
                var handlerExtension = pathItem.Extensions.OfType<PathHandlerExtension>().Single();
                Assert.AreEqual(4, handlerExtension.PointThumbs.Count, "ReapplyAllExtensions must have rebuilt PathHandlerExtension against the FINAL StartPoint + 3 LineSegments, not a stale state from StartDrawItem");

                var toolService = surface.DesignContext.Services.GetService<IToolService>();
                Assert.AreSame(toolService.PointerTool, toolService.CurrentTool, "the gesture must switch the tool back to PointerTool once it ends");
            });
        }
    }
}
