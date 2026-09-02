using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// Placement behavior for StackPanel. Ported from the reference's
    /// Extensions/StackPanelPlacementSupport.cs. It does NOT constrain a resize: it compares the
    /// dragged item's center against its siblings' centers to compute an insertion index,
    /// reorders the item to that index inside the parent's ContentProperty collection, and draws
    /// a 2px insertion-point bar. It bases directly on DefaultPlacementBehavior (no Raster/
    /// Snapline chain - reordering is not a coordinate-snapping concern).
    ///
    /// WinUI substitutions: UIElement.TranslatePoint -> TransformToVisual().TransformPoint()
    /// (absent, compiler-confirmed); Brushes.Black -> new SolidColorBrush(Microsoft.UI.Colors.Black)
    /// (no Brushes class in WinUI); Visibility.Hidden -> Visibility.Collapsed (WinUI's Visibility
    /// has only Visible/Collapsed).
    ///
    /// Three deliberate deviations from the reference, all bug fixes:
    /// (1) FindVerticalRectanglePlacementOffset compares (Left + Right)/2. The reference
    ///     computes (Left + Top)/2 - an X coordinate averaged with a Y coordinate - while its
    ///     vertical-orientation twin correctly uses (Top + Bottom)/2.
    /// (2) SetPosition recomputes "is this item being resized" on every call. The reference only
    ///     reassigns its _isItemGettingResized field when a ResizeThumbExtension is found, so the
    ///     flag goes stale when the item is not selected and has no such extension.
    /// (3) OnInitialized/BeginPlacement guard ContentProperty for null/non-collection. The
    ///     reference dereferences it unguarded, which throws for exactly the shapes
    ///     DefaultPlacementBehavior.OnInitialized already tolerates.
    /// ChangePositionTo additionally skips the remove+insert when the target index is the
    /// current one - identity in the reference too, but here it would otherwise push two no-op
    /// transaction items onto the open ChangeGroup on every drag tick.
    /// </summary>
    [ExtensionFor(typeof(StackPanel), OverrideExtension = typeof(DefaultPlacementBehavior))]
    public class StackPanelPlacementSupport : DefaultPlacementBehavior
    {
        StackPanel _stackPanel;
        AdornerPanel _adornerPanel;
        Rectangle _rectangle = new Rectangle();     // the insertion-point indicator
        readonly List<Rect> _rects = new List<Rect>(); // bounds of every child, panel-relative
        int _indexToInsert;

        /// <summary>Test seams, same idiom as ResizeThumbExtension.DragListeners: there is no
        /// other way to reach the exact adorner instance this behavior created.</summary>
        internal AdornerPanel IndicatorAdornerPanel { get { return _adornerPanel; } }
        internal Rectangle IndicatorRectangle { get { return _rectangle; } }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _stackPanel = this.ExtendedItem.View as StackPanel;
            CollectChildRects();
        }

        void CollectChildRects()
        {
            _rects.Clear();
            var content = this.ExtendedItem.ContentProperty;
            if (content == null || !content.IsCollection || this.ExtendedItem.View == null) return;
            foreach (var child in content.CollectionElements) {
                if (child.View == null) continue;
                var p = child.View.TransformToVisual(this.ExtendedItem.View).TransformPoint(new Point(0, 0));
                _rects.Add(new Rect(p, child.View.RenderSize));
            }
        }

        public override void BeginPlacement(PlacementOperation operation)
        {
            base.BeginPlacement(operation);
            CollectChildRects();

            var designPanel = this.ExtendedItem.Services.DesignPanel;
            if (_adornerPanel != null && designPanel.Adorners.Contains(_adornerPanel))
                designPanel.Adorners.Remove(_adornerPanel);

            _adornerPanel = new AdornerPanel { Order = AdornerOrder.Foreground };
            _rectangle = new Rectangle { Fill = new SolidColorBrush(Microsoft.UI.Colors.Black), IsHitTestVisible = false };
            _adornerPanel.SetAdornedElement(this.ExtendedItem.View, this.ExtendedItem);
            _adornerPanel.Children.Add(_rectangle);
            designPanel.Adorners.Add(_adornerPanel);
        }

        public override void EndPlacement(PlacementOperation operation)
        {
            base.EndPlacement(operation);
            var designPanel = this.ExtendedItem.Services.DesignPanel;
            if (_adornerPanel != null && designPanel.Adorners.Contains(_adornerPanel))
                designPanel.Adorners.Remove(_adornerPanel);
        }

        public override void EnterContainer(PlacementOperation operation)
        {
            base.EnterContainer(operation);
            _rectangle.Visibility = Visibility.Visible;
        }

        public override void LeaveContainer(PlacementOperation operation)
        {
            base.LeaveContainer(operation);
            // Hide the bar while the item is being dragged into another container, otherwise it
            // renders on top of that container.
            _rectangle.Visibility = Visibility.Collapsed;
        }

        public override void SetPosition(PlacementInformation info)
        {
            base.SetPosition(info);

            bool isItemGettingResized = info.Item.Extensions.OfType<ResizeThumbExtension>().Any(e => e.IsResizing);
            if (_stackPanel == null || isItemGettingResized) return;

            if (_stackPanel.Orientation == Orientation.Vertical)
                DrawHorizontalRectangle(FindHorizontalRectanglePlacementOffset(info.Bounds));
            else
                DrawVerticalRectangle(FindVerticalRectanglePlacementOffset(info.Bounds));

            ChangePositionTo(info.Item, _indexToInsert);
        }

        void ChangePositionTo(DesignItem element, int index)
        {
            var content = this.ExtendedItem.ContentProperty;
            if (content == null || !content.IsCollection) return;
            var elements = content.CollectionElements;
            int elementIndex = elements.IndexOf(element);
            if (elementIndex >= 0 && index > elementIndex)
                index--;
            if (elementIndex == index) return;
            if (elementIndex >= 0)
                elements.Remove(element);
            elements.Insert(index, element);
        }

        double FindHorizontalRectanglePlacementOffset(Rect rect)
        {
            _rects.Sort((r1, r2) => r1.Top.CompareTo(r2.Top));
            var itemCenter = (rect.Top + rect.Bottom) / 2;
            for (int i = 0; i < _rects.Count; i++) {
                var rectCenter = (_rects[i].Top + _rects[i].Bottom) / 2;
                if (rectCenter >= itemCenter) {
                    _indexToInsert = i;
                    return _rects[i].Top;
                }
            }
            _indexToInsert = _rects.Count;
            return _rects.Count > 0 ? _rects[_rects.Count - 1].Bottom : 0;
        }

        double FindVerticalRectanglePlacementOffset(Rect rect)
        {
            _rects.Sort((r1, r2) => r1.Left.CompareTo(r2.Left));
            var itemCenter = (rect.Left + rect.Right) / 2;
            for (int i = 0; i < _rects.Count; i++) {
                // Reference bug fixed here - see the class comment.
                var rectCenter = (_rects[i].Left + _rects[i].Right) / 2;
                if (rectCenter >= itemCenter) {
                    _indexToInsert = i;
                    return _rects[i].Left;
                }
            }
            _indexToInsert = _rects.Count;
            return _rects.Count > 0 ? _rects[_rects.Count - 1].Right : 0;
        }

        void DrawHorizontalRectangle(double offset)
        {
            _rectangle.Height = 2;
            // Clear the other axis so a stale explicit size can't survive an orientation flip
            // (the reference leaves it set).
            _rectangle.Width = double.NaN;
            AdornerPanel.SetPlacement(_rectangle,
                new RelativePlacement(HorizontalAlignment.Stretch, VerticalAlignment.Top) { YOffset = offset });
        }

        void DrawVerticalRectangle(double offset)
        {
            _rectangle.Width = 2;
            _rectangle.Height = double.NaN;
            AdornerPanel.SetPlacement(_rectangle,
                new RelativePlacement(HorizontalAlignment.Left, VerticalAlignment.Stretch) { XOffset = offset });
        }
    }
}
