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
    // (X1/Y1/X2/Y2, Points[i]) directly. TransformToVisual only reports an element's own layout
    // position - it cannot see Stretch's internal geometry scaling (empirically confirmed: the
    // same element's TransformToVisual result is identical regardless of its Stretch value), so
    // these tests use VisualTreeHelper.FindElementsInHostCoordinates instead - real hit-testing
    // against the actual rendered/composited geometry, already proven working elsewhere in this
    // codebase (LiveLayoutTests.cs, DesignPanel.HitTest).
    //
    // Ruling (this task's deliverable - Task 9 should cite this comment rather than re-deriving
    // it): PointTrackerPlacementSupport uses a shape's raw logical point directly, with no
    // transform. This is correct for Stretch.None with no explicit Width/Height - the only
    // configuration this phase's own Draw*Extension tools ever produce (spec decision 5) - and is
    // an explicitly accepted, documented limitation for Stretch != None shapes loaded from
    // hand-authored or external XAML: their point-thumbs may not sit exactly on the rendered
    // pixel. This is a scope decision, not a bug to fix in this plan - replicating WPF/WinUI's
    // internal Stretch geometry-scaling algorithm from scratch is disproportionate net-new scope
    // nothing in this phase's stated goals requires, and no crash or data corruption results
    // (only a cosmetic thumb-placement offset on an edge-case shape configuration).
    [TestFixture]
    public class RenderedGeometryInvestigationTests
    {
        [Test]
        public void StretchNoneLineRendersAtItsRawX1Y1X2Y2WithNoAdditionalTransform()
        {
            WinUITestHost.RunWithWindow(window => {
                var canvas = new Canvas { Width = 300, Height = 300 };
                // No explicit Width/Height on the Line itself - matches every shape this phase's
                // own Draw*Extension tools ever create (spec decision 5).
                var line = new Line { X1 = 50, Y1 = 150, X2 = 250, Y2 = 150, Stretch = Stretch.None, Stroke = new SolidColorBrush(Microsoft.UI.Colors.Black), StrokeThickness = 8 };
                canvas.Children.Add(line);
                window.Content = canvas;
                window.Activate();
                canvas.UpdateLayout();

                // Real hit-test (not TransformToVisual, which is Stretch-blind) at the line's own
                // raw midpoint, taken with zero correction: canvas-relative ((X1+X2)/2,(Y1+Y2)/2).
                // If this hits the line, the raw point is a valid substitute for its actual
                // rendered position - this would fail if WinUI applied any internal translation to
                // normalize the geometry's bounding box, which the weaker TransformToVisual check
                // this test previously used could not have detected either way.
                var rawMidpoint = new Point((line.X1 + line.X2) / 2, (line.Y1 + line.Y2) / 2);
                var hostPoint = canvas.TransformToVisual(null).TransformPoint(rawMidpoint);
                var hits = VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, canvas);

                CollectionAssert.Contains(hits, line,
                    "raw X1/Y1/X2/Y2 midpoint should hit the actual rendered line under Stretch.None with no explicit Width/Height");
            });
        }

        [Test]
        public void StretchFillPolylineRendersAwayFromItsRawPointValues()
        {
            WinUITestHost.RunWithWindow(window => {
                var canvas = new Canvas { Width = 300, Height = 300 };
                // Natural span of these points is (0,0)-(50,50). Width/Height below deliberately
                // don't match that span, and Stretch.Fill forces WinUI to scale the rendered
                // geometry to fill the given box - exactly the WPF RenderedGeometry-transform
                // behavior the spec worried about not being able to query directly.
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
                    StrokeThickness = 8
                };
                canvas.Children.Add(polyline);
                window.Content = canvas;
                window.Activate();
                canvas.UpdateLayout();

                // The raw second point (50,0) sits at 100% of the natural 0-50 X-span and 0% of
                // the natural 0-50 Y-span. Taken with zero correction relative to the polyline's
                // own canvas-relative origin (Canvas does not auto-position children, so that
                // origin is (0,0)), that would place the raw point at canvas (50,0). Under
                // Stretch.Fill scaling that same corner into the 200x200 box, it actually renders
                // at canvas (200,0) instead - a point no raw X/Y value in this polyline (all <=50)
                // could ever reach unscaled. A hit there can only be explained by Stretch's scale
                // transform, proving the raw-point substitute is NOT accurate for Stretch != None.
                var stretchedCorner = new Point(200, 0);
                var hostPoint = canvas.TransformToVisual(null).TransformPoint(stretchedCorner);
                var hits = VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, canvas);

                CollectionAssert.Contains(hits, polyline,
                    "Stretch.Fill should have rescaled the polyline's rendered corner well past any of its own raw point values");
            });
        }
    }
}
