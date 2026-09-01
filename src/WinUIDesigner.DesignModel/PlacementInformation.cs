using Windows.Foundation;

namespace WinUIDesigner.DesignModel
{
    /// <summary>Stores information about the placement of an individual item during a
    /// PlacementOperation. No WPF-specific API - mechanical port.</summary>
    public sealed class PlacementInformation
    {
        public const int BoundsPrecision = 0;

        public DesignItem Item { get; }
        public PlacementOperation Operation { get; }
        public Rect OriginalBounds { get; set; }
        public Rect Bounds { get; set; }
        public PlacementAlignment? ResizeThumbAlignment { get; set; }

        internal PlacementInformation(DesignItem item, PlacementOperation operation)
        {
            Item = item;
            Operation = operation;
        }

        public override string ToString() => "[PlacementInformation OriginalBounds=" + OriginalBounds + " Bounds=" + Bounds + " Item=" + Item + "]";
    }
}
