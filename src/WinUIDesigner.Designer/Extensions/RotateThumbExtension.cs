using System;
using System.Linq;
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
    /// The rotate thumb above the primary selection. Ported from the reference's
    /// RotateThumbExtension.cs. WindowClone guard dropped (that type isn't part of this port).
    /// The custom .cur rotate cursor (ZoomControl.GetCursor, out of scope) is replaced with
    /// InputSystemCursorShape.Hand - the reference's own first Cursor assignment before its
    /// dead-code second assignment overwrote it. WPF's ambient Mouse.GetPosition(this.parent)
    /// is replaced with DragListener.GetPosition(UIElement) (Task 1) - the reference's own
    /// callback signature (void(DragListener drag)) never received raw event args either.
    ///
    /// Two fixes on top of the brief's literal code, both confirmed against the real SDK (see
    /// task-5-report.md "Deviations"): (1) the "start" angle is captured from
    /// DragListener.StartPoint (transformed into parent space), not drag.GetPosition(parent) -
    /// GetPosition always reads CurrentPoint, which DragListener.MoveTo already updated to the
    /// triggering move's position *before* Started fires, so drag.GetPosition(parent) inside
    /// Drag_Started returned the exact same point Drag_Changed would read for that same call,
    /// making the angle delta identically zero. (2) the rotation pivot no longer reads
    /// fe.RenderTransformOrigin directly - RenderTransformOrigin defaults to (0,0) (top-left,
    /// confirmed empirically) until a transform is first committed, at which point
    /// ModelTools.ApplyTransform/SkewThumbExtension.ApplySkew always set it to (0.5, 0.5); since
    /// Drag_Started captures centerPoint *before* that happens, the very first rotate on a
    /// never-transformed element pivoted around the top-left corner instead of the visual
    /// center. Falls back to (0.5, 0.5) only when the property is genuinely unset, mirroring
    /// SkewThumbExtension.ApplySkew's own IsSet check on the same property - a user-authored
    /// custom RenderTransformOrigin already present in the loaded XAML is still respected.
    /// </summary>
    [ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
    [ExtensionFor(typeof(FrameworkElement))]
    public sealed class RotateThumbExtension : AdornerProvider
    {
        readonly AdornerPanel adornerPanel;
        readonly DesignItem[] extendedItemArray = new DesignItem[1];
        internal DragListener DragListener;

        Windows.Foundation.Point centerPoint;
        UIElement parent;
        double startAngleFromCenter;
        double initialAngle;
        RotateTransform rotateTransform;

        public RotateThumbExtension()
        {
            adornerPanel = new AdornerPanel { Order = AdornerOrder.Foreground };
            this.Adorners.Add(adornerPanel);
            CreateRotateThumb();
        }

        void CreateRotateThumb()
        {
            DesignerThumb thumb = new RotateThumb();
            thumb.SetDesignCursor(InputSystemCursorShape.Hand);
            thumb.Alignment = PlacementAlignment.Top;
            AdornerPanel.SetPlacement(thumb, new RelativePlacement(HorizontalAlignment.Center, VerticalAlignment.Top) { WidthRelativeToContentWidth = 1, HeightOffset = 0 });
            adornerPanel.Children.Add(thumb);

            DragListener = new DragListener(thumb);
            DragListener.Started += Drag_Started;
            DragListener.Changed += Drag_Changed;
            DragListener.Completed += Drag_Completed;
        }

        void Drag_Started(WinUIDesigner.Designer.Controls.DragListener drag)
        {
            var designerItem = this.ExtendedItem.View;
            parent = (UIElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(designerItem);
            var fe = (FrameworkElement)designerItem;
            var origin = this.ExtendedItem.Properties.GetProperty("RenderTransformOrigin").IsSet
                ? fe.RenderTransformOrigin
                : new Windows.Foundation.Point(0.5, 0.5);
            centerPoint = designerItem.TransformToVisual(parent).TransformPoint(
                new Windows.Foundation.Point(fe.ActualWidth * origin.X, fe.ActualHeight * origin.Y));

            var start = TransformToParent(drag.StartPoint, parent) ?? centerPoint;
            startAngleFromCenter = Math.Atan2(start.Y - centerPoint.Y, start.X - centerPoint.X);
            initialAngle = rotateTransform != null ? rotateTransform.Angle : 0;
        }

        void Drag_Changed(WinUIDesigner.Designer.Controls.DragListener drag)
        {
            var current = drag.GetPosition(parent) ?? centerPoint;
            var currentAngleFromCenter = Math.Atan2(current.Y - centerPoint.Y, current.X - centerPoint.X);
            var angleDelta = (currentAngleFromCenter - startAngleFromCenter) * 180 / Math.PI;

            var destAngle = initialAngle + Math.Round(angleDelta, 0);
            if ((drag.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) != Windows.System.VirtualKeyModifiers.Control)
                destAngle = ((int)destAngle / 15) * 15;

            ModelTools.ApplyTransform(this.ExtendedItem, new RotateTransform { Angle = destAngle }, false);
        }

        void Drag_Completed(WinUIDesigner.Designer.Controls.DragListener drag)
        {
        }

        /// <summary>Converts a raw window-space point (DragListener.StartPoint, which has no
        /// relativeTo-aware accessor of its own - DragListener.GetPosition only ever reads
        /// CurrentPoint) into relativeTo's coordinate space. Mirrors GetPosition's own transform
        /// logic; returns null under the same condition GetPosition does (a non-invertible
        /// transform).</summary>
        static Windows.Foundation.Point? TransformToParent(Windows.Foundation.Point point, UIElement relativeTo)
        {
            if (relativeTo == null) return point;
            var inverse = relativeTo.TransformToVisual(null).Inverse;
            if (inverse == null) return null;
            return inverse.TransformPoint(point);
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            extendedItemArray[0] = this.ExtendedItem;
            this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
            OnPrimarySelectionChanged(null, null);

            var fe = (FrameworkElement)this.ExtendedItem.Component;
            rotateTransform = fe.RenderTransform as RotateTransform;
            if (rotateTransform == null && fe.RenderTransform is TransformGroup tg)
                rotateTransform = tg.Children.OfType<RotateTransform>().FirstOrDefault();
        }

        protected override void OnRemove()
        {
            this.Services.Selection.PrimarySelectionChanged -= OnPrimarySelectionChanged;
            base.OnRemove();
        }

        void OnPrimarySelectionChanged(object sender, EventArgs e)
        {
            bool isPrimarySelection = this.Services.Selection.PrimarySelection == this.ExtendedItem;
            foreach (RotateThumb thumb in adornerPanel.Children.OfType<RotateThumb>()) thumb.IsPrimarySelection = isPrimarySelection;
        }
    }
}
