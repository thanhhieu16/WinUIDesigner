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

        /// <summary>Final-review fix (defense in depth): Index is bounds-checked against the LIVE
        /// Points collection before it is used, and an out-of-range Index arranges an empty rect
        /// instead of a thumb. AdornerPanel.ArrangeOverride calls this for every child
        /// unconditionally, so a thumb left holding a stale Index after Points shrank externally
        /// (an Undo of a Shift+Alt point-insert, say) reaches this method.
        ///
        /// EMPIRICAL CORRECTION to the final review's own stated premise, verified here rather than
        /// assumed: an out-of-range index does NOT throw ArgumentOutOfRangeException on this SDK's
        /// Microsoft.UI.Xaml.Media.PointCollection - `polyline.Points[99]` on a 3-point collection
        /// returns Point(0,0) with no exception at all (probed directly on a live WinUI object; the
        /// projected IVector&lt;Point&gt; indexer does not surface E_BOUNDS the way WPF's
        /// PointCollection would). So the real symptom of a stale thumb was never a layout crash -
        /// it was a silently WRONG thumb: a full-size 7x7 handle parked on the shape's own local
        /// origin, indistinguishable from a real point handle sitting at (0,0). Arranging 0x0
        /// instead makes the degenerate case invisible rather than misleading, and keeps this method
        /// correct if a future collection type ever does throw. Arranging 0x0 (rather than skipping
        /// Arrange entirely) also keeps every child arranged exactly once per pass, which is what a
        /// Panel's ArrangeOverride contract expects. The staleness itself is fixed at the source by
        /// PolyLineHandlerExtension/PathHandlerExtension rebuilding their thumbs on any external
        /// Points/Data change.</summary>
        public override void Arrange(AdornerPanel panel, UIElement adorner, Size adornedElementSize)
        {
            Point p = default(Point);
            if (_shape is Line line) {
                p = _alignment == PlacementAlignment.BottomRight ? new Point(line.X2, line.Y2) : new Point(line.X1, line.Y1);
            } else if (_shape is Polygon polygon) {
                if (Index < 0 || Index >= polygon.Points.Count) { adorner.Arrange(new Rect(0, 0, 0, 0)); return; }
                p = polygon.Points[Index];
            } else if (_shape is Polyline polyline) {
                if (Index < 0 || Index >= polyline.Points.Count) { adorner.Arrange(new Rect(0, 0, 0, 0)); return; }
                p = polyline.Points[Index];
            }
            adorner.Arrange(new Rect(p.X - 3.5, p.Y - 3.5, 7, 7));
        }
    }
}
