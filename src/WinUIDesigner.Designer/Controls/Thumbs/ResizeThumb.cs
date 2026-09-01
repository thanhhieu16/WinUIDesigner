using Windows.Foundation;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Controls.Thumbs
{
    /// <summary>Resize thumb that automatically disappears if the adorned element is too small
    /// along the checked axis. No WPF-specific API - mechanical port (base class changed to
    /// DesignerThumb : Grid, see DesignerThumb's remarks).</summary>
    public sealed class ResizeThumb : DesignerThumb
    {
        readonly bool checkWidth, checkHeight;

        public ResizeThumb(bool checkWidth, bool checkHeight)
        {
            this.checkWidth = checkWidth;
            this.checkHeight = checkHeight;
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            if (this.Parent is WinUIDesigner.DesignModel.Adorners.AdornerPanel parent && parent.AdornedElement != null) {
                if (checkWidth)
                    this.ThumbVisible = PlacementOperation.GetRealElementSize(parent.AdornedElement).Width > 14;
                else if (checkHeight)
                    this.ThumbVisible = PlacementOperation.GetRealElementSize(parent.AdornedElement).Height > 14;
            }
            return base.ArrangeOverride(arrangeBounds);
        }
    }
}
