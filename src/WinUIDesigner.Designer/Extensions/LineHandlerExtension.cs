using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Shapes;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>Post-creation point-editing for Line: two PointThumbs at (X1,Y1) and (X2,Y2).
    /// Ported from the reference's Extensions/LineHandlerExtension.cs. OverrideExtensions is
    /// scoped to the 3 extensions that exist in this port (spec decision 7) rather than the
    /// reference's 7-type list. The DragListener property's singular/last-created-wins shape is
    /// deliberately faithful to the reference (spec decision 4's documented ordering
    /// dependency) - Task 8's DrawLineExtension.ExternalStart depends on it landing on
    /// BottomRight/X2,Y2.</summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(Line), OverrideExtensions = new[] { typeof(WinUIDesigner.Designer.Extensions.ResizeThumbExtension), typeof(WinUIDesigner.Designer.Extensions.RotateThumbExtension), typeof(WinUIDesigner.Designer.Extensions.SkewThumbExtension) })]
    public sealed class LineHandlerExtension : PointEditExtensionBase
    {
        PointThumb _topLeftThumb;
        PointThumb _bottomRightThumb;
        double _originalX1, _originalY1, _originalX2, _originalY2;

        public DragListener DragListener { get; private set; }
        internal readonly Dictionary<PlacementAlignment, DragListener> DragListeners = new Dictionary<PlacementAlignment, DragListener>();

        protected override IEnumerable<DesignerThumb> Thumbs {
            get { yield return _topLeftThumb; yield return _bottomRightThumb; }
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            // Order matters (spec decision 4): TopLeft first, BottomRight second, so the public
            // singular DragListener property ends up on BottomRight, matching DrawLineExtension's
            // (Task 8) dependency on exactly that.
            _topLeftThumb = CreateThumb(PlacementAlignment.TopLeft, InputSystemCursorShape.Cross);
            _bottomRightThumb = CreateThumb(PlacementAlignment.BottomRight, InputSystemCursorShape.Cross);
            UpdateAdornerVisibility();
        }

        PointThumb CreateThumb(PlacementAlignment alignment, InputSystemCursorShape cursor)
        {
            var thumb = new PointThumb { Alignment = alignment };
            thumb.SetDesignCursor(cursor);
            AdornerPanel.SetPlacement(thumb, Place(alignment, 0));
            adornerPanel.Children.Add(thumb);

            var drag = new DragListener(thumb);
            drag.Started += Drag_Started;
            drag.Changed += Drag_Changed;
            drag.Completed += Drag_Completed;
            DragListener = drag;
            DragListeners[alignment] = drag;

            return thumb;
        }

        void Drag_Started(DragListener drag)
        {
            var line = (Line)ExtendedItem.View;
            _originalX1 = line.X1; _originalY1 = line.Y1;
            _originalX2 = line.X2; _originalY2 = line.Y2;
            SetOperation();
        }

        void SetOperation()
        {
            if (resizeBehavior != null)
                operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
            else
                changeGroup = ExtendedItem.OpenGroup("Resize");
            IsResizing = true;
        }

        void Drag_Changed(DragListener drag)
        {
            var (dx, dy) = drag.Delta;
            if (drag.Target == _topLeftThumb) {
                ExtendedItem.Properties["X1"].SetValue(System.Math.Round(_originalX1 + dx));
                ExtendedItem.Properties["Y1"].SetValue(System.Math.Round(_originalY1 + dy));
            } else {
                ExtendedItem.Properties["X2"].SetValue(System.Math.Round(_originalX2 + dx));
                ExtendedItem.Properties["Y2"].SetValue(System.Math.Round(_originalY2 + dy));
            }
            Invalidate();
        }

        void Drag_Completed(DragListener drag)
        {
            if (drag.IsCanceled) {
                operation?.Abort();
                operation = null;
                changeGroup?.Abort();
                changeGroup = null;
            } else {
                operation?.Commit();
                operation = null;
                changeGroup?.Commit();
                changeGroup = null;
            }
            IsResizing = false;
            Invalidate();
        }
    }
}
