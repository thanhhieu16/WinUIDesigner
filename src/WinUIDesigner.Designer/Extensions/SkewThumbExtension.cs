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
    /// </summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(FrameworkElement))]
    public sealed class SkewThumbExtension : AdornerProvider
    {
        readonly AdornerPanel adornerPanel;
        readonly DesignItem[] extendedItemArray = new DesignItem[1];
        DesignerThumb thumbX, thumbY;
        internal DragListener DragListenerX, DragListenerY;

        UIElement parent;
        SkewTransform skewTransform;
        double skewX, skewY;

        public SkewThumbExtension()
        {
            adornerPanel = new AdornerPanel { Order = AdornerOrder.BeforeForeground };
            this.Adorners.Add(adornerPanel);
        }

        void DragX_Started(DragListener drag)
        {
            parent = (UIElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this.ExtendedItem.View);
            (skewX, skewY) = skewTransform != null ? (skewTransform.AngleX, skewTransform.AngleY) : (0, 0);
        }

        void DragX_Changed(DragListener drag)
        {
            var delta = drag.Delta;
            var destAngle = -0.5 * delta.X + skewX;
            ApplySkew(angleX: destAngle, angleY: skewY);
        }

        void DragY_Started(DragListener drag)
        {
            parent = (UIElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this.ExtendedItem.View);
            (skewX, skewY) = skewTransform != null ? (skewTransform.AngleX, skewTransform.AngleY) : (0, 0);
        }

        void DragY_Changed(DragListener drag)
        {
            var delta = drag.Delta;
            var destAngle = -0.5 * delta.Y + skewY;
            ApplySkew(angleX: skewX, angleY: destAngle);
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
            extendedItemArray[0] = this.ExtendedItem;
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
            DragListenerY = new DragListener(thumbY);
            DragListenerY.Started += DragY_Started;
            DragListenerY.Changed += DragY_Changed;
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
