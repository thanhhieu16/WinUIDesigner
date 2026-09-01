using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// The resize thumb frame around the primary selection. Ported from the reference's
    /// ResizeThumbExtension.cs (SelectionAdornerProvider abstract base not ported - subclasses
    /// AdornerProvider directly with its own [ExtensionServer], matching Phase 3's own concrete
    /// SelectionAdornerProvider pattern - see spec). The reference's "is GridPlacementSupport"
    /// branch is dropped (that type doesn't exist yet - 4b) - every resize now takes the
    /// unconditional ModelTools.Resize path, which also removes a latent
    /// NullReferenceException present in the reference (operation is null whenever
    /// resizeBehavior was null at drag-start, but the dropped branch dereferenced
    /// operation.CurrentContainerBehavior unconditionally). SizeDisplayExtension/
    /// MarginHandleExtension interaction is deleted outright (not null-checked) - those types
    /// don't exist in this port (4e territory).
    /// </summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(FrameworkElement))]
    public sealed class ResizeThumbExtension : AdornerProvider
    {
        readonly AdornerPanel adornerPanel;
        readonly DesignerThumb[] designerThumbs;
        readonly DesignItem[] extendedItemArray = new DesignItem[1];
        IPlacementBehavior resizeBehavior;
        PlacementOperation operation;
        Windows.Foundation.Size oldSize;

        /// <summary>Exposes each corner/edge thumb's DragListener by alignment - tests drive
        /// drags through these directly (SimulateDown/Move/Up), since there is no other way to
        /// reach the exact listener instance wired to this extension's own Drag_Started/
        /// Drag_Changed/Drag_Completed handlers.</summary>
        internal readonly System.Collections.Generic.Dictionary<PlacementAlignment, DragListener> DragListeners =
            new System.Collections.Generic.Dictionary<PlacementAlignment, DragListener>();

        public ResizeThumbExtension()
        {
            adornerPanel = new AdornerPanel { Order = AdornerOrder.Foreground };
            this.Adorners.Add(adornerPanel);

            designerThumbs = new[] {
                CreateThumb(PlacementAlignment.TopLeft, InputSystemCursorShape.SizeNorthwestSoutheast),
                CreateThumb(PlacementAlignment.Top, InputSystemCursorShape.SizeNorthSouth),
                CreateThumb(PlacementAlignment.TopRight, InputSystemCursorShape.SizeNortheastSouthwest),
                CreateThumb(PlacementAlignment.Left, InputSystemCursorShape.SizeWestEast),
                CreateThumb(PlacementAlignment.Right, InputSystemCursorShape.SizeWestEast),
                CreateThumb(PlacementAlignment.BottomLeft, InputSystemCursorShape.SizeNortheastSouthwest),
                CreateThumb(PlacementAlignment.Bottom, InputSystemCursorShape.SizeNorthSouth),
                CreateThumb(PlacementAlignment.BottomRight, InputSystemCursorShape.SizeNorthwestSoutheast),
            };
        }

        DesignerThumb CreateThumb(PlacementAlignment alignment, InputSystemCursorShape cursor)
        {
            DesignerThumb thumb = new ResizeThumb(cursor == InputSystemCursorShape.SizeNorthSouth, cursor == InputSystemCursorShape.SizeWestEast);
            thumb.SetDesignCursor(cursor);
            thumb.Alignment = alignment;
            AdornerPanel.SetPlacement(thumb, Place(alignment));
            // Final-review fix (finding #3): the reference's Place sets Opacity = 0 on the two
            // stretched, full-edge center thumbs (Top/Bottom-center, Left/Right-center - the ones
            // using WidthRelativeToContentWidth/HeightRelativeToContentHeight = 1 below) so they
            // stay hit-testable but invisible, framing the selection with a thin strip instead of
            // a visible white-with-black-border bar. This port had dropped it entirely, so those
            // four thumbs rendered as visible bars on every selection.
            if (alignment.Horizontal == HorizontalAlignment.Center || alignment.Vertical == VerticalAlignment.Center)
                thumb.Opacity = 0;
            adornerPanel.Children.Add(thumb);

            var drag = new DragListener(thumb);
            drag.Started += Drag_Started;
            drag.Changed += Drag_Changed;
            drag.Completed += Drag_Completed;
            DragListeners[alignment] = drag;
            return thumb;
        }

        RelativePlacement Place(PlacementAlignment alignment)
        {
            var placement = new RelativePlacement(alignment.Horizontal, alignment.Vertical);
            if (alignment.Horizontal == HorizontalAlignment.Center) {
                placement.WidthRelativeToContentWidth = 1;
                placement.HeightOffset = 6;
                return placement;
            }
            if (alignment.Vertical == VerticalAlignment.Center) {
                placement.HeightRelativeToContentHeight = 1;
                placement.WidthOffset = 6;
                return placement;
            }
            placement.WidthOffset = 6;
            placement.HeightOffset = 6;
            return placement;
        }

        void Drag_Started(DragListener drag)
        {
            drag.Transform = this.ExtendedItem.GetCompleteAppliedTransformationToView();
            oldSize = new Windows.Foundation.Size(ModelTools.GetWidth(ExtendedItem.View), ModelTools.GetHeight(ExtendedItem.View));
            // When resizeBehavior is null, operation stays null and every ModelTools.Resize call
            // below commits its own top-level undo entry instead of being grouped into one - the
            // same gap PlacementOperation.Start would otherwise close. Currently unreachable: all
            // eight thumbs are Visibility.Collapsed (UpdateAdornerVisibility) whenever
            // resizeBehavior is null, so no drag can start. Low priority - left as a flagged gap
            // rather than dead-until-4b code (final-review minor finding).
            operation = resizeBehavior != null ? PlacementOperation.Start(extendedItemArray, PlacementType.Resize) : null;
        }

        void Drag_Changed(DragListener drag)
        {
            double dx = 0, dy = 0;
            var alignment = ((DesignerThumb)drag.Target).Alignment;
            var delta = drag.Delta;

            if (alignment.Horizontal == HorizontalAlignment.Left) dx = -delta.X;
            if (alignment.Horizontal == HorizontalAlignment.Right) dx = delta.X;
            if (alignment.Vertical == VerticalAlignment.Top) dy = -delta.Y;
            if (alignment.Vertical == VerticalAlignment.Bottom) dy = delta.Y;

            bool constrain = (drag.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) == Windows.System.VirtualKeyModifiers.Control
                && alignment.Horizontal != HorizontalAlignment.Center && alignment.Vertical != VerticalAlignment.Center;
            if (constrain) {
                if (dx > dy) dx = dy; else dy = dx;
            }

            var newWidth = Math.Max(0, oldSize.Width + dx);
            var newHeight = Math.Max(0, oldSize.Height + dy);

            ModelTools.Resize(ExtendedItem, newWidth, newHeight);

            if (operation != null) {
                var info = operation.PlacedItems[0];
                var result = info.OriginalBounds;
                if (alignment.Horizontal == HorizontalAlignment.Left) result.X = Math.Min(result.Right, result.X - dx);
                if (alignment.Vertical == VerticalAlignment.Top) result.Y = Math.Min(result.Bottom, result.Y - dy);
                result.Width = newWidth;
                result.Height = newHeight;

                info.Bounds = result.Round();
                info.ResizeThumbAlignment = alignment;
                operation.CurrentContainerBehavior.BeforeSetPosition(operation);
                operation.CurrentContainerBehavior.SetPosition(info);
            }
        }

        void Drag_Completed(DragListener drag)
        {
            if (operation != null) {
                if (drag.IsCanceled) operation.Abort(); else operation.Commit();
                operation = null;
            }
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            extendedItemArray[0] = this.ExtendedItem;
            this.ExtendedItem.PropertyChanged += OnPropertyChanged;
            this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
            resizeBehavior = PlacementOperation.GetPlacementBehavior(extendedItemArray);
            UpdateAdornerVisibility();
            OnPrimarySelectionChanged(null, null);
        }

        void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateAdornerVisibility();

        protected override void OnRemove()
        {
            this.ExtendedItem.PropertyChanged -= OnPropertyChanged;
            this.Services.Selection.PrimarySelectionChanged -= OnPrimarySelectionChanged;
            base.OnRemove();
        }

        void OnPrimarySelectionChanged(object sender, EventArgs e)
        {
            bool isPrimarySelection = this.Services.Selection.PrimarySelection == this.ExtendedItem;
            foreach (var thumb in designerThumbs) thumb.IsPrimarySelection = isPrimarySelection;
        }

        void UpdateAdornerVisibility()
        {
            // Known limitation, flagged not fixed (final-review minor finding): ThumbVisible is
            // also written by ResizeThumb.ArrangeOverride itself ("too small along this axis" -
            // ThumbVisible = actualSize > 14), independently of this method's "doesn't apply to
            // this container" check. Both writers share the same DependencyProperty/Visibility
            // channel with no coordination between them, so a resize that changes CanPlace and
            // element size in the same tick can - in principle - have one writer's value briefly
            // clobbered by the other's next arrange pass before settling. This is the plan's own
            // design choice (not an implementer slip); fixing it would mean introducing a second,
            // combined visibility signal, judged not worth the plan-deviation risk for 4a.
            foreach (var thumb in designerThumbs) {
                bool visible = resizeBehavior != null && resizeBehavior.CanPlace(extendedItemArray, PlacementType.Resize, thumb.Alignment);
                thumb.ThumbVisible = visible;
            }
        }
    }
}
