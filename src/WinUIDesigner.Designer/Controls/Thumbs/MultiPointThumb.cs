using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.Designer.Extensions;

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
