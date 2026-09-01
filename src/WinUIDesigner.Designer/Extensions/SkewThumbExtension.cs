using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Adorners;
using WinUIDesigner.DesignModel.Extensions;
using WinUIDesigner.Designer.Controls;
using WinUIDesigner.Designer.Controls.Thumbs;

namespace WinUIDesigner.Designer.Extensions
{
    /// <summary>
    /// The two skew handles (horizontal and vertical) around the primary selection. Ported
    /// from the reference's SkewThumbExtension.cs. The reference's two raw `new Thumb()`
    /// instances become DesignerThumb (Grid-based) instead - Thumb is sealed, so even direct
    /// instantiation with a public Cursor setter isn't available (UIElement.Cursor isn't
    /// public in WinUI). WindowClone guard dropped (not part of this port).
    ///
    /// Final-review fix (findings #1/#2): the reference brackets each of its two drags
    /// (dragX_Started/dragX_Completed, dragY_Started/dragY_Completed) in its own
    /// PlacementOperation.Start()/.Commit() - a fact this port had dropped, with no Completed
    /// handler wired up for either DragListener at all. Restored via DesignItem.OpenGroup/
    /// ChangeGroup.Commit()/Abort() (not PlacementOperation.Start - see RotateThumbExtension's
    /// class remarks for why), one independent ChangeGroup per handle (skewXGroup/skewYGroup),
    /// matching the reference's own per-handle granularity rather than combining both handles
    /// into a single group. Without this, every ApplySkew tick committed its own top-level
    /// undo-stack entry and Escape-cancel had nothing to Abort() - same defect as
    /// RotateThumbExtension's.
    ///
    /// Known gap, flagged not fixed (final-review minor finding): DragX_Changed/DragY_Changed
    /// compute their delta in raw window/pointer space (drag.Delta with no drag.Transform set),
    /// unlike ResizeThumbExtension.Drag_Started which sets
    /// drag.Transform = GetCompleteAppliedTransformationToView() to compensate for any
    /// rotate/scale already applied to the element or an ancestor. This only diverges once such
    /// a transform is already in play (out of scope for 4a's own DoD, and no 4a test exercises a
    /// pre-rotated/pre-scaled ancestor) - left for 4b/4e to address alongside the rest of the
    /// transform-composition work.
    /// </summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(FrameworkElement))]
    public sealed class SkewThumbExtension : AdornerProvider
    {
        readonly AdornerPanel adornerPanel;
        DesignerThumb thumbX, thumbY;
        internal DragListener DragListenerX, DragListenerY;

        UIElement parent;
        SkewTransform skewTransform;
        double skewX, skewY;
        ChangeGroup skewXGroup, skewYGroup;

        public SkewThumbExtension()
        {
            adornerPanel = new AdornerPanel { Order = AdornerOrder.BeforeForeground };
            this.Adorners.Add(adornerPanel);
        }

        void DragX_Started(DragListener drag)
        {
            parent = (UIElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this.ExtendedItem.View);
            (skewX, skewY) = skewTransform != null ? (skewTransform.AngleX, skewTransform.AngleY) : (0, 0);
            // Final-review fix (findings #1/#2): bracket this handle's whole drag in its own
            // outer ChangeGroup - see class remarks.
            skewXGroup = this.ExtendedItem.OpenGroup("Skew");
        }

        void DragX_Changed(DragListener drag)
        {
            var delta = drag.Delta;
            var destAngle = -0.5 * delta.X + skewX;
            ApplySkew(angleX: destAngle, angleY: skewY);
        }

        void DragX_Completed(DragListener drag)
        {
            if (skewXGroup != null) {
                if (drag.IsCanceled) skewXGroup.Abort(); else skewXGroup.Commit();
                skewXGroup = null;
            }
        }

        void DragY_Started(DragListener drag)
        {
            parent = (UIElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this.ExtendedItem.View);
            (skewX, skewY) = skewTransform != null ? (skewTransform.AngleX, skewTransform.AngleY) : (0, 0);
            // Final-review fix (findings #1/#2): bracket this handle's whole drag in its own
            // outer ChangeGroup - see class remarks.
            skewYGroup = this.ExtendedItem.OpenGroup("Skew");
        }

        void DragY_Changed(DragListener drag)
        {
            var delta = drag.Delta;
            var destAngle = -0.5 * delta.Y + skewY;
            ApplySkew(angleX: skewX, angleY: destAngle);
        }

        void DragY_Completed(DragListener drag)
        {
            if (skewYGroup != null) {
                if (drag.IsCanceled) skewYGroup.Abort(); else skewYGroup.Commit();
                skewYGroup = null;
            }
        }

        void ApplySkew(double angleX, double angleY)
        {
            if (angleX == 0 && angleY == 0) {
                this.ExtendedItem.Properties.GetProperty("RenderTransform").Reset();
                skewTransform = null;
            } else {
                skewTransform = new SkewTransform { AngleX = angleX, AngleY = angleY };
                if (!this.ExtendedItem.Properties.GetProperty("RenderTransformOrigin").IsSet)
                    this.ExtendedItem.Properties.GetProperty("RenderTransformOrigin").SetValue(new Windows.Foundation.Point(0.5, 0.5));
                this.ExtendedItem.Properties.GetProperty("RenderTransform").SetValue(skewTransform);
            }
            var designPanel = this.ExtendedItem.Services.DesignPanel as WinUIDesigner.Designer.DesignPanel;
            designPanel?.AdornerLayer.UpdateAdornersForElement(this.ExtendedItem.View, true);
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            this.ExtendedItem.PropertyChanged += OnPropertyChanged;

            var fe = (FrameworkElement)this.ExtendedItem.Component;
            skewTransform = fe.RenderTransform as SkewTransform;
            if (skewTransform != null) { skewX = skewTransform.AngleX; skewY = skewTransform.AngleY; }

            thumbX = new DesignerThumb { Width = 4, Height = 14 };
            thumbX.SetDesignCursor(InputSystemCursorShape.SizeWestEast);
            thumbY = new DesignerThumb { Width = 14, Height = 4 };
            thumbY.SetDesignCursor(InputSystemCursorShape.SizeNorthSouth);

            OnPropertyChanged(null, null);

            adornerPanel.Children.Add(thumbX);
            adornerPanel.Children.Add(thumbY);

            DragListenerX = new DragListener(thumbX);
            DragListenerX.Started += DragX_Started;
            DragListenerX.Changed += DragX_Changed;
            DragListenerX.Completed += DragX_Completed;
            DragListenerY = new DragListener(thumbY);
            DragListenerY.Started += DragY_Started;
            DragListenerY.Changed += DragY_Changed;
            DragListenerY.Completed += DragY_Completed;
        }

        void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender != null && e.PropertyName != "Width" && e.PropertyName != "Height") return;

            var size = PlacementOperation.GetRealElementSize(this.ExtendedItem.View);
            AdornerPanel.SetPlacement(thumbX, new RelativePlacement(HorizontalAlignment.Center, VerticalAlignment.Top) { YOffset = 0, XOffset = -size.Width / 4 });
            AdornerPanel.SetPlacement(thumbY, new RelativePlacement(HorizontalAlignment.Left, VerticalAlignment.Center) { YOffset = -size.Height / 4, XOffset = 0 });

            var designPanel = this.ExtendedItem.Services.DesignPanel as WinUIDesigner.Designer.DesignPanel;
            designPanel?.AdornerLayer.UpdateAdornersForElement(this.ExtendedItem.View, true);
        }

        protected override void OnRemove()
        {
            this.ExtendedItem.PropertyChanged -= OnPropertyChanged;
            base.OnRemove();
        }
    }
}
