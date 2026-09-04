using System.Collections.Generic;
using Microsoft.UI.Xaml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Shared base for LineHandlerExtension/PolyLineHandlerExtension/PathHandlerExtension
    /// (Tasks 7/9/11). Ported from the reference's Extensions/LineExtensionBase.cs, renamed
    /// (this base is shared by Polygon/Polyline/Path too, not just Line) and inheriting
    /// AdornerProvider directly rather than the reference's SelectionAdornerProvider - that WPF
    /// base class (an empty marker whose only job is inheriting [ExtensionServer(typeof(
    /// SelectionExtensionServer))]) has no port; 4a's ResizeThumbExtension already established
    /// the AdornerProvider-direct precedent for this exact shape of extension (spec decision 3).
    /// Dropped entirely (not stubbed): HideSizeAndShowHandles/SetSurfaceInfo/the drag-angle text
    /// overlay/ResetWidthHeightProperties - these reference SizeDisplayExtension/
    /// MarginHandleExtension, which ResizeThumbExtension's own class remarks already established
    /// as "deleted outright" 4e territory in this port; the angle-text overlay is a cosmetic
    /// diagnostic with no DoD requirement, and reimplementing it needs its own Canvas/TextBlock
    /// positioning work nothing in this phase's scope asks for.</summary>
    public abstract class PointEditExtensionBase : AdornerProvider
    {
        protected readonly AdornerPanel adornerPanel;
        protected readonly DesignItem[] extendedItemArray = new DesignItem[1];
        protected IPlacementBehavior resizeBehavior;
        protected PlacementOperation operation;
        protected ChangeGroup changeGroup;

        public bool IsResizing { get; protected set; }

        protected PointEditExtensionBase()
        {
            adornerPanel = new AdornerPanel { Order = AdornerOrder.Foreground };
            Adorners.Add(adornerPanel);
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            extendedItemArray[0] = ExtendedItem;
            ExtendedItem.PropertyChanged += OnPropertyChanged;
            resizeBehavior = PlacementOperation.GetPlacementBehavior(extendedItemArray);
        }

        protected override void OnRemove()
        {
            ExtendedItem.PropertyChanged -= OnPropertyChanged;
            base.OnRemove();
        }

        void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateAdornerVisibility();

        /// <summary>Every DesignerThumb this extension currently shows. Line's is a fixed 2-item
        /// array; Polyline/Path's is the internal index spec decision 12 requires (kept in
        /// lockstep with adornerPanel.Children on every insert/remove).</summary>
        protected abstract IEnumerable<DesignerThumb> Thumbs { get; }

        /// <summary>Hides every point-thumb when this item's container has no placement behavior
        /// that can place a MovePoint - mirrors ResizeThumbExtension.UpdateAdornerVisibility
        /// exactly (spec decision 13).</summary>
        protected void UpdateAdornerVisibility()
        {
            bool visible = resizeBehavior != null && resizeBehavior.CanPlace(extendedItemArray, PlacementType.MovePoint, PlacementAlignment.BottomRight);
            foreach (var thumb in Thumbs) thumb.ThumbVisible = visible;
        }

        protected PointTrackerPlacementSupport Place(PlacementAlignment alignment, int index) =>
            new PointTrackerPlacementSupport((Microsoft.UI.Xaml.Shapes.Shape)ExtendedItem.View, alignment, index);

        protected void Invalidate()
        {
            (ExtendedItem.View as FrameworkElement)?.InvalidateArrange();
        }
    }
}
