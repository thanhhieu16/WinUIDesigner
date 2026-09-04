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
