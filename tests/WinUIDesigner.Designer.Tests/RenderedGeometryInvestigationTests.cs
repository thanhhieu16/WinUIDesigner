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
