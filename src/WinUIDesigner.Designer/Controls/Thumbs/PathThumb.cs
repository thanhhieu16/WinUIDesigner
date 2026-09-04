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
